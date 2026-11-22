namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using Cloudstrap.Extensions;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using Yarp.ReverseProxy.Configuration;

    /// <summary>
    /// AC-PX4 (D-12, mechanics (d) and (e)): a <c>Client</c> cluster, the default, forwards with the application's
    /// token from the client-credentials seam, and fails loudly when no provider is registered. No request is ever sent
    /// without the token.
    /// </summary>
    [TestFixture]
    public sealed class ClientTokenModeTests
    {
        private const string _expectedToken = $"Bearer app:{ProxyConfig.ClusterId}";
        private const string _tokenRequestSection = $"Cloudstrap:Proxy:Clusters:{ProxyConfig.ClusterId}:TokenRequestParameters";

        private EchoServer _echo = null!;

        [SetUp]
        public async Task StartEchoAsync()
        {
            _echo = await EchoServer.StartAsync();
        }

        [TearDown]
        public async Task StopEchoAsync()
        {
            await _echo.DisposeAsync();
        }

        [Test]
        public async Task ClientCluster_ForwardedRequest_CarriesTheApplicationToken()
        {
            // Arrange
            FakeClientTokenHandlerProvider provider = new();
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.Client)));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(provider);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act: the caller's own "Authorization: Test alice" is removed, then the application token is set
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo(_expectedToken));
            });
        }

        [Test]
        public async Task ClusterWithoutTokenMode_DefaultsToClient()
        {
            // Arrange: no Cloudstrap.TokenMode metadata, no DefaultTokenMode setting
            FakeClientTokenHandlerProvider provider = new();
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: null));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(provider);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo(_expectedToken));
            });
        }

        [Test]
        public async Task ClientCluster_ProviderIsAskedWithTheClusterId_AndItsTokenRequestParameters()
        {
            // Arrange: mechanic (d), the parameters come from Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters
            FakeClientTokenHandlerProvider provider = new();
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.Client));
            settings[$"{_tokenRequestSection}:Scope"] = "orders.read";
            settings[$"{_tokenRequestSection}:Resource"] = "urn:orders";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(provider);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            ClientTokenRequest asked = provider.Requests.Single();
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(asked.ClientName, Is.EqualTo(ProxyConfig.ClusterId));
                Assert.That(asked.TokenRequest?.Scope, Is.EqualTo("orders.read"));
                Assert.That(asked.TokenRequest?.Resource, Is.EqualTo("urn:orders"));
            });
        }

        [Test]
        public async Task ClientCluster_NoTokenRequestParameters_ProviderIsAskedWithNull()
        {
            // Arrange: mechanic (d), no subsection means the provider's defaults
            FakeClientTokenHandlerProvider provider = new();
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.Client)));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(provider);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            ClientTokenRequest asked = provider.Requests.Single();
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(asked.ClientName, Is.EqualTo(ProxyConfig.ClusterId));
                Assert.That(asked.TokenRequest, Is.Null);
            });
        }

        [Test]
        public void ClientCluster_NoProviderRegistered_StartFailsNamingClusterKeyAndPackage()
        {
            // Arrange: a Client cluster and no IClientAccessTokenHandlerProvider
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.Client)));
            builder.AddCloudstrapProxy();

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    failure!.ToString(),
                    Does.Contain($"'{ProxyConfig.ClusterId}'")
                        .And.Contain(ProxyMetadataKeys.TokenMode)
                        .And.Contain("Cloudstrap.Authentication.ClientCredentials"));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        [Test]
        public async Task ClientCluster_TokenHandlerThrows_Returns502_AndForwardsNothing()
        {
            // Arrange: the token endpoint is unreachable; no unauthenticated retry may follow
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.Client)));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider, ThrowingClientTokenHandlerProvider>();
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        [Test]
        public async Task ClientCluster_Downstream401_IsPassedThrough()
        {
            // Arrange: the downstream API rejects the application token
            _echo.StatusCode = StatusCodes.Status401Unauthorized;
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.Client)));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(new FakeClientTokenHandlerProvider());
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act: alice is signed in, so a 401 can only come from the destination
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo(_expectedToken));
            });
        }

        [Test]
        public async Task ConfigReload_AddedClientCluster_AlsoCarriesTheToken()
        {
            // Arrange: an in-memory YARP config provider next to the configuration section, updated after start
            const string reloadedCluster = "reloaded-cluster";
            FakeClientTokenHandlerProvider provider = new();
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(provider);
            builder.AddCloudstrapProxy().LoadFromMemory([], []);
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);
            InMemoryConfigProvider memory = frontDoor.App.Services.GetRequiredService<InMemoryConfigProvider>();
            memory.Update(
                [new RouteConfig { RouteId = "reloaded-route", ClusterId = reloadedCluster, Match = new RouteMatch { Path = "/reloaded/{**catch-all}" } }],
                [new ClusterConfig
                {
                    ClusterId = reloadedCluster,
                    Destinations = new Dictionary<string, DestinationConfig> { ["primary"] = new() { Address = _echo.Address.ToString() } },
                }]);
            await ReloadWaiter.WaitUntilForwardedAsync(frontDoor, "/reloaded/orders");

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/reloaded/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests.Where(request => request.Path == "/reloaded/orders"), Is.Not.Empty);
                Assert.That(
                    _echo.Requests.Where(request => request.Path == "/reloaded/orders").Select(request => request.Headers.GetValueOrDefault("Authorization")),
                    Has.All.EqualTo($"Bearer app:{reloadedCluster}"));
                Assert.That(provider.Requests.Select(request => request.ClientName), Does.Contain(reloadedCluster));
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): the stripping transform of Step 3 already runs before the forwarder's
        /// handler chain. With the fake now keeping an existing <c>Authorization</c> as the real client-credentials
        /// handler does, this proves the caller's header is gone before the token handler runs; were it not, the
        /// caller's own credentials would reach the destination instead of the application token.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ClientCluster_CallerAuthorization_IsRemovedBeforeTheAppTokenIsAdded()
        {
            // Arrange
            FakeClientTokenHandlerProvider provider = new();
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.Client)));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(provider);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act: the caller signs in with its own "Authorization: Test alice"
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: "alice");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(provider.ArrivingAuthorizations, Is.EqualTo(new string?[] { null }), "the caller's Authorization reached the token handler");
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo(_expectedToken));
            });
        }

        /// <summary>
        /// Central's decision (fail closed): the client-credentials handler keeps an <c>Authorization</c> already on the
        /// request, so a route that forwards the caller's credentials to a <c>Client</c> cluster would send the
        /// caller's token instead of the application's. That combination fails startup.
        /// </summary>
        /// <param name="tokenMode">The cluster's token mode metadata; <see langword="null"/> means the default, <c>Client</c>.</param>
        [TestCase(nameof(ProxyTokenMode.Client))]
        [TestCase("client")]
        [TestCase(null)]
        public void ClientCluster_RouteForwardsInboundCredentials_StartFailsNamingRouteClusterAndKey(string? tokenMode)
        {
            // Arrange
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, tokenMode: tokenMode);
            settings[$"ReverseProxy:Routes:{ProxyConfig.RouteId}:Metadata:{ProxyMetadataKeys.ForwardInboundCredentials}"] = "true";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(new FakeClientTokenHandlerProvider());
            builder.AddCloudstrapProxy();

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null, "startup succeeded");
                Assert.That(
                    failure?.ToString(),
                    Does.Contain($"'{ProxyConfig.RouteId}'")
                        .And.Contain($"'{ProxyConfig.ClusterId}'")
                        .And.Contain(ProxyMetadataKeys.ForwardInboundCredentials));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Central's decision (fail closed): the global opt-out keeps the caller's <c>Authorization</c> on every route,
        /// so with a <c>Client</c> cluster the caller's token would reach the destination instead of the application's.
        /// Whether set in configuration or in code, that combination fails startup.
        /// </summary>
        /// <param name="tokenMode">The cluster's token mode metadata; <see langword="null"/> means the default, <c>Client</c>.</param>
        /// <param name="inConfiguration">Whether the opt-out is set in configuration rather than in code.</param>
        [TestCase(nameof(ProxyTokenMode.Client), false)]
        [TestCase(null, false)]
        [TestCase(null, true)]
        public void ClientCluster_StripInboundCredentialsFalse_StartFailsNamingClusterAndKey(string? tokenMode, bool inConfiguration)
        {
            // Arrange
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, tokenMode: tokenMode);
            if (inConfiguration)
            {
                settings["Cloudstrap:Proxy:StripInboundCredentials"] = "false";
            }

            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(new FakeClientTokenHandlerProvider());
            builder.AddCloudstrapProxy(options =>
            {
                if (!inConfiguration)
                {
                    options.StripInboundCredentials = false;
                }
            });

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null, "startup succeeded");
                Assert.That(
                    failure?.ToString(),
                    Does.Contain($"'{ProxyConfig.ClusterId}'")
                        .And.Contain("Cloudstrap:Proxy:StripInboundCredentials")
                        .And.Contain(ProxyMetadataKeys.TokenMode));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        [Test]
        public void ClientCluster_ProviderReturnsHandlerWithInnerHandlerSet_IsNotSupported()
        {
            // Arrange: the seam contract (#4) forbids a pre-set InnerHandler; the proxy owns the chain
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.Client)));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider, InnerHandlerSetClientTokenHandlerProvider>();
            builder.AddCloudstrapProxy();

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure!.ToString(), Does.Contain("InnerHandler").And.Contain($"'{ProxyConfig.ClusterId}'"));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }
    }
}
