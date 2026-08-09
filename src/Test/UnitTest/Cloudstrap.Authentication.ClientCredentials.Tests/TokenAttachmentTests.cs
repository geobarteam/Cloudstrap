namespace Cloudstrap.Authentication.ClientCredentials.Tests
{
    using System.Net.Http.Headers;
    using Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Cloudstrap.TestIdentityProvider;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.IdentityModel.JsonWebTokens;
    using NUnit.Framework;

    /// <summary>
    /// The deliverable's headline (AC-CC1): a typed client already flagged in configuration transparently
    /// carries a bearer token issued by a real token endpoint after one registration call — and nothing
    /// else about the pipeline changes.
    /// </summary>
    [TestFixture]
    public sealed class TokenAttachmentTests
    {
        private static readonly string[] _expectedCorrelationIds = ["corr-cc"];

        [Test]
        public async Task FlaggedClient_AfterOneRegistrationCall_CarriesABearerTokenIssuedByTheTestIdp()
        {
            // Arrange — the shipped typed-client registration plus exactly one new call; the test contains
            // no other consumer code
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — a Bearer token, and specifically one the test identity provider issued for the
            // configured client
            AuthenticationHeaderValue? authorization = capturing.LastRequest!.Headers.Authorization;
            Assert.That(authorization, Is.Not.Null);
            JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(authorization!.Parameter);
            Assert.Multiple(() =>
            {
                Assert.That(authorization.Scheme, Is.EqualTo("Bearer"));
                Assert.That(new Uri(jwt.Issuer), Is.EqualTo(identityProvider.BaseAddress));
                Assert.That(jwt.GetClaim("client_id").Value, Is.EqualTo(ClientCredentialsTestHost.ClientId));
            });
        }

        [Test]
        public async Task UnflaggedClient_IsUntouched()
        {
            // Arrange — a second client without the flag, in the same host
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Orders:BaseAddress"] = "https://orders.contoso.example/";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            CapturingPrimaryHandler ordersCapturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            builder.Services.AddCloudstrapHttpServiceClient<IOrdersClient, OrdersClient>("Orders")
                .ConfigurePrimaryHttpMessageHandler(() => ordersCapturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            IOrdersClient ordersClient = host.Services.GetRequiredService<IOrdersClient>();

            // Act
            using HttpResponseMessage response =
                await ordersClient.Client.GetAsync(new Uri("baskets", UriKind.Relative));

            // Assert
            Assert.That(ordersCapturing.LastRequest!.Headers.Authorization, Is.Null);
        }

        [Test]
        public async Task PreSetAuthorizationHeader_SurvivesTheClientCredentialsHandler()
        {
            // Arrange
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act — an Authorization header another handler already set (the user-token handler, when a
            // client is flagged for both token kinds) must reach the peer untouched
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("orders", UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "placeholder-pre-set-user-token");
            using HttpResponseMessage response = await client.Client.SendAsync(request);

            // Assert — the pre-set header survives and no machine token is acquired for the request
            Assert.Multiple(() =>
            {
                Assert.That(
                    capturing.LastRequest!.Headers.Authorization!.Parameter,
                    Is.EqualTo("placeholder-pre-set-user-token"));
                Assert.That(identityProvider.TokenRequestCount, Is.Zero);
            });
        }

        [Test]
        public async Task FlaggedClient_StillCarriesTheCorrelationHeader()
        {
            // Arrange
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-cc";
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the #4 ordering is preserved: token handler upstream, correlation still attached
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.Authorization, Is.Not.Null);
                Assert.That(capturing.LastRequest!.Headers.GetValues("X-Correlation-ID"), Is.EqualTo(_expectedCorrelationIds));
            });
        }
    }
}
