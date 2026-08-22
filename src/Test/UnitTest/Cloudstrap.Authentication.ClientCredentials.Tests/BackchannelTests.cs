namespace Cloudstrap.Authentication.ClientCredentials.Tests
{
    using Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Cloudstrap.TestIdentityProvider;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// The token backchannel is a quiet, dedicated client (Behaviors rows): no correlation header goes
    /// to the identity provider, the backchannel hook reaches only the token client, the client name is
    /// overridable, and the liveness probe never carries a token.
    /// </summary>
    [TestFixture]
    public sealed class BackchannelTests
    {
        [Test]
        public async Task TokenEndpointRequest_CarriesNoCorrelationHeader()
        {
            // Arrange
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            CapturingPassThroughHandler backchannelCapture = new(identityProvider.CreateHandler());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(configurator =>
                configurator.Backchannel = http => http.ConfigurePrimaryHttpMessageHandler(() => backchannelCapture));

            using IHost host = builder.Build();
            host.Start();
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-backchannel";
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the consumer's request correlates; the token request does not (Deliberate
            // Behavior Change 10)
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.Contains("X-Correlation-ID"), Is.True);
                Assert.That(backchannelCapture.LastRequest, Is.Not.Null);
                Assert.That(backchannelCapture.LastRequest!.Headers.Contains("X-Correlation-ID"), Is.False);
            });
        }

        [Test]
        public async Task BackchannelHook_ReachesOnlyTheTokenEndpointClient()
        {
            // Arrange — the hook adds a marker handler
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            CapturingPassThroughHandler backchannelCapture = new(identityProvider.CreateHandler());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(configurator =>
                configurator.Backchannel = http =>
                {
                    http.ConfigurePrimaryHttpMessageHandler(() => backchannelCapture);
                    http.AddHttpMessageHandler(() => new BackchannelMarkerHandler());
                });

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the token request carries the mark; the consumer client's request does not
            Assert.Multiple(() =>
            {
                Assert.That(backchannelCapture.LastRequest!.Headers.Contains("X-Backchannel-Mark"), Is.True);
                Assert.That(capturing.LastRequest!.Headers.Contains("X-Backchannel-Mark"), Is.False);
            });
        }

        [Test]
        public async Task BackchannelHttpClientName_IsOverridable()
        {
            // Arrange — the renamed backchannel is configured by name, the standard way
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:ClientCredentials:BackchannelHttpClientName"] = "contoso-backchannel";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            CapturingPrimaryHandler capturing = new();
            CapturingPassThroughHandler renamedCapture = new(identityProvider.CreateHandler());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddHttpClient("contoso-backchannel")
                .ConfigurePrimaryHttpMessageHandler(() => renamedCapture);
            builder.Services.AddCloudstrapClientCredentials();

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the renamed client is the one that called the identity provider
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.Authorization, Is.Not.Null);
                Assert.That(renamedCapture.LastRequest, Is.Not.Null);
                Assert.That(
                    renamedCapture.LastRequest!.RequestUri!.AbsolutePath,
                    Does.EndWith("/connect/token"));
            });
        }

        [Test]
        public async Task LivenessProbeClient_SendsNoToken()
        {
            // Arrange — a flagged client that also probes its dependency
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Catalog:EnableHealthCheck"] = "true";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            CapturingPrimaryHandler capturing = new();
            CapturingPrimaryHandler probeCapturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddHttpClient("Catalog-liveness")
                .ConfigurePrimaryHttpMessageHandler(() => probeCapturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act — one authenticated call, one probe run
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));
            await host.Services.GetRequiredService<HealthCheckService>()
                .CheckHealthAsync(TestContext.CurrentContext.CancellationToken);

            // Assert — the client's own requests carry the token, the probe never does (Behaviors row /
            // Deliberate Behavior Change 9 — true by construction in #4's wiring, pinned here forever)
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.Authorization, Is.Not.Null);
                Assert.That(probeCapturing.LastRequest, Is.Not.Null);
                Assert.That(probeCapturing.LastRequest!.Headers.Authorization, Is.Null);
            });
        }
    }
}
