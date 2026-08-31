namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Microsoft.AspNetCore.Antiforgery;
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
        /// back to <c>/</c>. No front-/back-channel logout, and no consent, registration or account
        /// pages are mapped — deliberately. The BFF user-info endpoint is its own opt-in:
        /// <see cref="MapCloudstrapBffUserEndpoint"/> (DL-2 — superseding this method's original
        /// "no user-info endpoint" posture).
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
        /// Maps the opt-in BFF user endpoint: <c>GET {UserEndpointPath}</c> (default <c>/bff/user</c>),
        /// which answers the browser client's session probe — 200 always, anonymous-safe — and issues
        /// the XSRF request token in the <c>{XsrfHeaderName}</c> response header.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder to map into.</param>
        /// <returns>The endpoint route builder, so further mappings can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The antiforgery services are not registered — the endpoint would issue tokens nothing
        /// validates, so the omission fails loud at map time.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The wire contract is camelCase JSON, <c>200</c> always:
        /// <c>{ "isAuthenticated": bool, "userName": string?, "claims": [{ "type", "value" }]? }</c> —
        /// <c>userName</c> and <c>claims</c> are present only for a signed-in session, and the claims
        /// mirror the cookie principal 1:1. <c>Cloudstrap.BlazorWasm</c>'s authentication state
        /// provider consumes exactly this shape.
        /// </para>
        /// <para>
        /// Token <em>issuance</em> lives here; <em>validation</em> stays the consumer's stock wiring:
        /// register <c>AddAntiforgery(options =&gt; options.HeaderName = ...)</c> with a header name
        /// matching <c>Cloudstrap:OpenIdConnect:XsrfHeaderName</c> and validate mutating endpoints
        /// (for example <c>[ValidateAntiForgeryToken]</c> on controllers, or
        /// <c>IAntiforgery.ValidateRequestAsync</c> in minimal APIs). A token issued to an anonymous
        /// session does not validate for the later signed-in user — the full-page login navigation
        /// reloads the client, which refetches this endpoint and picks up a fresh token.
        /// </para>
        /// </remarks>
        public static IEndpointRouteBuilder MapCloudstrapBffUserEndpoint(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            if (endpoints.ServiceProvider.GetService<IAntiforgery>() is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(MapCloudstrapBffUserEndpoint)} requires the antiforgery services: call "
                    + "services.AddAntiforgery(options => options.HeaderName = \"...\") — with a header "
                    + "name matching 'Cloudstrap:OpenIdConnect:XsrfHeaderName' — before building the "
                    + "application. Issuing tokens nothing validates would be security theater.");
            }

            CloudstrapOpenIdConnectOptions options = endpoints.ServiceProvider
                .GetRequiredService<IOptions<CloudstrapOpenIdConnectOptions>>().Value;

            endpoints.MapGet(options.UserEndpointPath, HandleUser).AllowAnonymous();

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
        /// Answers the session probe from the cookie principal and issues the XSRF request token as
        /// the configured response header.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The wire-contract JSON, 200 always.</returns>
        private static IResult HandleUser(HttpContext context)
        {
            CloudstrapOpenIdConnectOptions options = context.RequestServices
                .GetRequiredService<IOptions<CloudstrapOpenIdConnectOptions>>().Value;
            IAntiforgery antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();

            AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
            if (tokens.RequestToken is not null)
            {
                context.Response.Headers[options.XsrfHeaderName] = tokens.RequestToken;
            }

            bool isAuthenticated = context.User.Identity?.IsAuthenticated ?? false;

            BffUserInfo payload = isAuthenticated
                ? new BffUserInfo(
                    IsAuthenticated: true,
                    UserName: context.User.Identity!.Name,
                    Claims: [.. context.User.Claims.Select(claim => new BffUserClaim(claim.Type, claim.Value))])
                : new BffUserInfo(IsAuthenticated: false, UserName: null, Claims: null);

            return Results.Json(payload);
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

        /// <summary>
        /// The user endpoint's wire contract — serialized with the application's JSON defaults
        /// (camelCase), consumed by <c>Cloudstrap.BlazorWasm</c>.
        /// </summary>
        /// <param name="IsAuthenticated">Whether the session is signed in.</param>
        /// <param name="UserName">The signed-in identity's name; absent when anonymous.</param>
        /// <param name="Claims">The cookie principal's claims 1:1; absent when anonymous.</param>
        private sealed record BffUserInfo(bool IsAuthenticated, string? UserName, List<BffUserClaim>? Claims);

        /// <summary>
        /// One claim on the wire contract.
        /// </summary>
        /// <param name="Type">The claim type.</param>
        /// <param name="Value">The claim value.</param>
        private sealed record BffUserClaim(string Type, string Value);
    }
}
