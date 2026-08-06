namespace Cloudstrap.Authentication.ClientCredentials.Tests
{
    using System.Diagnostics;
    using Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Cloudstrap.TestIdentityProvider;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// Tokens never leak into the application's caches (AC-CC12, D-3): the isolated default provably
    /// writes nothing to a registered <c>IDistributedCache</c>, the <c>Shared</c> opt-in provably does,
    /// and the mode in force is visible in the log — not magic.
    /// </summary>
    [TestFixture]
    public sealed class CachePostureTests
    {
        [Test]
        public async Task DefaultIsolatedMode_ARegisteredIDistributedCacheNeverReceivesAnything()
        {
            // Arrange — the application registers a distributed cache for its own purposes
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            RecordingDistributedCache distributedCache = new();
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddSingleton<IDistributedCache>(distributedCache);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act — tokens are acquired, cached and reused
            for (int call = 0; call < 2; call++)
            {
                using HttpResponseMessage response =
                    await client.Client.GetAsync(new Uri("orders", UriKind.Relative));
            }

            // Assert — caching provably worked, and the application cache saw zero writes (the D-3 headline)
            Assert.Multiple(() =>
            {
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(1));
                Assert.That(distributedCache.WriteCount, Is.Zero);
            });
        }

        [Test]
        public async Task SharedMode_TokensUseTheApplicationCacheIncludingItsDistributedTier()
        {
            // Arrange — the documented opt-in
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:ClientCredentials:TokenCache"] = "Shared";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            RecordingDistributedCache distributedCache = new();
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddSingleton<IDistributedCache>(distributedCache);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // The distributed tier may be written asynchronously — bounded wait, no bare sleep
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (distributedCache.WriteCount == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(2))
            {
                await Task.Delay(50);
            }

            // Assert — the opt-in works and is observable
            Assert.That(distributedCache.WriteCount, Is.GreaterThan(0));
        }

        [Test]
        public async Task SharedModeWithoutADistributedCache_WorksWithoutErrorOrExtraWarning()
        {
            // Arrange — Shared, but the application registered no distributed tier (edge-case row)
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:ClientCredentials:TokenCache"] = "Shared";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            CapturingPrimaryHandler capturing = new();
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            for (int call = 0; call < 2; call++)
            {
                using HttpResponseMessage response =
                    await client.Client.GetAsync(new Uri("orders", UriKind.Relative));
            }

            // Assert — fully functional, memory-tier only, and quiet about it
            Assert.Multiple(() =>
            {
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(1));
                Assert.That(
                    logs.Entries.Where(entry => entry.Level >= LogLevel.Warning),
                    Is.Empty,
                    "Shared mode without a distributed tier must neither warn nor fail.");
            });
        }

        [Test]
        public void StartupLog_StatesTheTokenCacheModeInForce()
        {
            // Arrange & Act — one default (Isolated) run and one opted-in (Shared) run
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();

            CapturingLoggerProvider isolatedLogs = new();
            HostApplicationBuilder isolatedBuilder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            isolatedBuilder.Logging.AddProvider(isolatedLogs);
            isolatedBuilder.Services.AddCloudstrapClientCredentials(
                ClientCredentialsTestHost.BackchannelTo(identityProvider));
            using (IHost isolatedHost = isolatedBuilder.Build())
            {
                isolatedHost.Start();
            }

            Dictionary<string, string?> sharedConfig = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            sharedConfig["Cloudstrap:ClientCredentials:TokenCache"] = "Shared";
            CapturingLoggerProvider sharedLogs = new();
            HostApplicationBuilder sharedBuilder = ClientCredentialsTestHost.CreateBuilder(sharedConfig);
            sharedBuilder.Logging.AddProvider(sharedLogs);
            sharedBuilder.Services.AddCloudstrapClientCredentials(
                ClientCredentialsTestHost.BackchannelTo(identityProvider));
            using (IHost sharedHost = sharedBuilder.Build())
            {
                sharedHost.Start();
            }

            // Assert — exactly one line naming the mode in force, per run ("visible, not magic", D-3)
            Assert.Multiple(() =>
            {
                Assert.That(
                    isolatedLogs.Entries.Count(entry => entry.Level == LogLevel.Information
                        && entry.Message.Contains("Isolated", StringComparison.Ordinal)),
                    Is.EqualTo(1));
                Assert.That(
                    sharedLogs.Entries.Count(entry => entry.Level == LogLevel.Information
                        && entry.Message.Contains("Shared", StringComparison.Ordinal)),
                    Is.EqualTo(1));
            });
        }
    }
}
