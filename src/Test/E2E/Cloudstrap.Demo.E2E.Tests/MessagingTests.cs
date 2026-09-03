namespace Cloudstrap.Demo.E2E.Tests
{
    using System.Net;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Text.Json;
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Deliverable #14 live (AC-MSG16): the Api demo host stages an order and sends
    /// <c>PlaceOrderCommand</c> through the transactional outbox, the Worker demo host consumes it over
    /// the SQL Server transport on one LocalDB database, marks the order processed and records the flowed
    /// correlation id — proven through the running processes, with the Worker's stdout captured. The Worker
    /// is self-booted here on health port 5351 (5350 belongs to <see cref="WorkerHostTests"/>).
    /// </summary>
    [TestFixture]
    public sealed class MessagingTests
    {
        private const string _workerBaseUrl = "http://127.0.0.1:5351";
        private const string _workerProjectPath = "src/demo/Worker/Cloudstrap.Demo.Worker.csproj";
        private const string _tokenEndpoint = "http://127.0.0.1:5310/connect/token";
        private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(30);

        private SutProcess? _workerHost;
        private HttpClient _api = null!;
        private string _sentinelPath = null!;

        [OneTimeSetUp]
        public async Task StartWorkerHostAsync()
        {
            _sentinelPath = Path.Combine(Path.GetTempPath(), $"cloudstrap-demo-messaging-{Guid.NewGuid():N}.sentinel");

            // A generic host ignores ASPNETCORE_URLS — the port arrives as configuration. The SQL override
            // (spec D-3) is forwarded when set, exactly as the fixture forwards it to the Api.
            List<string> arguments =
            [
                "--Cloudstrap:Worker:HealthPort=5351",
                "--Demo:OutageSentinelPath=" + _sentinelPath,
            ];
            string? sqlOverride = Environment.GetEnvironmentVariable("CLOUDSTRAP_TEST_SQL");
            if (!string.IsNullOrWhiteSpace(sqlOverride))
            {
                arguments.Add("--ConnectionStrings:DefaultConnection=" + sqlOverride);
            }

            _workerHost = SutProcess.Start(_workerBaseUrl, arguments, _workerProjectPath);
            using HttpClient probe = new HttpClient { BaseAddress = new Uri(_workerBaseUrl) };
            await WaitUntilReadyAsync(probe, _workerHost);

            _api = new HttpClient { BaseAddress = new Uri(E2eFixture.ApiBaseUrl) };
            _api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await AcquireMachineTokenAsync());
        }

        [OneTimeTearDown]
        public void StopWorkerHost()
        {
            _api.Dispose();
            _workerHost?.Dispose();
            if (File.Exists(_sentinelPath))
            {
                File.Delete(_sentinelPath);
            }
        }

        [Test]
        public async Task Messaging_OrderPlacedThroughTheApiOutbox_IsProcessedByTheWorker_WithTheCorrelationIdObserved()
        {
            // Arrange — a business correlation id on the HTTP request, the way a real caller sets one.
            string correlationId = $"e2e-{Guid.NewGuid():N}";
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/v1/orders", UriKind.Relative))
            {
                Content = JsonContent.Create(new { description = "e2e order" }),
            };
            request.Headers.Add("X-Correlation-ID", correlationId);

            // Act — 202 + id from the outbox path; then poll the query endpoint until the Worker processed it.
            using HttpResponseMessage accepted = await _api.SendAsync(request);
            Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Guid orderId = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            JsonElement order = await WaitForStatusAsync(orderId, "Processed");

            // Assert — the command crossed processes over SQL Server, dispatched after the commit, and the
            // correlation id survived the hop into the Worker's handler.
            Assert.Multiple(() =>
            {
                Assert.That(order.GetProperty("status").GetString(), Is.EqualTo("Processed"));
                Assert.That(order.GetProperty("processedCorrelationId").GetString(), Is.EqualTo(correlationId));
            });
        }

        [Test]
        public async Task Messaging_WorkerLogsTheHandledCommandTypeAndId_NeverThePayload()
        {
            // Arrange — a sentinel description that must never reach the Worker's output.
            const string sentinel = "sentinel-description-never-logged-9f3c";
            using HttpResponseMessage accepted = await _api.PostAsJsonAsync(
                new Uri("/api/v1/orders", UriKind.Relative),
                new
                {
                    description = sentinel
                });
            Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Guid orderId = (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await WaitForStatusAsync(orderId, "Processed");

            // Act — the Worker's captured stdout after handling.
            string output = await WaitForOutputAsync(
                () => _workerHost!.CapturedOutput,
                text => text.Contains("PlaceOrderCommand", StringComparison.Ordinal));

            // Assert — type (and id) logged, payload never (the AC-MSG6 posture, live).
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.Contain("PlaceOrderCommand"));
                Assert.That(output, Does.Not.Contain(sentinel));
            });
        }

        [Test]
        public async Task Messaging_AnonymousOrdersPost_Returns401()
        {
            // Arrange — no bearer token at all.
            using HttpClient anonymous = new HttpClient { BaseAddress = new Uri(E2eFixture.ApiBaseUrl) };

            // Act
            using HttpResponseMessage response = await anonymous.PostAsJsonAsync(
                new Uri("/api/v1/orders", UriKind.Relative),
                new
                {
                    description = "anonymous"
                });

            // Assert — the hardened default still gates the new endpoint.
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        private async Task<JsonElement> WaitForStatusAsync(Guid orderId, string expected)
        {
            DateTime deadline = DateTime.UtcNow + _deadline;
            JsonElement last = default;
            while (DateTime.UtcNow < deadline)
            {
                using HttpResponseMessage response = await _api.GetAsync(new Uri($"/api/v1/orders/{orderId}", UriKind.Relative));
                if (response.IsSuccessStatusCode)
                {
                    last = await response.Content.ReadFromJsonAsync<JsonElement>();
                    if (last.GetProperty("status").GetString() == expected)
                    {
                        return last;
                    }
                }

                await Task.Delay(250);
            }

            Assert.Fail($"Order {orderId} never reached status '{expected}'. Last: {last}. Worker output:{Environment.NewLine}{_workerHost?.CapturedOutput}");
            return last;
        }

        private static async Task<string> AcquireMachineTokenAsync()
        {
            using HttpClient client = new HttpClient();
            using FormUrlEncodedContent form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "demo-machine",
                ["client_secret"] = "local-e2e-placeholder-secret-machine",
                ["scope"] = "selfapi",
            });
            using HttpResponseMessage response = await client.PostAsync(new Uri(_tokenEndpoint), form);
            string body = await response.Content.ReadAsStringAsync();
            Assert.That(response.IsSuccessStatusCode, Is.True, $"token request failed: {body}");
            return JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
        }

        private static async Task<string> WaitForOutputAsync(Func<string> captured, Func<string, bool> ready)
        {
            DateTime deadline = DateTime.UtcNow + _deadline;
            string output = captured();
            while (!ready(output) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                output = captured();
            }

            return output;
        }

        private static async Task WaitUntilReadyAsync(HttpClient client, SutProcess process)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    break;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet — keep polling until the deadline.
                }

                await Task.Delay(250);
            }

            throw new InvalidOperationException(
                $"The Worker demo host did not become ready at {client.BaseAddress}.{Environment.NewLine}{process.CapturedOutput}");
        }
    }
}
