namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;
    using Yarp.ReverseProxy.Transforms;

    /// <summary>
    /// AC-PX1 / AC-PX10 (section): one registration call over YARP's own configuration forwards a configured route to
    /// its cluster, and both calls hand back YARP's own builders.
    /// </summary>
    [TestFixture]
    public sealed class RegistrationTests
    {
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
        public async Task MapCloudstrapProxy_ConfiguredRoute_ForwardsToTheClusterDestination()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders/42");
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Does.Contain("\"echo\":\"downstream\"").And.Contain("/api/orders/42"));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests.Select(request => request.Path), Has.All.EqualTo("/api/orders/42"));
            });
        }

        [Test]
        public async Task MapCloudstrapProxy_UnmatchedPath_IsNotForwarded()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/elsewhere/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        [Test]
        public async Task AddCloudstrapProxy_ReturnsYarpsReverseProxyBuilder_SoYarpExtensionsChain()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            IReverseProxyBuilder proxy = builder.AddCloudstrapProxy();
            proxy.AddTransforms(context => context.AddRequestHeader("X-Chained", "yes", append: false));
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests.Single().Headers, Does.ContainKey("X-Chained").WithValue("yes"));
            });
        }

        [Test]
        public async Task AddCloudstrapProxy_CustomConfigurationSection_IsUsed()
        {
            // Arrange
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, section: "Gateway");
            settings["Cloudstrap:Proxy:ConfigurationSection"] = "Gateway";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void AddCloudstrapProxy_ConfigurationSectionMissing_FailsStartNamingTheKey()
        {
            // Arrange: routes live under "Gateway", but nothing points the package at it
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, section: "Gateway"));
            builder.AddCloudstrapProxy();

            // Act
            OptionsValidationException? failure = Assert.ThrowsAsync<OptionsValidationException>(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.That(failure!.Message, Does.Contain("Cloudstrap:Proxy:ConfigurationSection").And.Contain("'ReverseProxy'"));
        }

        [Test]
        public void AddCloudstrapProxy_CalledTwice_ThrowsInvalidOperationException()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address));
            builder.AddCloudstrapProxy();

            // Act
            InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(() => builder.AddCloudstrapProxy());

            // Assert
            Assert.That(failure!.Message, Does.Contain(nameof(HostApplicationBuilderExtensions.AddCloudstrapProxy)));
        }

        [Test]
        public void AddCloudstrapProxy_NullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            IHostApplicationBuilder builder = null!;

            // Act & Assert
            Assert.That(() => builder.AddCloudstrapProxy(), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void MapCloudstrapProxy_NullEndpoints_ThrowsArgumentNullException()
        {
            // Arrange
            IEndpointRouteBuilder endpoints = null!;

            // Act & Assert
            Assert.That(() => endpoints.MapCloudstrapProxy(), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("endpoints"));
        }
    }
}
