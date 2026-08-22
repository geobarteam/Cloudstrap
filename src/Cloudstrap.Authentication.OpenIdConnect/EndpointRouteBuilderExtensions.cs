namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.OpenIdConnect;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.IdentityModel.Protocols.OpenIdConnect;

    /// <summary>
    /// Maps the opt-in authentication endpoints — login and logout, and nothing else (decision D-5).
    /// Nothing is mapped unless the consumer calls the mapper; the schemes alone map no endpoint.
    /// </summary>
    public static class EndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps exactly two endpoints: <c>GET {LoginPath}</c> (default <c>/account/login</c>), which
        /// challenges to the identity provider and returns to a <em>local</em> URL only, and
        /// <c>GET {LogoutPath}</c> (default <c>/account/logout</c>), which signs out of both the local
        /// session and the identity provider session (RP-initiated).
        /// </summary>
        /// <param name="endpoints">The endpoint route builder to map into.</param>
        /// <returns>The endpoint route builder, so further mappings can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Both endpoints allow anonymous callers, so the <c>RequireAuthenticatedEndpoints</c> fallback
        /// policy cannot make login itself require a login.
        /// </para>
        /// <para>
        /// A caller-supplied <c>returnUrl</c> is honored only when it is local; anything else falls
        /// back to <c>/</c>. No user-info endpoint, no front-/back-channel logout, and no consent,
        /// registration or account pages are mapped — deliberately.
        /// </para>
        /// </remarks>
        public static IEndpointRouteBuilder MapCloudstrapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            CloudstrapOpenIdConnectOptions options = endpoints.ServiceProvider
                .GetRequiredService<IOptions<CloudstrapOpenIdConnectOptions>>().Value;

            endpoints.MapGet(options.LoginPath, HandleLogin).AllowAnonymous();
            endpoints.MapGet(options.LogoutPath, (Delegate)HandleLogout).AllowAnonymous();

            endpoints.ServiceProvider.GetRequiredService<AuthenticationEndpointsState>()
                .MarkMapped(options.LoginPath, options.LogoutPath);

            return endpoints;
        }

        /// <summary>
        /// Challenges to the identity provider, returning the signed-in user to the caller's
        /// <c>returnUrl</c> when it is local and to <c>/</c> otherwise.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The challenge result.</returns>
        private static IResult HandleLogin(HttpContext context)
        {
            string redirectUri = LocalReturnUrl.ResolveOrDefault(
                context.Request.Query["returnUrl"].ToString(),
                "/");

            return Results.Challenge(new AuthenticationProperties { RedirectUri = redirectUri });
        }

        /// <summary>
        /// Signs out of both the cookie and OpenID Connect schemes (RP-initiated). When the identity
        /// provider advertises no <c>end_session_endpoint</c> — or its metadata cannot be read — the
        /// local sign-out still completes and a warning is written once.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The sign-out result.</returns>
        private static async Task<IResult> HandleLogout(HttpContext context)
        {
            AuthenticationProperties properties = new()
            {
                RedirectUri = LocalReturnUrl.ResolveOrDefault(context.Request.Query["returnUrl"].ToString(), "/"),
            };

            if (!await ProviderAdvertisesEndSessionAsync(context))
            {
                return Results.SignOut(properties, [CloudstrapOpenIdConnect.CookieScheme]);
            }

            return Results.SignOut(
                properties,
                [CloudstrapOpenIdConnect.CookieScheme, CloudstrapOpenIdConnect.ChallengeScheme]);
        }

        /// <summary>
        /// Checks the identity provider's metadata for an end-session endpoint, logging the spec's
        /// edge case once when it is absent.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns><see langword="true"/> when RP-initiated sign-out can reach the provider.</returns>
        private static async Task<bool> ProviderAdvertisesEndSessionAsync(HttpContext context)
        {
            OpenIdConnectOptions oidcOptions = context.RequestServices
                .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(CloudstrapOpenIdConnect.ChallengeScheme);

            string? endSessionEndpoint = null;

            if (oidcOptions.ConfigurationManager is not null)
            {
                try
                {
                    OpenIdConnectConfiguration configuration = await oidcOptions.ConfigurationManager
                        .GetConfigurationAsync(context.RequestAborted);
                    endSessionEndpoint = configuration.EndSessionEndpoint;
                }
                catch (InvalidOperationException)
                {
                    // Metadata unavailable: treated exactly like a provider without an end-session
                    // endpoint — the local sign-out must never fail because the provider is down.
                }
            }

            if (!string.IsNullOrEmpty(endSessionEndpoint))
            {
                return true;
            }

            AuthenticationEndpointsState state =
                context.RequestServices.GetRequiredService<AuthenticationEndpointsState>();
            if (state.TryMarkEndSessionWarningLogged())
            {
                OpenIdConnectLog.EndSessionEndpointUnavailable(
                    context.RequestServices.GetRequiredService<ILogger<AuthenticationEndpointsState>>());
            }

            return false;
        }
    }
}
