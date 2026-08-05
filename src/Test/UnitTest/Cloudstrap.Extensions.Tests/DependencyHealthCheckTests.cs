namespace Cloudstrap.Extensions.Tests
{
    using Cloudstrap.Observability;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-E10: a client marked <c>EnableHealthCheck</c> is probed on its peer's health path, and the result
    /// surfaces as a readiness-tagged check on the stock health-check builder.
    /// </summary>
    [TestFixture]
    public sealed class DependencyHealthCheckTests
    {
        [Test]
        public void EnableHealthCheck_RegistersReadyTaggedLivenessCheckNamedAfterClient()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(CatalogWithHealthCheck());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

            // Act
            using IHost host = builder.Build();
            HealthCheckServiceOptions options = host.Services
                .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

            // Assert
            HealthCheckRegistration registration = options.Registrations.Single();
            Assert.Multiple(() =>
            {
                Assert.That(registration.Name, Is.EqualTo("Catalog-liveness"));
                Assert.That(registration.Tags, Does.Contain(CloudstrapHealthCheckTags.Readiness));
                Assert.That(registration.FailureStatus, Is.EqualTo(HealthStatus.Unhealthy));
            });
        }

        [Test]
        public void EnableHealthCheck_WithPrefix_UsesPrefixForTheName()
        {
            // Arrange
            Dictionary<string, string?> config = CatalogWithHealthCheck();
            config["Cloudstrap:HttpClients:Catalog:HealthCheckPrefix"] = "ContosoCatalog";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

            // Act
            using IHost host = builder.Build();
            HealthCheckServiceOptions options = host.Services
                .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

            // Assert — the override wins over the convention
            Assert.That(options.Registrations.Single().Name, Is.EqualTo("ContosoCatalog-liveness"));
        }

        [Test]
        public void EnableHealthCheck_False_RegistersNoCheck()
        {
            // Arrange — the flag defaults to off
            HostApplicationBuilder builder = TestHostBuilder.Create(TestHostBuilder.CatalogSection());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

            // Act
            using IHost host = builder.Build();
            HealthCheckServiceOptions options = host.Services
                .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

            // Assert
            Assert.That(options.Registrations, Is.Empty);
        }

        [Test]
        public void EnableHealthCheck_RegisteredTwice_AddsOneCheck()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(CatalogWithHealthCheck());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

            HostApplicationBuilder second = TestHostBuilder.Create(CatalogWithHealthCheck());
            second.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

            // Act
            using IHost host = builder.Build();
            using IHost secondHost = second.Build();

            // Assert — deduped within a container, and a second container keeps its own registration
            Assert.Multiple(() =>
            {
                Assert.That(
                    host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
                    Has.Count.EqualTo(1));
                Assert.That(
                    secondHost.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
                    Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task EnableHealthCheck_HealthyPeer_ReportsHealthy()
        {
            // Arrange — the peer serves its probes with MapCloudstrapHealthChecks, on the default path
            await using WebApplication peer = await StartPeerAsync();
            using IHost host = BuildConsumer(CatalogWithHealthCheck(), peer);

            // Act
            HealthReport report = await host.Services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(report.Entries["Catalog-liveness"].Status, Is.EqualTo(HealthStatus.Healthy));
        }

        [Test]
        public async Task EnableHealthCheck_PeerServing404OnConfiguredPath_ReportsUnhealthy()
        {
            // Arrange — the same peer, probed on a path it does not serve
            Dictionary<string, string?> config = CatalogWithHealthCheck();
            config["Cloudstrap:HttpClients:Catalog:HealthCheckPath"] = "/nope";

            await using WebApplication peer = await StartPeerAsync();
            using IHost host = BuildConsumer(config, peer);

            // Act
            HealthReport report = await host.Services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(TestContext.CurrentContext.CancellationToken);

            // Assert — the verdict is the status code, not the body
            Assert.That(report.Entries["Catalog-liveness"].Status, Is.EqualTo(HealthStatus.Unhealthy));
        }

        private static Dictionary<string, string?> CatalogWithHealthCheck()
        {
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:EnableHealthCheck"] = "true";

            return config;
        }

        private static async Task<WebApplication> StartPeerAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddHealthChecks().AddCheck(
                "peer-live",
                () => HealthCheckResult.Healthy(),
                tags: [CloudstrapHealthCheckTags.Liveness]);

            WebApplication peer = builder.Build();
            peer.MapCloudstrapHealthChecks();
            await peer.StartAsync(TestContext.CurrentContext.CancellationToken);

            return peer;
        }

        private static IHost BuildConsumer(Dictionary<string, string?> config, WebApplication peer)
        {
            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

            // The probe client is a named client like any other — which is also how a consumer overrides it
            builder.Services.AddHttpClient("Catalog-liveness")
                .ConfigurePrimaryHttpMessageHandler(peer.GetTestServer().CreateHandler);

            return builder.Build();
        }
    }
}
