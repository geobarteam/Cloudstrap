namespace Cloudstrap.Authentication.ClientCredentials.Tests
{
    using System.Net;
    using Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Cloudstrap.TestIdentityProvider;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// Failure is loud, lazy and secret-free (AC-CC7, AC-CC8): a rejected, failing or unreachable
    /// identity provider never lets an unauthenticated request out, names token acquisition and the
    /// endpoint, and is logged once — while an IdP outage never stops the application from starting.
    /// </summary>
    [TestFixture]
    public sealed class FailureModeTests
    {
        [Test]
        public void RejectedCredential_FailsNamingAcquisitionAndTheEndpoint_AndSendsNothingDownstream()
        {
            // Arrange — the consumer's configured secret does not match the IdP's
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:ClientCredentials:ClientSecret"] = "placeholder-wrong-secret";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            CapturingPrimaryHandler capturing = new();
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            Exception? exception = Assert.CatchAsync(async () =>
                await client.Client.GetAsync(new Uri("orders", UriKind.Relative)));

            // Assert — loud (acquisition + endpoint named), request-free, once-logged (AC-CC8)
            Assert.Multiple(() =>
            {
                Assert.That(exception!.ToString(), Does.Contain("token acquisition").IgnoreCase);
                Assert.That(exception!.ToString(), Does.Contain(identityProvider.TokenEndpoint.AbsoluteUri));
                Assert.That(capturing.RequestCount, Is.Zero);
                Assert.That(
                    logs.Entries.Count(entry => entry.Level == LogLevel.Error
                        && entry.Message.Contains("acquisition", StringComparison.OrdinalIgnoreCase)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void TokenEndpoint500_SameContract()
        {
            // Arrange — the token endpoint answers 500 to everything
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "https://catalog.contoso.example/",
                ["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true",
                ["Cloudstrap:ClientCredentials:TokenEndpoint"] = "https://sts.contoso.example/connect/token",
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
                ["Cloudstrap:ClientCredentials:ClientSecret"] = "placeholder-not-a-real-secret",
            });
            CapturingPrimaryHandler capturing = new();
            CapturingLoggerProvider logs = new();
            StatusCodeHandler failingBackchannel = new(HttpStatusCode.InternalServerError);
            builder.Logging.AddProvider(logs);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(configurator =>
                configurator.Backchannel = http => http.ConfigurePrimaryHttpMessageHandler(() => failingBackchannel));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            Exception? exception = Assert.CatchAsync(async () =>
                await client.Client.GetAsync(new Uri("orders", UriKind.Relative)));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(exception!.ToString(), Does.Contain("token acquisition").IgnoreCase);
                Assert.That(exception!.ToString(), Does.Contain("https://sts.contoso.example/connect/token"));
                Assert.That(capturing.RequestCount, Is.Zero);
                Assert.That(
                    logs.Entries.Count(entry => entry.Level == LogLevel.Error
                        && entry.Message.Contains("acquisition", StringComparison.OrdinalIgnoreCase)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void TokenEndpointUnreachable_SameContract()
        {
            // Arrange — the token endpoint cannot be reached at all
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "https://catalog.contoso.example/",
                ["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true",
                ["Cloudstrap:ClientCredentials:TokenEndpoint"] = "https://sts.contoso.example/connect/token",
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
                ["Cloudstrap:ClientCredentials:ClientSecret"] = "placeholder-not-a-real-secret",
            });
            CapturingPrimaryHandler capturing = new();
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(configurator =>
                configurator.Backchannel = http => http.ConfigurePrimaryHttpMessageHandler(() => new ThrowingHandler()));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            Exception? exception = Assert.CatchAsync(async () =>
                await client.Client.GetAsync(new Uri("orders", UriKind.Relative)));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(exception!.ToString(), Does.Contain("token acquisition").IgnoreCase);
                Assert.That(exception!.ToString(), Does.Contain("https://sts.contoso.example/connect/token"));
                Assert.That(capturing.RequestCount, Is.Zero);
                Assert.That(
                    logs.Entries.Count(entry => entry.Level == LogLevel.Error
                        && entry.Message.Contains("acquisition", StringComparison.OrdinalIgnoreCase)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void StartupSucceedsWithTheIdpDown_TheFirstCallFailsInstead()
        {
            // Arrange — a valid configuration whose IdP is simply not running (lazy acquisition: a
            // transient IdP outage must never stop a service from starting)
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "https://catalog.contoso.example/",
                ["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true",
                ["Cloudstrap:ClientCredentials:TokenEndpoint"] = "http://127.0.0.1:59999/connect/token",
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
                ["Cloudstrap:ClientCredentials:ClientSecret"] = "placeholder-not-a-real-secret",
            });
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials();

            using IHost host = builder.Build();

            // Act & Assert — the host starts; the first outbound call is what fails
            Assert.DoesNotThrow(host.Start);
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();
            Exception? exception = Assert.CatchAsync(async () =>
                await client.Client.GetAsync(new Uri("orders", UriKind.Relative)));
            Assert.Multiple(() =>
            {
                Assert.That(exception!.ToString(), Does.Contain("http://127.0.0.1:59999/connect/token"));
                Assert.That(capturing.RequestCount, Is.Zero);
            });
        }

        [Test]
        public void UserFlagWithOnlyThisPackageInstalled_FailsFastNamingOnlyTheUserFlagAndTheOpenIdConnectPackage()
        {
            // Arrange — the real package fills the client seam; the user flag is set too (AC-CC7:
            // Step 1 proved the mechanism with doubles, this proves it with the shipped provider)
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();
            Dictionary<string, string?> config = ClientCredentialsTestHost.DefaultConfig(identityProvider);
            config["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(config);
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(ClientCredentialsTestHost.BackchannelTo(identityProvider));

            using IHost host = builder.Build();
            host.Start();

            // Act — client creation itself fails, before any request
            InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
                () => host.Services.GetRequiredService<ICatalogClient>());

            // Assert — only the missing user half is named, pointing at #10's package
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("Cloudstrap:HttpClients:Catalog:AddUserAccessToken"));
                Assert.That(exception!.Message, Does.Contain("Cloudstrap.Authentication.OpenIdConnect"));
                Assert.That(exception!.Message, Does.Not.Contain("AddClientAccessToken"));
                Assert.That(capturing.RequestCount, Is.Zero);
            });
        }
    }
}
