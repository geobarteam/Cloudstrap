namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using System.Collections.Concurrent;
    using System.Net.Http.Headers;
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;

    /// <summary>One call the proxy made to a client-token seam.</summary>
    /// <param name="ClientName">The client name it was asked for (the cluster id).</param>
    /// <param name="TokenRequest">The token request parameters it was given.</param>
    internal sealed record ClientTokenRequest(string ClientName, TokenRequestOptions? TokenRequest);

    /// <summary>
    /// The mechanic (h) client-token seam: its handler sets <c>Authorization: Bearer app:{clientName}</c>, and it
    /// records every <c>(clientName, tokenRequest)</c> it was asked for.
    /// </summary>
    /// <remarks>
    /// The handler follows the real <c>ClientCredentialsTokenHandler</c> rule: an <c>Authorization</c> header already
    /// on the request is kept and no token is attached. So a caller's header that reached the handler would reach the
    /// destination too, exactly as in production. The <c>Authorization</c> value each request arrived with is recorded
    /// in <see cref="ArrivingAuthorizations"/>.
    /// </remarks>
    internal sealed class FakeClientTokenHandlerProvider : IClientAccessTokenHandlerProvider
    {
        private readonly ConcurrentQueue<ClientTokenRequest> _requests = new();
        private readonly ConcurrentQueue<string?> _arrivingAuthorizations = new();

        /// <summary>Gets the calls made so far, in order.</summary>
        public IReadOnlyList<ClientTokenRequest> Requests => [.. _requests];

        /// <summary>
        /// Gets the <c>Authorization</c> header each forwarded request carried when it reached the token handler
        /// (<see langword="null"/> when it had none), in order.
        /// </summary>
        public IReadOnlyList<string?> ArrivingAuthorizations => [.. _arrivingAuthorizations];

        /// <inheritdoc />
        public DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
            _requests.Enqueue(new ClientTokenRequest(clientName, tokenRequest));
            return new BearerHandler($"app:{clientName}", _arrivingAuthorizations);
        }

        /// <summary>
        /// Sets a fixed bearer token on every request that has no <c>Authorization</c> yet, as the real handler does.
        /// </summary>
        private sealed class BearerHandler(string token, ConcurrentQueue<string?> arrivingAuthorizations) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                arrivingAuthorizations.Enqueue(request.Headers.Authorization?.ToString());
                if (request.Headers.Authorization is null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                return base.SendAsync(request, cancellationToken);
            }
        }
    }

    /// <summary>
    /// A client-token seam whose handler fails every request, as when the token endpoint is unreachable.
    /// </summary>
    internal sealed class ThrowingClientTokenHandlerProvider : IClientAccessTokenHandlerProvider
    {
        /// <inheritdoc />
        public DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
            return new ThrowingHandler();
        }

        private sealed class ThrowingHandler : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new HttpRequestException("The token endpoint is unreachable (simulated).");
            }
        }
    }

    /// <summary>
    /// A client-token seam that breaks the contract: its handler already has an <c>InnerHandler</c>.
    /// </summary>
    internal sealed class InnerHandlerSetClientTokenHandlerProvider : IClientAccessTokenHandlerProvider
    {
        /// <inheritdoc />
        public DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
#pragma warning disable CA2000 // the inner handler is owned by the returned handler
            return new PassThroughHandler { InnerHandler = new HttpClientHandler() };
#pragma warning restore CA2000
        }

        private sealed class PassThroughHandler : DelegatingHandler;
    }
}
