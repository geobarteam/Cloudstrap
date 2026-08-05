namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using System.Reflection;
    using System.Text.Json;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability;
    using Cloudstrap.Observability.Correlation;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using NUnit.Framework;

    /// <summary>
    /// AC-W11 and AC-W12: the one pipeline call composes the shipped Cloudstrap seams — probes from
    /// deliverable #4 and correlation from #2 — around the configured path base and the consumer's own
    /// middleware at four documented hook points.
    /// </summary>
    [TestFixture]
    public sealed class PipelineCompositionTests
    {
        [Test]
        public async Task Use_ServesLivenessAndReadinessProbes()
        {
            // Arrange — a failing readiness check must stay invisible to liveness
            await using WebApplication app = await WebApiTestHost.StartAsync(
                beforeBuild: AddOneLiveOneFailingReadyCheck);
            HttpClient client = app.GetTestClient();

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
            await using WebApplication app = await WebApiTestHost.StartAsync(
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
            await using WebApplication app = await WebApiTestHost.StartAsync();
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/api/correlation", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", "contoso-0f3c");

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    JsonDocument.Parse(body).RootElement.GetProperty("correlationId").GetString(),
                    Is.EqualTo("contoso-0f3c"));
            });
        }

        [Test]
        public async Task Use_OnCorrelationRequiredEndpointWithoutHeader_Returns400ProblemDetails()
        {
            // Arrange — endpoint metadata is only visible after routing, which is where the middleware sits
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/api/correlation/required", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
                Assert.That(body, Does.Contain("X-Correlation-ID"));
            });
        }

        [Test]
        public void WebApiAssembly_DeclaresNoCorrelationMiddlewareOfItsOwn()
        {
            // Arrange
            Assembly webApi = typeof(WebApiOptions).Assembly;

            // Act
            string[] offenders =
            [
                .. webApi.GetTypes()
                    .Select(type => type.Name)
                    .Where(name => name.Contains("Correlation", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert — correlation is deliberately consumed from Cloudstrap.Observability, never rebuilt
            Assert.That(offenders, Is.Empty, $"Correlation types: {string.Join(", ", offenders)}");
        }

        [Test]
        public async Task Use_WithConfiguredPathBase_ServesUnderItAndGeneratesPrefixedLinks()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Application:PathBase"] = "myapi" });
            HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage prefixed = await client.GetAsync(
                new Uri("/myapi/api/v1/widgets", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string link = await client.GetStringAsync(
                new Uri("/myapi/api/link", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert — the prefix is stripped for routing and re-applied to every generated link
            Assert.Multiple(() =>
            {
                Assert.That(prefixed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    JsonDocument.Parse(link).RootElement.GetProperty("path").GetString(),
                    Is.EqualTo("/myapi/api/link/target"));
            });
        }

        [Test]
        public async Task Use_WithNoPathBaseConfigured_PrefixesNothing()
        {
            // Arrange — no path-base magic: nothing is applied unless it is configured
            await using WebApplication app = await WebApiTestHost.StartAsync();
            HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage bare = await client.GetAsync(
                new Uri("/api/v1/widgets", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage prefixed = await client.GetAsync(
                new Uri("/myapi/api/v1/widgets", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(bare.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(prefixed.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public async Task Use_Hooks_RunInTheDocumentedOrder()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(pipeline: hooks =>
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
        public async Task Use_EstablishesCorrelationBeforeTheAuthorizationSlot()
        {
            // Arrange — every request that reaches routing carries an ambient identifier, including the ones
            // authorization is about to reject
            await using WebApplication app = await WebApiTestHost.StartAsync(pipeline: hooks =>
            {
                hooks.BeforeAuthorization = branch => branch.Use(async (context, next) =>
                {
                    ICorrelationContextAccessor accessor = context.RequestServices
                        .GetRequiredService<ICorrelationContextAccessor>();
                    TraceOf(context).Add(accessor.CorrelationId ?? "none");
                    await next(context);
                });

                hooks.ConfigureEndpoints = endpoints => endpoints.MapGet(
                    "/correlation-slot",
                    (HttpContext context) => string.Join(",", TraceOf(context)));
            });

            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri("/correlation-slot", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", "contoso-77a1");

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .SendAsync(request, TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(body, Is.EqualTo("contoso-77a1"));
        }

        [Test]
        public async Task Use_WithMapControllersFalse_MapsNoControllersButKeepsProbesAndHooks()
        {
            // Arrange — the minimal-API-only host
            await using WebApplication app = await WebApiTestHost.StartAsync(pipeline: hooks =>
            {
                hooks.MapControllers = false;
                hooks.ConfigureEndpoints = endpoints => endpoints.MapGet("/minimal", () => "minimal");
            });
            HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage controller = await client.GetAsync(
                new Uri("/api/v1/widgets", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage probe = await client.GetAsync(
                new Uri("/healthz", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage minimal = await client.GetAsync(
                new Uri("/minimal", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(controller.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(probe.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(minimal.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public async Task Use_CalledTwice_ThrowsInvalidOperationException()
        {
            // Arrange — service registrations repeat safely; a request pipeline is built once
            await using WebApplication app = WebApiTestHost.Build();
            app.UseCloudstrapWebApi();

            // Act + Assert
            Assert.That(
                () => app.UseCloudstrapWebApi(),
                Throws.InvalidOperationException.With.Message.Contains(nameof(WebApplicationExtensions.UseCloudstrapWebApi)));
        }

        [Test]
        public async Task Use_ComposesAroundStaticFilesAndASpaFallback()
        {
            // Arrange — the shape the Blazor SUT host uses: static files before routing, SPA fallback after
            await using WebApplication app = await WebApiTestHost.StartAsync(
                beforeBuild: AddOneLiveOneFailingReadyCheck,
                pipeline: hooks =>
                {
                    hooks.BeforeRouting = branch => branch.Use(async (context, next) =>
                    {
                        if (context.Request.Path.Equals("/app.css", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Response.ContentType = "text/css";
                            await context.Response.WriteAsync("body{}");
                            return;
                        }

                        await next(context);
                    });

                    hooks.ConfigureEndpoints = endpoints => endpoints.MapFallback(() => "index-stub");
                });
            HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage api = await client.GetAsync(
                new Uri("/api/v1/widgets", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage probe = await client.GetAsync(
                new Uri("/healthz", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage stylesheet = await client.GetAsync(
                new Uri("/app.css", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string fallback = await client.GetStringAsync(
                new Uri("/some/spa/route", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert — the composite swallows neither the static-file branch nor the SPA fallback
            Assert.Multiple(() =>
            {
                Assert.That(api.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(probe.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(stylesheet.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/css"));
                Assert.That(fallback, Is.EqualTo("index-stub"));
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
