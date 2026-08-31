namespace Cloudstrap.BlazorWasm
{
    using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Http;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registers the Cloudstrap Blazor WebAssembly client services in one composite call.
    /// </summary>
    public static class WebAssemblyHostBuilderExtensions
    {
        /// <summary>
        /// Registers everything a BFF-hosted Blazor WebAssembly client needs: the shared antiforgery
        /// token store, the cookie+XSRF <see cref="CookieHandler"/>, the named auth
        /// <see cref="HttpClient"/> against the host's base address, BFF-driven authentication state
        /// (<see cref="Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider"/> +
        /// the <see cref="IBffAuthenticationStateProvider"/> refresh seam), authorization core
        /// services and cascading authentication state.
        /// </summary>
        /// <param name="builder">The WebAssembly host builder to register into.</param>
        /// <param name="configure">
        /// An optional delegate overriding <see cref="CloudstrapBlazorWasmOptions"/> — it wins over
        /// the <c>Cloudstrap:BlazorWasm</c> configuration section.
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// The composite registers <strong>no</strong> localization — call <c>AddLocalization()</c>
        /// yourself if you need it — and <strong>no</strong> login or logout machinery: sign-in and
        /// sign-out are full-page navigations to the BFF's endpoints (Cloudstrap's OIDC package maps
        /// them at <c>/account/login</c> and <c>/account/logout</c>), after which
        /// <see cref="IBffAuthenticationStateProvider.ClearAuthenticationState"/> refreshes the state.
        /// </para>
        /// <para>
        /// Repeat calls are safe: services register once, options delegates compose.
        /// </para>
        /// </remarks>
        public static WebAssemblyHostBuilder AddCloudstrapBlazorWasm(
            this WebAssemblyHostBuilder builder,
            Action<CloudstrapBlazorWasmOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.AddCloudstrapBlazorWasmServices(
                builder.HostEnvironment.BaseAddress,
                builder.Configuration,
                configure);

            return builder;
        }

        /// <summary>
        /// The composite's testable seam: a <see cref="WebAssemblyHostBuilder"/> cannot be
        /// constructed outside a browser, so the registration list lives here against a plain
        /// service collection.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="baseAddress">The application's base address — the BFF host.</param>
        /// <param name="configuration">The configuration carrying the optional <c>Cloudstrap:BlazorWasm</c> section.</param>
        /// <param name="configure">The optional code-level override delegate; it wins over configuration.</param>
        /// <returns>The same <paramref name="services"/> instance.</returns>
        internal static IServiceCollection AddCloudstrapBlazorWasmServices(
            this IServiceCollection services,
            string baseAddress,
            IConfiguration configuration,
            Action<CloudstrapBlazorWasmOptions>? configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseAddress);
            ArgumentNullException.ThrowIfNull(configuration);

            OptionsBuilder<CloudstrapBlazorWasmOptions> options = services
                .AddOptions<CloudstrapBlazorWasmOptions>()
                .Bind(configuration.GetSection(CloudstrapBlazorWasmOptions.SectionName));

            if (configure is not null)
            {
                options.Configure(configure);
            }

            services.TryAddSingleton<IAntiforgeryTokenStore, AntiforgeryTokenStore>();
            services.TryAddTransient<CookieHandler>();
            services.TryAddSingleton(new BlazorWasmRegistrationState(baseAddress));

            // The named auth client: the factory infrastructure plus a deferred configurator that
            // reads the client name from the bound options at resolve time (never a throwaway
            // options instance at registration time).
            services.AddHttpClient();
            services.TryAddEnumerable(
                ServiceDescriptor.Transient<IConfigureOptions<HttpClientFactoryOptions>,
                    AuthHttpClientConfigurator>());

            services.TryAddScoped<BffAuthenticationStateProvider>();
            services.TryAddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(
                sp => sp.GetRequiredService<BffAuthenticationStateProvider>());
            services.TryAddScoped<IBffAuthenticationStateProvider>(
                sp => sp.GetRequiredService<BffAuthenticationStateProvider>());

            services.AddAuthorizationCore();
            services.AddCascadingAuthenticationState();

            return services;
        }
    }
}
