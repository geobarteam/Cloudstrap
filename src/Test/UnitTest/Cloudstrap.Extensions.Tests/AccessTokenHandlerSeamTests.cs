namespace Cloudstrap.Extensions.Tests
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// The seam Cloudstrap.Authentication.* fills (AC-E8, AC-E9, AC-CC13): configuration flags decide which
    /// clients get a token handler, the independently registered providers supply them, and a flag without
    /// its provider is a loud failure naming exactly the missing piece rather than a silently
    /// unauthenticated — or partially authenticated — request.
    /// </summary>
    [TestFixture]
    public sealed class AccessTokenHandlerSeamTests
    {
        private static readonly string[] _userTokenKind = ["user"];
        private static readonly string[] _clientTokenKind = ["client"];
        private static readonly string[] _userThenClientTokenKinds = ["user", "client"];

        [Test]
        public async Task FlaggedClient_WithRegisteredProvider_HasUserTokenHandlerInChain()
        {
            // Arrange
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";
            config["Cloudstrap:HttpClients:Catalog:TokenRequestParameters:Scope"] = "catalog.read";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            RecordingUserTokenHandlerProvider provider = new();
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider>(provider);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);

            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the handler is in the chain, and the provider was told which client and with what
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.GetValues("X-Token-Kind"), Is.EqualTo(_userTokenKind));
                Assert.That(provider.Calls, Has.Count.EqualTo(1));
                Assert.That(provider.Calls[0].ClientName, Is.EqualTo("Catalog"));
                Assert.That(provider.Calls[0].TokenRequest?.Scope, Is.EqualTo("catalog.read"));
            });
        }

        [Test]
        public async Task FlaggedClient_WithClientFlag_HasClientTokenHandlerInChain()
        {
            // Arrange
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            RecordingClientTokenHandlerProvider provider = new();
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(provider);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);

            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert
            Assert.That(capturing.LastRequest!.Headers.GetValues("X-Token-Kind"), Is.EqualTo(_clientTokenKind));
        }

        [Test]
        public async Task UnflaggedClient_WithRegisteredProvider_GetsNoTokenHandler()
        {
            // Arrange — one flagged client, one not; the provider is available to both
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";
            config["Cloudstrap:HttpClients:Orders:BaseAddress"] = "https://orders.contoso.example/";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            RecordingUserTokenHandlerProvider provider = new();
            CapturingPrimaryHandler ordersCapturing = new();
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider>(provider);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            builder.Services.AddCloudstrapHttpServiceClient<IOrdersClient, OrdersClient>("Orders")
                .ConfigurePrimaryHttpMessageHandler(() => ordersCapturing);

            using IHost host = builder.Build();
            IOrdersClient ordersClient = host.Services.GetRequiredService<IOrdersClient>();

            // Act
            using HttpResponseMessage response =
                await ordersClient.Client.GetAsync(new Uri("baskets", UriKind.Relative));

            // Assert — the provider is consulted for exactly the flagged clients
            Assert.Multiple(() =>
            {
                Assert.That(ordersCapturing.LastRequest!.Headers.Contains("X-Token-Kind"), Is.False);
                Assert.That(provider.Calls.Any(call => call.ClientName == "Orders"), Is.False);
            });
        }

        [Test]
        public async Task BothFlagsTrue_WithTwoSeparateProviders_AddsBothHandlersUserFirst()
        {
            // Arrange — two independently shipped packages, each registering only its own seam (D-4)
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";
            config["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true";
            config["Cloudstrap:HttpClients:Catalog:TokenRequestParameters:Scope"] = "catalog.read";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            RecordingUserTokenHandlerProvider userProvider = new();
            RecordingClientTokenHandlerProvider clientProvider = new();
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider>(userProvider);
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(clientProvider);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);

            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — both handlers attached, user first, each provider consulted exactly once with the
            // right client name and parameters (AC-CC13)
            Assert.Multiple(() =>
            {
                Assert.That(
                    capturing.LastRequest!.Headers.GetValues("X-Token-Kind"),
                    Is.EqualTo(_userThenClientTokenKinds));
                Assert.That(userProvider.Calls, Has.Count.EqualTo(1));
                Assert.That(userProvider.Calls[0].ClientName, Is.EqualTo("Catalog"));
                Assert.That(userProvider.Calls[0].TokenRequest?.Scope, Is.EqualTo("catalog.read"));
                Assert.That(clientProvider.Calls, Has.Count.EqualTo(1));
                Assert.That(clientProvider.Calls[0].ClientName, Is.EqualTo("Catalog"));
                Assert.That(clientProvider.Calls[0].TokenRequest?.Scope, Is.EqualTo("catalog.read"));
            });
        }

        [Test]
        public void FlaggedClient_WithoutProvider_FailsFastNamingFlagAndPackage()
        {
            // Arrange — the flag is on, but nothing implements the seam
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            using IHost host = builder.Build();

            // Act
            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
                () => host.Services.GetRequiredService<ICatalogClient>());

            // Assert — the message names the flag, the seam and the package that fills it
            Assert.Multiple(() =>
            {
                Assert.That(
                    exception!.Message,
                    Does.Contain("Cloudstrap:HttpClients:Catalog:AddUserAccessToken"));
                Assert.That(exception!.Message, Does.Contain(nameof(IUserAccessTokenHandlerProvider)));
                Assert.That(exception!.Message, Does.Contain("Cloudstrap.Authentication.OpenIdConnect"));
            });
        }

        [Test]
        public void BothFlagsTrue_WithOnlyClientProviderRegistered_FailsNamingOnlyTheUserFlagAndItsPackage()
        {
            // Arrange — the client-credentials package is present, the OpenID Connect one is not
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";
            config["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            builder.Services.AddSingleton<IClientAccessTokenHandlerProvider>(
                new RecordingClientTokenHandlerProvider());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            using IHost host = builder.Build();

            // Act
            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
                () => host.Services.GetRequiredService<ICatalogClient>());

            // Assert — only the missing half is named; the message must not suggest the satisfied client
            // flag has anything to do with the failure (AC-CC7's mechanism)
            Assert.Multiple(() =>
            {
                Assert.That(
                    exception!.Message,
                    Does.Contain("Cloudstrap:HttpClients:Catalog:AddUserAccessToken"));
                Assert.That(exception!.Message, Does.Contain("Cloudstrap.Authentication.OpenIdConnect"));
                Assert.That(exception!.Message, Does.Not.Contain("AddClientAccessToken"));
            });
        }

        [Test]
        public void BothFlagsTrue_WithOnlyUserProviderRegistered_FailsNamingOnlyTheClientFlagAndItsPackage()
        {
            // Arrange — the OpenID Connect package is present, the client-credentials one is not
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";
            config["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider>(
                new RecordingUserTokenHandlerProvider());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            using IHost host = builder.Build();

            // Act
            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
                () => host.Services.GetRequiredService<ICatalogClient>());

            // Assert — the mirror case: only the missing client half is named
            Assert.Multiple(() =>
            {
                Assert.That(
                    exception!.Message,
                    Does.Contain("Cloudstrap:HttpClients:Catalog:AddClientAccessToken"));
                Assert.That(exception!.Message, Does.Contain("Cloudstrap.Authentication.ClientCredentials"));
                Assert.That(exception!.Message, Does.Not.Contain("AddUserAccessToken"));
            });
        }

        [Test]
        public void BothFlagsTrue_WithNoProviderRegistered_FailsListingBothFlagsAndBothPackages()
        {
            // Arrange — both flags on, nothing implements either seam
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";
            config["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            using IHost host = builder.Build();

            // Act
            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
                () => host.Services.GetRequiredService<ICatalogClient>());

            // Assert — one exception, aggregating both flags and both packages
            Assert.Multiple(() =>
            {
                Assert.That(
                    exception!.Message,
                    Does.Contain("Cloudstrap:HttpClients:Catalog:AddUserAccessToken"));
                Assert.That(exception!.Message, Does.Contain("Cloudstrap.Authentication.OpenIdConnect"));
                Assert.That(
                    exception!.Message,
                    Does.Contain("Cloudstrap:HttpClients:Catalog:AddClientAccessToken"));
                Assert.That(exception!.Message, Does.Contain("Cloudstrap.Authentication.ClientCredentials"));
            });
        }

        [Test]
        public async Task FlaggedClient_ProviderRegisteredAfterClientRegistration_StillWorks()
        {
            // Arrange — the auth package's call comes after the client's, as it may in any Program.cs
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            RecordingUserTokenHandlerProvider provider = new();
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider>(provider);

            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — resolution is lazy, so registration order never matters
            Assert.That(capturing.LastRequest!.Headers.GetValues("X-Token-Kind"), Is.EqualTo(_userTokenKind));
        }

        [Test]
        public async Task TokenHandler_RunsBeforeCorrelationHandler()
        {
            // Arrange
            Dictionary<string, string?> config = TestHostBuilder.CatalogSection();
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            RecordingUserTokenHandlerProvider provider = new();
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddSingleton<IUserAccessTokenHandlerProvider>(provider);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);

            using IHost host = builder.Build();
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-token";
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the token handler is upstream of correlation: it does not yet see the header the
            // request eventually carries
            Assert.Multiple(() =>
            {
                Assert.That(provider.CreatedHandlers[0].SawCorrelationHeader, Is.False);
                Assert.That(capturing.LastRequest!.Headers.Contains("X-Correlation-ID"), Is.True);
            });
        }
    }

    /// <summary>
    /// Stands in for the Cloudstrap.Authentication.OpenIdConnect implementation of the user-token seam:
    /// records every request the wiring makes and returns handlers that mark the outgoing request.
    /// </summary>
    internal sealed class RecordingUserTokenHandlerProvider : IUserAccessTokenHandlerProvider
    {
        public List<(string ClientName, TokenRequestOptions? TokenRequest)> Calls { get; } = [];

        public List<MarkerHandler> CreatedHandlers { get; } = [];

        public DelegatingHandler CreateUserTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
            Calls.Add((clientName, tokenRequest));
            MarkerHandler handler = new("user");
            CreatedHandlers.Add(handler);

            return handler;
        }
    }

    /// <summary>
    /// Stands in for the Cloudstrap.Authentication.ClientCredentials implementation of the client-token
    /// seam: records every request the wiring makes and returns handlers that mark the outgoing request.
    /// </summary>
    internal sealed class RecordingClientTokenHandlerProvider : IClientAccessTokenHandlerProvider
    {
        public List<(string ClientName, TokenRequestOptions? TokenRequest)> Calls { get; } = [];

        public List<MarkerHandler> CreatedHandlers { get; } = [];

        public DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
            Calls.Add((clientName, tokenRequest));
            MarkerHandler handler = new("client");
            CreatedHandlers.Add(handler);

            return handler;
        }
    }

    /// <summary>
    /// Marks the request with the token kind it stands for, and records whether the correlation header was
    /// already present when it ran — which pins its position in the chain.
    /// </summary>
    internal sealed class MarkerHandler : DelegatingHandler
    {
        private readonly string _kind;

        public MarkerHandler(string kind)
        {
            _kind = kind;
        }

        public bool SawCorrelationHeader
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            SawCorrelationHeader = request.Headers.Contains("X-Correlation-ID");
            request.Headers.Add("X-Token-Kind", _kind);

            return base.SendAsync(request, cancellationToken);
        }
    }
}
