namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.TestHost;
    using NUnit.Framework;

    /// <summary>
    /// AC-PX8 (D-14): the destination receives YARP's <c>X-Forwarded-For/Proto/Host/Prefix</c>, a caller-sent
    /// <c>X-Forwarded-Prefix</c> is replaced by the front door's <c>PathBase</c>, and <c>Host</c> is the destination's
    /// own. These are regression guards (green on arrival): they characterize YARP defaults that are the contract
    /// #18's dashboard proxy relies on, and they did not drive a production change.
    /// </summary>
    [TestFixture]
    [Category(TestCategories.RegressionGuard)]
    public sealed class ForwardedHeaderTests
    {
        private const string _frontPathBase = "/front";

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
        public async Task PathPrefixedRoute_Destination_ReceivesXForwardedPrefixFromPathBase()
        {
            // Arrange
            await using FrontDoor frontDoor = await StartPathPrefixedFrontDoorAsync();

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync($"{_frontPathBase}/api/orders", user: "alice");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Path, Is.EqualTo("/orders"), "PathRemovePrefix applied");
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("X-Forwarded-Prefix"), Is.EqualTo(_frontPathBase));
            });
        }

        [Test]
        public async Task CallerSentXForwardedPrefix_IsReplaced_NotPassedThrough()
        {
            // Arrange
            await using FrontDoor frontDoor = await StartPathPrefixedFrontDoorAsync();
            HttpRequestMessage request = new(HttpMethod.Get, $"{_frontPathBase}/api/orders");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "alice");
            request.Headers.Add("X-Forwarded-Prefix", "/spoofed");

            // Act
            using HttpResponseMessage response = await frontDoor.Client.SendAsync(request);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("X-Forwarded-Prefix"), Is.EqualTo(_frontPathBase));
            });
        }

        [Test]
        public async Task Destination_ReceivesXForwardedForProtoAndHost()
        {
            // Arrange: TestServer has no remote address by default, so set one (see the plan's Pin record note)
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);
            IPAddress caller = IPAddress.Parse("203.0.113.7");

            // Act
            HttpContext context = await frontDoor.App.GetTestServer().SendAsync(httpContext =>
            {
                httpContext.Request.Method = HttpMethods.Get;
                httpContext.Request.Scheme = "https";
                httpContext.Request.Host = new HostString("front.example.test");
                httpContext.Request.Path = "/api/orders";
                httpContext.Request.Headers.Authorization = $"{TestAuthenticationHandler.SchemeName} alice";
                httpContext.Connection.RemoteIpAddress = caller;
            });

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("X-Forwarded-For"), Is.EqualTo(caller.ToString()));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("X-Forwarded-Proto"), Is.EqualTo("https"));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("X-Forwarded-Host"), Is.EqualTo("front.example.test"));
            });
        }

        [Test]
        public async Task Destination_HostHeader_IsTheDestinationsOwn()
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
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Host"), Is.EqualTo(_echo.Address.Authority));
            });
        }

        /// <summary>
        /// Starts a front door under <c>PathBase</c> <c>/front</c> whose route strips the <c>/api</c> prefix.
        /// </summary>
        private Task<FrontDoor> StartPathPrefixedFrontDoorAsync()
        {
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address);
            settings[$"ReverseProxy:Routes:{ProxyConfig.RouteId}:Transforms:0:PathRemovePrefix"] = "/api";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.AddCloudstrapProxy();
            return FrontDoor.StartAsync(builder, app => app.UsePathBase(_frontPathBase));
        }
    }
}
