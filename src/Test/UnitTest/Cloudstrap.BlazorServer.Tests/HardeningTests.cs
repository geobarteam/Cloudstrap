namespace Cloudstrap.BlazorServer.Tests
{
    using System.Net;
    using Cloudstrap.BlazorServer.Tests.Infrastructure;
    using Microsoft.AspNetCore.Antiforgery;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-BS2 and AC-BS8's hardening half: every page response is hardened by default — three
    /// set-if-absent security headers with the D-12 frame-options switch, a hardened antiforgery cookie
    /// with the configurator's final say, HSTS outside <c>Development</c> from configuration, and the
    /// exception-handling ladder — and every hardening default has a proven override.
    /// </summary>
    [TestFixture]
    public sealed class HardeningTests
    {
        [Test]
        public async Task SecurityHeaders_OnAPageResponse_CarryAllThreeDefaults()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/static-page", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(Header(response, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
                Assert.That(Header(response, "Referrer-Policy"), Is.EqualTo("no-referrer"));
                Assert.That(Header(response, "X-Frame-Options"), Is.EqualTo("SAMEORIGIN"));
            });
        }

        [Test]
        public async Task SecurityHeaders_SetByTheApplication_AreNeverOverwritten()
        {
            // Arrange — an application that set its own value meant it
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                pipeline: hooks => hooks.ConfigureEndpoints = endpoints => endpoints.MapGet(
                    "/own-headers",
                    (HttpContext context) =>
                    {
                        context.Response.Headers["X-Frame-Options"] = "DENY";
                        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                        return "own-headers";
                    }));

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/own-headers", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(Header(response, "X-Frame-Options"), Is.EqualTo("DENY"));
                Assert.That(Header(response, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
            });
        }

        [Test]
        public async Task SecurityHeaders_WithEnableFrameOptionsFalse_OmitOnlyTheFrameOptionsHeader()
        {
            // Arrange — the D-12 switch: an embedded application, or one shipping frame-ancestors itself
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:BlazorServer:EnableFrameOptions"] = "false",
                });

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/static-page", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.Headers.Contains("X-Frame-Options"), Is.False);
                Assert.That(Header(response, "X-Content-Type-Options"), Is.EqualTo("nosniff"));
                Assert.That(Header(response, "Referrer-Policy"), Is.EqualTo("no-referrer"));
            });
        }

        [Test]
        public async Task Antiforgery_Defaults_AreHardened()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync();

            // Act
            AntiforgeryOptions antiforgery = app.Services
                .GetRequiredService<IOptions<AntiforgeryOptions>>()
                .Value;

            // Assert — D-2: the hardening delta over the framework defaults
            Assert.Multiple(() =>
            {
                Assert.That(antiforgery.Cookie.HttpOnly, Is.True);
                Assert.That(antiforgery.Cookie.SecurePolicy, Is.EqualTo(CookieSecurePolicy.Always));
                Assert.That(antiforgery.Cookie.SameSite, Is.EqualTo(SameSiteMode.Strict));
            });
        }

        [Test]
        public async Task Antiforgery_ConfiguratorHook_HasTheFinalSay()
        {
            // Arrange — the override ladder: hardened default → hook last
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                configure: configurator => configurator.Antiforgery = antiforgery =>
                    antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);

            // Act
            AntiforgeryOptions antiforgery = app.Services
                .GetRequiredService<IOptions<AntiforgeryOptions>>()
                .Value;

            // Assert — the hook's value wins; the untouched defaults stand
            Assert.Multiple(() =>
            {
                Assert.That(antiforgery.Cookie.SecurePolicy, Is.EqualTo(CookieSecurePolicy.SameAsRequest));
                Assert.That(antiforgery.Cookie.HttpOnly, Is.True);
            });
        }

        [Test]
        public async Task Antiforgery_Middleware_RejectsATokenlessFormPost()
        {
            // Arrange — a minimal-API form endpoint carries antiforgery metadata; the middleware sits
            // after auth, before endpoints
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                pipeline: hooks => hooks.ConfigureEndpoints = endpoints => endpoints.MapPost(
                    "/form",
                    ([FromForm] string name) => Results.Text(name)));

            // Act — over HTTPS: the hardened Secure-only cookie makes antiforgery an SSL-only affair
            using FormUrlEncodedContent tokenless = new([new KeyValuePair<string, string>("name", "contoso")]);
            using HttpResponseMessage rejected = await app.GetTestClient().PostAsync(
                new Uri("https://app.contoso.example/form"),
                tokenless,
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        [Test]
        public async Task Hsts_InProductionOverHttps_EmitsTheConfiguredHeader()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync();

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
            // Arrange — pinning a developer's localhost would be a nuisance cleared by hand (D-11)
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                environment: "Development");

            // Act
            string? header = await HstsHeader(app);

            // Assert
            Assert.That(header, Is.Null);
        }

        [Test]
        public async Task Hsts_WithEnabledFalse_EmitsNothing()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:BlazorServer:Hsts:Enabled"] = "false" });

            // Act
            string? header = await HstsHeader(app);

            // Assert
            Assert.That(header, Is.Null);
        }

        [Test]
        public async Task Hsts_WithConfiguredValues_ReflectsThem()
        {
            // Arrange — AC-BS8: every convention has an override
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:BlazorServer:Hsts:MaxAgeDays"] = "30",
                    ["Cloudstrap:BlazorServer:Hsts:IncludeSubDomains"] = "false",
                    ["Cloudstrap:BlazorServer:Hsts:Preload"] = "true",
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
        public async Task ExceptionHandling_OutsideDevelopment_ReExecutesTheConfiguredErrorPath()
        {
            // Arrange — the consumer supplies the error endpoint; Cloudstrap supplies the wiring
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                pipeline: hooks => hooks.ConfigureEndpoints = endpoints => endpoints.MapGet(
                    "/error",
                    () => "error-page-marker"));

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/throws", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(body, Does.Contain("error-page-marker"));
            });
        }

        [Test]
        public async Task ExceptionHandling_UseDeveloperExceptionPageFalseInDevelopment_KeepsTheHandler()
        {
            // Arrange — the bool? ladder: explicit false beats the Development environment default
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:BlazorServer:ExceptionHandling:UseDeveloperExceptionPage"] = "false",
                },
                pipeline: hooks => hooks.ConfigureEndpoints = endpoints => endpoints.MapGet(
                    "/error",
                    () => "error-page-marker"),
                environment: "Development");

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/throws", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
                Assert.That(body, Does.Contain("error-page-marker"));
            });
        }

        [Test]
        public void Options_InvalidHstsMaxAge_FailsStartupNamingTheKey()
        {
            // Arrange
            async Task Start()
            {
                await using WebApplication app = await BlazorServerTestHost.StartAsync(
                    new Dictionary<string, string?>
                    {
                        ["Cloudstrap:BlazorServer:Hsts:MaxAgeDays"] = "0",
                    });
            }

            // Act + Assert
            Assert.That(
                Start,
                Throws.Exception.With.Message.Contains("Cloudstrap:BlazorServer:Hsts:MaxAgeDays"));
        }

        [Test]
        public async Task Options_AbsentSection_AllDefaultsApply()
        {
            // Arrange — the section is optional: no Cloudstrap:BlazorServer entries at all
            await using WebApplication app = await BlazorServerTestHost.StartAsync();

            // Act
            CloudstrapBlazorServerOptions options = app.Services
                .GetRequiredService<IOptions<CloudstrapBlazorServerOptions>>()
                .Value;

            // Assert — startup validation passed (StartAsync did not throw) and every default stands
            Assert.Multiple(() =>
            {
                Assert.That(options.Hsts.Enabled, Is.True);
                Assert.That(options.Hsts.MaxAgeDays, Is.EqualTo(365));
                Assert.That(options.EnableFrameOptions, Is.True);
                Assert.That(options.ExceptionHandling.UseDeveloperExceptionPage, Is.Null);
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
                new Uri("https://app.contoso.example/static-page"),
                TestContext.CurrentContext.CancellationToken);

            return Header(response, "Strict-Transport-Security");
        }
    }
}
