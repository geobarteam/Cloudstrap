namespace Cloudstrap.Extensions
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Http;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Connects a client's token flags to the <see cref="IUserAccessTokenHandlerProvider"/> and
    /// <see cref="IClientAccessTokenHandlerProvider"/> seams.
    /// </summary>
    /// <remarks>
    /// A client may set both flags. Each seam is then resolved independently and both handlers are added,
    /// user first — the providers ship in separate packages, so the pair works whenever both packages are
    /// registered, and a partial registration fails naming exactly the missing flag and its package rather
    /// than sending a partially authenticated request.
    /// </remarks>
    internal static class AccessTokenHandlerWiring
    {
        /// <summary>
        /// Arranges for the flagged client's pipeline to be given the providers' handlers when it is built.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="clientName">The logical client name.</param>
        /// <remarks>
        /// The providers are looked up while the pipeline is being built, not while it is being registered,
        /// so the authentication packages may be added to the container before or after the client.
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
        /// The client asks for a token whose provider seam has no registered implementation.
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

            IUserAccessTokenHandlerProvider? userProvider = options.AddUserAccessToken
                ? handlerBuilder.Services.GetService<IUserAccessTokenHandlerProvider>()
                : null;
            IClientAccessTokenHandlerProvider? clientProvider = options.AddClientAccessToken
                ? handlerBuilder.Services.GetService<IClientAccessTokenHandlerProvider>()
                : null;

            bool userMissing = options.AddUserAccessToken && userProvider is null;
            bool clientMissing = options.AddClientAccessToken && clientProvider is null;

            if (userMissing || clientMissing)
            {
                throw new InvalidOperationException(
                    BuildMissingProviderMessage(clientName, userMissing, clientMissing));
            }

            int position = 0;

            if (userProvider is not null)
            {
                handlerBuilder.AdditionalHandlers.Insert(
                    position++,
                    userProvider.CreateUserTokenHandler(clientName, options.TokenRequestParameters));
            }

            if (clientProvider is not null)
            {
                handlerBuilder.AdditionalHandlers.Insert(
                    position,
                    clientProvider.CreateClientTokenHandler(clientName, options.TokenRequestParameters));
            }
        }

        /// <summary>
        /// Builds the failure message, naming only the flag(s) whose provider is missing and the package
        /// that satisfies each — never a flag whose provider is present.
        /// </summary>
        /// <param name="clientName">The logical client name.</param>
        /// <param name="userMissing">Whether the user-token seam is flagged but unimplemented.</param>
        /// <param name="clientMissing">Whether the client-token seam is flagged but unimplemented.</param>
        /// <returns>The message.</returns>
        private static string BuildMissingProviderMessage(string clientName, bool userMissing, bool clientMissing)
        {
            string sectionPath = $"{ServiceCollectionExtensions.HttpClientsSectionPath}:{clientName}";
            List<string> missing = [];

            if (userMissing)
            {
                missing.Add(
                    $"'{sectionPath}:AddUserAccessToken' requires an {nameof(IUserAccessTokenHandlerProvider)}"
                    + " from Cloudstrap.Authentication.OpenIdConnect");
            }

            if (clientMissing)
            {
                missing.Add(
                    $"'{sectionPath}:AddClientAccessToken' requires an {nameof(IClientAccessTokenHandlerProvider)}"
                    + " from Cloudstrap.Authentication.ClientCredentials");
            }

            return $"HTTP client '{clientName}' is configured to attach an access token, but a required "
                + $"provider is not registered: {string.Join("; ", missing)}. Reference the missing package "
                + "and call its AddCloudstrap… registration, or turn its flag off.";
        }
    }
}
