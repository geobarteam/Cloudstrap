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
