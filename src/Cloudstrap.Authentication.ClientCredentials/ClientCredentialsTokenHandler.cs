namespace Cloudstrap.Authentication.ClientCredentials
{
    using System.Net;
    using System.Net.Http.Headers;
    using Duende.AccessTokenManagement;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Attaches a client-credentials access token to every request and, on a 401 response, forces one
    /// token renewal and re-sends the request once — re-executing the whole inner chain — before
    /// returning the response to the caller.
    /// </summary>
    /// <remarks>
    /// Acquisition, caching and renewal are entirely Duende's
    /// <see cref="IClientCredentialsTokenManager"/>; this handler only attaches the result and applies
    /// the single-retry-on-401 semantics Duende AccessTokenManagement v4 documents for its own composed
    /// pipeline (its <c>AccessTokenRequestHandler</c> plus default access-token resiliency — a
    /// composition the one-handler <c>IClientAccessTokenHandlerProvider</c> seam hosts as one piece).
    /// A failed acquisition throws — an unauthenticated request is never sent in its place.
    /// </remarks>
    internal sealed class ClientCredentialsTokenHandler : DelegatingHandler
    {
        private readonly IClientCredentialsTokenManager _tokenManager;
        private readonly TokenRequestParameters _parameters;
        private readonly IOptions<CloudstrapClientCredentialsOptions> _options;
        private readonly ILogger<ClientCredentialsTokenHandler> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientCredentialsTokenHandler"/> class.
        /// </summary>
        /// <param name="tokenManager">Duende's token manager.</param>
        /// <param name="parameters">The client's mapped token request parameters.</param>
        /// <param name="options">The bound Cloudstrap options — the failure contract names the endpoint.</param>
        /// <param name="logger">The logger failures are reported to.</param>
        public ClientCredentialsTokenHandler(
            IClientCredentialsTokenManager tokenManager,
            TokenRequestParameters parameters,
            IOptions<CloudstrapClientCredentialsOptions> options,
            ILogger<ClientCredentialsTokenHandler> logger)
        {
            _tokenManager = tokenManager;
            _parameters = parameters;
            _options = options;
            _logger = logger;
        }

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            HttpResponseMessage response =
                await SendWithTokenAsync(request, _parameters, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is not HttpStatusCode.Unauthorized)
            {
                return response;
            }

            response.Dispose();

            return await SendWithTokenAsync(
                request,
                _parameters with
                {
                    ForceTokenRenewal = true
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Acquires a token with the given parameters, attaches it, and sends the request down the rest
        /// of the chain.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <param name="parameters">The token request parameters for this attempt.</param>
        /// <param name="cancellationToken">The caller's cancellation token.</param>
        /// <returns>The downstream response.</returns>
        /// <exception cref="InvalidOperationException">Token acquisition failed.</exception>
        private async Task<HttpResponseMessage> SendWithTokenAsync(
            HttpRequestMessage request,
            TokenRequestParameters parameters,
            CancellationToken cancellationToken)
        {
            TokenResult<ClientCredentialsToken> result = await _tokenManager
                .GetAccessTokenAsync(CloudstrapClientCredentials.TokenClient, parameters, cancellationToken)
                .ConfigureAwait(false);

            if (!result.WasSuccessful(out ClientCredentialsToken? token, out FailedResult? failure))
            {
                Uri? tokenEndpoint = _options.Value.TokenEndpoint;
                ClientCredentialsLog.TokenAcquisitionFailed(_logger, tokenEndpoint, failure.Error);

                throw new InvalidOperationException(
                    $"Client credentials token acquisition against '{tokenEndpoint}' failed:"
                    + $" {failure.Error}. The outbound request was not sent.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue(
                token.AccessTokenType?.ToString() ?? "Bearer",
                token.AccessToken.ToString());

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
