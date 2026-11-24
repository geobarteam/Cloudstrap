namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using System.Net.Http.Headers;
    using Cloudstrap.Extensions;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Configuration;
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
        /// A <c>Client</c> cluster configured with no destination at all: the signed-in caller gets YARP's 503, the
        /// token handler never runs (no request leaves the front door), and an anonymous caller is still stopped at 401.
        /// </summary>
        [Test]
        public async Task ClusterWithNoDestination_SignedInCaller_Gets503AndNothingIsSent()
        {
            // Arrange: the route and the Client cluster, but no Destinations entry
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_logicalAddress, tokenMode: nameof(ProxyTokenMode.Client));
            settings.Remove($"ReverseProxy:Clusters:{ProxyConfig.ClusterId}:Destinations:primary:Address");
            FakeClientTokenHandlerProvider tokens = new();
            StaticDestinationResolver resolver = new(_logicalAddress, _echo.Address);
            await using FrontDoor frontDoor = await StartAsync(settings, tokens, resolver);

            // Act
            using HttpResponseMessage signedIn = await frontDoor.GetAsync("/api/orders");
            using HttpResponseMessage anonymous = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(signedIn.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(tokens.ArrivingAuthorizations, Is.Empty, "a request reached the token handler with no destination to send it to");
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// The logical destination resolves to nothing (service discovery found no instance): the same outcome as a
        /// cluster with no destination, 503 with no request sent and no token attached anywhere.
        /// </summary>
        [Test]
        public async Task ResolvedToNoDestination_SignedInCaller_Gets503AndNothingIsSent()
        {
            // Arrange
            FakeClientTokenHandlerProvider tokens = new();
            StaticDestinationResolver resolver = new(_logicalAddress, physicalAddress: null);
            await using FrontDoor frontDoor = await StartAsync(tokens, resolver);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
                Assert.That(resolver.ResolvedCount, Is.GreaterThan(0), "the resolver was never asked, so the test proves nothing");
                Assert.That(resolver.LastDestinationCount, Is.Zero);
                Assert.That(tokens.ArrivingAuthorizations, Is.Empty, "a request reached the token handler with no destination to send it to");
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// A configuration reload points the cluster's destination at another logical address: YARP resolves it again
        /// and the new destination still gets the application's token and never the caller's credentials.
        /// </summary>
        [Test]
        public async Task ConfigReload_DestinationChanged_IsResolvedAgainAndKeepsTokenAndStripping()
        {
            // Arrange: two logical addresses, each resolved to its own echo server
            Uri secondLogicalAddress = new("http://downstream-api-v2/");
            await using EchoServer second = await EchoServer.StartAsync();
            FakeClientTokenHandlerProvider tokens = new();
            StaticDestinationResolver resolver = new(new Dictionary<Uri, Uri?>
            {
                [_logicalAddress] = _echo.Address,
                [secondLogicalAddress] = second.Address,
            });
            await using FrontDoor frontDoor = await StartAsync(
                ProxyConfig.SingleRoute(_logicalAddress, tokenMode: nameof(ProxyTokenMode.Client)),
                tokens,
                resolver);
            int resolvedBeforeReload = resolver.ResolvedCount;

            IConfigurationRoot configuration = (IConfigurationRoot)frontDoor.App.Configuration;
            configuration[$"ReverseProxy:Clusters:{ProxyConfig.ClusterId}:Destinations:primary:Address"] = secondLogicalAddress.ToString();
            configuration.Reload();
            await WaitUntilReachedAsync(frontDoor, second);
            int firstEchoCountAfterReload = _echo.Requests.Count;

            // Act
            using HttpRequestMessage request = new(HttpMethod.Get, "/api/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "alice");
            request.Headers.Add("Cookie", "session=caller-secret");
            using HttpResponseMessage response = await frontDoor.Client.SendAsync(request);

            // Assert
            RecordedRequest forwarded = second.Requests[^1];
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(resolver.ResolvedCount, Is.GreaterThan(resolvedBeforeReload), "the reloaded destination was not resolved again");
                Assert.That(_echo.Requests, Has.Count.EqualTo(firstEchoCountAfterReload), "a request still went to the old destination");
                Assert.That(forwarded.Path, Is.EqualTo("/api/orders"));
                Assert.That(forwarded.Headers.ContainsKey("Cookie"), Is.False, "the caller's cookie reached the new destination");
                Assert.That(forwarded.Headers.GetValueOrDefault("Authorization"), Is.EqualTo($"Bearer app:{ProxyConfig.ClusterId}"));
                Assert.That(tokens.ArrivingAuthorizations, Has.All.Null, "the caller's Authorization reached the token handler");
            });
        }

        /// <summary>Polls as a signed-in user until a request reaches <paramref name="target"/>.</summary>
        private static async Task WaitUntilReachedAsync(FrontDoor frontDoor, EchoServer target)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (target.Requests.Count == 0)
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("The reloaded destination was not used within 10 s.");
                }

                using HttpResponseMessage probe = await frontDoor.GetAsync("/api/orders");
                await Task.Delay(50);
            }
        }

        /// <summary>
        /// A front door whose single <c>Client</c> cluster points at the logical address, with the resolver registered
        /// on the builder <c>AddCloudstrapProxy()</c> returns.
        /// </summary>
        private static Task<FrontDoor> StartAsync(FakeClientTokenHandlerProvider tokens, StaticDestinationResolver resolver)
        {
            return StartAsync(ProxyConfig.SingleRoute(_logicalAddress, tokenMode: nameof(ProxyTokenMode.Client)), tokens, resolver);
        }

        /// <summary>A front door over the given settings, with the token seam and the resolver registered.</summary>
        private static Task<FrontDoor> StartAsync(
            Dictionary<string, string?> settings,
            FakeClientTokenHandlerProvider tokens,
            StaticDestinationResolver resolver)
        {
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(tokens);
            builder.AddCloudstrapProxy().Services.AddSingleton<IDestinationResolver>(resolver);
            return FrontDoor.StartAsync(builder);
        }
    }
}
