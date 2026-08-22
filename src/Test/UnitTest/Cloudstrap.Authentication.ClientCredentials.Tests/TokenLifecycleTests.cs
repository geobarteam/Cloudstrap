namespace Cloudstrap.Authentication.ClientCredentials.Tests
{
    using System.Diagnostics;
    using System.Net;
    using Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Cloudstrap.TestIdentityProvider;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Http;
    using Microsoft.Extensions.Http.Resilience;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// The token lifecycle (AC-CC2, AC-CC3/AC-A2, AC-CC6, AC-CC9): cached within a lifetime, renewed
    /// transparently at expiry, scoped per client without leakage, refreshed exactly once on a 401
    /// through an intact consumer pipeline — and the two user-token settings warn once and do nothing.
    /// </summary>
    [TestFixture]
    public sealed class TokenLifecycleTests
    {
        [Test]
        public async Task SeveralRequestsWithinOneLifetime_CallTheTokenEndpointExactlyOnce()
        {
            // Arrange
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act — three outbound calls well within one token lifetime
            for (int call = 0; call < 3; call++)
            {
                using HttpResponseMessage response =
                    await client.Client.GetAsync(new Uri("orders", UriKind.Relative));
            }

            // Assert — one token request, the same token value on all three calls (AC-CC2)
            Assert.Multiple(() =>
            {
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(1));
                Assert.That(capturing.SeenBearerTokens, Has.Count.EqualTo(3));
                Assert.That(capturing.SeenBearerTokens.Distinct(), Has.Exactly(1).Items);
            });
        }

        [Test]
        public async Task ElapsedLifetime_RenewsTransparentlyWithExactlyOneNewTokenRequest()
        {
            // Arrange — a 2-second token lifetime at the IdP, ATM's cache lifetime buffer zeroed so the
            // cached entry expires with the token itself (plan-level pick 2: short real lifetimes; see
            // the Gate 3 report for the FakeTimeProvider spike outcome)
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider(
                options => options.AccessTokenLifetime = TimeSpan.FromSeconds(2));
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(configurator =>
            {
                ClientCredentialsTestHost.BackchannelTo(identityProvider)(configurator);
                configurator.TokenManagement = options => options.CacheLifetimeBuffer = 0;
            });

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act — first call acquires; then poll (bounded, not sleep-flaky) until a call renews
            using HttpResponseMessage firstResponse =
                await client.Client.GetAsync(new Uri("orders", UriKind.Relative));
            Stopwatch stopwatch = Stopwatch.StartNew();
            HttpStatusCode lastStatus = firstResponse.StatusCode;

            while (identityProvider.TokenRequestCount < 2 && stopwatch.Elapsed < TimeSpan.FromSeconds(8))
            {
                await Task.Delay(250);
                using HttpResponseMessage pollResponse =
                    await client.Client.GetAsync(new Uri("orders", UriKind.Relative));
                lastStatus = pollResponse.StatusCode;
            }

            // Assert — exactly one new token request; the caller observed no failure at any point (AC-CC3)
            Assert.Multiple(() =>
            {
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(2));
                Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(lastStatus, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(capturing.SeenBearerTokens.Distinct().Count(), Is.EqualTo(2));
            });
        }

        [Test]
        public async Task TwoFlaggedClientsWithDifferentScopes_GetTwoDistinctSeparatelyCachedTokens()
        {
            // Arrange — per-client scopes through the shipped TokenRequestParameters section
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider(
                options => options.Clients[0].Scopes.Add("orders.write"));
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Catalog:TokenRequestParameters:Scope"] = "catalog.read";
            config["Cloudstrap:HttpClients:Orders:BaseAddress"] = "https://orders.contoso.example/";
            config["Cloudstrap:HttpClients:Orders:AddClientAccessToken"] = "true";
            config["Cloudstrap:HttpClients:Orders:TokenRequestParameters:Scope"] = "orders.write";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            CapturingPrimaryHandler catalogCapturing = new();
            CapturingPrimaryHandler ordersCapturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => catalogCapturing);
            builder.Services.AddCloudstrapHttpServiceClient<IOrdersClient, OrdersClient>("Orders")
                .ConfigurePrimaryHttpMessageHandler(() => ordersCapturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();

            // Act
            using HttpResponseMessage catalogResponse = await host.Services.GetRequiredService<ICatalogClient>()
                .Client.GetAsync(new Uri("orders", UriKind.Relative));
            using HttpResponseMessage ordersResponse = await host.Services.GetRequiredService<IOrdersClient>()
                .Client.GetAsync(new Uri("baskets", UriKind.Relative));

            // Assert — two token requests, two different tokens, each carrying its own scope, and neither
            // client ever sends the other's (AC-CC6)
            string? catalogToken = catalogCapturing.SeenBearerTokens.Single();
            string? ordersToken = ordersCapturing.SeenBearerTokens.Single();
            Assert.Multiple(() =>
            {
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(2));
                Assert.That(catalogToken, Is.Not.EqualTo(ordersToken));
                Assert.That(ReadScopeClaim(catalogToken!), Is.EqualTo("catalog.read"));
                Assert.That(ReadScopeClaim(ordersToken!), Is.EqualTo("orders.write"));
            });
        }

        [Test]
        public async Task TwoFlaggedClientsWithIdenticalParameters_ShareOneCachedToken()
        {
            // Arrange — two flagged clients, no per-client overrides: identical token requests
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Orders:BaseAddress"] = "https://orders.contoso.example/";
            config["Cloudstrap:HttpClients:Orders:AddClientAccessToken"] = "true";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            CapturingPrimaryHandler catalogCapturing = new();
            CapturingPrimaryHandler ordersCapturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => catalogCapturing);
            builder.Services.AddCloudstrapHttpServiceClient<IOrdersClient, OrdersClient>("Orders")
                .ConfigurePrimaryHttpMessageHandler(() => ordersCapturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();

            // Act
            using HttpResponseMessage catalogResponse = await host.Services.GetRequiredService<ICatalogClient>()
                .Client.GetAsync(new Uri("orders", UriKind.Relative));
            using HttpResponseMessage ordersResponse = await host.Services.GetRequiredService<IOrdersClient>()
                .Client.GetAsync(new Uri("baskets", UriKind.Relative));

            // Assert — the cache key derives from the token request, not the client name (edge-case row)
            Assert.Multiple(() =>
            {
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(1));
                Assert.That(
                    catalogCapturing.SeenBearerTokens.Single(),
                    Is.EqualTo(ordersCapturing.SeenBearerTokens.Single()));
            });
        }

        [Test]
        public async Task Response401_TriggersExactlyOneRefreshAndReExecutesTheInnerChain()
        {
            // Arrange — a consumer resilience handler via ConfigureHttpClientDefaults (the AC-ASP3
            // posture), a consumer marker on the client, correlation, and a downstream scripted to 401
            // the first token
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            UnauthorizedForFirstTokenHandler scripted = new();
            CountingMarkerHandler marker = new();
            HandlerChainCapture chainCapture = new("Catalog");
            builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(chainCapture);
            builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => scripted)
                .AddHttpMessageHandler(() => marker);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-401";
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the caller sees 200; exactly one refresh happened; both attempts traversed the
            // intact inner chain (marker + correlation); the resilience handler is present exactly once
            // and the token handler sits outermost in the consumer pipeline — ahead of resilience and the
            // consumer's own handler (the factory's logging wrappers are framework infrastructure)
            // (AC-CC9 / AC-ASP3)
            int tokenHandlerIndex = IndexOfHandler(chainCapture, "ClientCredentialsTokenHandler");
            int resilienceIndex = chainCapture.HandlerTypes.ToList().FindIndex(
                type => type.Namespace?.StartsWith("Microsoft.Extensions.Http.Resilience", StringComparison.Ordinal) == true);
            int markerIndex = IndexOfHandler(chainCapture, nameof(CountingMarkerHandler));
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(2));
                Assert.That(scripted.RequestCount, Is.EqualTo(2));
                Assert.That(marker.InvocationCount, Is.EqualTo(2));
                Assert.That(scripted.SawCorrelationHeader, Is.All.True);
                Assert.That(chainCapture.ResilienceHandlerCount, Is.EqualTo(1));
                Assert.That(tokenHandlerIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(resilienceIndex, Is.GreaterThan(tokenHandlerIndex));
                Assert.That(markerIndex, Is.GreaterThan(tokenHandlerIndex));
            });
        }

        [Test]
        public async Task StaticForceRenewal_BypassesTheCacheAndWarnsOnceAtStartup()
        {
            // Arrange
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Catalog:TokenRequestParameters:ForceRenewal"] = "true";
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

            // Act — three calls
            for (int call = 0; call < 3; call++)
            {
                using HttpResponseMessage response =
                    await client.Client.GetAsync(new Uri("orders", UriKind.Relative));
            }

            // Assert — every request fetched a fresh token, and exactly one warning names the key
            Assert.Multiple(() =>
            {
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(3));
                Assert.That(
                    logs.Entries.Count(entry => entry.Level == LogLevel.Warning
                        && entry.Message.Contains("Cloudstrap:HttpClients:Catalog:TokenRequestParameters:ForceRenewal", StringComparison.Ordinal)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public async Task SignInSchemeOnAClientTokenRequest_IsIgnoredWithOneWarningNamingTheKey()
        {
            // Arrange — both user-token settings configured on a client-token request
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Catalog:TokenRequestParameters:SignInScheme"] = "Cookies";
            config["Cloudstrap:HttpClients:Catalog:TokenRequestParameters:ChallengeScheme"] = "oidc";
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
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the token request is unaffected, and each ignored key warns exactly once
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.Authorization, Is.Not.Null);
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(1));
                Assert.That(
                    logs.Entries.Count(entry => entry.Level == LogLevel.Warning
                        && entry.Message.Contains("Cloudstrap:HttpClients:Catalog:TokenRequestParameters:SignInScheme", StringComparison.Ordinal)),
                    Is.EqualTo(1));
                Assert.That(
                    logs.Entries.Count(entry => entry.Level == LogLevel.Warning
                        && entry.Message.Contains("Cloudstrap:HttpClients:Catalog:TokenRequestParameters:ChallengeScheme", StringComparison.Ordinal)),
                    Is.EqualTo(1));
            });
        }

        private static int IndexOfHandler(HandlerChainCapture chainCapture, string typeName) =>
            chainCapture.HandlerTypes.ToList().FindIndex(
                type => string.Equals(type.Name, typeName, StringComparison.Ordinal));

        private static string ReadScopeClaim(string token)
        {
            Microsoft.IdentityModel.JsonWebTokens.JsonWebToken jwt =
                new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler().ReadJsonWebToken(token);

            return jwt.GetClaim("scope").Value;
        }
    }
}
