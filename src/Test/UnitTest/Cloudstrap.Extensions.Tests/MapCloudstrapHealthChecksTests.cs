namespace Cloudstrap.Extensions.Tests
{
    using System.Net;
    using Cloudstrap.Observability;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using NUnit.Framework;

    /// <summary>
    /// AC-E11: one call serves both standard probes, filtered by the shared tag vocabulary, over real HTTP.
    /// </summary>
    [TestFixture]
    public sealed class MapCloudstrapHealthChecksTests
    {
        [Test]
        public async Task Map_WithDefaults_ServesLiveTaggedChecksOnHealthz()
        {
            // Arrange — a failing readiness check must stay invisible to liveness
            await using WebApplication app = BuildApp(configureChecks: AddOneLiveOneFailingReadyCheck);
            app.MapCloudstrapHealthChecks();
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task Map_WithDefaults_ServesReadyTaggedChecksOnReady()
        {
            // Arrange — the same fixture: readiness sees the failing check liveness ignored
            await using WebApplication app = BuildApp(configureChecks: AddOneLiveOneFailingReadyCheck);
            app.MapCloudstrapHealthChecks();
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);

            // Act
            using HttpResponseMessage response = await app.GetTestClient()
                .GetAsync(new Uri("/ready", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        }

        [Test]
        public async Task Map_WithConfiguredPaths_ServesThem()
        {
            // Arrange
            await using WebApplication app = BuildApp(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:HealthChecks:LivenessPath"] = "/alive",
                    ["Cloudstrap:HealthChecks:ReadinessPath"] = "/prepared",
                },
                AddOneLiveOneFailingReadyCheck);
            app.MapCloudstrapHealthChecks();
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);
            HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage configured = await client
                .GetAsync(new Uri("/alive", UriKind.Relative), TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage defaultPath = await client
                .GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(configured.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(defaultPath.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public async Task Map_WithEnabledFalse_MapsNothing()
        {
            // Arrange
            await using WebApplication app = BuildApp(
                new Dictionary<string, string?> { ["Cloudstrap:HealthChecks:Enabled"] = "false" },
                AddOneLiveOneFailingReadyCheck);

            // Act — the call itself stays safe; it simply maps nothing
            app.MapCloudstrapHealthChecks();
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);
            HttpClient client = app.GetTestClient();
            using HttpResponseMessage liveness = await client
                .GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage readiness = await client
                .GetAsync(new Uri("/ready", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(liveness.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(readiness.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public async Task Map_CalledTwice_ServesOneSetOfEndpoints()
        {
            // Arrange
            await using WebApplication app = BuildApp(configureChecks: AddOneLiveOneFailingReadyCheck);

            // Act — a second call must not produce a duplicate route
            app.MapCloudstrapHealthChecks();
            app.MapCloudstrapHealthChecks();
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage response = await app.GetTestClient()
                .GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert — an ambiguous match would surface as a 500 here
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task Map_WithNoChecksRegistered_BothProbesReturnHealthy()
        {
            // Arrange — the stock empty-set behavior, documented rather than special-cased
            await using WebApplication app = BuildApp();
            app.MapCloudstrapHealthChecks();
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);
            HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage liveness = await client
                .GetAsync(new Uri("/healthz", UriKind.Relative), TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage readiness = await client
                .GetAsync(new Uri("/ready", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(liveness.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(readiness.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public void Map_OnNullEndpoints_ThrowsArgumentNullException()
        {
            // Arrange
            IEndpointRouteBuilder endpoints = null!;

            // Act + Assert
            Assert.That(() => endpoints.MapCloudstrapHealthChecks(), Throws.ArgumentNullException);
        }

        private static void AddOneLiveOneFailingReadyCheck(IHealthChecksBuilder checks)
        {
            checks.AddCheck(
                "catalog-live",
                () => HealthCheckResult.Healthy(),
                tags: [CloudstrapHealthCheckTags.Liveness]);
            checks.AddCheck(
                "catalog-ready",
                () => HealthCheckResult.Unhealthy("dependency unavailable"),
                tags: [CloudstrapHealthCheckTags.Readiness]);
        }

        private static WebApplication BuildApp(
            Dictionary<string, string?>? configValues = null,
            Action<IHealthChecksBuilder>? configureChecks = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.Configuration.AddInMemoryCollection(configValues ?? []);
            builder.WebHost.UseTestServer();

            IHealthChecksBuilder checks = builder.Services.AddHealthChecks();
            configureChecks?.Invoke(checks);

            return builder.Build();
        }
    }
}
