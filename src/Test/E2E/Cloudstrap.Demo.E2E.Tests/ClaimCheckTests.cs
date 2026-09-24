namespace Cloudstrap.Demo.E2E.Tests
{
    using System.Net;
    using System.Net.Http.Headers;
    using System.Net.Http.Json;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Azure.Storage.Blobs;
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Deliverable #15 live (AC-CK12, AC-CK10): an order whose notes exceed the claim-check threshold
    /// crosses from the Api demo host to the Worker demo host as one blob in the <c>demo-claimcheck</c>
    /// container on Azurite while the Worker records the notes' length and SHA-256; a small order adds no
    /// blob; the Api's startup posture line names container, threshold and client source and never the
    /// connection string. The Worker is self-booted here on health port 5352 (5350 belongs to
    /// <see cref="WorkerHostTests"/>, 5351 to <see cref="MessagingTests"/>).
    /// </summary>
    [TestFixture]
    public sealed class ClaimCheckTests
    {
        private const string _workerBaseUrl = "http://127.0.0.1:5352";
        private const string _workerProjectPath = "src/demo/Worker/Cloudstrap.Demo.Worker.csproj";
        private const string _tokenEndpoint = "http://127.0.0.1:5310/connect/token";
        private const string _claimCheckContainer = "demo-claimcheck";
        private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(30);

        private SutProcess? _workerHost;
        private HttpClient _api = null!;
        private string _sentinelPath = null!;

        [OneTimeSetUp]
        public async Task StartWorkerHostAsync()
        {
            _sentinelPath = Path.Combine(Path.GetTempPath(), $"cloudstrap-demo-claimcheck-{Guid.NewGuid():N}.sentinel");

            List<string> arguments =
            [
                "--Cloudstrap:Worker:HealthPort=5352",
                "--Demo:OutageSentinelPath=" + _sentinelPath,
            ];
            string? sqlOverride = Environment.GetEnvironmentVariable("CLOUDSTRAP_TEST_SQL");
            if (!string.IsNullOrWhiteSpace(sqlOverride))
            {
                arguments.Add("--ConnectionStrings:DefaultConnection=" + sqlOverride);
            }

            string? blobOverride = Environment.GetEnvironmentVariable(AzuriteProcess.EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(blobOverride))
            {
                arguments.Add("--Cloudstrap:Storage:ConnectionString=" + blobOverride);
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
        public async Task ClaimCheck_OrderWithNotesAboveTheThreshold_IsProcessedByTheWorker_WithLengthAndHashRecorded_AndExactlyOneNewBlobInTheClaimCheckContainer()
        {
            // Arrange — 300 000 ASCII characters of notes carrying a sentinel that must never be logged.
            const string sentinel = "notes-sentinel-never-logged-7b2e";
            string notes = string.Concat(Enumerable.Repeat(sentinel + " ", 300_000 / (sentinel.Length + 1) + 1))[..300_000];
            int blobsBefore = await CountClaimCheckBlobsAsync();

            // Act
            Guid orderId = await PlaceOrderAsync("e2e large order", notes);
            JsonElement order = await WaitForStatusAsync(orderId, "Processed");
            int blobsAfter = await CountClaimCheckBlobsAsync();

            // Assert — processed whole (length + hash), one blob crossed the wire, the notes never hit a log.
            Assert.Multiple(() =>
            {
                Assert.That(order.GetProperty("notesLength").GetInt32(), Is.EqualTo(300_000));
                Assert.That(order.GetProperty("notesSha256").GetString(), Is.EqualTo(Sha256Hex(notes)).IgnoreCase);
                Assert.That(blobsAfter, Is.EqualTo(blobsBefore + 1), "exactly one new blob in the claim-check container");
                Assert.That(_workerHost!.CapturedOutput, Does.Not.Contain(sentinel));
                Assert.That(E2eFixture.CapturedApiOutput, Does.Not.Contain(sentinel));
            });
        }

        [Test]
        public async Task ClaimCheck_OrderWithNotesBelowTheThreshold_IsProcessed_AndAddsNoBlob()
        {
            // Arrange — 1 024 characters: far under the 200 KiB threshold.
            string notes = new string('n', 1024);
            int blobsBefore = await CountClaimCheckBlobsAsync();

            // Act
            Guid orderId = await PlaceOrderAsync("e2e small order", notes);
            JsonElement order = await WaitForStatusAsync(orderId, "Processed");
            int blobsAfter = await CountClaimCheckBlobsAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(order.GetProperty("notesLength").GetInt32(), Is.EqualTo(1024));
                Assert.That(order.GetProperty("notesSha256").GetString(), Is.EqualTo(Sha256Hex(notes)).IgnoreCase);
                Assert.That(blobsAfter, Is.EqualTo(blobsBefore), "no blob for a small body");
            });
        }

        [Test]
        public void ClaimCheck_ApiStartupPostureLine_NamesContainerThresholdAndClientSource_NeverTheConnectionString()
        {
            // Arrange
            string output = E2eFixture.CapturedApiOutput;

            // Assert — AC-CK10 live, through the Api's console pipeline.
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.Contain(_claimCheckContainer));
                Assert.That(output, Does.Contain("204800"));
                Assert.That(output, Does.Contain("AddCloudstrapBlobStorage"));
                Assert.That(output, Does.Not.Contain("UseDevelopmentStorage"));
                Assert.That(output, Does.Not.Contain("devstoreaccount1"));
            });
        }

        private static string Sha256Hex(string text)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }

        private static async Task<int> CountClaimCheckBlobsAsync()
        {
            BlobContainerClient container = new BlobContainerClient(E2eFixture.BlobConnectionString, _claimCheckContainer);
            if (!await container.ExistsAsync())
            {
                return 0;
            }

            int count = 0;
            await foreach (Azure.Storage.Blobs.Models.BlobItem _ in container.GetBlobsAsync())
            {
                count++;
            }

            return count;
        }

        private async Task<Guid> PlaceOrderAsync(string description, string notes)
        {
            using HttpResponseMessage accepted = await _api.PostAsJsonAsync(
                new Uri("/api/v1/orders", UriKind.Relative),
                new
                {
                    description,
                    notes,
                });
            Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            return (await accepted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
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
