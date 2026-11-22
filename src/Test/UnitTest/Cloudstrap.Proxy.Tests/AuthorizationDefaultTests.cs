namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-PX2 (D-4, D-10, mechanic (f) default): a proxied route with no authorization policy requires a signed-in
    /// user, and nothing reaches the destination otherwise. Named policies and an explicit <c>Anonymous</c> are kept.
    /// </summary>
    [TestFixture]
    public sealed class AuthorizationDefaultTests
    {
        private const string _routePolicyKey = $"ReverseProxy:Routes:{ProxyConfig.RouteId}:AuthorizationPolicy";
        private const string _clusterTokenModeKey = $"ReverseProxy:Clusters:{ProxyConfig.ClusterId}:Metadata:{ProxyMetadataKeys.TokenMode}";

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
        public async Task RouteWithoutPolicy_AnonymousCaller_Returns401_AndForwardsNothing()
        {
            // Arrange: the test scheme challenges with 401, like a bearer scheme
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        [Test]
        public async Task RouteWithoutPolicy_AnonymousCaller_CookieFrontDoor_IsChallenged_AndForwardsNothing()
        {
            // Arrange: a cookie scheme is the front door's default challenge, as in a BFF
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.Services.AddAuthentication(options => options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options => options.LoginPath = "/signin");
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);
            using HttpClient client = frontDoor.App.GetTestServer().CreateClient();

            // Act
            using HttpResponseMessage response = await client.GetAsync(new Uri("/api/orders", UriKind.Relative));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                Assert.That(response.Headers.Location?.ToString(), Does.Contain("/signin"));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival, before <c>ProxyRouteConfigFilter</c> existed): a signed-in caller must
        /// keep being forwarded once the default policy applies. Not a behavior driver.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task RouteWithoutPolicy_SignedInCaller_IsForwarded()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: "alice");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task RouteWithoutPolicy_AppWithoutFallbackPolicy_StillRequiresASignedInUser()
        {
            // Arrange: stock YARP forwards a policy-less route to anyone when the app sets no fallback policy
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);
            AuthorizationOptions authorization = frontDoor.App.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(authorization.FallbackPolicy, Is.Null, "precondition: the app sets no fallback policy");
                Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival, before <c>ProxyRouteConfigFilter</c> existed): YARP already enforces a
        /// named policy, and the filter must not replace it. Not a behavior driver.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task RouteWithNamedPolicy_CallerFailingIt_Returns403_AndForwardsNothing()
        {
            // Arrange
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address);
            settings[_routePolicyKey] = "Admins";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.Services.AddAuthorizationBuilder().AddPolicy("Admins", policy => policy.RequireRole("admin"));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act: alice is signed in but has no admin role
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: "alice");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival, before <c>ProxyRouteConfigFilter</c> existed): YARP already forwards an
        /// explicit <c>Anonymous</c> route, and the filter must keep it public. Not a behavior driver.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task RouteMarkedAnonymous_AnonymousCaller_IsForwarded()
        {
            // Arrange: an explicit public route on a cluster that attaches no token
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address);
            settings[_routePolicyKey] = "Anonymous";
            settings[_clusterTokenModeKey] = nameof(ProxyTokenMode.None);
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): the filter defaults only a null or empty policy, so a blank policy name
        /// reaches YARP's validation and fails startup instead of being silently widened.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public void RouteWithWhitespaceOnlyPolicy_FailsStart()
        {
            // Arrange: a blank policy name is neither "no policy" (which gets Default) nor a real policy
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address);
            settings[_routePolicyKey] = "   ";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.AddCloudstrapProxy();

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert: YARP rejects the unknown policy name; it is never widened to Default or Anonymous
            Assert.Multiple(() =>
            {
                Assert.That(failure!.ToString(), Does.Contain("Authorization policy").And.Contain(ProxyConfig.RouteId));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): YARP runs config filters on every reload, so a route added by a
        /// configuration reload gets the same signed-in default as one present at startup. Without the filter, this
        /// app (no fallback policy) would forward the anonymous caller.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ConfigReload_AddedRouteWithoutPolicy_StillRequiresASignedInUser()
        {
            // Arrange: start with one route, then add a policy-less route through a configuration reload
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);
            IConfigurationRoot configuration = (IConfigurationRoot)frontDoor.App.Configuration;
            configuration["ReverseProxy:Routes:reloaded-route:ClusterId"] = ProxyConfig.ClusterId;
            configuration["ReverseProxy:Routes:reloaded-route:Match:Path"] = "/reloaded/{**catch-all}";
            configuration.Reload();
            await WaitUntilForwardedAsync(frontDoor, "/reloaded/orders");
            int forwardedBefore = _echo.Requests.Count;

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/reloaded/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(_echo.Requests, Has.Count.EqualTo(forwardedBefore));
            });
        }

        /// <summary>Polls as a signed-in user until the reloaded route forwards, so the reload is known applied.</summary>
        private static async Task WaitUntilForwardedAsync(FrontDoor frontDoor, string path)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                using HttpResponseMessage probe = await frontDoor.GetAsync(path, user: "alice");
                if (probe.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }

                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail($"The reloaded route '{path}' was not applied within 10 s (last status {probe.StatusCode}).");
                }

                await Task.Delay(50);
            }
        }
    }
}
