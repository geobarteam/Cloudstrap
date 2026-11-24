namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using System.Net.Http.Headers;
    using Cloudstrap.Extensions;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using Yarp.ReverseProxy.ServiceDiscovery;

    /// <summary>
    /// AC-PX12 (D-8): a destination resolved through YARP's <see cref="IDestinationResolver"/> hook (the seam
    /// <c>Microsoft.Extensions.ServiceDiscovery.Yarp</c> uses) keeps every Cloudstrap behavior: the application's token
    /// is attached, the caller's credentials are stripped and the signed-in default still applies. The cluster's
    /// configured address is the logical <c>http://downstream-api/</c>; a test-local resolver maps it to the echo
    /// server. Neither service discovery nor any <c>Aspire.*</c> package is referenced.
    /// </summary>
    /// <remarks>
    /// Every test here is a regression guard: it passed as soon as the test-local resolver compiled, with no
    /// production change (the token and credential handling sit in the forwarder's handler chain and transforms, which
    /// do not depend on how the destination address was obtained).
    /// </remarks>
    [TestFixture]
    [Category(TestCategories.RegressionGuard)]
    public sealed class DestinationResolverCompositionTests
    {
        private static readonly Uri _logicalAddress = new("http://downstream-api/");

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
        public async Task ResolvedDestination_IsForwardedTo_WithTheApplicationToken()
        {
            // Arrange
            FakeClientTokenHandlerProvider tokens = new();
            StaticDestinationResolver resolver = new(_logicalAddress, _echo.Address);
            await using FrontDoor frontDoor = await StartAsync(tokens, resolver);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(resolver.ResolvedCount, Is.GreaterThan(0), "the resolver was never asked, so the test proves nothing");
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Path, Is.EqualTo("/api/orders"));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo($"Bearer app:{ProxyConfig.ClusterId}"));
                Assert.That(tokens.Requests.Select(request => request.ClientName), Is.EqualTo(new[] { ProxyConfig.ClusterId }));
            });
        }

        [Test]
        public async Task ResolvedDestination_CallerCredentials_AreStillStripped()
        {
            // Arrange
            FakeClientTokenHandlerProvider tokens = new();
            StaticDestinationResolver resolver = new(_logicalAddress, _echo.Address);
            await using FrontDoor frontDoor = await StartAsync(tokens, resolver);
            using HttpRequestMessage request = new(HttpMethod.Get, "/api/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "alice");
            request.Headers.Add("Cookie", "session=caller-secret");

            // Act
            using HttpResponseMessage response = await frontDoor.Client.SendAsync(request);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(resolver.ResolvedCount, Is.GreaterThan(0), "the resolver was never asked, so the test proves nothing");
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Cookie"), Is.False, "the caller's cookie reached the destination");
                Assert.That(tokens.ArrivingAuthorizations, Is.EqualTo(new string?[] { null }), "the caller's Authorization reached the token handler");
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo($"Bearer app:{ProxyConfig.ClusterId}"));
            });
        }

        [Test]
        public async Task ResolvedDestination_AnonymousCaller_IsStillRejected()
        {
            // Arrange
            FakeClientTokenHandlerProvider tokens = new();
            StaticDestinationResolver resolver = new(_logicalAddress, _echo.Address);
            await using FrontDoor frontDoor = await StartAsync(tokens, resolver);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(resolver.ResolvedCount, Is.GreaterThan(0), "the resolver was never asked, so the test proves nothing");
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// A front door whose single <c>Client</c> cluster points at the logical address, with the resolver registered
        /// on the builder <c>AddCloudstrapProxy()</c> returns.
        /// </summary>
        private static Task<FrontDoor> StartAsync(FakeClientTokenHandlerProvider tokens, StaticDestinationResolver resolver)
        {
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_logicalAddress, tokenMode: nameof(ProxyTokenMode.Client)));
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(tokens);
            builder.AddCloudstrapProxy().Services.AddSingleton<IDestinationResolver>(resolver);
            return FrontDoor.StartAsync(builder);
        }
    }
}
