namespace Cloudstrap.Authentication.ClientCredentials
{
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Duende.AccessTokenManagement;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Fills the <see cref="IClientAccessTokenHandlerProvider"/> seam declared by
    /// <c>Cloudstrap.Extensions</c>: dependencies are resolved from the container at pipeline-build time,
    /// a fresh handler is created per call, and its <see cref="DelegatingHandler.InnerHandler"/> is never
    /// pre-set — the HTTP client factory owns the chain.
    /// </summary>
    /// <remarks>
    /// This is also where a client's configured <c>TokenRequestParameters</c> are inspected once: the
    /// user-token settings (<c>SignInScheme</c>, <c>ChallengeScheme</c>) warn and do nothing, and a
    /// static <c>ForceRenewal</c> warns that the cache is being bypassed.
    /// </remarks>
    internal sealed class ClientCredentialsAccessTokenHandlerProvider : IClientAccessTokenHandlerProvider
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientCredentialsAccessTokenHandlerProvider"/> class.
        /// </summary>
        /// <param name="serviceProvider">The container the handler's dependencies come from.</param>
        public ClientCredentialsAccessTokenHandlerProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc/>
        public DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientName);

            if (tokenRequest is not null)
            {
                WarnAboutUnusualSettings(clientName, tokenRequest);
            }

            return new ClientCredentialsTokenHandler(
                _serviceProvider.GetRequiredService<IClientCredentialsTokenManager>(),
                TokenRequestParameterMapper.Map(tokenRequest),
                _serviceProvider.GetRequiredService<IOptions<CloudstrapClientCredentialsOptions>>(),
                _serviceProvider.GetRequiredService<ILogger<ClientCredentialsTokenHandler>>());
        }

        /// <summary>
        /// Writes the one-time warnings for settings that are ignored or unusual on a client-credentials
        /// token request, naming the exact configuration key of each.
        /// </summary>
        /// <param name="clientName">The logical client name.</param>
        /// <param name="tokenRequest">The client's configured parameters.</param>
        private void WarnAboutUnusualSettings(string clientName, TokenRequestOptions tokenRequest)
        {
            ILogger logger =
                _serviceProvider.GetRequiredService<ILogger<ClientCredentialsAccessTokenHandlerProvider>>();
            string sectionPath = $"Cloudstrap:HttpClients:{clientName}:TokenRequestParameters";

            if (tokenRequest.SignInScheme is not null)
            {
                ClientCredentialsLog.UserTokenSettingIgnored(logger, $"{sectionPath}:SignInScheme");
            }

            if (tokenRequest.ChallengeScheme is not null)
            {
                ClientCredentialsLog.UserTokenSettingIgnored(logger, $"{sectionPath}:ChallengeScheme");
            }

            if (tokenRequest.ForceRenewal)
            {
                ClientCredentialsLog.ForceRenewalConfigured(logger, $"{sectionPath}:ForceRenewal");
            }
        }
    }
}
