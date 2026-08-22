namespace Cloudstrap.Authentication.ClientCredentials.Tests
{
    using Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Cloudstrap.TestIdentityProvider;
    using Duende.AccessTokenManagement;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// The credential's whole journey is secret-optional and secret-free (AC-CC11, AC-CC5's logs half):
    /// a consumer-registered assertion service replaces the secret and is never overwritten, and a
    /// configured secret value appears in no log line and no exception.
    /// </summary>
    [TestFixture]
    public sealed class CredentialTests
    {
        [Test]
        public async Task ConsumerRegisteredClientAssertionService_WithNoSecret_SendsTheAssertionAndSucceeds()
        {
            // Arrange — no ClientSecret configured anywhere; the consumer supplies the assertion. The
            // token response is scripted at the backchannel: the assertion-carrying request is the
            // observable (the test IdP deliberately does not learn private_key_jwt).
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "https://catalog.contoso.example/",
                ["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true",
                ["Cloudstrap:ClientCredentials:TokenEndpoint"] = "https://sts.contoso.example/connect/token",
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
                ["Cloudstrap:ClientCredentials:Scope"] = "catalog.read",
            });
            CapturingPrimaryHandler capturing = new();
            FormCapturingTokenEndpointHandler tokenEndpoint = new();
            RecordingClientAssertionService assertionService = new();
            builder.Services.AddSingleton<IClientAssertionService>(assertionService);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(configurator =>
                configurator.Backchannel = http => http.ConfigurePrimaryHttpMessageHandler(() => tokenEndpoint));

            using IHost host = builder.Build();

            // Act — startup validation passes without a secret, and the flagged call succeeds
            Assert.DoesNotThrow(host.Start);
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the token request carried the assertion and no secret; the consumer's service was
            // the one invoked (Cloudstrap never overwrote it)
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccessStatusCode, Is.True);
                Assert.That(capturing.LastRequest!.Headers.Authorization, Is.Not.Null);
                Assert.That(tokenEndpoint.LastForm, Does.ContainKey("client_assertion"));
                Assert.That(
                    tokenEndpoint.LastForm["client_assertion"],
                    Is.EqualTo("placeholder-client-assertion-jwt"));
                Assert.That(tokenEndpoint.LastForm, Does.Not.ContainKey("client_secret"));
                Assert.That(assertionService.InvocationCount, Is.GreaterThanOrEqualTo(1));
            });
        }

        [Test]
        public async Task BothSecretAndAssertionPresent_TheAssertionWins_AndTheStartupLogStatesTheCredentialType()
        {
            // Arrange — a secret is configured AND the consumer registered an assertion service
            HostApplicationBuilder builder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "https://catalog.contoso.example/",
                ["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true",
                ["Cloudstrap:ClientCredentials:TokenEndpoint"] = "https://sts.contoso.example/connect/token",
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
                ["Cloudstrap:ClientCredentials:ClientSecret"] = "placeholder-not-a-real-secret",
            });
            CapturingPrimaryHandler capturing = new();
            FormCapturingTokenEndpointHandler tokenEndpoint = new();
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);
            builder.Services.AddSingleton<IClientAssertionService>(new RecordingClientAssertionService());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapClientCredentials(configurator =>
                configurator.Backchannel = http => http.ConfigurePrimaryHttpMessageHandler(() => tokenEndpoint));

            using IHost host = builder.Build();
            host.Start();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — Duende's own precedence made visible, and the startup log states the credential
            // type in force (a name, never a value)
            Assert.Multiple(() =>
            {
                Assert.That(tokenEndpoint.LastForm, Does.ContainKey("client_assertion"));
                Assert.That(
                    logs.Entries.Count(entry => entry.Level == LogLevel.Information
                        && entry.Message.Contains("client assertion", StringComparison.OrdinalIgnoreCase)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void SecretValueNeverAppearsInLogsOrExceptions()
        {
            // Arrange — Debug-and-below capture across startup, a successful acquisition and a failed one
            const string secretValue = "placeholder-not-a-real-secret";
            using TestIdentityProviderHost identityProvider = ClientCredentialsTestHost.StartIdentityProvider();

            CapturingLoggerProvider successLogs = new();
            HostApplicationBuilder successBuilder = ClientCredentialsTestHost.CreateBuilder(
                ClientCredentialsTestHost.DefaultConfig(identityProvider));
            successBuilder.Logging.SetMinimumLevel(LogLevel.Trace);
            successBuilder.Logging.AddProvider(successLogs);
            CapturingPrimaryHandler capturing = new();
            successBuilder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            successBuilder.Services.AddCloudstrapClientCredentials(
                ClientCredentialsTestHost.BackchannelTo(identityProvider));

            CapturingLoggerProvider failureLogs = new();
            HostApplicationBuilder failureBuilder = ClientCredentialsTestHost.CreateBuilder(new Dictionary<string, string?>
            {
                ["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "https://catalog.contoso.example/",
                ["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true",
                ["Cloudstrap:ClientCredentials:TokenEndpoint"] = "https://sts.contoso.example/connect/token",
                ["Cloudstrap:ClientCredentials:ClientId"] = "contoso-service",
                ["Cloudstrap:ClientCredentials:ClientSecret"] = secretValue,
            });
            failureBuilder.Logging.SetMinimumLevel(LogLevel.Trace);
            failureBuilder.Logging.AddProvider(failureLogs);
            failureBuilder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            failureBuilder.Services.AddCloudstrapClientCredentials(configurator =>
                configurator.Backchannel = http => http.ConfigurePrimaryHttpMessageHandler(() => new ThrowingHandler()));

            // Act — a successful acquisition and a failed one, both fully logged
            Exception? failureException = null;
            using (IHost successHost = successBuilder.Build())
            {
                successHost.Start();
                ICatalogClient client = successHost.Services.GetRequiredService<ICatalogClient>();
                using HttpResponseMessage response = client.Client
                    .GetAsync(new Uri("orders", UriKind.Relative)).GetAwaiter().GetResult();
            }

            using (IHost failureHost = failureBuilder.Build())
            {
                failureHost.Start();
                ICatalogClient client = failureHost.Services.GetRequiredService<ICatalogClient>();
                failureException = Assert.CatchAsync(async () =>
                    await client.Client.GetAsync(new Uri("orders", UriKind.Relative)));
            }

            // Assert — the secret value appears in no log line, no exception message, no inner exception
            List<string> allLogLines =
            [
                .. successLogs.Entries.Select(entry => entry.Message),
                .. failureLogs.Entries.Select(entry => entry.Message),
            ];
            Assert.Multiple(() =>
            {
                Assert.That(allLogLines, Has.None.Contains(secretValue));
                Assert.That(failureException!.ToString(), Does.Not.Contain(secretValue));
            });
        }
    }
}
