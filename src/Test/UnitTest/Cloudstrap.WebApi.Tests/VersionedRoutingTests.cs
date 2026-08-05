namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using Asp.Versioning;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using NUnit.Framework;

    /// <summary>
    /// AC-W1 and AC-W2: two calls in <c>Program.cs</c> serve a versioned API over real HTTP — supported
    /// versions reported, both stock readers live, and unattributed controllers assuming the configured
    /// default version.
    /// </summary>
    [TestFixture]
    public sealed class VersionedRoutingTests
    {
        [Test]
        public async Task AddAndUse_VersionedController_ServesTheEndpointAndReportsSupportedVersions()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .GetAsync(new Uri("/api/v1/widgets", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    string.Join(", ", response.Headers.GetValues("api-supported-versions")),
                    Does.Contain("1.0"));
            });
        }

        [Test]
        public async Task AddAndUse_QueryStringVersion_SelectsTheRequestedVersion()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();
            HttpClient client = app.GetTestClient();

            // Act
            string first = await client.GetStringAsync(
                new Uri("/api/widgets?api-version=1.0", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string second = await client.GetStringAsync(
                new Uri("/api/widgets?api-version=2.0", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(first, Does.Contain("\"version\":\"1.0\""));
                Assert.That(second, Does.Contain("\"version\":\"2.0\""));
            });
        }

        [Test]
        public async Task AddAndUse_UnattributedController_AssumesTheDefaultVersion()
        {
            // Arrange — no [ApiVersion] anywhere on the controller, no version in the request
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .GetAsync(new Uri("/api/legacy", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    string.Join(", ", response.Headers.GetValues("api-supported-versions")),
                    Is.EqualTo("1.0"));
            });
        }

        [Test]
        public async Task AddAndUse_WithConfiguredDefaultVersion_AssumesThatOne()
        {
            // Arrange — the default version comes from Cloudstrap:WebApi, never from documentation settings
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:WebApi:ApiVersioning:DefaultVersion"] = "2.0",
                });

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .GetAsync(new Uri("/api/legacy", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    string.Join(", ", response.Headers.GetValues("api-supported-versions")),
                    Is.EqualTo("2.0"));
            });
        }

        [Test]
        public async Task AddAndUse_WithReportApiVersionsFalse_OmitsTheHeader()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:WebApi:ApiVersioning:ReportApiVersions"] = "false",
                });

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .GetAsync(new Uri("/api/v1/widgets", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Headers.Contains("api-supported-versions"), Is.False);
            });
        }

        [Test]
        public async Task AddAndUse_UnsupportedVersion_Returns400ProblemDetails()
        {
            // Arrange — the library's stock response, deliberately not customized
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .GetAsync(
                    new Uri("/api/widgets?api-version=9.0", UriKind.Relative),
                    TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            });
        }

        [Test]
        public void AddCloudstrapWebApi_InvalidDefaultVersion_FailsStartupNamingTheKey()
        {
            // Arrange
            async Task Start()
            {
                await using WebApplication app = await WebApiTestHost.StartAsync(
                    new Dictionary<string, string?>
                    {
                        ["Cloudstrap:WebApi:ApiVersioning:DefaultVersion"] = "abc",
                    });
            }

            // Act + Assert
            Assert.That(
                Start,
                Throws.Exception.With.Message.Contains("Cloudstrap:WebApi:ApiVersioning:DefaultVersion"));
        }

        [Test]
        public async Task AddCloudstrapWebApi_VersioningHook_RunsAfterCloudstrapDefaults()
        {
            // Arrange — the hook adds a reader Cloudstrap never configures
            await using WebApplication app = await WebApiTestHost.StartAsync(
                configure: configurator => configurator.ApiVersioning = options =>
                    options.ApiVersionReader = ApiVersionReader.Combine(
                        options.ApiVersionReader,
                        new HeaderApiVersionReader("x-api-version")));

            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/api/widgets", UriKind.Relative));
            request.Headers.Add("x-api-version", "2.0");

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Does.Contain("\"version\":\"2.0\""));
            });
        }
    }
}
