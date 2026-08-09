namespace Cloudstrap.TestIdentityProvider
{
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using OpenIddict.Abstractions;
    using OpenIddict.Server.AspNetCore;
    using static OpenIddict.Abstractions.OpenIddictConstants;

    /// <summary>
    /// Maps the test identity provider's endpoints: the token endpoint, and — for the interactive
    /// flows — the authorization endpoint with its minimal login form, the userinfo endpoint and the
    /// end-session endpoint. Discovery and JWKS are served by OpenIddict itself at their standard
    /// well-known paths.
    /// </summary>
    public static class EndpointRouteBuilderExtensions
    {
        private static readonly string[] _getAndPostMethods = ["GET", "POST"];

        /// <summary>
        /// Maps the endpoints of the test identity provider: <c>/connect/token</c>,
        /// <c>/connect/authorize</c>, <c>/connect/login</c>, <c>/connect/userinfo</c> and
        /// <c>/connect/logout</c>.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder to map into.</param>
        /// <returns>The endpoint route builder, so further mappings can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// The authorization and login endpoints answer only when the interactive grants are enabled
        /// (users or redirect URIs configured); a client-credentials-only provider never advertises
        /// them, and requests to them are rejected by OpenIddict as unknown endpoints.
        /// </remarks>
        public static IEndpointRouteBuilder MapCloudstrapTestIdentityProvider(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapPost("/connect/token", (Delegate)HandleTokenRequest);
            endpoints.MapMethods("/connect/authorize", _getAndPostMethods, (Delegate)HandleAuthorizeRequest);
            endpoints.MapGet("/connect/login", HandleLoginPage);
            endpoints.MapPost("/connect/login", (Delegate)HandleLoginSubmit);
            endpoints.MapMethods("/connect/userinfo", _getAndPostMethods, (Delegate)HandleUserInfoRequest);
            endpoints.MapMethods("/connect/logout", _getAndPostMethods, (Delegate)HandleEndSessionRequest);

            return endpoints;
        }

        /// <summary>
        /// Issues tokens. OpenIddict has already authenticated the client — a wrong or unknown
        /// credential never reaches this handler — so all that remains is shaping the principal the
        /// token is generated from: a fresh client principal for the client-credentials grant, or the
        /// principal carried by the authorization code.
        /// </summary>
        /// <param name="context">The HTTP context of the validated token request.</param>
        /// <returns>The sign-in result OpenIddict turns into a token response.</returns>
        private static async Task<IResult> HandleTokenRequest(HttpContext context)
        {
            OpenIddictRequest request = context.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The request is not handled by the OpenIddict server.");

            if (request.IsClientCredentialsGrantType())
            {
                return HandleClientCredentialsGrant(context, request);
            }

            if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
            {
                if (request.IsRefreshTokenGrantType())
                {
                    context.RequestServices.GetRequiredService<TestIdentityProviderCounters>()
                        .IncrementRefreshTokenRequestCount();
                }

                // The principal was produced by the authorization endpoint below and travels inside
                // the code — and then inside the refresh token — with its claims, scopes, audiences
                // and destinations unchanged.
                AuthenticateResult result =
                    await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
                ClaimsPrincipal principal = result.Principal
                    ?? throw new InvalidOperationException("The grant carried no principal.");

                return Results.SignIn(
                    principal,
                    properties: null,
                    OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            throw new NotSupportedException(
                "The client-credentials, authorization-code and refresh-token grants are the only grant types this test identity provider supports.");
        }

        /// <summary>
        /// Issues the client-credentials token, exactly as before the interactive flows arrived.
        /// </summary>
        /// <param name="context">The HTTP context of the validated token request.</param>
        /// <param name="request">The validated token request.</param>
        /// <returns>The sign-in result OpenIddict turns into a token response.</returns>
        private static IResult HandleClientCredentialsGrant(HttpContext context, OpenIddictRequest request)
        {
            TestIdentityProviderOptions options = context.RequestServices
                .GetRequiredService<IOptions<TestIdentityProviderOptions>>().Value;
            TestIdentityProviderClient client = options.Clients.First(candidate =>
                string.Equals(candidate.ClientId, request.ClientId, StringComparison.Ordinal));

            ClaimsIdentity identity = new(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);
            identity.SetClaim(Claims.Subject, request.ClientId);
            identity.SetScopes(request.GetScopes());
            identity.SetAudiences([.. client.Audiences]);

            // Every client-credentials token is an access token, so all three configured claim sets
            // apply to it. Multi-valued claims become JSON arrays.
            AddConfiguredClaims(identity, client.TokenClaims.Common);
            AddConfiguredClaims(identity, client.TokenClaims.AccessToken);
            AddConfiguredClaims(identity, client.TokenClaims.ClientCredentialsToken);

            identity.SetDestinations(static _ => [Destinations.AccessToken]);

            return Results.SignIn(
                new ClaimsPrincipal(identity),
                properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// Handles the authorization endpoint: renders the minimal login form when no login session
        /// exists, and completes the authorization — auto-consent, no consent UI — when it does.
        /// </summary>
        /// <param name="context">The HTTP context of the validated authorization request.</param>
        /// <returns>The login form, or the sign-in result OpenIddict turns into a code response.</returns>
        private static async Task<IResult> HandleAuthorizeRequest(HttpContext context)
        {
            context.RequestServices.GetRequiredService<TestIdentityProviderCounters>()
                .IncrementAuthorizeRequestCount();

            OpenIddictRequest request = context.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The request is not handled by the OpenIddict server.");
            TestIdentityProviderOptions options = context.RequestServices
                .GetRequiredService<IOptions<TestIdentityProviderOptions>>().Value;

            AuthenticateResult session =
                await context.AuthenticateAsync(ServiceCollectionExtensions.SessionAuthenticationScheme);
            TestIdentityProviderUser? user = session.Succeeded
                ? options.Users.FirstOrDefault(candidate => string.Equals(
                    candidate.Username,
                    session.Principal.Identity?.Name,
                    StringComparison.Ordinal))
                : null;

            if (user is null)
            {
                return RenderLoginForm(BuildAuthorizeReturnUrl(context, request), invalidCredentials: false);
            }

            TestIdentityProviderClient client = options.Clients.First(candidate =>
                string.Equals(candidate.ClientId, request.ClientId, StringComparison.Ordinal));

            ClaimsIdentity identity = new(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);
            identity.SetClaim(Claims.Subject, user.Username);
            identity.SetScopes(request.GetScopes());
            identity.SetAudiences([.. client.Audiences]);

            AddConfiguredClaims(identity, user.Claims);
            AddConfiguredClaims(identity, client.TokenClaims.Common);
            AddConfiguredClaims(identity, client.TokenClaims.AccessToken);
            AddConfiguredClaims(identity, client.TokenClaims.IdToken);

            identity.SetDestinations(claim => GetInteractiveDestinations(claim, client));

            return Results.SignIn(
                new ClaimsPrincipal(identity),
                properties: null,
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        /// <summary>
        /// Serves the login form directly, for a browser navigating to it outside an authorization
        /// round trip.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>The login form.</returns>
        private static IResult HandleLoginPage(HttpContext context)
        {
            string returnUrl = context.Request.Query["returnUrl"].ToString();

            return RenderLoginForm(IsLocalUrl(returnUrl) ? returnUrl : "/", invalidCredentials: false);
        }

        /// <summary>
        /// Validates the posted credentials against the configured users: on success signs the user
        /// into the session cookie and returns to the authorization endpoint; on failure re-renders
        /// the form without issuing anything.
        /// </summary>
        /// <param name="context">The HTTP context of the form post.</param>
        /// <returns>The redirect back to the authorization endpoint, or the re-rendered form.</returns>
        private static async Task<IResult> HandleLoginSubmit(HttpContext context)
        {
            IFormCollection form = await context.Request.ReadFormAsync();
            string username = form["username"].ToString();
            string password = form["password"].ToString();
            string returnUrl = form["returnUrl"].ToString();

            if (!IsLocalUrl(returnUrl))
            {
                returnUrl = "/";
            }

            TestIdentityProviderOptions options = context.RequestServices
                .GetRequiredService<IOptions<TestIdentityProviderOptions>>().Value;
            TestIdentityProviderUser? user = options.Users.FirstOrDefault(candidate =>
                string.Equals(candidate.Username, username, StringComparison.Ordinal)
                && string.Equals(candidate.Password, password, StringComparison.Ordinal));

            if (user is null)
            {
                return RenderLoginForm(returnUrl, invalidCredentials: true);
            }

            ClaimsIdentity sessionIdentity = new(ServiceCollectionExtensions.SessionAuthenticationScheme);
            sessionIdentity.AddClaim(new Claim(ClaimTypes.Name, user.Username));
            await context.SignInAsync(
                ServiceCollectionExtensions.SessionAuthenticationScheme,
                new ClaimsPrincipal(sessionIdentity));

            return Results.LocalRedirect(returnUrl);
        }

        /// <summary>
        /// Serves the userinfo endpoint. OpenIddict has already validated the access token — a missing
        /// or invalid token never reaches this handler — so all that remains is shaping the response:
        /// <c>sub</c> plus exactly the client's <c>UserInfo</c> claim set.
        /// </summary>
        /// <param name="context">The HTTP context of the validated userinfo request.</param>
        /// <returns>The userinfo JSON document.</returns>
        private static async Task<IResult> HandleUserInfoRequest(HttpContext context)
        {
            AuthenticateResult result =
                await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            ClaimsPrincipal principal = result.Principal
                ?? throw new InvalidOperationException("The userinfo request carried no principal.");

            TestIdentityProviderOptions options = context.RequestServices
                .GetRequiredService<IOptions<TestIdentityProviderOptions>>().Value;
            TestIdentityProviderClient? client = options.Clients.FirstOrDefault(candidate =>
                string.Equals(candidate.ClientId, principal.GetClaim(Claims.ClientId), StringComparison.Ordinal));

            Dictionary<string, object> claims = new(StringComparer.Ordinal)
            {
                [Claims.Subject] = principal.GetClaim(Claims.Subject) ?? string.Empty,
            };

            if (client is not null)
            {
                foreach ((string type, IList<string> values) in client.TokenClaims.UserInfo)
                {
                    claims[type] = values.Count == 1 ? values[0] : (object)values.ToArray();
                }
            }

            return Results.Json(claims);
        }

        /// <summary>
        /// Handles the end-session endpoint. OpenIddict has already validated <c>id_token_hint</c> and
        /// checked <c>post_logout_redirect_uri</c> against the client's registered whitelist; this
        /// handler clears the login session cookie and lets OpenIddict complete the redirect.
        /// </summary>
        /// <param name="context">The HTTP context of the validated end-session request.</param>
        /// <returns>The sign-out result OpenIddict turns into the post-logout redirect.</returns>
        private static async Task<IResult> HandleEndSessionRequest(HttpContext context)
        {
            await context.SignOutAsync(ServiceCollectionExtensions.SessionAuthenticationScheme);

            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        /// <summary>
        /// Decides which tokens a claim lands in: the client's <c>IdToken</c> claim set goes to the id
        /// token only, its <c>AccessToken</c> set to the access token only, and everything else — the
        /// subject, the user's claims and the <c>Common</c> set — to both.
        /// </summary>
        /// <param name="claim">The claim being destined.</param>
        /// <param name="client">The client whose claim sets decide the split.</param>
        /// <returns>The destinations for the claim.</returns>
        private static IEnumerable<string> GetInteractiveDestinations(Claim claim, TestIdentityProviderClient client)
        {
            bool idTokenOnly = client.TokenClaims.IdToken.ContainsKey(claim.Type);
            bool accessTokenOnly = client.TokenClaims.AccessToken.ContainsKey(claim.Type);

            if (idTokenOnly && !accessTokenOnly)
            {
                return [Destinations.IdentityToken];
            }

            if (accessTokenOnly && !idTokenOnly)
            {
                return [Destinations.AccessToken];
            }

            return [Destinations.AccessToken, Destinations.IdentityToken];
        }

        /// <summary>
        /// Rebuilds the authorization request URL the login form returns to after a successful sign-in.
        /// </summary>
        /// <param name="context">The HTTP context of the authorization request.</param>
        /// <param name="request">The validated authorization request.</param>
        /// <returns>The local return URL.</returns>
        private static string BuildAuthorizeReturnUrl(HttpContext context, OpenIddictRequest request)
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                return context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            }

            // A POST authorization request: serialize the validated parameters back into a GET URL.
            QueryString query = default;
            foreach ((string name, OpenIddictParameter value) in request.GetParameters())
            {
                query = query.Add(name, (string?)value ?? string.Empty);
            }

            return context.Request.PathBase + context.Request.Path + query;
        }

        /// <summary>
        /// Renders the minimal login page: one username/password form, no account management, no
        /// consent UI.
        /// </summary>
        /// <param name="returnUrl">The local URL the form returns to after a successful sign-in.</param>
        /// <param name="invalidCredentials">Whether to show the invalid-credentials message.</param>
        /// <returns>The HTML result.</returns>
        private static IResult RenderLoginForm(string returnUrl, bool invalidCredentials)
        {
            string encodedReturnUrl = HtmlEncoder.Default.Encode(returnUrl);
            string message = invalidCredentials
                ? """<p data-testid="login-error">Invalid username or password.</p>"""
                : string.Empty;
            string html = $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head><meta charset="utf-8" /><title>Test Identity Provider</title></head>
                <body>
                  <h1>Test identity provider sign-in</h1>
                  {{message}}
                  <form method="post" action="/connect/login" data-testid="login-form">
                    <input name="username" data-testid="username" />
                    <input name="password" type="password" data-testid="password" />
                    <input name="returnUrl" type="hidden" value="{{encodedReturnUrl}}" />
                    <button type="submit" data-testid="submit">Sign in</button>
                  </form>
                </body>
                </html>
                """;

            return Results.Content(html, "text/html; charset=utf-8");
        }

        /// <summary>
        /// Accepts only local return URLs: a single leading slash, rejecting absolute URLs and the
        /// protocol-relative (<c>//host</c>) and backslash variants.
        /// </summary>
        /// <param name="url">The candidate URL.</param>
        /// <returns><see langword="true"/> when the URL is local.</returns>
        private static bool IsLocalUrl(string? url) =>
            !string.IsNullOrEmpty(url)
            && url[0] == '/'
            && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

        /// <summary>
        /// Stamps one configured claim set onto the identity: single values as plain claims,
        /// multi-valued claims as JSON arrays.
        /// </summary>
        /// <param name="identity">The identity the token is generated from.</param>
        /// <param name="claims">Claim values by claim type.</param>
        private static void AddConfiguredClaims(ClaimsIdentity identity, IDictionary<string, IList<string>> claims)
        {
            foreach ((string type, IList<string> values) in claims)
            {
                if (values.Count == 1)
                {
                    identity.SetClaim(type, values[0]);
                }
                else
                {
                    identity.SetClaims(type, [.. values]);
                }
            }
        }
    }
}
