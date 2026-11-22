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
    internal sealed class FakeClientTokenHandlerProvider : IClientAccessTokenHandlerProvider
    {
        private readonly ConcurrentQueue<ClientTokenRequest> _requests = new();

        /// <summary>Gets the calls made so far, in order.</summary>
        public IReadOnlyList<ClientTokenRequest> Requests => [.. _requests];

        /// <inheritdoc />
        public DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
            _requests.Enqueue(new ClientTokenRequest(clientName, tokenRequest));
            return new BearerHandler($"app:{clientName}");
        }

        /// <summary>Sets a fixed bearer token on every request.</summary>
        private sealed class BearerHandler(string token) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
