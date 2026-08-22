namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Cloudstrap.Extensions;
    using Duende.AccessTokenManagement.OpenIdConnect;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Authentication.OpenIdConnect;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registers interactive OpenID Connect login on the stock handlers and Duende
    /// AccessTokenManagement: one call, and the application signs users in with authorization code +
    /// PKCE, holds the session in a hardened <c>__Host-Cloudstrap</c> cookie, and refreshes user tokens
    /// transparently.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds interactive OpenID Connect login to the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="configure">
        /// An optional delegate for the code-level hooks configuration values cannot express.
        /// </param>
        /// <returns>The service collection, so further registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Settings bind from the <see cref="CloudstrapOpenIdConnectOptions.SectionName"/> section and
        /// are validated at host startup: a missing or malformed value fails fast naming the exact key,
        /// never echoing a configured value.
        /// </para>
        /// <para>
        /// The flow is authorization code with PKCE (S256) and <c>form_post</c> — not configurable: no
        /// implicit, no hybrid, no PKCE opt-out. Tokens are stored in the authentication session (the
        /// cookie ticket, <c>SaveTokens</c>) — Duende AccessTokenManagement's requirement and decision
        /// D-2; no server-side ticket store, distributed token store or cache is registered.
        /// </para>
        /// <para>
        /// The default scheme (cookie), challenge scheme (OpenID Connect) and sign-out scheme are set
        /// deterministically through a post-configuration step, so they hold whichever order this call
        /// and <c>AddCloudstrapJwtBearer</c> appear in.
        /// </para>
        /// <para>
        /// The registration is idempotent — calling it twice registers everything once. It registers
        /// no inbound JWT validation (<c>Cloudstrap.WebApi</c>'s <c>AddCloudstrapJwtBearer</c>), no
        /// client-credentials services (<c>Cloudstrap.Authentication.ClientCredentials</c>) and no
        /// Blazor helpers.
        /// </para>
        /// </remarks>
        public static IServiceCollection AddCloudstrapOpenIdConnect(
            this IServiceCollection services,
            Action<CloudstrapOpenIdConnectConfigurator>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
            {
                return services;
            }

            services.AddSingleton<RegistrationMarker>();
            services.AddSingleton<AuthenticationEndpointsState>();

            CloudstrapOpenIdConnectConfigurator configurator = new();
            configure?.Invoke(configurator);

            services.AddOptions<CloudstrapOpenIdConnectOptions>()
                .BindConfiguration(CloudstrapOpenIdConnectOptions.SectionName)
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<CloudstrapOpenIdConnectOptions>, CloudstrapOpenIdConnectOptionsValidator>();
            services.AddSingleton<IValidateOptions<CloudstrapOpenIdConnectOptions>, OpenIdConnectShapeValidator>();

            services.AddAuthentication()
                .AddCookie(CloudstrapOpenIdConnect.CookieScheme)
                .AddOpenIdConnect(CloudstrapOpenIdConnect.ChallengeScheme, static _ => { });

            services.AddOptions<CookieAuthenticationOptions>(CloudstrapOpenIdConnect.CookieScheme)
                .Configure<IOptions<CloudstrapOpenIdConnectOptions>>(static (cookie, settings) =>
                    ConfigureCookie(cookie, settings.Value));

            services.AddOptions<OpenIdConnectOptions>(CloudstrapOpenIdConnect.ChallengeScheme)
                .Configure<IOptions<CloudstrapOpenIdConnectOptions>, IHostEnvironment>(
                    static (oidc, settings, environment) =>
                        ConfigureOpenIdConnect(oidc, settings.Value, environment));

            // Deterministic default schemes, whichever order the consumer registered the auth packages
            // in: stock AddAuthentication(string) is last-call-wins, a post-configuration step is not.
            services.PostConfigure<AuthenticationOptions>(static authentication =>
            {
                authentication.DefaultScheme = CloudstrapOpenIdConnect.CookieScheme;
                authentication.DefaultChallengeScheme = CloudstrapOpenIdConnect.ChallengeScheme;
                authentication.DefaultSignOutScheme = CloudstrapOpenIdConnect.ChallengeScheme;
            });

            services.AddOpenIdConnectAccessTokenManagement();

            // The user-token half of the #4/#9 seam: every Cloudstrap typed client flagged
            // AddUserAccessToken gets its handler from this provider, resolved lazily when the
            // client's pipeline is built.
            services.AddHttpContextAccessor();
            services.AddSingleton<IUserAccessTokenHandlerProvider, UserAccessTokenHandlerProvider>();

            services.AddAuthorization();
            services.AddOptions<AuthorizationOptions>()
                .Configure<IOptions<CloudstrapOpenIdConnectOptions>>(static (authorization, settings) =>
                {
                    if (settings.Value.RequireAuthenticatedEndpoints)
                    {
                        authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
                            .RequireAuthenticatedUser()
                            .Build();
                    }
                });

            // The configurator hooks run after every Cloudstrap-owned configuration step above, so
            // they have the final say over it.
            if (configurator.TokenManagement is not null)
            {
                services.Configure(configurator.TokenManagement);
            }

            if (configurator.Cookie is not null)
            {
                services.Configure(CloudstrapOpenIdConnect.CookieScheme, configurator.Cookie);
            }

            if (configurator.OpenIdConnect is not null)
            {
                services.Configure(CloudstrapOpenIdConnect.ChallengeScheme, configurator.OpenIdConnect);
            }

            services.AddHostedService<OpenIdConnectStartupLogger>();

            return services;
        }

        /// <summary>
        /// Applies the D-1 cookie posture: the configured name, lifetime and sliding behavior, plus the
        /// hardened constants — <c>HttpOnly</c>, <c>Secure</c> always, <c>SameSite=Lax</c> (Strict
        /// would break the OpenID Connect callback navigation) and <c>Path=/</c>.
        /// </summary>
        /// <param name="cookie">The stock cookie options being configured.</param>
        /// <param name="settings">The bound Cloudstrap settings.</param>
        private static void ConfigureCookie(
            CookieAuthenticationOptions cookie,
            CloudstrapOpenIdConnectOptions settings)
        {
            cookie.Cookie.Name = settings.Cookie.Name;
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            cookie.Cookie.SameSite = SameSiteMode.Lax;
            cookie.Cookie.Path = "/";
            cookie.ExpireTimeSpan = settings.Cookie.Lifetime;
            cookie.SlidingExpiration = settings.Cookie.SlidingExpiration;

            // AC-OIDC9: a request that authenticates itself with a bearer header belongs to the JWT
            // scheme (when #5 registered it) — it must never be served from a browser session.
            cookie.ForwardDefaultSelector = BearerCoexistence.SelectForwardScheme;
        }

        /// <summary>
        /// Applies the protocol posture: authorization code + PKCE with <c>form_post</c>, tokens saved
        /// into the authentication session (D-2 — deliberately hard-set, not a toggle), claims as
        /// issued, and the configured scope replacing the stock defaults entirely.
        /// </summary>
        /// <param name="oidc">The stock OpenID Connect options being configured.</param>
        /// <param name="settings">The bound Cloudstrap settings.</param>
        /// <param name="environment">The hosting environment, deciding the HTTPS-metadata default.</param>
        private static void ConfigureOpenIdConnect(
            OpenIdConnectOptions oidc,
            CloudstrapOpenIdConnectOptions settings,
            IHostEnvironment environment)
        {
            oidc.Authority = settings.Authority;
            oidc.ClientId = settings.ClientId;

            if (!string.IsNullOrEmpty(settings.ClientSecret))
            {
                oidc.ClientSecret = settings.ClientSecret;
            }

            oidc.ResponseType = "code";
            oidc.ResponseMode = "form_post";
            oidc.UsePkce = true;
            oidc.SaveTokens = true;
            oidc.MapInboundClaims = settings.MapInboundClaims;
            oidc.TokenValidationParameters.NameClaimType = "name";
            oidc.TokenValidationParameters.RoleClaimType = "role";
            oidc.CallbackPath = settings.CallbackPath;
            oidc.SignedOutCallbackPath = settings.SignedOutCallbackPath;
            oidc.SignInScheme = CloudstrapOpenIdConnect.CookieScheme;
            oidc.RequireHttpsMetadata = settings.RequireHttpsMetadata ?? !environment.IsDevelopment();

            // AC-OIDC9: a failed bearer request must get a 401 through the JWT scheme, never a login
            // redirect — the challenge for a bearer-carrying request forwards to it (when registered).
            oidc.ForwardDefaultSelector = BearerCoexistence.SelectForwardScheme;

            // Bare SignOut() reaches only the default sign-out scheme (the OpenID Connect scheme).
            // Ending the provider session while leaving the local cookie alive would look like a
            // sign-out and silently keep the user in — so signing out of the challenge scheme always
            // clears the cookie session too.
            oidc.Events.OnRedirectToIdentityProviderForSignOut = static async redirectContext =>
                await redirectContext.HttpContext.SignOutAsync(CloudstrapOpenIdConnect.CookieScheme);

            // Cleared before applying, so the stock defaults ("openid profile") cannot silently append
            // to — or survive — the configured scope space.
            oidc.Scope.Clear();
            foreach (string scope in settings.Scope.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                oidc.Scope.Add(scope);
            }
        }

        /// <summary>
        /// Marks the collection as already carrying this package's registrations, so a second call is a
        /// no-op (AC-OIDC10).
        /// </summary>
        private sealed class RegistrationMarker;
    }
}
