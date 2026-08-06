namespace Cloudstrap.Authentication.ClientCredentials
{
    using Cloudstrap.Extensions;
    using Duende.AccessTokenManagement;
    using Microsoft.Extensions.Caching.Hybrid;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registers machine-to-machine (client credentials) token acquisition on Duende
    /// AccessTokenManagement: one call, and every Cloudstrap typed client flagged
    /// <c>AddClientAccessToken</c> transparently carries a cached, renewed bearer token.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds client-credentials token acquisition to the dependency injection container.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="configure">
        /// An optional delegate for the code-level hooks configuration values cannot express.
        /// </param>
        /// <returns>The service collection, so further registrations can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Settings bind from the <see cref="CloudstrapClientCredentialsOptions.SectionName"/> section and
        /// are validated at host startup: a missing or malformed value fails fast naming the exact key,
        /// never echoing a configured value.
        /// </para>
        /// <para>
        /// The registration is idempotent — calling it twice registers everything once — and additive next
        /// to a consumer's own <c>AddClientCredentialsTokenManagement()</c> clients: the
        /// <see cref="CloudstrapClientCredentials.TokenClientName"/> client never collides with consumer
        /// client names.
        /// </para>
        /// <para>
        /// This method registers no inbound authentication, no authorization policy and no user-token
        /// provider — validating inbound tokens is <c>Cloudstrap.WebApi</c>'s
        /// <c>AddCloudstrapJwtBearer</c>; interactive user tokens are
        /// <c>Cloudstrap.Authentication.OpenIdConnect</c>.
        /// </para>
        /// </remarks>
        public static IServiceCollection AddCloudstrapClientCredentials(
            this IServiceCollection services,
            Action<CloudstrapClientCredentialsConfigurator>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
            {
                return services;
            }

            services.AddSingleton<RegistrationMarker>();

            CloudstrapClientCredentialsConfigurator configurator = new();
            configure?.Invoke(configurator);

            services.AddOptions<CloudstrapClientCredentialsOptions>()
                .BindConfiguration(CloudstrapClientCredentialsOptions.SectionName)
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<CloudstrapClientCredentialsOptions>, CloudstrapClientCredentialsOptionsValidator>();
            services.AddSingleton<IValidateOptions<CloudstrapClientCredentialsOptions>, TokenEndpointShapeValidator>();

            services.AddClientCredentialsTokenManagement()
                .AddClient(CloudstrapClientCredentials.TokenClientName, static _ => { });

            if (configurator.TokenManagement is not null)
            {
                services.Configure(configurator.TokenManagement);
            }

            services.AddSingleton<IConfigureOptions<ClientCredentialsClient>, CloudstrapTokenClientConfigurator>();

            if (configurator.Client is not null)
            {
                services.AddSingleton<IPostConfigureOptions<ClientCredentialsClient>>(
                    new PostConfigureOptions<ClientCredentialsClient>(
                        CloudstrapClientCredentials.TokenClientName,
                        configurator.Client));
            }

            IHttpClientBuilder backchannel =
                services.AddHttpClient(CloudstrapClientCredentialsOptions.DefaultBackchannelHttpClientName);
            configurator.Backchannel?.Invoke(backchannel);

            services.TryAddSingleton<IsolatedTokenCache>();
            services.TryAddKeyedSingleton(
                ServiceProviderKeys.ClientCredentialsTokenCache,
                static (provider, _) =>
                    provider.GetRequiredService<IOptions<CloudstrapClientCredentialsOptions>>().Value.TokenCache
                        == TokenCacheMode.Shared
                        ? provider.GetRequiredService<HybridCache>()
                        : provider.GetRequiredService<IsolatedTokenCache>().Cache);

            services.AddSingleton<IClientAccessTokenHandlerProvider, ClientCredentialsAccessTokenHandlerProvider>();
            services.AddHostedService<ClientCredentialsStartupLogger>();

            return services;
        }

        /// <summary>
        /// Marks the collection as already carrying this package's registrations, so a second call is a
        /// no-op (AC-CC10).
        /// </summary>
        private sealed class RegistrationMarker;
    }
}
