namespace Cloudstrap.Proxy
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

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
            return builder.Services.AddReverseProxy()
                .LoadFromConfig(builder.Configuration.GetSection(section));
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
