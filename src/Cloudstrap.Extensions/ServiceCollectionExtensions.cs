namespace Cloudstrap.Extensions
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registers Cloudstrap's outbound HTTP service clients in a dependency injection container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// The configuration section the per-client settings are bound from, one subsection per client name.
        /// </summary>
        internal const string HttpClientsSectionPath = "Cloudstrap:HttpClients";

        /// <summary>
        /// Registers a typed <see cref="HttpClient"/> driven by the
        /// <c>Cloudstrap:HttpClients:{name}</c> configuration section: base address and timeout come from
        /// configuration, and the correlation identifier is propagated on every outbound request.
        /// </summary>
        /// <typeparam name="TInterface">The contract the client is injected as.</typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation, which must accept an <see cref="HttpClient"/> in its constructor.
        /// </typeparam>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="name">
        /// The logical client name, which is also the configuration subsection name. Defaults to
        /// <typeparamref name="TInterface"/>'s name without its leading <c>I</c> — <c>ICatalogClient</c>
        /// binds <c>Cloudstrap:HttpClients:CatalogClient</c>.
        /// </param>
        /// <param name="configureClient">
        /// An optional hook applied to the client after Cloudstrap's own configuration, so it has the final
        /// say over every value.
        /// </param>
        /// <param name="configureBuilder">
        /// An optional hook applied to the builder after Cloudstrap's own wiring, for handlers or policies
        /// the consumer wants on this client.
        /// </param>
        /// <returns>The client builder, so further configuration can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty or white space.</exception>
        /// <remarks>
        /// <para>
        /// Configuration is validated: a missing section, or a <c>BaseAddress</c> that is absent or
        /// relative, fails at host startup — or at first resolution of the client, whichever comes first —
        /// with a message naming the offending key. A misconfigured client never reaches the network.
        /// </para>
        /// <para>
        /// The correlation handler is attached exactly once, even when the consumer already registered it
        /// through <c>ConfigureHttpClientDefaults</c>.
        /// </para>
        /// <para>
        /// No resilience handler is ever added: retries, circuit breakers and hedging stay the consumer's
        /// choice, applied per client or through <c>ConfigureHttpClientDefaults</c>, and Cloudstrap leaves
        /// that pipeline untouched. The primary handler is likewise never replaced.
        /// </para>
        /// <para>
        /// When the section sets <c>AddUserAccessToken</c> or <c>AddClientAccessToken</c>, the registered
        /// <see cref="IAccessTokenHandlerProvider"/> supplies the token handler, which runs ahead of the
        /// correlation handler. The provider ships with the Cloudstrap.Authentication packages; its absence
        /// while a flag is set fails client creation rather than sending an unauthenticated request.
        /// </para>
        /// <para>
        /// When the section sets <c>EnableHealthCheck</c>, a health check named
        /// <c>{HealthCheckPrefix ?? name}-liveness</c> is added to the application's stock health check
        /// builder, tagged <see cref="CloudstrapHealthCheckTags.Readiness"/>: an unreachable dependency
        /// takes the instance out of rotation without provoking a restart. It probes
        /// <c>BaseAddress + HealthCheckPath</c> and judges by status code. The probe has its own named
        /// <see cref="HttpClient"/>, <c>{name}-liveness</c>, which a consumer may reconfigure like any other.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddCloudstrapHttpServiceClient<TInterface, TImplementation>(
            this IServiceCollection services,
            string? name = null,
            Action<HttpClient>? configureClient = null,
            Action<IHttpClientBuilder>? configureBuilder = null)
            where TInterface : class
            where TImplementation : class, TInterface
        {
            ArgumentNullException.ThrowIfNull(services);

            if (name is not null)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(name);
            }

            string clientName = name ?? DeriveClientName(typeof(TInterface).Name);

            if (RegisterClient(services, clientName))
            {
                AccessTokenHandlerWiring.Register(services, clientName);
                DependencyHealthCheckSetup.Register(services, clientName);
            }

            IHttpClientBuilder builder = services.AddHttpClient<TInterface, TImplementation>(
                clientName,
                (serviceProvider, client) =>
                {
                    HttpClientServiceOptions options = serviceProvider
                        .GetRequiredService<IOptionsMonitor<HttpClientServiceOptions>>()
                        .Get(clientName);

                    if (options.BaseAddress is not null)
                    {
                        client.BaseAddress = options.BaseAddress;
                    }

                    client.Timeout = options.Timeout;

                    configureClient?.Invoke(client);
                });

            services.AddCloudstrapCorrelation();
            builder.AddCloudstrapCorrelationHandler();

            configureBuilder?.Invoke(builder);

            return builder;
        }

        /// <summary>
        /// Derives the client name from the contract's type name, dropping the interface's leading
        /// <c>I</c> when one is present.
        /// </summary>
        /// <param name="typeName">The contract type name.</param>
        /// <returns>The derived client name.</returns>
        private static string DeriveClientName(string typeName) =>
            typeName.Length > 1 && typeName[0] == 'I' && char.IsUpper(typeName[1])
                ? typeName[1..]
                : typeName;

        /// <summary>
        /// Binds and validates one client's settings. Repeated registration of the same name binds nothing
        /// further: doing so would only duplicate identical work and identical failure messages.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="clientName">The client name, which is also the configuration subsection name.</param>
        /// <returns><see langword="true"/> when this is the first registration of that client.</returns>
        private static bool RegisterClient(IServiceCollection services, string clientName)
        {
            HttpServiceClientRegistry registry = GetOrAddRegistry(services);

            if (!registry.Add(clientName))
            {
                return false;
            }

            services.AddOptions<HttpClientServiceOptions>(clientName)
                .BindConfiguration($"{HttpClientsSectionPath}:{clientName}")
                .ValidateOnStart();

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<HttpClientServiceOptions>,
                    HttpClientServiceOptionsValidator>());

            return true;
        }

        /// <summary>
        /// Returns the registry belonging to this service collection, creating and registering it on first
        /// use so registration-time bookkeeping never leaks between containers.
        /// </summary>
        /// <param name="services">The service collection the registry belongs to.</param>
        /// <returns>The registry instance.</returns>
        private static HttpServiceClientRegistry GetOrAddRegistry(IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(HttpServiceClientRegistry)
                    && descriptor.ImplementationInstance is HttpServiceClientRegistry existing)
                {
                    return existing;
                }
            }

            HttpServiceClientRegistry registry = new();
            services.AddSingleton(registry);

            return registry;
        }
    }
}
