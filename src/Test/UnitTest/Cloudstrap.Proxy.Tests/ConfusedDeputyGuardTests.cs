namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using Cloudstrap.Extensions;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

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
        /// A front door with both fake token seams, one route with the given policy and override metadata, and a
        /// cluster with the given token mode.
        /// </summary>
        private WebApplicationBuilder CreateBuilder(string? tokenMode, string policy, string? overrideValue)
        {
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, tokenMode: tokenMode);
            settings[_routePolicyKey] = policy;
            if (overrideValue is not null)
            {
                settings[_overrideKey] = overrideValue;
            }

            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(new FakeClientTokenHandlerProvider());
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider, FakeUserTokenHandlerProvider>();
            builder.AddCloudstrapProxy();
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
