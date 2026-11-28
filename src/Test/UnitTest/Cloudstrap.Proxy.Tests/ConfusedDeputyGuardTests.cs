namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using Cloudstrap.Extensions;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Testing;
    using NUnit.Framework;
    using Yarp.ReverseProxy.Configuration;

    /// <summary>
    /// AC-PX5 (D-4, mechanic (f) guard): a route anyone can call must not forward with a token, or an anonymous caller
    /// borrows the application's (or a user's) identity. A route with policy <c>Anonymous</c> on a cluster whose
    /// effective mode is <c>Client</c> or <c>User</c> fails startup, unless its
    /// <see cref="ProxyMetadataKeys.AllowAnonymousWithToken"/> metadata is a case-insensitive <c>true</c>.
    /// </summary>
    [TestFixture]
    public sealed class ConfusedDeputyGuardTests
    {
        private const string _routePolicyKey = $"ReverseProxy:Routes:{ProxyConfig.RouteId}:AuthorizationPolicy";
        private const string _overrideKey = $"ReverseProxy:Routes:{ProxyConfig.RouteId}:Metadata:{ProxyMetadataKeys.AllowAnonymousWithToken}";
        private const string _routeClusterKey = $"ReverseProxy:Routes:{ProxyConfig.RouteId}:ClusterId";
        private const string _clusterTokenModeKey = $"ReverseProxy:Clusters:{ProxyConfig.ClusterId}:Metadata:{ProxyMetadataKeys.TokenMode}";
        private const string _publicClusterId = "public-api";
        private const string _memoryRouteId = "memory-route";
        private const string _memoryClusterId = "memory-cluster";

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

        /// <param name="policy">The route's policy; YARP's reserved names are case-insensitive.</param>
        [TestCase("Anonymous")]
        [TestCase("anonymous")]
        public void AnonymousRoute_ClientCluster_StartFailsNamingRouteClusterAndOverride(string policy)
        {
            // Arrange
            WebApplicationBuilder builder = CreateBuilder(nameof(ProxyTokenMode.Client), policy, overrideValue: null);

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            AssertGuardFailure(failure);
        }

        [Test]
        public void AnonymousRoute_UserCluster_StartFails()
        {
            // Arrange: a User cluster's token belongs to a signed-in user, which an anonymous route does not have
            WebApplicationBuilder builder = CreateBuilder(nameof(ProxyTokenMode.User), "Anonymous", overrideValue: null);

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            AssertGuardFailure(failure);
        }

        [Test]
        public void AnonymousRoute_DefaultModeCluster_StartFails()
        {
            // Arrange: no cluster metadata, so the default mode (Client) applies
            WebApplicationBuilder builder = CreateBuilder(tokenMode: null, "Anonymous", overrideValue: null);

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            AssertGuardFailure(failure);
        }

        /// <summary>
        /// Regression guard (green on arrival, trivially, while no guard existed): with the override the deliberately
        /// public route starts and forwards with the application's token; it now proves the guard honors the override.
        /// </summary>
        /// <param name="overrideValue">The route's override metadata; only <c>true</c> in any case counts.</param>
        [TestCase("true")]
        [TestCase("TRUE")]
        [Category(TestCategories.RegressionGuard)]
        public async Task AnonymousRoute_ClientCluster_WithOverride_StartsAndForwardsWithTheAppToken(string overrideValue)
        {
            // Arrange
            WebApplicationBuilder builder = CreateBuilder(nameof(ProxyTokenMode.Client), "Anonymous", overrideValue);
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act: an anonymous caller on the deliberately public route
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo($"Bearer app:{ProxyConfig.ClusterId}"));
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): a <c>None</c> cluster attaches no token, so an anonymous route to it lends
        /// no identity and starts without the override.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task AnonymousRoute_NoneCluster_Starts()
        {
            // Arrange
            WebApplicationBuilder builder = CreateBuilder(nameof(ProxyTokenMode.None), "Anonymous", overrideValue: null);
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Authorization"), Is.False);
            });
        }

        /// <param name="overrideValue">An override value that is not <c>true</c>.</param>
        [TestCase("yes")]
        [TestCase("1")]
        [TestCase("false")]
        [TestCase(" true")]
        public void OverrideValueNotTrue_IsIgnored(string overrideValue)
        {
            // Arrange
            WebApplicationBuilder builder = CreateBuilder(nameof(ProxyTokenMode.Client), "Anonymous", overrideValue);

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            AssertGuardFailure(failure);
        }

        /// <summary>
        /// Case (a) of the reload guard: a reload that makes a signed-in route <c>Anonymous</c> on a token-bearing
        /// cluster is rejected, and YARP keeps the previous configuration: an anonymous caller is still challenged and
        /// nothing is forwarded. The app keeps running.
        /// </summary>
        /// <param name="tokenMode">The cluster's token mode.</param>
        [TestCase(nameof(ProxyTokenMode.Client))]
        [TestCase(nameof(ProxyTokenMode.User))]
        [Category(TestCategories.RegressionGuard)]
        public async Task ConfigReload_RouteBecomesAnonymousOnTokenCluster_IsRejectedAndOldConfigKept(string tokenMode)
        {
            // Arrange: the route requires a signed-in user (no policy, so Default)
            FakeLogCollector logs = new();
            WebApplicationBuilder builder = CreateBuilder(tokenMode, policy: null, overrideValue: null);
            builder.Logging.AddProvider(new FakeLoggerProvider(logs));
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            IConfigurationRoot configuration = (IConfigurationRoot)frontDoor.App.Configuration;
            configuration[_routePolicyKey] = "Anonymous";
            configuration.Reload();
            string rejection = await ReloadWaiter.WaitUntilRejectedAsync(logs, ProxyMetadataKeys.AllowAnonymousWithToken);
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Does.Contain($"'{ProxyConfig.RouteId}'").And.Contain($"'{ProxyConfig.ClusterId}'"));
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "the Anonymous policy was applied");
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Case (b) of the reload guard: a reload that moves an <c>Anonymous</c> route from a <c>None</c> cluster to a
        /// <c>Client</c> cluster is rejected; the route keeps forwarding to the <c>None</c> cluster, without a token.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ConfigReload_AnonymousRouteMovesToClientCluster_IsRejectedAndOldConfigKept()
        {
            // Arrange: the Anonymous route starts on a None cluster; the Client cluster exists alongside it
            FakeLogCollector logs = new();
            WebApplicationBuilder builder = CreateBuilder(nameof(ProxyTokenMode.Client), "Anonymous", overrideValue: null);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [_routeClusterKey] = _publicClusterId,
                [$"ReverseProxy:Clusters:{_publicClusterId}:Destinations:primary:Address"] = _echo.Address.ToString(),
                [$"ReverseProxy:Clusters:{_publicClusterId}:Metadata:{ProxyMetadataKeys.TokenMode}"] = nameof(ProxyTokenMode.None),
            });
            builder.Logging.AddProvider(new FakeLoggerProvider(logs));
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            IConfigurationRoot configuration = (IConfigurationRoot)frontDoor.App.Configuration;
            configuration[_routeClusterKey] = ProxyConfig.ClusterId;
            configuration.Reload();
            string rejection = await ReloadWaiter.WaitUntilRejectedAsync(logs, ProxyMetadataKeys.AllowAnonymousWithToken);
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Does.Contain($"'{ProxyConfig.RouteId}'").And.Contain($"'{ProxyConfig.ClusterId}'"));
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Authorization"), Is.False, "the route moved to the Client cluster");
            });
        }

        /// <summary>
        /// Case (c) of the reload guard: a reload that switches the cluster behind an <c>Anonymous</c> route from
        /// <c>None</c> to <c>Client</c> is rejected; the cluster stays <c>None</c> and forwards without a token.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ConfigReload_ClusterUnderAnonymousRouteBecomesClient_IsRejectedAndOldConfigKept()
        {
            // Arrange
            FakeLogCollector logs = new();
            WebApplicationBuilder builder = CreateBuilder(nameof(ProxyTokenMode.None), "Anonymous", overrideValue: null);
            builder.Logging.AddProvider(new FakeLoggerProvider(logs));
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            IConfigurationRoot configuration = (IConfigurationRoot)frontDoor.App.Configuration;
            configuration[_clusterTokenModeKey] = nameof(ProxyTokenMode.Client);
            configuration.Reload();
            string rejection = await ReloadWaiter.WaitUntilRejectedAsync(logs, ProxyMetadataKeys.AllowAnonymousWithToken);
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Does.Contain($"'{ProxyConfig.RouteId}'").And.Contain($"'{ProxyConfig.ClusterId}'"));
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Authorization"), Is.False, "the cluster switched to Client");
            });
        }

        /// <summary>
        /// Case (d), at startup: an <c>Anonymous</c> route to a <c>Client</c> cluster supplied in code
        /// (<c>LoadFromMemory</c>) fails startup like one from configuration.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public void InCodeAnonymousRoute_ClientCluster_StartFails()
        {
            // Arrange
            WebApplicationBuilder builder = CreateBuilder(
                nameof(ProxyTokenMode.None),
                policy: null,
                overrideValue: null,
                proxy => proxy.LoadFromMemory([MemoryAnonymousRoute()], [MemoryClientCluster()]));

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null, "startup succeeded");
                Assert.That(
                    failure?.ToString(),
                    Does.Contain($"'{_memoryRouteId}'").And.Contain($"'{_memoryClusterId}'").And.Contain(ProxyMetadataKeys.AllowAnonymousWithToken));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Case (d), at runtime: an <c>Anonymous</c> route to a <c>Client</c> cluster added in code
        /// (<see cref="InMemoryConfigProvider.Update"/>) is rejected; the route never exists, so nothing is forwarded.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task InCodeUpdate_AnonymousRouteToClientCluster_IsRejectedAndOldConfigKept()
        {
            // Arrange
            FakeLogCollector logs = new();
            WebApplicationBuilder builder = CreateBuilder(
                nameof(ProxyTokenMode.None),
                policy: null,
                overrideValue: null,
                proxy => proxy.LoadFromMemory([], []));
            builder.Logging.AddProvider(new FakeLoggerProvider(logs));
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            frontDoor.App.Services.GetRequiredService<InMemoryConfigProvider>().Update([MemoryAnonymousRoute()], [MemoryClientCluster()]);
            string rejection = await ReloadWaiter.WaitUntilRejectedAsync(logs, ProxyMetadataKeys.AllowAnonymousWithToken);
            using HttpResponseMessage response = await frontDoor.GetAsync("/memory/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(rejection, Does.Contain($"'{_memoryRouteId}'").And.Contain($"'{_memoryClusterId}'"));
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "the rejected route was applied");
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>An <c>Anonymous</c> route matching <c>/memory/{**catch-all}</c> to the in-code Client cluster.</summary>
        private static RouteConfig MemoryAnonymousRoute()
        {
            return new RouteConfig
            {
                RouteId = _memoryRouteId,
                ClusterId = _memoryClusterId,
                AuthorizationPolicy = "Anonymous",
                Match = new RouteMatch { Path = "/memory/{**catch-all}" },
            };
        }

        /// <summary>An in-code cluster on the echo server in <c>Client</c> mode.</summary>
        private ClusterConfig MemoryClientCluster()
        {
            return new ClusterConfig
            {
                ClusterId = _memoryClusterId,
                Destinations = new Dictionary<string, DestinationConfig> { ["primary"] = new() { Address = _echo.Address.ToString() } },
                Metadata = new Dictionary<string, string> { [ProxyMetadataKeys.TokenMode] = nameof(ProxyTokenMode.Client) },
            };
        }

        /// <summary>
        /// A front door with both fake token seams, one route with the given policy and override metadata, and a
        /// cluster with the given token mode.
        /// </summary>
        /// <param name="tokenMode">The cluster's token mode metadata, or <see langword="null"/> for the default mode.</param>
        /// <param name="policy">The route's policy, or <see langword="null"/> for none (Cloudstrap then sets Default).</param>
        /// <param name="overrideValue">The route's override metadata, or <see langword="null"/> for none.</param>
        /// <param name="proxy">Optional further YARP registration, for example <c>LoadFromMemory</c>.</param>
        private WebApplicationBuilder CreateBuilder(string? tokenMode, string? policy, string? overrideValue, Action<IReverseProxyBuilder>? proxy = null)
        {
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, tokenMode: tokenMode);
            if (policy is not null)
            {
                settings[_routePolicyKey] = policy;
            }

            if (overrideValue is not null)
            {
                settings[_overrideKey] = overrideValue;
            }

            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(new FakeClientTokenHandlerProvider());
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider, FakeUserTokenHandlerProvider>();
            IReverseProxyBuilder reverseProxy = builder.AddCloudstrapProxy();
            proxy?.Invoke(reverseProxy);
            return builder;
        }

        private void AssertGuardFailure(Exception? failure)
        {
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null, "startup succeeded");
                Assert.That(
                    failure?.ToString(),
                    Does.Contain($"'{ProxyConfig.RouteId}'")
                        .And.Contain($"'{ProxyConfig.ClusterId}'")
                        .And.Contain(ProxyMetadataKeys.AllowAnonymousWithToken));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }
    }
}
