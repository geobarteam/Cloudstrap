namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Fills the <see cref="IUserAccessTokenHandlerProvider"/> seam declared by
    /// <c>Cloudstrap.Extensions</c>: dependencies are resolved from the container at pipeline-build
    /// time, a fresh handler is created per call, and its
    /// <see cref="DelegatingHandler.InnerHandler"/> is never pre-set — the HTTP client factory owns
    /// the chain.
    /// </summary>
    /// <remarks>
    /// Unlike the client-credentials provider, nothing here warns: all five
    /// <c>TokenRequestParameters</c> members are honored for user tokens (spec finding 10).
    /// </remarks>
    internal sealed class UserAccessTokenHandlerProvider : IUserAccessTokenHandlerProvider
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserAccessTokenHandlerProvider"/> class.
        /// </summary>
        /// <param name="serviceProvider">The container the handler's dependencies come from.</param>
        public UserAccessTokenHandlerProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc/>
        public DelegatingHandler CreateUserTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientName);

            // Deliberately no IUserTokenManager here: the manager and its token store are scoped to
            // the request whose ticket they operate on, and the handler resolves them per request.
            return new UserTokenHandler(
                _serviceProvider.GetRequiredService<IHttpContextAccessor>(),
                clientName,
                UserTokenRequestParameterMapper.Map(tokenRequest),
                _serviceProvider.GetRequiredService<ILogger<UserTokenHandler>>());
        }
    }
}
