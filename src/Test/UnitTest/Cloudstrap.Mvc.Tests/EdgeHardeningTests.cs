namespace Cloudstrap.Mvc.Tests
{
    using Cloudstrap.Mvc.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using NUnit.Framework;

    /// <summary>
    /// AC-MVC8 and AC-MVC9: every response — page and probe alike — carries the two constant security
    /// headers without overwriting app-set values, HSTS is emitted exactly where it belongs and never
    /// claims preload unasked, and CORS stays genuinely absent until origins are configured.
    /// </summary>
    [TestFixture]
    public sealed class EdgeHardeningTests
    {
        [Test]
        public async Task EveryResponse_CarriesNosniffAndNoReferrer()
        {
            // Arrange — the middleware sits before routing, so short-circuited probes are covered too
            await using WebApplication app = await MvcTestHost.StartAsync();
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage page = await client.GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage probe = await client.GetAsync(
                new Uri("/healthz", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(Header(page, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
                Assert.That(Header(page, "Referrer-Policy"), Is.EqualTo("no-referrer"));
                Assert.That(Header(probe, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
                Assert.That(Header(probe, "Referrer-Policy"), Is.EqualTo("no-referrer"));
            });
        }

        [Test]
        public async Task SecurityHeaders_DoNotOverwriteAValueTheAppAlreadySet()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/headers", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(Header(response, "Referrer-Policy"), Is.EqualTo("same-origin"));
                Assert.That(Header(response, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
            });
        }

        [Test]
        public async Task Hsts_InProductionOverHttps_EmitsStrictTransportSecurityWithoutPreload()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            string? header = await HstsHeader(app);

            // Assert — a browser-preload-list commitment belongs to the domain owner, not to a library
            Assert.Multiple(() =>
            {
                Assert.That(header, Does.Contain("max-age=31536000"));
                Assert.That(header, Does.Contain("includeSubDomains"));
                Assert.That(header, Does.Not.Contain("preload"));
            });
        }

        [Test]
        public async Task Hsts_InDevelopment_EmitsNothing()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(environment: "Development");

            // Act
            string? header = await HstsHeader(app);

            // Assert
            Assert.That(header, Is.Null);
        }

        [Test]
        public async Task Hsts_WithEnabledFalse_EmitsNothing()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Mvc:Hsts:Enabled"] = "false" });

            // Act
            string? header = await HstsHeader(app);

            // Assert
            Assert.That(header, Is.Null);
        }

        [Test]
        public async Task Hsts_WithConfiguredMaxAgeAndPreload_ReflectsThem()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:Hsts:MaxAgeDays"] = "30",
                    ["Cloudstrap:Mvc:Hsts:IncludeSubDomains"] = "false",
                    ["Cloudstrap:Mvc:Hsts:Preload"] = "true",
                });

            // Act
            string? header = await HstsHeader(app);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(header, Does.Contain("max-age=2592000"));
                Assert.That(header, Does.Contain("preload"));
                Assert.That(header, Does.Not.Contain("includeSubDomains"));
            });
        }

        [Test]
        public void Hsts_WithMaxAgeDaysZero_FailsStartupNamingTheKey()
        {
            // Arrange
            async Task Start()
            {
                await using WebApplication app = await MvcTestHost.StartAsync(
                    new Dictionary<string, string?> { ["Cloudstrap:Mvc:Hsts:MaxAgeDays"] = "0" });
            }

            // Act + Assert
            Assert.That(Start, Throws.Exception.With.Message.Contains("Cloudstrap:Mvc:Hsts:MaxAgeDays"));
        }

        [Test]
        public async Task Cors_WithNoOriginsConfigured_NeverEmitsAccessControlAllowOrigin()
        {
            // Arrange — no origins means no policy at all: browsers keep their same-origin default
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage preflight = await Preflight(app, "https://app.contoso.example");

            // Assert
            Assert.That(preflight.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
        }

        [Test]
        public async Task Cors_WithConfiguredOrigin_PreflightSucceedsForThatOriginOnly()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:Cors:AllowedOrigins:0"] = "https://app.contoso.example",
                });

            // Act
            using HttpResponseMessage allowed = await Preflight(app, "https://app.contoso.example");
            using HttpResponseMessage rejected = await Preflight(app, "https://app.fabrikam.example");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    Header(allowed, "Access-Control-Allow-Origin"),
                    Is.EqualTo("https://app.contoso.example"));
                Assert.That(Header(allowed, "Access-Control-Allow-Credentials"), Is.EqualTo("true"));
                Assert.That(rejected.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
            });
        }

        [Test]
        public async Task Cors_WithWildcardSubdomainOrigin_AllowsMatchingSubdomains()
        {
            // Arrange — the kept source capability
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:Cors:AllowedOrigins:0"] = "https://*.contoso.example",
                });

            // Act
            using HttpResponseMessage allowed = await Preflight(app, "https://app.contoso.example");
            using HttpResponseMessage rejected = await Preflight(app, "https://app.fabrikam.example");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    Header(allowed, "Access-Control-Allow-Origin"),
                    Is.EqualTo("https://app.contoso.example"));
                Assert.That(rejected.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
            });
        }

        private static string? Header(HttpResponseMessage response, string name)
        {
            return response.Headers.TryGetValues(name, out IEnumerable<string>? values)
                ? string.Join(", ", values)
                : null;
        }

        private static async Task<string?> HstsHeader(WebApplication app)
        {
            // HSTS is only emitted over HTTPS, and the framework's stock ExcludedHosts — localhost,
            // 127.0.0.1 and [::1] — is deliberately kept, so the probe uses a routable-looking host.
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("https://app.contoso.example/"),
                TestContext.CurrentContext.CancellationToken);

            return Header(response, "Strict-Transport-Security");
        }

        private static async Task<HttpResponseMessage> Preflight(WebApplication app, string origin)
        {
            using HttpRequestMessage request = new(
                HttpMethod.Options,
                new Uri("/", UriKind.Relative));
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "GET");

            return await app.GetTestClient().SendAsync(request, TestContext.CurrentContext.CancellationToken);
        }
    }
}
