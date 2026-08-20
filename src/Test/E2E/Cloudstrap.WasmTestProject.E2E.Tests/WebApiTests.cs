namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates Cloudstrap.WebApi (deliverable #5) through the running app: the whole Bff pipeline is now
    /// one <c>UseCloudstrapWebApi</c> call, serving versioned endpoints, one OpenAPI document per version,
    /// the Scalar reference UI and a hardened problem-details error response — all of it anonymously, since
    /// this host deliberately never calls <c>AddCloudstrapJwtBearer</c> (AC-W10).
    /// </summary>
    [TestFixture]
    public sealed class WebApiTests
    {
        private HttpClient _client = null!;

        [SetUp]
        public void SetUp() => _client = new HttpClient { BaseAddress = new Uri(E2eFixture.BaseUrl) };

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public async Task VersionedEndpoint_Get_ReturnsPayloadAndReportsItsSupportedVersion()
        {
            // Act — no Authorization header anywhere: the running-app proof that the pipeline never assumes
            // authentication exists
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/v1/status", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            using JsonDocument document = JsonDocument.Parse(body);

            Assert.Multiple(() =>
            {
                Assert.That(document.RootElement.GetProperty("apiVersion").GetString(), Is.EqualTo("1.0"));
                Assert.That(
                    document.RootElement.GetProperty("workloadName").GetString(),
                    Is.EqualTo("wasmtestproject-application-bff"));

                // Each version lives on its own controller, so URL-segment versioning gives each one its own
                // endpoint and its own reported version set. That both versions exist is proven by the v2
                // payload test and by the two OpenAPI documents.
                Assert.That(
                    string.Join(", ", response.Headers.GetValues("api-supported-versions")),
                    Is.EqualTo("1.0"));
            });
        }

        [Test]
        public async Task VersionedEndpoint_V2_ReturnsTheSecondVersionsPayload()
        {
            // Act
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/v2/status", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            using JsonDocument document = JsonDocument.Parse(body);

            Assert.Multiple(() =>
            {
                Assert.That(document.RootElement.GetProperty("apiVersion").GetString(), Is.EqualTo("2.0"));
                Assert.That(
                    string.Join(", ", response.Headers.GetValues("api-supported-versions")),
                    Is.EqualTo("2.0"));
            });
        }

        [Test]
        public async Task OpenApiDocuments_AreServedPerVersion()
        {
            // Act
            string[] v1 = await DocumentPaths("openapi/v1.json");
            string[] v2 = await DocumentPaths("openapi/v2.json");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(v1, Does.Contain("/api/v1/status"));
                Assert.That(v1, Does.Not.Contain("/api/v2/status"));
                Assert.That(v2, Does.Contain("/api/v2/status"));
                Assert.That(v2, Does.Not.Contain("/api/v1/status"));

                // The unversioned controllers were assigned the default version by the ported convention,
                // so they belong to the v1 document
                Assert.That(v1, Does.Contain("/api/doctor"));
                Assert.That(v1, Has.Some.StartsWith("/api/diagnostics"));
            });
        }

        [Test]
        public async Task OpenApiDocument_CarriesTheConfiguredTitle()
        {
            // Act
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("openapi/v1.json", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.That(
                document.RootElement.GetProperty("info").GetProperty("title").GetString(),
                Is.EqualTo("WasmTestProject Bff API"));
        }

        [Test]
        public async Task Error_Get_ReturnsProblemDetailsWithoutExceptionDetail()
        {
            // Act — the SUT pins IncludeDetails to false, so the hardened shape is what a Development run
            // must produce
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/v1/status/boom", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(
                    response.Content.Headers.ContentType?.MediaType,
                    Is.EqualTo("application/problem+json"));
                Assert.That(body, Does.Contain("\"title\""));
                Assert.That(body, Does.Not.Contain("InvalidOperationException"));
                Assert.That(body, Does.Not.Contain("status projection failed"));
                Assert.That(body, Does.Not.Contain("stackTrace"));
            });
        }

        [Test]
        public async Task Error_ReturnsTheCorrelationIdTheCallerSent()
        {
            // Arrange
            string correlationId = Guid.NewGuid().ToString("N");
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get, new Uri("api/v1/status/boom", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", correlationId);

            // Act
            using HttpResponseMessage response = await _client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            // Assert — the identifier a support request would quote
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.That(
                document.RootElement.GetProperty("correlationId").GetString(),
                Is.EqualTo(correlationId));
        }

        [Test]
        public async Task Error_WithIncludeDetailsEnabled_ReturnsTheExceptionDetail()
        {
            // Arrange — a second, short-lived instance with the detail switch flipped on
            using SutProcess sut = SutProcess.Start(
                "http://127.0.0.1:5303",
                ["--Cloudstrap:WebApi:ExceptionHandling:IncludeDetails=true"]);
            using HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5303") };
            await WaitUntilReadyAsync(client, sut);

            // Act
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("api/v1/status/boom", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.Multiple(() =>
            {
                Assert.That(
                    document.RootElement.GetProperty("exceptionType").GetString(),
                    Is.EqualTo("System.InvalidOperationException"));
                Assert.That(
                    document.RootElement.GetProperty("exceptionMessage").GetString(),
                    Does.Contain("status projection failed"));
                Assert.That(
                    document.RootElement.GetProperty("innerExceptions")[0].GetProperty("message").GetString(),
                    Does.Contain("unusable row"));
            });
        }

        [Test]
        public async Task Scalar_Get_ServesTheReferenceUiShell()
        {
            // Act — the library redirects the prefix to the page for a specific document, which this client
            // follows automatically, exactly as a browser does
            using HttpResponseMessage response = await _client.GetAsync(new Uri("scalar", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
                Assert.That(body, Does.Contain("\"url\":\"openapi/v1.json\""));
                Assert.That(body, Does.Contain("\"url\":\"openapi/v2.json\""));
            });
        }

        [Test]
        public async Task Response_EchoesTheCorrelationIdBackToTheCaller()
        {
            // Arrange
            string correlationId = Guid.NewGuid().ToString("N");
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Get, new Uri("api/v1/status", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", correlationId);

            // Act
            using HttpResponseMessage echoed = await _client.SendAsync(request);
            using HttpResponseMessage generated = await _client.GetAsync(
                new Uri("api/v1/status", UriKind.Relative));

            // Assert — a caller who sent none still learns the identifier the request was handled under
            Assert.Multiple(() =>
            {
                Assert.That(
                    echoed.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.EqualTo(correlationId));
                Assert.That(
                    generated.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.Not.Empty);
            });
        }

        [Test]
        public async Task EveryResponse_CarriesTheConstantSecurityHeaders()
        {
            // Act
            using HttpResponseMessage api = await _client.GetAsync(new Uri("api/v1/status", UriKind.Relative));
            using HttpResponseMessage probe = await _client.GetAsync(new Uri("healthz", UriKind.Relative));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(api.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
                Assert.That(api.Headers.GetValues("Referrer-Policy").Single(), Is.EqualTo("no-referrer"));
                Assert.That(probe.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
            });
        }

        private async Task<string[]> DocumentPaths(string route)
        {
            using HttpResponseMessage response = await _client.GetAsync(new Uri(route, UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);

            using JsonDocument document = JsonDocument.Parse(body);

            return [.. document.RootElement.GetProperty("paths").EnumerateObject().Select(path => path.Name)];
        }

        private static async Task WaitUntilReadyAsync(HttpClient client, SutProcess sut)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (sut.HasExited)
                {
                    break;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(
                        new Uri("healthz", UriKind.Relative));
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
                $"The second SUT instance did not become ready.{Environment.NewLine}{sut.CapturedOutput}");
        }
    }
}
