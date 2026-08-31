namespace Cloudstrap.BlazorWasm
{
    using System.Text.Json;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Refit;

    /// <summary>
    /// One-line registrations for API clients that ride the Cloudstrap cookie+XSRF pipeline.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        private static readonly RefitSettings _defaultRefitSettings = new()
        {
            ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            }),
        };

        /// <summary>
        /// Registers a typed HTTP client whose pipeline includes browser credentials and XSRF
        /// attachment — the Cloudstrap cookie+XSRF pipeline for a BFF-hosted WASM app.
        /// </summary>
        /// <typeparam name="TClient">
        /// The typed client class; it must have a constructor accepting <see cref="HttpClient"/>.
        /// </typeparam>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="baseAddress">
        /// The BFF's base address, typically the host environment's base address. Passed to
        /// <see cref="Uri"/> as-is — end it with a trailing slash so relative paths resolve under it.
        /// </param>
        /// <param name="configureClient">An optional hook further configuring the <see cref="HttpClient"/>.</param>
        /// <returns>The client's <see cref="IHttpClientBuilder"/>, for further chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="baseAddress"/> is <see langword="null"/>, empty or white-space.</exception>
        public static IHttpClientBuilder AddCloudstrapWasmHttpClient<TClient>(
            this IServiceCollection services,
            string baseAddress,
            Action<HttpClient>? configureClient = null)
            where TClient : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);

            services.TryAddSingleton<IAntiforgeryTokenStore, AntiforgeryTokenStore>();
            services.TryAddTransient<CookieHandler>();

            return services.AddHttpClient<TClient>(client =>
            {
                client.BaseAddress = new Uri(baseAddress);
                configureClient?.Invoke(client);
            })
            .AddHttpMessageHandler<CookieHandler>();
        }

        /// <summary>
        /// Registers a Refit client interface whose pipeline includes browser credentials and XSRF
        /// attachment — the same Cloudstrap cookie+XSRF pipeline, with the interface implemented by
        /// Refit.
        /// </summary>
        /// <typeparam name="TClient">The Refit client interface.</typeparam>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="baseAddress">
        /// The BFF's base address. Passed to <see cref="Uri"/> as-is — end it with a trailing slash
        /// so relative paths resolve under it.
        /// </param>
        /// <param name="refitSettings">
        /// Optional per-registration Refit settings; <see langword="null"/> applies the default
        /// System.Text.Json serialization (camelCase, case-insensitive).
        /// </param>
        /// <returns>The client's <see cref="IHttpClientBuilder"/>, for further chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="baseAddress"/> is <see langword="null"/>, empty or white-space.</exception>
        /// <remarks>
        /// Registration goes through <see cref="RestService"/> on the factory-built
        /// <see cref="HttpClient"/> — deliberately never through <c>Refit.HttpClientFactory</c>,
        /// which was compiled against <c>Microsoft.Extensions.Http</c> 9.x and throws
        /// <see cref="MissingMethodException"/> on a .NET 10 WebAssembly app.
        /// </remarks>
        public static IHttpClientBuilder AddCloudstrapWasmRefitClient<TClient>(
            this IServiceCollection services,
            string baseAddress,
            RefitSettings? refitSettings = null)
            where TClient : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);

            services.TryAddSingleton<IAntiforgeryTokenStore, AntiforgeryTokenStore>();
            services.TryAddTransient<CookieHandler>();

            return services.AddHttpClient(typeof(TClient).Name)
                .ConfigureHttpClient(client => client.BaseAddress = new Uri(baseAddress))
                .AddHttpMessageHandler<CookieHandler>()
                .AddTypedClient((httpClient, _) =>
                    RestService.For<TClient>(httpClient, refitSettings ?? _defaultRefitSettings));
        }
    }
}
