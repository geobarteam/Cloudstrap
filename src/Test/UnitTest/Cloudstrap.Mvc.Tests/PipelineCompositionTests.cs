namespace Cloudstrap.Mvc.Tests
{
    using System.Net;
    using System.Reflection;
    using Cloudstrap.Authentication.OpenIdConnect;
    using Cloudstrap.Extensions;
    using Cloudstrap.Mvc.Tests.Infrastructure;
    using Cloudstrap.Observability;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Options;
    using Microsoft.IdentityModel.Protocols.OpenIdConnect;
    using NUnit.Framework;

    /// <summary>
    /// AC-MVC1 (probes), AC-MVC7 and AC-MVC10: the one pipeline call composes the shipped Cloudstrap
    /// seams — probes from deliverable #4 and correlation from #2 — around the configured path base,
    /// antiforgery, the consumer's own middleware at four documented hook points, and authentication
    /// middleware that appears exactly when a scheme is registered.
    /// </summary>
    [TestFixture]
    public sealed class PipelineCompositionTests
    {
        [Test]
        public async Task Use_ServesLivenessAndReadinessProbes()
        {
            // Arrange — a failing readiness check must stay invisible to liveness
            await using WebApplication app = await MvcTestHost.StartAsync(
                beforeBuild: AddOneLiveOneFailingReadyCheck);
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage liveness = await client.GetAsync(
                new Uri("/healthz", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage readiness = await client.GetAsync(
                new Uri("/ready", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(liveness.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(readiness.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            });
        }

        [Test]
        public async Task Use_WithExplicitMapCloudstrapHealthChecksCall_DoesNotDuplicateEndpoints()
        {
            // Arrange — deliverable #4's marker-based idempotence, exercised from inside this pipeline
            await using WebApplication app = await MvcTestHost.StartAsync(
                beforeBuild: AddOneLiveOneFailingReadyCheck,
                afterUse: started => started.MapCloudstrapHealthChecks());

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/healthz", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert — an ambiguous match would surface as a 500 here
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task Use_FlowsInboundCorrelationId()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri("/correlation", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", "contoso-0f3c");

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Is.EqualTo("contoso-0f3c"));
            });
        }

        [Test]
        public async Task Use_GeneratesAndEchoesACorrelationIdWhenAbsent()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/correlation", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert — a fresh identifier exists and is echoed per the EchoInResponse default
            Assert.Multiple(() =>
            {
                Assert.That(body, Is.Not.Empty);
                Assert.That(
                    response.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.EqualTo(body));
            });
        }

        [Test]
        public async Task Use_OnCorrelationRequiredEndpointWithoutHeader_Returns400ProblemDetails()
        {
            // Arrange — endpoint metadata is only visible after routing, which is where the middleware sits
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/correlation/required", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(
                    response.Content.Headers.ContentType?.MediaType,
                    Is.EqualTo("application/problem+json"));
                Assert.That(body, Does.Contain("X-Correlation-ID"));
            });
        }

        [Test]
        public void MvcAssembly_DeclaresNoCorrelationMiddlewareOfItsOwn()
        {
            // Arrange
            Assembly mvc = typeof(MvcPipelineOptions).Assembly;

            // Act
            string[] offenders =
            [
                .. mvc.GetTypes()
                    .Select(type => type.Name)
                    .Where(name => name.Contains("Correlation", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert — correlation is deliberately consumed from Cloudstrap.Observability, never rebuilt
            Assert.That(offenders, Is.Empty, $"Correlation types: {string.Join(", ", offenders)}");
        }

        [Test]
        public async Task Use_WithConfiguredPathBase_ServesUnderIt()
        {
            // Arrange
            await using WebApplication prefixed = await MvcTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Application:PathBase"] = "contoso" });
            await using WebApplication bare = await MvcTestHost.StartAsync();
            using HttpClient prefixedClient = prefixed.GetTestClient();
            using HttpClient bareClient = bare.GetTestClient();

            // Act
            using HttpResponseMessage underPrefix = await prefixedClient.GetAsync(
                new Uri("/contoso/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string link = await prefixedClient.GetStringAsync(
                new Uri("/contoso/home/link", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage bareUnderPrefix = await bareClient.GetAsync(
                new Uri("/contoso/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert — the prefix is stripped for routing, re-applied to generated links, and never
            // applied unless configured (no env-var/workload magic)
            Assert.Multiple(() =>
            {
                Assert.That(underPrefix.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(link, Does.StartWith("/contoso"));
                Assert.That(bareUnderPrefix.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public async Task Use_Hooks_RunInTheDocumentedOrder()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(pipeline: hooks =>
            {
                hooks.BeforeRouting = branch => branch.Use(Trace("BeforeRouting"));
                hooks.BeforeAuthorization = branch => branch.Use(Trace("BeforeAuthorization"));
                hooks.BeforeEndpoints = branch => branch.Use(Trace("BeforeEndpoints"));
                hooks.ConfigureEndpoints = endpoints => endpoints.MapGet(
                    "/hooks",
                    (HttpContext context) => string.Join(",", TraceOf(context)));
            });

            // Act
            string body = await app.GetTestClient().GetStringAsync(
                new Uri("/hooks", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(body, Is.EqualTo("BeforeRouting,BeforeAuthorization,BeforeEndpoints"));
        }

        [Test]
        public async Task Use_CalledTwice_ThrowsInvalidOperationException()
        {
            // Arrange — service registrations repeat safely; a request pipeline is built once
            await using WebApplication app = MvcTestHost.Build();
            app.UseCloudstrapMvc();

            // Act + Assert
            Assert.That(
                () => app.UseCloudstrapMvc(),
                Throws.InvalidOperationException.With.Message
                    .Contains(nameof(WebApplicationExtensions.UseCloudstrapMvc)));
        }

        [Test]
        public async Task Use_WithoutAnyAuthenticationScheme_AddsNoAuthMiddlewareAndEverythingIsAnonymous()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage page = await app.GetTestClient().GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            AuthenticationOptions authentication = app.Services
                .GetRequiredService<IOptions<AuthenticationOptions>>()
                .Value;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(authentication.SchemeMap, Is.Empty);
            });
        }

        [Test]
        public async Task Use_WithAConsumerCookieScheme_ChallengesAfterRouting()
        {
            // Arrange — a scheme the consumer registered wires the middleware through the scheme-map
            // predicate, placed after routing so endpoint metadata is visible
            await using WebApplication app = await MvcTestHost.StartAsync(
                beforeBuild: builder => builder.Services
                    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie());

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/secure", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(response.Headers.Location?.ToString(), Does.Contain("/Account/Login"));
            });
        }

        [Test]
        public async Task Use_WithCloudstrapOpenIdConnectRegistered_ChallengeRedirectsToTheSeededAuthority()
        {
            // Arrange — the real AddCloudstrapOpenIdConnect with metadata pre-seeded through its
            // documented hook, so the authority is never contacted
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:OpenIdConnect:Authority"] = "https://idp.example.com/",
                    ["Cloudstrap:OpenIdConnect:ClientId"] = "contoso-web",
                    ["Cloudstrap:OpenIdConnect:ClientSecret"] = "placeholder-not-a-real-secret",
                    ["Cloudstrap:OpenIdConnect:RequireAuthenticatedEndpoints"] = "false",
                },
                beforeBuild: builder => builder.Services.AddCloudstrapOpenIdConnect(configurator =>
                    configurator.OpenIdConnect = oidc => oidc.Configuration = SeededMetadata()));

            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/secure", UriKind.Relative));
            request.Headers.Add("Accept", "text/html");

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(
                    response.Headers.Location?.ToString(),
                    Does.StartWith("https://idp.example.com/connect/authorize"));
            });
        }

        [Test]
        public async Task Use_AntiforgeryMiddleware_RejectsATokenlessFormPost()
        {
            // Arrange — a minimal-API form endpoint carries antiforgery metadata; MVC actions validate
            // through filters and stay unaffected by the middleware
            await using WebApplication app = await MvcTestHost.StartAsync(pipeline: hooks =>
                hooks.ConfigureEndpoints = endpoints => endpoints.MapPost(
                    "/form",
                    ([FromForm] string name) => Results.Text(name)));
            using HttpClient client = app.GetTestClient();

            // Act
            using FormUrlEncodedContent tokenless = new([new KeyValuePair<string, string>("name", "contoso")]);
            using HttpResponseMessage rejected = await client.PostAsync(
                new Uri("/form", UriKind.Relative),
                tokenless,
                TestContext.CurrentContext.CancellationToken);
            using FormUrlEncodedContent mvcForm = new([new KeyValuePair<string, string>("name", "contoso")]);
            using HttpResponseMessage mvcAction = await client.PostAsync(
                new Uri("/widgets/create", UriKind.Relative),
                mvcForm,
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(mvcAction.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public async Task Use_WithMapDefaultControllerRouteFalseAndNoEndpointsHook_StillServesStaticFilesAndProbes()
        {
            // Arrange — the consumer's explicit choice, not an error: static files and probes only
            await using WebApplication app = await MvcTestHost.StartAsync(
                pipeline: hooks => hooks.MapDefaultControllerRoute = false);
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage stylesheet = await client.GetAsync(
                new Uri("/site.css", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage probe = await client.GetAsync(
                new Uri("/healthz", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage page = await client.GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(stylesheet.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(probe.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        private static void AddOneLiveOneFailingReadyCheck(WebApplicationBuilder builder)
        {
            builder.Services.AddHealthChecks()
                .AddCheck(
                    "catalog-live",
                    () => HealthCheckResult.Healthy(),
                    tags: [CloudstrapHealthCheckTags.Liveness])
                .AddCheck(
                    "catalog-ready",
                    () => HealthCheckResult.Unhealthy("dependency unavailable"),
                    tags: [CloudstrapHealthCheckTags.Readiness]);
        }

        private static OpenIdConnectConfiguration SeededMetadata() => new()
        {
            Issuer = "https://idp.example.com/",
            AuthorizationEndpoint = "https://idp.example.com/connect/authorize",
            TokenEndpoint = "https://idp.example.com/connect/token",
        };

        private static Func<HttpContext, RequestDelegate, Task> Trace(string name)
        {
            return async (context, next) =>
            {
                TraceOf(context).Add(name);
                await next(context);
            };
        }

        private static List<string> TraceOf(HttpContext context)
        {
            if (context.Items.TryGetValue("trace", out object? existing) && existing is List<string> trace)
            {
                return trace;
            }

            List<string> created = [];
            context.Items["trace"] = created;

            return created;
        }
    }
}
