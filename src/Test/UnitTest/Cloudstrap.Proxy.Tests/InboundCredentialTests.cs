namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using System.Net.Http.Headers;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Configuration;
    using NUnit.Framework;

    /// <summary>
    /// AC-PX3 (D-5, D-11, mechanic (g)): the caller's <c>Cookie</c> and <c>Authorization</c> headers stay at the front
    /// door, whatever the hosting platform, unless the route or the application explicitly opts out.
    /// </summary>
    [TestFixture]
    public sealed class InboundCredentialTests
    {
        private const string _routeForwardCredentialsKey = $"ReverseProxy:Routes:{ProxyConfig.RouteId}:Metadata:{ProxyMetadataKeys.ForwardInboundCredentials}";
        private const string _callerCookie = "session=caller-secret";

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
        public async Task ProxiedRequest_CallerCookie_IsAbsentDownstream()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await SendAsSignedInCallerAsync(frontDoor);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Cookie"), Is.False, "the caller's cookie reached the destination");
            });
        }

        [Test]
        public async Task ProxiedRequest_CallerAuthorization_IsAbsentDownstream()
        {
            // Arrange: the caller's own "Authorization: Test alice" signs it in at the front door
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await SendAsSignedInCallerAsync(frontDoor);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Authorization"), Is.False, "the caller's Authorization reached the destination");
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): YARP already copies ordinary request headers; the stripping transform
        /// must remove only the two credential headers.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ProxiedRequest_OtherHeaders_AreForwardedUnchanged()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await SendAsSignedInCallerAsync(frontDoor);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers["Accept-Language"], Is.EqualTo("nl-BE"));
                Assert.That(_echo.Requests[0].Headers["X-Demo"], Is.EqualTo("kept"));
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): before the stripping transform existed every header was forwarded,
        /// so the per-route opt-out passes trivially; it guards that the opt-out keeps working once stripping is on.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task RouteOptOut_ForwardInboundCredentialsTrue_KeepsBothHeaders()
        {
            // Arrange
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address);
            settings[_routeForwardCredentialsKey] = "true";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await SendAsSignedInCallerAsync(frontDoor);

            // Assert
            AssertBothCredentialHeadersForwarded(response);
        }

        /// <summary>
        /// Regression guard (green on arrival): as for the route opt-out, the global opt-out passes trivially before
        /// the stripping transform exists; it guards that the opt-out keeps working once stripping is on.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task GlobalOptOut_StripInboundCredentialsFalse_KeepsBothHeaders()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy(options => options.StripInboundCredentials = false);
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await SendAsSignedInCallerAsync(frontDoor);

            // Assert
            AssertBothCredentialHeadersForwarded(response);
        }

        /// <summary>
        /// Regression guard (green on arrival): YARP already drops <c>Proxy-Authorization</c> as a hop-by-hop header,
        /// so no Cloudstrap transform is needed; this guards that it keeps doing so.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ProxiedRequest_CallerProxyAuthorization_IsAbsentDownstream()
        {
            // Arrange: a caller-sent Proxy-Authorization is a credential for the front door's hop, not the destination
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);
            using HttpRequestMessage request = CreateSignedInCallerRequest("/api/orders");
            request.Headers.ProxyAuthorization = new AuthenticationHeaderValue("Basic", "Y2FsbGVyOnNlY3JldA==");

            // Act
            using HttpResponseMessage response = await frontDoor.Client.SendAsync(request);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Proxy-Authorization"), Is.False, "the caller's Proxy-Authorization reached the destination");
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): YARP rebuilds each route's transforms on reload through the same
        /// <c>AddTransforms</c> callback, so a route added by a configuration reload is stripped like a startup one.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task ConfigReload_AddedRoute_StripsCookieAndAuthorization()
        {
            // Arrange: start with one route, then add a second one through a configuration reload
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);
            IConfigurationRoot configuration = (IConfigurationRoot)frontDoor.App.Configuration;
            configuration["ReverseProxy:Routes:reloaded-route:ClusterId"] = ProxyConfig.ClusterId;
            configuration["ReverseProxy:Routes:reloaded-route:Match:Path"] = "/reloaded/{**catch-all}";
            configuration.Reload();
            await ReloadWaiter.WaitUntilForwardedAsync(frontDoor, "/reloaded/orders");

            // Act
            using HttpRequestMessage request = CreateSignedInCallerRequest("/reloaded/orders");
            using HttpResponseMessage response = await frontDoor.Client.SendAsync(request);

            // Assert
            RecordedRequest forwarded = _echo.Requests[^1];
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(forwarded.Path, Is.EqualTo("/reloaded/orders"));
                Assert.That(forwarded.Headers.ContainsKey("Cookie"), Is.False, "the caller's cookie reached the destination");
                Assert.That(forwarded.Headers.ContainsKey("Authorization"), Is.False, "the caller's Authorization reached the destination");
            });
        }

        /// <summary>
        /// Sends <c>GET /api/orders</c> as the signed-in caller <c>alice</c>, with a cookie and two ordinary headers.
        /// </summary>
        private static Task<HttpResponseMessage> SendAsSignedInCallerAsync(FrontDoor frontDoor)
        {
            return frontDoor.Client.SendAsync(CreateSignedInCallerRequest("/api/orders"));
        }

        /// <summary>
        /// A <c>GET</c> as the signed-in caller <c>alice</c>, with a cookie and two ordinary headers.
        /// </summary>
        private static HttpRequestMessage CreateSignedInCallerRequest(string path)
        {
            HttpRequestMessage request = new(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "alice");
            request.Headers.Add("Cookie", _callerCookie);
            request.Headers.Add("Accept-Language", "nl-BE");
            request.Headers.Add("X-Demo", "kept");
            return request;
        }

        private void AssertBothCredentialHeadersForwarded(HttpResponseMessage response)
        {
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Cookie"), Is.EqualTo(_callerCookie));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo($"{TestAuthenticationHandler.SchemeName} alice"));
            });
        }
    }
}
