namespace Cloudstrap.BlazorServer.Tests
{
    using System.Net;
    using Cloudstrap.BlazorServer.Tests.Fixtures;
    using Cloudstrap.BlazorServer.Tests.Infrastructure;
    using Cloudstrap.Observability;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using NUnit.Framework;

    /// <summary>
    /// AC-BS1 and AC-BS7: two calls give a Blazor Server application routable components with Interactive
    /// Server rendering, anonymous probes and correlated responses; the interactivity decision is made once
    /// at registration time and honored by the pipeline; a second <c>Use</c> call and a missing <c>Add</c>
    /// call fail loud.
    /// </summary>
    [TestFixture]
    public sealed class CompositeBootTests
    {
        [Test]
        public void AddCloudstrapBlazorServer_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            WebApplicationBuilder builder = null!;

            // Act + Assert
            Assert.That(() => builder.AddCloudstrapBlazorServer(), Throws.ArgumentNullException);
        }

        [Test]
        public void Use_OnNullApp_ThrowsArgumentNullException()
        {
            // Arrange
            WebApplication app = null!;

            // Act + Assert
            Assert.That(() => app.UseCloudstrapBlazorServer<App>(), Throws.ArgumentNullException);
        }

        [Test]
        public async Task Composite_ServesARoutableComponent()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/static-page", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Does.Contain("fixture-static-page"));
            });
        }

        [Test]
        public async Task Composite_DefaultInteractivity_PrerendersTheInteractiveServerMarker()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/interactive", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert — the framework's server-marker comment proves Interactive Server rendering is wired
            // end to end: AddInteractiveServerComponents at registration, AddInteractiveServerRenderMode on
            // the endpoints. It is emitted on the initial HTTP response — no WebSocket needed on TestServer.
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Does.Contain("<!--Blazor:"));
            });
        }

        [Test]
        public async Task Composite_ServesAnonymousProbes()
        {
            // Arrange — a failing readiness check must stay invisible to liveness
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                beforeBuild: builder => builder.Services.AddHealthChecks()
                    .AddCheck(
                        "catalog-live",
                        () => HealthCheckResult.Healthy(),
                        tags: [CloudstrapHealthCheckTags.Liveness])
                    .AddCheck(
                        "catalog-ready",
                        () => HealthCheckResult.Unhealthy("dependency unavailable"),
                        tags: [CloudstrapHealthCheckTags.Readiness]));
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
        public async Task Composite_EchoesACorrelationIdOnEveryResponse()
        {
            // Arrange
            await using WebApplication app = await BlazorServerTestHost.StartAsync();
            using HttpClient client = app.GetTestClient();

            // Act — one request without a correlation header, one carrying its own
            using HttpResponseMessage fresh = await client.GetAsync(
                new Uri("/static-page", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri("/static-page", UriKind.Relative));
            request.Headers.Add("X-Correlation-ID", "contoso-0f3c");
            using HttpResponseMessage echoed = await client.SendAsync(
                request,
                TestContext.CurrentContext.CancellationToken);

            // Assert — the #2 middleware is active after routing: a fresh identifier is generated and
            // echoed, and an inbound identifier flows back unchanged
            Assert.Multiple(() =>
            {
                Assert.That(fresh.Headers.GetValues("X-Correlation-ID").Single(), Is.Not.Empty);
                Assert.That(
                    echoed.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.EqualTo("contoso-0f3c"));
            });
        }

        [Test]
        public async Task Composite_StaticServerInteractivity_WiresNothingInteractive()
        {
            // Arrange — the decision is made once, at registration time; there is no Use-side knob
            WebApplication registrations = BlazorServerTestHost.Build(
                beforeBuild: builder => builder.Services.Insert(0, ServiceDescriptor.Singleton(
                    new ServiceCollectionProbe(builder.Services))),
                configure: configurator =>
                    configurator.Interactivity = BlazorInteractivity.StaticServer);
            await using WebApplication app = await BlazorServerTestHost.StartAsync(
                configure: configurator =>
                    configurator.Interactivity = BlazorInteractivity.StaticServer);
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage staticPage = await client.GetAsync(
                new Uri("/static-page", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            HttpStatusCode? interactiveStatus = null;
            Exception? interactiveFailure = null;
            try
            {
                using HttpResponseMessage interactive = await client.GetAsync(
                    new Uri("/interactive", UriKind.Relative),
                    TestContext.CurrentContext.CancellationToken);
                interactiveStatus = interactive.StatusCode;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                interactiveFailure = ex;
            }

            bool circuitServicesRegistered = registrations.Services
                .GetRequiredService<ServiceCollectionProbe>()
                .Services
                .Any(descriptor => descriptor.ServiceType.FullName?.Contains(
                    "Circuit", StringComparison.Ordinal) == true);
            await registrations.DisposeAsync();

            // Assert — the rest of the pipeline is unchanged; the interactive page fails with the
            // framework's own error (the documented edge case — no package detection); and nothing from
            // AddInteractiveServerComponents is registered
            Assert.Multiple(() =>
            {
                Assert.That(staticPage.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    interactiveFailure is not null || (int?)interactiveStatus >= 500,
                    Is.True,
                    $"Expected the framework's own failure; got {interactiveStatus}.");
                Assert.That(circuitServicesRegistered, Is.False);
            });
        }

        [Test]
        public async Task Composite_DefaultInteractivity_RegistersCircuitServices()
        {
            // Arrange — the counterpart proving the probe used above actually observes the interactive
            // wiring in the default case
            await using WebApplication registrations = BlazorServerTestHost.Build(
                beforeBuild: builder => builder.Services.Insert(0, ServiceDescriptor.Singleton(
                    new ServiceCollectionProbe(builder.Services))));

            // Act
            bool circuitServicesRegistered = registrations.Services
                .GetRequiredService<ServiceCollectionProbe>()
                .Services
                .Any(descriptor => descriptor.ServiceType.FullName?.Contains(
                    "Circuit", StringComparison.Ordinal) == true);

            // Assert
            Assert.That(circuitServicesRegistered, Is.True);
        }

        [Test]
        public async Task Use_CalledTwice_ThrowsInvalidOperationException()
        {
            // Arrange — service registrations repeat safely; a request pipeline is built once. The fixture
            // assembly carries no static-asset manifest, so the first call opts out (mechanic (d)).
            await using WebApplication app = BlazorServerTestHost.Build();
            app.UseCloudstrapBlazorServer<App>(options => options.MapStaticAssets = false);

            // Act + Assert
            Assert.That(
                () => app.UseCloudstrapBlazorServer<App>(),
                Throws.InvalidOperationException.With.Message
                    .Contains(nameof(WebApplicationExtensions.UseCloudstrapBlazorServer)));
        }

        [Test]
        public async Task Use_WithoutAdd_ThrowsInvalidOperationExceptionNamingTheAddCall()
        {
            // Arrange — a bare application that never called the Add half
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            await using WebApplication app = builder.Build();

            // Act + Assert
            Assert.That(
                () => app.UseCloudstrapBlazorServer<App>(),
                Throws.InvalidOperationException.With.Message
                    .Contains(nameof(WebApplicationBuilderExtensions.AddCloudstrapBlazorServer)));
        }

        /// <summary>
        /// Captures the service collection at registration time, so a test can assert over the registered
        /// descriptors after the application is built.
        /// </summary>
        private sealed class ServiceCollectionProbe(IServiceCollection services)
        {
            public IServiceCollection Services { get; } = services;
        }
    }
}
