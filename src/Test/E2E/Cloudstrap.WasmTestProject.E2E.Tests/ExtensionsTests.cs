namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates Cloudstrap.Extensions (deliverable #4) through the running app: a typed HTTP client
    /// resolved from configuration makes a real outbound hop carrying the caller's correlation identifier,
    /// and that client's dependency health check feeds the readiness probe served by
    /// <c>MapCloudstrapHealthChecks</c>.
    /// </summary>
    [TestFixture]
    public sealed class ExtensionsTests
    {
        private HttpClient _client = null!;

        [SetUp]
        public void SetUp() => _client = new HttpClient { BaseAddress = new Uri(E2eFixture.BaseUrl) };

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public async Task Outbound_TypedClientHop_PropagatesTheCallersCorrelationId()
        {
            // Arrange
            string correlationId = Guid.NewGuid().ToString("N");
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get, new Uri("api/diagnostics/outbound", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", correlationId);

            // Act — the endpoint calls back into the app through the Cloudstrap-registered typed client
            using HttpResponseMessage response = await _client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            // Assert — the peer saw the caller's id, so the client was configured from
            // Cloudstrap:HttpClients:SelfApi and its correlation handler carried the id across the hop
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.That(
                document.RootElement.GetProperty("peerCorrelationId").GetString(),
                Is.EqualTo(correlationId));
        }

        [Test]
        public async Task Ready_WithSelfApiProbe_Returns200Healthy()
        {
            // Act
            using HttpResponseMessage response = await _client.GetAsync(new Uri("/ready", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — readiness now aggregates the SelfApi-liveness URI check, which really executed
            // against the running app's own /healthz
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Is.EqualTo("Healthy"));
            });
        }

        [Test]
        public async Task Ready_WithUnreachablePeerConfigured_Returns503WhileHealthzStays200()
        {
            // Arrange — a second short-lived instance whose SelfApi client points at a dead port
            const string baseUrl = "http://127.0.0.1:5302";
            using SutProcess process = SutProcess.Start(
                baseUrl,
                ["--Cloudstrap:HttpClients:SelfApi:BaseAddress=http://127.0.0.1:59999/"]);
            using HttpClient client = new HttpClient { BaseAddress = new Uri(baseUrl) };

            // Act
            bool live = await WaitForLivenessAsync(process, client);
            using HttpResponseMessage readiness = await client.GetAsync(new Uri("/ready", UriKind.Relative));

            // Assert — an unreachable dependency takes the instance out of rotation without ever
            // threatening liveness: that separation is the whole point of the two tags
            Assert.Multiple(() =>
            {
                Assert.That(
                    live,
                    Is.True,
                    $"The SUT never served /healthz. Captured output:{Environment.NewLine}{process.CapturedOutput}");
                Assert.That(readiness.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            });
        }

        private static async Task<bool> WaitForLivenessAsync(SutProcess process, HttpClient client)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    return false;
                }

                try
                {
                    using HttpResponseMessage response =
                        await client.GetAsync(new Uri("/healthz", UriKind.Relative));
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch (HttpRequestException)
                {
                    // The host is not listening yet.
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }

            return false;
        }
    }
}
