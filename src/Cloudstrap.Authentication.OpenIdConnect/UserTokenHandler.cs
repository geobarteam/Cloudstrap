namespace Cloudstrap.Authentication.OpenIdConnect
{
    using System.Net;
    using System.Net.Http.Headers;
    using System.Security.Claims;
    using Duende.AccessTokenManagement;
    using Duende.AccessTokenManagement.OpenIdConnect;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Attaches the signed-in user's access token to every request and, on a 401 response, forces one
    /// token renewal and re-sends the request once — re-executing the whole inner chain — before
    /// returning the response to the caller.
    /// </summary>
    /// <remarks>
    /// Acquisition, storage and refresh are entirely Duende's <see cref="IUserTokenManager"/> —
    /// resolved per request from the request's own scope, because it and its token store are scoped
    /// services reading and writing the request's authentication ticket. The current principal is
    /// likewise resolved per request through <see cref="IHttpContextAccessor"/> — never captured.
    /// When there is no signed-in user, or no token can be produced, the handler throws — an
    /// unauthenticated request is never sent in its place (AC-OIDC8).
    /// </remarks>
    internal sealed class UserTokenHandler : DelegatingHandler
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _clientName;
        private readonly UserTokenRequestParameters _parameters;
        private readonly ILogger<UserTokenHandler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserTokenHandler"/> class.
        /// </summary>
        /// <param name="httpContextAccessor">The accessor the current request is resolved from.</param>
        /// <param name="clientName">The logical client name — the failure contract names its flag.</param>
        /// <param name="parameters">The client's mapped token request parameters.</param>
        /// <param name="logger">The logger failures are reported to.</param>
        public UserTokenHandler(
            IHttpContextAccessor httpContextAccessor,
            string clientName,
            UserTokenRequestParameters parameters,
            ILogger<UserTokenHandler> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _clientName = clientName;
            _parameters = parameters;
            _logger = logger;
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            HttpContext httpContext = ResolveAuthenticatedContext();

            HttpResponseMessage response = await SendWithTokenAsync(
                request,
                httpContext,
                _parameters,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is not HttpStatusCode.Unauthorized)
            {
                return response;
            }

            response.Dispose();

            return await SendWithTokenAsync(
                request,
                httpContext,
                new UserTokenRequestParameters
                {
                    Scope = _parameters.Scope,
                    Resource = _parameters.Resource,
                    SignInScheme = _parameters.SignInScheme,
                    ChallengeScheme = _parameters.ChallengeScheme,
                    ForceTokenRenewal = true,
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the current request's signed-in user, failing fast — before anything is sent —
        /// when there is no request context or no authenticated user.
        /// </summary>
        /// <returns>The current HTTP context, carrying an authenticated user.</returns>
        /// <exception cref="InvalidOperationException">No signed-in user is available.</exception>
        private HttpContext ResolveAuthenticatedContext()
        {
            HttpContext? httpContext = _httpContextAccessor.HttpContext;

            if (httpContext is null || httpContext.User.Identity?.IsAuthenticated != true)
            {
                OpenIdConnectLog.UserTokenUnavailable(
                    _logger,
                    $"Cloudstrap:HttpClients:{_clientName}:AddUserAccessToken",
                    "no HttpContext or signed-in user was available");

                throw new InvalidOperationException(
                    $"HTTP client '{_clientName}' is configured with"
                    + $" 'Cloudstrap:HttpClients:{_clientName}:AddUserAccessToken', but no HttpContext or"
                    + " no signed-in user was available — a user access token belongs to a signed-in"
                    + " user's request. For machine identity from background services, use"
                    + " 'AddClientAccessToken' with Cloudstrap.Authentication.ClientCredentials instead."
                    + " The outbound request was not sent.");
            }

            return httpContext;
        }

        /// <summary>
        /// Acquires the current user's token with the given parameters, attaches it, and sends the
        /// request down the rest of the chain.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="httpContext">The current HTTP context.</param>
        /// <param name="parameters">The token request parameters for this attempt.</param>
        /// <param name="cancellationToken">The caller's cancellation token.</param>
        /// <returns>The downstream response.</returns>
        /// <exception cref="InvalidOperationException">Token acquisition failed.</exception>
        private async Task<HttpResponseMessage> SendWithTokenAsync(
            HttpRequestMessage request,
            HttpContext httpContext,
            UserTokenRequestParameters parameters,
            CancellationToken cancellationToken)
        {
            // The token manager and its store are scoped to the request whose ticket they read and
            // update — they must come from the request's scope, never the root provider.
            IUserTokenManager tokenManager =
                httpContext.RequestServices.GetRequiredService<IUserTokenManager>();

            ClaimsPrincipal user = httpContext.User;
            TokenResult<UserToken> result = await tokenManager
                .GetAccessTokenAsync(user, parameters, cancellationToken)
                .ConfigureAwait(false);

            if (!result.WasSuccessful(out UserToken? token, out FailedResult? failure))
            {
                string? storedAccessToken = await httpContext
                    .GetTokenAsync("access_token")
                    .ConfigureAwait(false);

                string reason = storedAccessToken is null
                    ? "the authentication session carries no stored tokens — SaveTokens was disabled,"
                        + " but this package requires tokens in the session (decision D-2)"
                    : failure.Error;

                OpenIdConnectLog.UserTokenUnavailable(
                    _logger,
                    $"Cloudstrap:HttpClients:{_clientName}:AddUserAccessToken",
                    storedAccessToken is null ? "the session carries no stored tokens (SaveTokens)" : failure.Error);

                throw new InvalidOperationException(
                    $"User access token acquisition for HTTP client '{_clientName}' failed: {reason}."
                    + " The outbound request was not sent.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue(
                token.AccessTokenType?.ToString() ?? "Bearer",
                token.AccessToken.ToString());

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
