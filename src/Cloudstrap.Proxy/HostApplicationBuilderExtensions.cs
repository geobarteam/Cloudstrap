namespace Cloudstrap.Proxy
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using Microsoft.Net.Http.Headers;
    using Yarp.ReverseProxy.Configuration;
    using Yarp.ReverseProxy.Forwarder;
    using Yarp.ReverseProxy.Transforms;
    using Yarp.ReverseProxy.Transforms.Builder;

    /// <summary>
    /// Registers the trusted-subsystem proxy on the generic host: YARP over its own configuration section, plus the
    /// Cloudstrap options.
    /// </summary>
    public static class HostApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers YARP, loading routes and clusters from YARP's own configuration section
        /// (<see cref="CloudstrapProxyOptions.ConfigurationSection"/>, <c>ReverseProxy</c> by default), and binds the
        /// <c>Cloudstrap:Proxy</c> section with validation at host startup.
        /// </summary>
        /// <param name="builder">The host application builder to configure.</param>
        /// <param name="configure">
        /// Optional code-level settings, applied after the <c>Cloudstrap:Proxy</c> configuration section.
        /// </param>
        /// <returns>
        /// YARP's own <see cref="IReverseProxyBuilder"/>, so every YARP extension (transforms, config filters,
        /// destination resolvers, extra config providers) chains on it.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The method was already called on this host.</exception>
        /// <remarks>
        /// <para>
        /// The configuration section name is read when this method runs: add the configuration sources that set
        /// <c>Cloudstrap:Proxy:ConfigurationSection</c> <em>before</em> calling it. A section that does not exist fails
        /// host startup with a message naming that key.
        /// </para>
        /// <para>
        /// A route without an <c>AuthorizationPolicy</c> requires the application's default authorization policy (a
        /// signed-in user unless the application changed it); nothing is forwarded for a caller who fails it. Set the
        /// route's policy to <c>Anonymous</c> for a deliberately public route.
        /// </para>
        /// <para>
        /// The caller's <c>Cookie</c> and <c>Authorization</c> headers are removed from every proxied request, so the
        /// caller's credentials never reach the destination. The route metadata
        /// <see cref="ProxyMetadataKeys.ForwardInboundCredentials"/> = <c>true</c>, or
        /// <see cref="CloudstrapProxyOptions.StripInboundCredentials"/> = <see langword="false"/>, keeps them; both are
        /// unsafe for the trusted-subsystem pattern. <c>X-Forwarded-*</c> headers follow YARP's defaults (a caller-sent
        /// value is replaced), and the destination receives its own <c>Host</c>.
        /// </para>
        /// <para>
        /// A cluster in <see cref="ProxyTokenMode.Client"/> mode (the default) forwards with the application's token
        /// from the registered <c>IClientAccessTokenHandlerProvider</c>; without a provider, host startup fails. This
        /// replaces YARP's <c>IForwarderHttpClientFactory</c>.
        /// </para>
        /// <para>Map the routes with <see cref="EndpointRouteBuilderExtensions.MapCloudstrapProxy"/>.</para>
        /// </remarks>
        public static IReverseProxyBuilder AddCloudstrapProxy(
            this IHostApplicationBuilder builder,
            Action<CloudstrapProxyOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(ProxyRegistrationMarker)))
            {
                throw new InvalidOperationException(
                    $"{nameof(AddCloudstrapProxy)} was already called on this host. Call it once, and chain further YARP"
                    + " configuration on the builder it returns.");
            }

            builder.Services.AddSingleton<ProxyRegistrationMarker>();

            OptionsBuilder<CloudstrapProxyOptions> options = builder.Services.AddOptions<CloudstrapProxyOptions>()
                .BindConfiguration(CloudstrapProxyOptions.SectionName);
            if (configure is not null)
            {
                options.Configure(configure);
            }

            options.ValidateOnStart();
            builder.Services.AddSingleton<IValidateOptions<CloudstrapProxyOptions>, CloudstrapProxyOptionsValidator>();
            builder.Services.AddSingleton<IValidateOptions<CloudstrapProxyOptions>, ProxyConfigurationSectionValidator>();

            string section = ResolveConfigurationSection(builder.Configuration, configure);
            IReverseProxyBuilder proxy = builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection(section))
                .AddConfigFilter<ProxyRouteConfigFilter>()
                .AddTransforms(AddInboundCredentialRemoval);
            builder.Services.Replace(ServiceDescriptor.Singleton<IForwarderHttpClientFactory, CloudstrapForwarderHttpClientFactory>());
            return proxy;
        }

        /// <summary>
        /// Removes the caller's <c>Cookie</c> and <c>Authorization</c> request headers on every route, so the caller's
        /// credentials stay at the front door. Skipped when <see cref="CloudstrapProxyOptions.StripInboundCredentials"/>
        /// is <see langword="false"/> or the route's <see cref="ProxyMetadataKeys.ForwardInboundCredentials"/> metadata
        /// is <c>true</c>. Transforms run before the forwarder's handler chain, so an outbound token set there is never
        /// removed.
        /// </summary>
        private static void AddInboundCredentialRemoval(TransformBuilderContext context)
        {
            CloudstrapProxyOptions options = context.Services.GetRequiredService<IOptions<CloudstrapProxyOptions>>().Value;
            if (!options.StripInboundCredentials || ProxyMetadataReader.ForwardsInboundCredentials(context.Route))
            {
                return;
            }

            context.AddRequestHeaderRemove(HeaderNames.Cookie);
            context.AddRequestHeaderRemove(HeaderNames.Authorization);
        }

        /// <summary>
        /// Resolves the YARP section name at registration time, the same way the options will: the configured key,
        /// then the code-level delegate. Only that key is read here, so the rest of the section is validated once, at
        /// host startup.
        /// </summary>
        private static string ResolveConfigurationSection(IConfiguration configuration, Action<CloudstrapProxyOptions>? configure)
        {
            CloudstrapProxyOptions options = new();
            string? configured = configuration[$"{CloudstrapProxyOptions.SectionName}:{nameof(CloudstrapProxyOptions.ConfigurationSection)}"];
            if (configured is not null)
            {
                options.ConfigurationSection = configured;
            }

            configure?.Invoke(options);
            return options.ConfigurationSection ?? string.Empty;
        }

        /// <summary>
        /// Marks the collection as already carrying the proxy registration, so a second call fails fast.
        /// </summary>
        private sealed class ProxyRegistrationMarker;
    }
}
