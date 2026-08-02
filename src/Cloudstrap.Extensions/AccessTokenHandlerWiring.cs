namespace Cloudstrap.Extensions
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Http;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Connects a client's token flags to the <see cref="IAccessTokenHandlerProvider"/> seam.
    /// </summary>
    internal static class AccessTokenHandlerWiring
    {
        /// <summary>
        /// Arranges for the flagged client's pipeline to be given the provider's handlers when it is built.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="clientName">The logical client name.</param>
        /// <remarks>
        /// The provider is looked up while the pipeline is being built, not while it is being registered, so
        /// the authentication package may be added to the container before or after the client.
        /// </remarks>
        public static void Register(IServiceCollection services, string clientName)
        {
            services.Configure<HttpClientFactoryOptions>(
                clientName,
                factoryOptions => factoryOptions.HttpMessageHandlerBuilderActions.Add(
                    handlerBuilder => AddTokenHandlers(handlerBuilder, clientName)));
        }

        /// <summary>
        /// Inserts the token handlers at the head of the pipeline, so a token is attached before the
        /// correlation handler and everything downstream of it runs.
        /// </summary>
        /// <param name="handlerBuilder">The pipeline being built.</param>
        /// <param name="clientName">The logical client name.</param>
        /// <exception cref="InvalidOperationException">
        /// The client asks for a token but no <see cref="IAccessTokenHandlerProvider"/> is registered.
        /// </exception>
        private static void AddTokenHandlers(HttpMessageHandlerBuilder handlerBuilder, string clientName)
        {
            HttpClientServiceOptions options = handlerBuilder.Services
                .GetRequiredService<IOptionsMonitor<HttpClientServiceOptions>>()
                .Get(clientName);

            if (!options.AddUserAccessToken && !options.AddClientAccessToken)
            {
                return;
            }

            IAccessTokenHandlerProvider provider =
                handlerBuilder.Services.GetService<IAccessTokenHandlerProvider>()
                ?? throw new InvalidOperationException(BuildMissingProviderMessage(clientName, options));

            int position = 0;

            if (options.AddUserAccessToken)
            {
                handlerBuilder.AdditionalHandlers.Insert(
                    position++,
                    provider.CreateUserTokenHandler(clientName, options.TokenRequestParameters));
            }

            if (options.AddClientAccessToken)
            {
                handlerBuilder.AdditionalHandlers.Insert(
                    position,
                    provider.CreateClientTokenHandler(clientName, options.TokenRequestParameters));
            }
        }

        /// <summary>
        /// Builds the failure message, naming the flag that is set and the package that satisfies it.
        /// </summary>
        /// <param name="clientName">The logical client name.</param>
        /// <param name="options">The client's settings.</param>
        /// <returns>The message.</returns>
        private static string BuildMissingProviderMessage(string clientName, HttpClientServiceOptions options)
        {
            string sectionPath = $"{ServiceCollectionExtensions.HttpClientsSectionPath}:{clientName}";
            List<string> flags = [];

            if (options.AddUserAccessToken)
            {
                flags.Add($"'{sectionPath}:AddUserAccessToken' requires Cloudstrap.Authentication.OpenIdConnect");
            }

            if (options.AddClientAccessToken)
            {
                flags.Add(
                    $"'{sectionPath}:AddClientAccessToken' requires Cloudstrap.Authentication.ClientCredentials");
            }

            return $"HTTP client '{clientName}' is configured to attach an access token, but no "
                + $"{nameof(IAccessTokenHandlerProvider)} is registered: {string.Join("; ", flags)}. Reference the "
                + "package and call its AddCloudstrap… registration, or turn the flag off.";
        }
    }
}
