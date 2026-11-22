namespace Cloudstrap.Proxy.Tests
{
    using System.Net;
    using System.Net.Http.Headers;
    using Cloudstrap.Extensions;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    /// <summary>
    /// AC-PX6, AC-PX10 token mode, D-6: a <c>User</c> cluster forwards with the signed-in user's token, a <c>None</c>
    /// cluster with no token, an unknown mode fails startup listing the allowed values, and no mode ever adds a
    /// user-identity header.
    /// </summary>
    [TestFixture]
    public sealed class TokenModeTests
    {
        private const string _allowedValues = "None, Client, User";
        private const string _defaultTokenModeKey = "Cloudstrap:Proxy:DefaultTokenMode";
        private const string _routePolicyKey = $"ReverseProxy:Routes:{ProxyConfig.RouteId}:AuthorizationPolicy";
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
        public async Task UserCluster_ForwardedRequest_CarriesTheUserToken()
        {
            // Arrange
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.User));
            settings[$"{_tokenRequestSection}:Scope"] = "orders.read";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            FakeClientTokenHandlerProvider clientProvider = RegisterFakeProviders(builder);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: "alice");

            // Assert
            UserTokenRequest asked = UserProvider(frontDoor).Requests.Single();
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.GetValueOrDefault("Authorization"), Is.EqualTo("Bearer user:alice"));
                Assert.That(asked.ClientName, Is.EqualTo(ProxyConfig.ClusterId));
                Assert.That(asked.TokenRequest?.Scope, Is.EqualTo("orders.read"));
                Assert.That(clientProvider.Requests, Is.Empty, "a User cluster asked the client-token seam");
            });
        }

        [Test]
        public void UserCluster_NoUserProviderRegistered_StartFailsNamingThePackage()
        {
            // Arrange: a User cluster, only the client-token seam registered
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.User)));
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
                    Does.Contain($"'{ProxyConfig.ClusterId}'")
                        .And.Contain(ProxyMetadataKeys.TokenMode)
                        .And.Contain(nameof(IUserAccessTokenHandlerProvider))
                        .And.Contain("Cloudstrap.Authentication.OpenIdConnect"));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): Step 4 already attaches nothing for a <c>None</c> cluster. An anonymous
        /// caller on an anonymous route sends no <c>Authorization</c>, so any header downstream would be the proxy's.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task NoneCluster_ForwardedRequest_HasNoAuthorizationHeader()
        {
            // Arrange
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.None));
            settings[_routePolicyKey] = "Anonymous";
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            FakeClientTokenHandlerProvider clientProvider = RegisterFakeProviders(builder);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: null);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Authorization"), Is.False);
                Assert.That(clientProvider.Requests, Is.Empty);
                Assert.That(UserProvider(frontDoor).Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): the stripping transform of Step 3 is independent of the token mode,
        /// so a <c>None</c> cluster loses the caller's <c>Authorization</c> too.
        /// </summary>
        [Test]
        [Category(TestCategories.RegressionGuard)]
        public async Task NoneCluster_CallerAuthorization_IsStillStripped()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: nameof(ProxyTokenMode.None)));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act: the caller signs in with its own "Authorization: Test alice"
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders", user: "alice");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.ContainsKey("Authorization"), Is.False, "the caller's Authorization reached the destination");
            });
        }

        /// <summary>Only the three names count, case-insensitively; numbers and combined values are refused.</summary>
        /// <param name="value">The cluster's token mode metadata.</param>
        [TestCase("Machine")]
        [TestCase("1")]
        [TestCase("2")]
        [TestCase("Client, User")]
        public void UnknownTokenModeMetadata_StartFailsListingTheAllowedValues(string value)
        {
            // Arrange: both seams registered, so only the value can fail
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: value));
            RegisterFakeProviders(builder);
            builder.AddCloudstrapProxy();

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null, "startup succeeded");
                Assert.That(
                    failure?.ToString(),
                    Does.Contain($"'{ProxyConfig.ClusterId}'")
                        .And.Contain(ProxyMetadataKeys.TokenMode)
                        .And.Contain($"'{value}'")
                        .And.Contain(_allowedValues));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// Regression guard (green on arrival): the names are accepted case-insensitively, as Step 4 already did.
        /// </summary>
        /// <param name="value">The cluster's token mode metadata.</param>
        [TestCase("none")]
        [TestCase("NONE")]
        [Category(TestCategories.RegressionGuard)]
        public async Task TokenModeMetadata_NameInAnyCase_IsAccepted(string value)
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: value));
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);

            // Act
            using HttpResponseMessage response = await frontDoor.GetAsync("/api/orders");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        /// <param name="value">The configured <c>Cloudstrap:Proxy:DefaultTokenMode</c>.</param>
        [TestCase("Machine")]
        [TestCase("1")]
        [TestCase("Client, User")]
        public void UnknownDefaultTokenMode_ValidateOnStartFailsNamingTheKey(string value)
        {
            // Arrange: the cluster has no metadata, so the default applies
            Dictionary<string, string?> settings = ProxyConfig.SingleRoute(_echo.Address, tokenMode: null);
            settings[_defaultTokenModeKey] = value;
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(settings);
            RegisterFakeProviders(builder);
            builder.AddCloudstrapProxy();

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null, "startup succeeded");
                Assert.That(failure?.ToString(), Does.Contain(_defaultTokenModeKey).And.Contain(_allowedValues));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        [Test]
        public void UndefinedDefaultTokenModeInCode_ValidateOnStartFailsNamingTheKey()
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: null));
            RegisterFakeProviders(builder);
            builder.AddCloudstrapProxy(options => options.DefaultTokenMode = (ProxyTokenMode)42);

            // Act
            Exception? failure = Assert.CatchAsync(() => FrontDoor.StartAsync(builder));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null, "startup succeeded");
                Assert.That(failure?.ToString(), Does.Contain(_defaultTokenModeKey).And.Contain(_allowedValues));
                Assert.That(_echo.Requests, Is.Empty);
            });
        }

        /// <summary>
        /// D-6: whatever the mode, the destination receives only the caller's own headers, YARP's forwarding and
        /// tracing headers, the token (<c>Client</c>/<c>User</c>) and the correlation header (Step 7). No header
        /// carries the user's identity. The <c>None</c> and <c>Client</c> cases were green on arrival (regression guards
        /// of Steps 3 and 4); the <c>User</c> case drove this step.
        /// </summary>
        /// <param name="mode">The cluster's token mode.</param>
        [TestCase(ProxyTokenMode.None, Category = TestCategories.RegressionGuard)]
        [TestCase(ProxyTokenMode.Client, Category = TestCategories.RegressionGuard)]
        [TestCase(ProxyTokenMode.User)]
        public async Task AnyMode_ForwardedRequest_CarriesNoUserIdentityHeader(ProxyTokenMode mode)
        {
            // Arrange
            WebApplicationBuilder builder = FrontDoor.CreateBuilder(ProxyConfig.SingleRoute(_echo.Address, tokenMode: mode.ToString()));
            RegisterFakeProviders(builder);
            builder.AddCloudstrapProxy();
            await using FrontDoor frontDoor = await FrontDoor.StartAsync(builder);
            using HttpRequestMessage request = new(HttpMethod.Get, "/api/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "alice");
            request.Headers.Add("Accept-Language", "nl-BE");

            // Act
            using HttpResponseMessage response = await frontDoor.Client.SendAsync(request);

            // Assert
            HashSet<string> expected = new(StringComparer.OrdinalIgnoreCase)
            {
                "Accept-Language", // the caller's own header
                "Host", "Content-Length", // transport
                "X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host", "X-Forwarded-Prefix", // YARP
                "traceparent", "tracestate", "baggage", // W3C trace context
                "X-Correlation-ID", // the correlation header (Step 7)
            };
            if (mode != ProxyTokenMode.None)
            {
                expected.Add("Authorization");
            }

            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(_echo.Requests, Has.Count.EqualTo(1));
                Assert.That(_echo.Requests[0].Headers.Keys.Where(name => !expected.Contains(name)), Is.Empty);
                Assert.That(
                    _echo.Requests[0].Headers.Where(header => header.Key != "Authorization").Select(header => header.Value),
                    Has.None.Contains("alice"),
                    "a header other than the user token carries the user's name");
            });
        }

        /// <summary>
        /// Registers both fake seams and the <see cref="IHttpContextAccessor"/> the user seam reads the user from.
        /// </summary>
        /// <returns>The client-token seam.</returns>
        private static FakeClientTokenHandlerProvider RegisterFakeProviders(WebApplicationBuilder builder)
        {
            FakeClientTokenHandlerProvider client = new();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(client);
            builder.Services.AddSingleton<FakeUserTokenHandlerProvider>();
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider>(services => services.GetRequiredService<FakeUserTokenHandlerProvider>());
            return client;
        }

        /// <summary>Gets the user-token seam of a started front door.</summary>
        private static FakeUserTokenHandlerProvider UserProvider(FrontDoor frontDoor)
        {
            return frontDoor.App.Services.GetRequiredService<FakeUserTokenHandlerProvider>();
        }
    }
}
