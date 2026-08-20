namespace Cloudstrap.Demo.E2E.Tests
{
    using System.Net;
    using System.Text.Json;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates Cloudstrap.Observability (deliverable #2) over plain HTTP: tagged health
    /// probes and the ambient correlation identifier established by the correlation middleware.
    /// </summary>
    [TestFixture]
    public sealed class HealthAndCorrelationTests
    {
        private HttpClient _client = null!;

        [SetUp]
        public void SetUp() => _client = new HttpClient { BaseAddress = new Uri(E2eFixture.BaseUrl) };

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public async Task Healthz_Get_Returns200Healthy()
        {
            // Act
            using HttpResponseMessage response = await _client.GetAsync(new Uri("/healthz", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Is.EqualTo("Healthy"));
            });
        }

        [Test]
        public async Task Ready_Get_Returns200Healthy()
        {
            // Act
            using HttpResponseMessage response = await _client.GetAsync(new Uri("/ready", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — body must be the health writer's status text, not the SPA fallback page
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Is.EqualTo("Healthy"));
            });
        }

        [Test]
        public async Task ApiRequest_WithCorrelationHeader_AmbientCorrelationEchoesIt()
        {
            // Arrange
            string correlationId = Guid.NewGuid().ToString("N");
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get, new Uri("api/diagnostics/correlation", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", correlationId);

            // Act
            using HttpResponseMessage response = await _client.SendAsync(request);
            string ambientId = await ReadCorrelationId(response);

            // Assert
            Assert.That(ambientId, Is.EqualTo(correlationId));
        }

        [Test]
        public async Task ApiRequest_WithoutCorrelationHeader_AmbientCorrelationIsGenerated()
        {
            // Act
            using HttpResponseMessage first = await _client.GetAsync(new Uri("api/diagnostics/correlation", UriKind.Relative));
            using HttpResponseMessage second = await _client.GetAsync(new Uri("api/diagnostics/correlation", UriKind.Relative));
            string firstId = await ReadCorrelationId(first);
            string secondId = await ReadCorrelationId(second);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(firstId, Is.Not.Empty);
                Assert.That(secondId, Is.Not.Empty);
                Assert.That(secondId, Is.Not.EqualTo(firstId), "Each request must get its own generated correlation id.");
            });
        }

        private static async Task<string> ReadCorrelationId(HttpResponseMessage response)
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            return document.RootElement.GetProperty("correlationId").GetString() ?? string.Empty;
        }
    }
}
