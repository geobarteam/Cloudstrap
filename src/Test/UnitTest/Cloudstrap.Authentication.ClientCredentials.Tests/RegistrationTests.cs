namespace Cloudstrap.Authentication.ClientCredentials.Tests
{
    using Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Cloudstrap.TestIdentityProvider;
    using Duende.AccessTokenManagement;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// Startup validation names exact configuration keys and never echoes a configured value (AC-CC4,
    /// AC-CC5's validation half); registration is idempotent and coexists with a consumer's own Duende
    /// setup (AC-CC10).
    /// </summary>
    [TestFixture]
    public sealed class RegistrationTests
    {
        [Test]
        public void MissingSection_FailsStartupNamingTheSection()
        {
            // Arrange — no Cloudstrap:ClientCredentials section at all
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder([]);
            builder.Services.AddCloudstrapClientCredentials();
            using IHost host = builder.Build();

            // Act
            Exception exception = Assert.Catch<Exception>(() => host.Start())!;

            // Assert
            Assert.That(exception.ToString(), Does.Contain("Cloudstrap:ClientCredentials"));
        }

        [Test]
        public void MissingTokenEndpoint_FailsNamingTheKey()
        {
            // Arrange
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
            });
            builder.Services.AddCloudstrapClientCredentials();
            using IHost host = builder.Build();

            // Act
            Exception exception = Assert.Catch<Exception>(() => host.Start())!;

            // Assert
            Assert.That(exception.ToString(), Does.Contain("Cloudstrap:ClientCredentials:TokenEndpoint"));
        }

        [Test]
        public void MissingClientId_FailsNamingTheKey()
        {
            // Arrange
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:ClientCredentials:TokenEndpoint"] = "https://sts.contoso.example/connect/token",
            });
            builder.Services.AddCloudstrapClientCredentials();
            using IHost host = builder.Build();

            // Act
            Exception exception = Assert.Catch<Exception>(() => host.Start())!;

            // Assert
            Assert.That(exception.ToString(), Does.Contain("Cloudstrap:ClientCredentials:ClientId"));
        }

        [Test]
        public void RelativeTokenEndpoint_FailsNamingTheKey()
        {
            // Arrange — present but relative: the no-authority-plus-path rule
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:ClientCredentials:TokenEndpoint"] = "connect/token",
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
            });
            builder.Services.AddCloudstrapClientCredentials();
            using IHost host = builder.Build();

            // Act
            Exception exception = Assert.Catch<Exception>(() => host.Start())!;

            // Assert
            Assert.That(exception.ToString(), Does.Contain("Cloudstrap:ClientCredentials:TokenEndpoint"));
        }

        [Test]
        public void ValidationFailure_NeverEchoesTheConfiguredSecret()
        {
            // Arrange — a secret is configured, another key is broken
            const string secretValue = "placeholder-super-secret-value";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
                ["Cloudstrap:ClientCredentials:ClientSecret"] = secretValue,
            });
            builder.Services.AddCloudstrapClientCredentials();
            using IHost host = builder.Build();

            // Act
            Exception exception = Assert.Catch<Exception>(() => host.Start())!;

            // Assert — the failure names the missing key, and the secret value appears nowhere
            Assert.Multiple(() =>
            {
                Assert.That(exception.ToString(), Does.Contain("Cloudstrap:ClientCredentials:TokenEndpoint"));
                Assert.That(exception.ToString(), Does.Not.Contain(secretValue));
            });
        }

        [Test]
        public async Task CalledTwice_RegistersEverythingOnce()
        {
            // Arrange — the registration call made twice, as two composition-root helpers might
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — one handler, one token request, one provider registration (AC-CC10)
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.GetValues("Authorization").Count(), Is.EqualTo(1));
                Assert.That(identityProvider.TokenRequestCount, Is.EqualTo(1));
                Assert.That(
                    host.Services.GetServices<IClientAccessTokenHandlerProvider>().Count(),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ConsumerOwnDuendeRegistration_CoexistsWithCloudstraps()
        {
            // Arrange — the consumer already manages a Duende client of their own name
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddClientCredentialsTokenManagement()
                .AddClient("contoso-own", client =>
                {
                    client.TokenEndpoint = identityProvider.TokenEndpoint;
                    client.ClientId = ClientId.Parse(ClientCredentialsTestHost.ClientId);
                    client.ClientSecret = ClientSecret.Parse(ClientCredentialsTestHost.ClientSecret);
                    client.Scope = Scope.Parse(ClientCredentialsTestHost.ScopeName);
                    client.HttpClient = new HttpClient(identityProvider.CreateHandler());
                });
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act — both the flagged client and the consumer's own token manager work
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));
            TokenResult<ClientCredentialsToken> ownToken = await host.Services
                .GetRequiredService<IClientCredentialsTokenManager>()
                .GetAccessTokenAsync(
                    ClientCredentialsClientName.Parse("contoso-own"),
                    new TokenRequestParameters(),
                    TestContext.CurrentContext.CancellationToken);

            // Assert — no collision in either direction
            Assert.Multiple(() =>
            {
                Assert.That(capturing.LastRequest!.Headers.Authorization, Is.Not.Null);
                Assert.That(ownToken.Succeeded, Is.True, () => ownToken.FailedResult?.ToString() ?? "failed");
            });
        }

        [Test]
        public void OnNullServices_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(
                () => ((IServiceCollection)null!).AddCloudstrapClientCredentials());
        }
    }
}
