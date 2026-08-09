namespace Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure
{
    using System.Security.Claims;
    using Cloudstrap.TestIdentityProvider;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Hosting.Server;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    /// <summary>
    /// The fixture idiom of this test project (plan mechanic (b)): a <see cref="WebApplication"/> on
    /// TestServer at <c>https://app.example.com/</c> with in-memory <c>Cloudstrap:</c> configuration,
    /// and the extended test identity provider in-process at <c>https://idp.example.com/</c> wired as
    /// the OIDC backchannel — so a <see cref="BrowserlessUserAgent"/> can drive the full sign-in with
    /// no sockets.
    /// </summary>
    internal sealed class OidcTestHost : IAsyncDisposable
    {
        public const string ClientId = "contoso-web";
        public const string ClientSecret = "placeholder-not-a-real-secret";
        public const string Username = "contoso.user";
        public const string UserDisplayName = "Contoso User";
        public const string UserRole = "tester";
        public const string Password = "placeholder-not-a-real-password";

        public static readonly Uri AppBase = new("https://app.example.com/");
        public static readonly Uri IdpBase = new("https://idp.example.com/");

        private OidcTestHost(TestIdentityProviderHost identityProvider, WebApplication app, TestServer appServer)
        {
            IdentityProvider = identityProvider;
            App = app;
            AppServer = appServer;
        }

        /// <summary>
        /// Gets the in-process identity provider.
        /// </summary>
        public TestIdentityProviderHost IdentityProvider
        {
            get;
        }

        /// <summary>
        /// Gets the application under test.
        /// </summary>
        public WebApplication App
        {
            get;
        }

        /// <summary>
        /// Gets the application's TestServer.
        /// </summary>
        public TestServer AppServer
        {
            get;
        }

        /// <summary>
        /// The standard <c>Cloudstrap:OpenIdConnect</c> section pointed at the in-process provider.
        /// </summary>
        /// <returns>The configuration values.</returns>
        public static Dictionary<string, string?> DefaultConfig() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Cloudstrap:OpenIdConnect:Authority"] = IdpBase.AbsoluteUri,
            ["Cloudstrap:OpenIdConnect:ClientId"] = ClientId,
            ["Cloudstrap:OpenIdConnect:ClientSecret"] = ClientSecret,
            ["Logging:LogLevel:Default"] = "Warning",
        };

        /// <summary>
        /// Starts the identity provider and the application.
        /// </summary>
        /// <param name="configuration">Configuration entries layered over <see cref="DefaultConfig"/>.</param>
        /// <param name="configure">The test's configurator hook, applied on top of the backchannel wiring.</param>
        /// <param name="configureIdentityProvider">Extra identity provider configuration.</param>
        /// <param name="mapEndpoints">Extra endpoints mapped after the standard fixture endpoints.</param>
        /// <param name="afterRegistration">
        /// Builder work applied <em>after</em> the host's own <c>AddCloudstrapOpenIdConnect</c> call —
        /// where the idempotence test places its second registration and other tests register typed
        /// clients or the client-credentials package (the running provider is passed for backchannel
        /// wiring).
        /// </param>
        /// <param name="stripEndSessionEndpoint">
        /// When <see langword="true"/>, the application sees the provider's real metadata and signing
        /// keys as a static configuration whose <c>end_session_endpoint</c> has been removed — the
        /// "provider without an end-session endpoint" edge case.
        /// </param>
        /// <returns>The started fixture.</returns>
        public static async Task<OidcTestHost> StartAsync(
            Dictionary<string, string?>? configuration = null,
            Action<CloudstrapOpenIdConnectConfigurator>? configure = null,
            Action<TestIdentityProviderOptions>? configureIdentityProvider = null,
            Action<WebApplication>? mapEndpoints = null,
            Action<WebApplicationBuilder, TestIdentityProviderHost>? afterRegistration = null,
            bool stripEndSessionEndpoint = false)
        {
            TestIdentityProviderHost identityProvider = TestIdentityProviderHost.StartInProcess(
                options =>
                {
                    TestIdentityProviderClient client = new()
                    {
                        ClientId = ClientId,
                        ClientSecret = ClientSecret,
                        Scopes = { "catalog.read" },
                        Audiences = { "contoso-api" },
                        RedirectUris = { new Uri(AppBase, "signin-oidc") },
                        PostLogoutRedirectUris = { new Uri(AppBase, "signout-callback-oidc") },
                    };
                    options.Clients.Add(client);
                    options.Users.Add(new TestIdentityProviderUser
                    {
                        Username = Username,
                        Password = Password,
                        Claims =
                        {
                            ["name"] = [UserDisplayName],
                            ["role"] = [UserRole],
                        },
                    });
                    configureIdentityProvider?.Invoke(options);
                },
                baseAddress: IdpBase);

            try
            {
                Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration? staticConfiguration =
                    stripEndSessionEndpoint
                        ? await FetchConfigurationWithoutEndSessionAsync(identityProvider)
                        : null;

                WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    EnvironmentName = "Production",
                    ApplicationName = "Cloudstrap.Authentication.OpenIdConnect.Tests",
                });
                builder.Configuration.AddInMemoryCollection(Compose(configuration));
                builder.WebHost.UseTestServer();

                // Development-grade container validation even though the fixture runs as Production,
                // so a scoped service resolved from the root provider fails here exactly as it would
                // in a consumer's Development run.
                builder.WebHost.UseDefaultServiceProvider(static providerOptions =>
                {
                    providerOptions.ValidateScopes = true;
                    providerOptions.ValidateOnBuild = true;
                });

                builder.Services.AddCloudstrapOpenIdConnect(configurator =>
                {
                    configure?.Invoke(configurator);
                    Action<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>? testHook =
                        configurator.OpenIdConnect;
                    configurator.OpenIdConnect = oidc =>
                    {
                        oidc.BackchannelHttpHandler = identityProvider.CreateHandler();
                        if (staticConfiguration is not null)
                        {
                            oidc.Configuration = staticConfiguration;
                        }

                        testHook?.Invoke(oidc);
                    };
                });

                afterRegistration?.Invoke(builder, identityProvider);

                WebApplication app = builder.Build();
                MapFixtureEndpoints(app);
                mapEndpoints?.Invoke(app);

                await app.StartAsync(TestContext.CurrentContext.CancellationToken);

                TestServer appServer = (TestServer)app.Services.GetRequiredService<IServer>();
                appServer.BaseAddress = AppBase;

                return new OidcTestHost(identityProvider, app, appServer);
            }
            catch
            {
                identityProvider.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a fresh browserless user agent — its own cookie jar, so two agents are two users'
        /// browsers.
        /// </summary>
        /// <returns>The agent. The caller owns and disposes it.</returns>
        public BrowserlessUserAgent CreateAgent() =>
            new(AppBase, AppServer.CreateHandler(), IdpBase, IdentityProvider.CreateHandler());

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            IdentityProvider.Dispose();
        }

        /// <summary>
        /// Maps the fixture's standard endpoints. With the D-6 fallback policy in force, everything
        /// under <c>/protected</c> requires a signed-in user without any attribute; <c>/open</c> is the
        /// documented per-endpoint opt-out.
        /// </summary>
        /// <param name="app">The application to map into.</param>
        private static void MapFixtureEndpoints(WebApplication app)
        {
            app.MapGet("/protected/page", (ClaimsPrincipal user) => Results.Json(new
            {
                authenticated = user.Identity?.IsAuthenticated ?? false,
                identityName = user.Identity?.Name,
                sub = user.FindFirst("sub")?.Value,
                name = user.FindFirst("name")?.Value,
                role = user.FindFirst("role")?.Value,
                legacyNameClaim = user.FindFirst(ClaimTypes.Name)?.Value,
            }));

            app.MapGet("/protected/tokens", async (HttpContext context) =>
            {
                string? accessToken = await context.GetTokenAsync("access_token");
                string? refreshToken = await context.GetTokenAsync("refresh_token");

                return Results.Json(new { accessToken, refreshToken });
            });

            app.MapGet("/protected/session", async (HttpContext context) =>
            {
                AuthenticateResult result = await context.AuthenticateAsync();

                return Results.Json(new { expiresUtc = result.Properties?.ExpiresUtc });
            });

            app.MapGet("/open", () => Results.Text("open")).AllowAnonymous();

            app.MapGet("/", () => Results.Text("home")).AllowAnonymous();
        }

        /// <summary>
        /// Fetches the provider's real discovery document and signing keys, and hands them back as a
        /// static configuration with the <c>end_session_endpoint</c> removed.
        /// </summary>
        /// <param name="identityProvider">The running provider.</param>
        /// <returns>The static configuration.</returns>
        private static async Task<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>
            FetchConfigurationWithoutEndSessionAsync(TestIdentityProviderHost identityProvider)
        {
            using HttpClient client = identityProvider.CreateClient();
            string discoveryJson = await client.GetStringAsync(
                new Uri(".well-known/openid-configuration", UriKind.Relative));
            Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration configuration =
                Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration.Create(discoveryJson);

            string jwksJson = await client.GetStringAsync(new Uri(configuration.JwksUri));
            configuration.JsonWebKeySet = new Microsoft.IdentityModel.Tokens.JsonWebKeySet(jwksJson);
            foreach (Microsoft.IdentityModel.Tokens.SecurityKey key in configuration.JsonWebKeySet.GetSigningKeys())
            {
                configuration.SigningKeys.Add(key);
            }

            configuration.EndSessionEndpoint = null;

            return configuration;
        }

        /// <summary>
        /// Layers the supplied entries over the standard section.
        /// </summary>
        /// <param name="configuration">The entries to layer on top, if any.</param>
        /// <returns>The composed configuration dictionary.</returns>
        private static Dictionary<string, string?> Compose(IDictionary<string, string?>? configuration)
        {
            Dictionary<string, string?> composed = DefaultConfig();

            if (configuration is not null)
            {
                foreach (KeyValuePair<string, string?> entry in configuration)
                {
                    composed[entry.Key] = entry.Value;
                }
            }

            return composed;
        }
    }
}
