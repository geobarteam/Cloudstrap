namespace Cloudstrap.TestIdentityProvider
{
    using Microsoft.Data.Sqlite;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using OpenIddict.Server;

    /// <summary>
    /// Registers the test identity provider: a real OpenIddict server supporting the
    /// client-credentials grant — plus, when interactive users or redirect URIs are configured, the
    /// authorization-code (PKCE-required) and refresh-token grants — backed by a per-instance
    /// in-memory SQLite store, signing with an ephemeral per-process key and issuing unencrypted
    /// (validatable) JWT access tokens.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// The cookie authentication scheme holding the interactive login session at the provider.
        /// </summary>
        internal const string SessionAuthenticationScheme = "TestIdentityProviderSession";

        /// <summary>
        /// Adds the test identity provider services to the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="configure">An optional delegate configuring <see cref="TestIdentityProviderOptions"/>.</param>
        /// <returns>The service collection, so further registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Each registration owns one open <see cref="SqliteConnection"/> to <c>Data Source=:memory:</c>
        /// for its whole lifetime, so two hosts are two fully isolated identity providers: separate
        /// client stores and separate ephemeral signing keys.
        /// </para>
        /// <para>
        /// Implicit and hybrid flows are not enabled and not configurable. The token endpoint is
        /// <c>/connect/token</c>; discovery and JWKS are served at their standard well-known paths.
        /// </para>
        /// </remarks>
        public static IServiceCollection AddCloudstrapTestIdentityProvider(
            this IServiceCollection services,
            Action<TestIdentityProviderOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (configure is not null)
            {
                services.Configure(configure);
            }

            services.AddSingleton(static _ =>
            {
                SqliteConnection connection = new("Data Source=:memory:");
                connection.Open();

                return connection;
            });

            services.AddDbContext<TestIdentityProviderDbContext>(static (provider, options) =>
            {
                options.UseSqlite(provider.GetRequiredService<SqliteConnection>());
                options.UseOpenIddict();
            });

            services.AddOpenIddict()
                .AddCore(static core => core.UseEntityFrameworkCore().UseDbContext<TestIdentityProviderDbContext>())
                .AddServer(static server =>
                {
                    server.AllowClientCredentialsFlow();
                    server.SetTokenEndpointUris("connect/token");
                    server.AddEphemeralEncryptionKey();
                    server.AddEphemeralSigningKey();
                    server.DisableAccessTokenEncryption();
                    server.UseAspNetCore()
                        .EnableTokenEndpointPassthrough()
                        .EnableAuthorizationEndpointPassthrough()
                        .EnableUserInfoEndpointPassthrough()
                        .EnableEndSessionEndpointPassthrough()
                        .DisableTransportSecurityRequirement();
                });

            // The interactive grants themselves (authorization code + PKCE requirement + refresh) are
            // enabled by TestIdentityProviderServerOptionsConfigurator only when users or redirect URIs
            // are configured, so a client-credentials-only provider keeps exactly its original discovery
            // document. The session cookie scheme carries the login-form session.
            services.AddAuthentication().AddCookie(SessionAuthenticationScheme, static cookie =>
                cookie.Cookie.Name = ".TestIdentityProvider.Session");

            services.AddSingleton<TestIdentityProviderCounters>();
            services.AddSingleton<IConfigureOptions<OpenIddictServerOptions>, TestIdentityProviderServerOptionsConfigurator>();
            services.AddHostedService<TestIdentityProviderSeeder>();

            return services;
        }
    }
}
