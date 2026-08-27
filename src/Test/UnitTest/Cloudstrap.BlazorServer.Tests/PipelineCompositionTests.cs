namespace Cloudstrap.BlazorServer.Tests
{
    using System.Net;
    using System.Reflection;
    using Cloudstrap.Authentication.OpenIdConnect;
    using Cloudstrap.BlazorServer.TestComponents;
    using Cloudstrap.BlazorServer.Tests.Infrastructure;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Components.Server;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using Microsoft.IdentityModel.Protocols.OpenIdConnect;
    using NUnit.Framework;

    /// <summary>
    /// AC-BS3 and AC-BS8's composition half: the pipeline composes around whatever the consumer
    /// registered — authentication middleware exactly when a scheme exists (after routing, before
    /// antiforgery), the path base from configuration only, the four hooks in the documented order,
    /// overridable static-asset mapping and additional routable assemblies, and the D-7 escape-hatch
    /// seams on the component endpoints and the razor-components builder.
    /// </summary>
    [TestFixture]
    public sealed class PipelineCompositionTests
    {
        [Test]
        public async Task Use_WithoutAnyAuthenticationScheme_AddsNoAuthMiddlewareAndEverythingIsAnonymous()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync();

            // Act
            using HttpResponseMessage page = await app.GetTestClient().GetAsync(
                new Uri("/static-page", UriKind.Relative),
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
        public async Task Use_WithAConsumerCookieScheme_ChallengesThroughTheSchemeMapPredicate()
        {
            // Arrange — a scheme the consumer registered wires the middleware through the scheme-map
            // predicate, placed after routing so endpoint metadata is visible (D-3)
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                beforeBuild: builder => builder.Services
                    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(),
                pipeline: hooks => hooks.ConfigureEndpoints = endpoints => endpoints
                    .MapGet("/secure", () => "secret")
                    .RequireAuthorization());

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
            // documented hook, so the authority is never contacted: the intended pairing works with zero
            // package coupling
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:OpenIdConnect:Authority"] = "https://idp.example.com/",
                    ["Cloudstrap:OpenIdConnect:ClientId"] = "contoso-web",
                    ["Cloudstrap:OpenIdConnect:ClientSecret"] = "placeholder-not-a-real-secret",
                    ["Cloudstrap:OpenIdConnect:RequireAuthenticatedEndpoints"] = "false",
                },
                beforeBuild: builder => builder.Services.AddCloudstrapOpenIdConnect(configurator =>
                    configurator.OpenIdConnect = oidc => oidc.Configuration = SeededMetadata()),
                pipeline: hooks => hooks.ConfigureEndpoints = endpoints => endpoints
                    .MapGet("/secure", () => "secret")
                    .RequireAuthorization());

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
        public async Task Use_Hooks_RunInTheDocumentedOrder()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync(pipeline: hooks =>
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

            // Assert — all four hooks fired, in order
            Assert.That(body, Is.EqualTo("BeforeRouting,BeforeAuthorization,BeforeEndpoints"));
        }

        [Test]
        public async Task Use_WithConfiguredPathBase_ServesUnderItAndNeverWithout()
        {
            // Arrange — D-4: configuration only, no environment-variable sniffing
            await using WebApplication prefixed = await BlazorServerTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Application:PathBase"] = "contoso" });
            await using WebApplication bare = await BlazorServerTestHost.StartAsync();

            // Act
            using HttpResponseMessage underPrefix = await prefixed.GetTestClient().GetAsync(
                new Uri("/contoso/static-page", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage bareUnderPrefix = await bare.GetTestClient().GetAsync(
                new Uri("/contoso/static-page", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(underPrefix.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(bareUnderPrefix.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public async Task Use_AdditionalAssemblies_MakeASecondAssemblysPagesRoutable()
        {
            // Arrange — the framework's assembly-boundary semantics, exercised in both directions
            await using WebApplication without = await BlazorServerTestHost.StartAsync();
            await using WebApplication with = await BlazorServerTestHost.StartAsync(
                pipeline: hooks => hooks.AdditionalAssemblies.Add(typeof(ExtraPage).Assembly));

            // Act
            using HttpResponseMessage missing = await without.GetTestClient().GetAsync(
                new Uri("/extra", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage found = await with.GetTestClient().GetAsync(
                new Uri("/extra", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await found.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(found.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Does.Contain("fixture-extra-page"));
            });
        }

        [Test]
        public async Task Use_ConfigureComponentEndpoints_RunsLastOnTheConventionBuilder()
        {
            // Arrange — the D-7 escape-hatch seam: a consumer-referenced WASM render mode would attach
            // here; the fixture attaches recognizable endpoint metadata instead
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                pipeline: hooks => hooks.ConfigureComponentEndpoints = components =>
                    components.Add(endpoint => endpoint.Metadata.Add(new ComponentEndpointMarker())));

            // Act
            bool markerPresent = app.Services.GetRequiredService<EndpointDataSource>()
                .Endpoints
                .Any(endpoint => endpoint.Metadata.GetMetadata<ComponentEndpointMarker>() is not null);

            // Assert
            Assert.That(markerPresent, Is.True);
        }

        [Test]
        public async Task Add_RazorComponentsHook_HasTheFinalSay()
        {
            // Arrange — the hook observably runs against the same razor-components builder
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                configure: configurator => configurator.RazorComponents = components =>
                    components.AddInteractiveServerComponents(circuits =>
                        circuits.DisconnectedCircuitMaxRetained = 13));

            // Act
            CircuitOptions circuits = app.Services
                .GetRequiredService<IOptions<CircuitOptions>>()
                .Value;

            // Assert
            Assert.That(circuits.DisconnectedCircuitMaxRetained, Is.EqualTo(13));
        }

        [Test]
        public async Task Use_MapStaticAssetsFalse_MapsNoStaticAssetEndpoints()
        {
            // Arrange — the default-on flag's override; default-on itself is exercised by the demo app
            // and its E2E tests, where a built asset manifest exists (mechanic (d))
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                pipeline: hooks => hooks.MapStaticAssets = false);

            // Act — asset endpoints carry the asset's file route as their display name; component routes
            // carry the component type name, so the fixture pages cannot collide with this probe
            string[] assetEndpoints =
            [
                .. app.Services.GetRequiredService<EndpointDataSource>().Endpoints
                    .Select(endpoint => endpoint.DisplayName)
                    .OfType<string>()
                    .Where(name => name.Contains(".css", StringComparison.OrdinalIgnoreCase)
                        || name.Contains(".js", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert
            Assert.That(assetEndpoints, Is.Empty);
        }

        [Test]
        public void BlazorServerAssembly_DeclaresNoCorrelationOrForwardedHeadersOrCorsWiring()
        {
            // Arrange
            Assembly package = typeof(BlazorServerPipelineOptions).Assembly;

            // Act
            string[] correlationOffenders =
            [
                .. package.GetTypes()
                    .Select(type => type.Name)
                    .Where(name => name.Contains("Correlation", StringComparison.OrdinalIgnoreCase)),
            ];
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            int before = builder.Services.Count(IsCorsOrForwardedHeaders);
            builder.AddCloudstrapBlazorServer();
            int after = builder.Services.Count(IsCorsOrForwardedHeaders);

            // Assert — correlation is deliberately consumed from Cloudstrap.Observability, never rebuilt;
            // CORS and forwarded headers are deliberately absent (D-5)
            Assert.Multiple(() =>
            {
                Assert.That(
                    correlationOffenders,
                    Is.Empty,
                    $"Correlation types: {string.Join(", ", correlationOffenders)}");
                Assert.That(after, Is.EqualTo(before));
            });
        }

        private static bool IsCorsOrForwardedHeaders(ServiceDescriptor descriptor)
        {
            string? name = descriptor.ServiceType.FullName;

            return name is not null
                && (name.Contains("Cors", StringComparison.Ordinal)
                    || name.Contains("ForwardedHeaders", StringComparison.Ordinal));
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

        /// <summary>
        /// Recognizable endpoint metadata the component-endpoints hook attaches, so its execution is
        /// observable on the built endpoints.
        /// </summary>
        private sealed class ComponentEndpointMarker;
    }
}
