namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using System.Collections.Concurrent;
    using System.Net.Http.Headers;
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Microsoft.AspNetCore.Http;

    /// <summary>One call the proxy made to a user-token seam.</summary>
    /// <param name="ClientName">The client name it was asked for (the cluster id).</param>
    /// <param name="TokenRequest">The token request parameters it was given.</param>
    internal sealed record UserTokenRequest(string ClientName, TokenRequestOptions? TokenRequest);

    /// <summary>
    /// The mechanic (h) user-token seam: its handler sets <c>Authorization: Bearer user:{name}</c> for the signed-in
    /// user of the current request (read through <see cref="IHttpContextAccessor"/>, as the real handler does), and it
    /// records every <c>(clientName, tokenRequest)</c> it was asked for.
    /// </summary>
    /// <remarks>
    /// Like the real <c>UserTokenHandler</c>, the handler always sets the header, and fails the request when there is
    /// no signed-in user, or when the signed-in user's session holds no token (<see cref="UsersWithoutToken"/>).
    /// </remarks>
    /// <param name="httpContextAccessor">The accessor the current request's user is read from.</param>
    internal sealed class FakeUserTokenHandlerProvider(IHttpContextAccessor httpContextAccessor) : IUserAccessTokenHandlerProvider
    {
        private readonly ConcurrentQueue<UserTokenRequest> _requests = new();

        /// <summary>Gets the calls made so far, in order.</summary>
        public IReadOnlyList<UserTokenRequest> Requests => [.. _requests];

        /// <summary>
        /// Gets the names of signed-in users whose session carries no token (simulated). The handler fails their
        /// requests before anything is sent, as the real handler does when token acquisition fails.
        /// </summary>
        public ConcurrentDictionary<string, bool> UsersWithoutToken { get; } = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public DelegatingHandler CreateUserTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
            _requests.Enqueue(new UserTokenRequest(clientName, tokenRequest));
            return new UserBearerHandler(httpContextAccessor, UsersWithoutToken);
        }

        /// <summary>Sets <c>Bearer user:{name}</c> for the current request's signed-in user.</summary>
        private sealed class UserBearerHandler(
            IHttpContextAccessor httpContextAccessor,
            ConcurrentDictionary<string, bool> usersWithoutToken) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string name = httpContextAccessor.HttpContext?.User.Identity is { IsAuthenticated: true, Name: { } userName }
                    ? userName
                    : throw new InvalidOperationException("No signed-in user (simulated). The outbound request was not sent.");
                if (usersWithoutToken.ContainsKey(name))
                {
                    throw new InvalidOperationException(
                        "The authentication session carries no stored tokens (simulated). The outbound request was not sent.");
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"user:{name}");
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
