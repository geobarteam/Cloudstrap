namespace Cloudstrap.Authentication.ClientCredentials.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Cloudstrap.TestIdentityProvider;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// Acquisition and validation interoperate end to end (AC-CC14, in-process): three real hosts
    /// chained — the test identity provider, a #5-protected API validating against its real discovery
    /// document and JWKS, and a consumer whose flagged typed client calls the API.
    /// </summary>
    [TestFixture]
    public sealed class EndToEndInteropTests
    {
        [Test]
        public async Task AcquiredToken_IsAcceptedByACloudstrapJwtBearerProtectedApi()
        {
            // Arrange — IdP → protected API → consumer, all in-process
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            await using ProtectedApiHost api = await ProtectedApiHost.StartAsync(identityProvider);
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "http://localhost/";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(api.CreateHandler);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(
                new Uri("api/v1/machine-echo/status", UriKind.Relative));
            string json = await response.Content.ReadAsStringAsync();

            // Assert — 200, and the API saw the configured client through genuine OIDC metadata (AC-CC14)
            Assert.That(
                response.StatusCode,
                Is.EqualTo(HttpStatusCode.OK),
                () => $"Expected 200 but got {(int)response.StatusCode}: {json}");
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.That(
                document.RootElement.GetProperty("clientId").GetString(),
                Is.EqualTo(ClientCredentialsTestHost.ClientId));
        }

        [Test]
        public async Task ProtectedApi_WithoutAToken_Returns401()
        {
            // Arrange — the same API called by an unflagged client: the control proving validation is
            // real, not permissive
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            await using ProtectedApiHost api = await ProtectedApiHost.StartAsync(identityProvider);
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Orders:BaseAddress"] = "http://localhost/";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            builder.Services.AddCloudstrapHttpServiceClient<IOrdersClient, OrdersClient>("Orders")
                .ConfigurePrimaryHttpMessageHandler(api.CreateHandler);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            IOrdersClient client = host.Services.GetRequiredService<IOrdersClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(
                new Uri("api/v1/machine-echo/status", UriKind.Relative));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task TokenMintedForAnotherAudience_IsRejectedWith401()
        {
            // Arrange — a second IdP client whose tokens carry a different audience
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider(
                options => options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "contoso-other",
                    ClientSecret = "placeholder-other-secret",
                    Scopes = { "other.scope" },
                    Audiences = { "other-api" },
                }));
            await using ProtectedApiHost api = await ProtectedApiHost.StartAsync(identityProvider);
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "http://localhost/";
            config["Cloudstrap:ClientCredentials:ClientId"] = "contoso-other";
            config["Cloudstrap:ClientCredentials:ClientSecret"] = "placeholder-other-secret";
            config["Cloudstrap:ClientCredentials:Scope"] = "other.scope";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(api.CreateHandler);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act — acquisition succeeds, validation rejects: the interop actually validates
            using HttpResponseMessage response = await client.Client.GetAsync(
                new Uri("api/v1/machine-echo/status", UriKind.Relative));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(identityProvider.TokenRequestCount, Is.GreaterThanOrEqualTo(1));
            });
        }
    }
}
