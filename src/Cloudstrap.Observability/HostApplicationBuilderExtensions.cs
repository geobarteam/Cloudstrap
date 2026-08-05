namespace Cloudstrap.Observability
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Serilog;
    using Serilog.Extensions.Logging;

    /// <summary>
    /// The Cloudstrap observability entry point for host applications.
    /// </summary>
    public static class HostApplicationBuilderExtensions
    {
        private static readonly string[] _frameworkCategorySeeds =
        [
            "Microsoft.AspNetCore",
            "Microsoft.AspNetCore.Hosting.Diagnostics",
            "System.Net.Http.HttpClient",
            "Microsoft.Hosting.Lifetime",
        ];

        /// <summary>
        /// Adds Cloudstrap observability to the host: Serilog console/file logging shaped by the
        /// <c>Cloudstrap:Logging</c> settings, and the configured minimum levels applied to the host's
        /// logging pipeline.
        /// </summary>
        /// <param name="builder">The host application builder to add observability to.</param>
        /// <param name="configure">An optional callback for code-level options.</param>
        /// <returns>A builder exposing the service collection and the bound telemetry settings.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <exception cref="ConfigurationValidationException">
        /// The <c>Cloudstrap</c> configuration section is absent or invalid. The section is read and validated
        /// eagerly, so misconfiguration surfaces at this call rather than at first use.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The Serilog provider is <em>added</em> to the host's logging: providers registered before this call
        /// are kept — <c>ClearProviders</c> is never called. The configured minimum level is applied to the
        /// host pipeline, common framework categories (<c>Microsoft.AspNetCore</c>,
        /// <c>Microsoft.AspNetCore.Hosting.Diagnostics</c>, <c>System.Net.Http.HttpClient</c>,
        /// <c>Microsoft.Hosting.Lifetime</c>) are seeded at <see cref="LogLevel.Warning"/>, and entries in
        /// <c>Cloudstrap:Logging:LevelOverrides</c> are applied last so consumer overrides win — including
        /// over the seeds.
        /// </para>
        /// <para>
        /// <see cref="CloudstrapObservabilityOptions.ConfigureSerilog"/> runs after Cloudstrap's own Serilog
        /// shape and has the final say. Telemetry pipeline behavior driven by
        /// <c>Cloudstrap:OpenTelemetry</c> is provided by this same package.
        /// </para>
        /// </remarks>
        public static CloudstrapObservabilityBuilder UseCloudstrapObservability(
            this IHostApplicationBuilder builder,
            Action<CloudstrapObservabilityOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            CloudstrapObservabilityOptions observabilityOptions = new();
            configure?.Invoke(observabilityOptions);

            CloudstrapOptions options = builder.Configuration.GetCloudstrapOptions();

            builder.Services.AddCloudstrapCore();

            builder.Services.AddCloudstrapCorrelation();
            builder.Services.AddCloudstrapBusinessTrace();

            ExporterContributionMarker contributionMarker = new();
            builder.Services.AddSingleton(contributionMarker);

            LoggerConfiguration loggerConfiguration = new();
            SerilogPipeline.Configure(loggerConfiguration, options);
            observabilityOptions.ConfigureSerilog?.Invoke(loggerConfiguration);
            Serilog.Core.Logger serilogLogger = loggerConfiguration.CreateLogger();

            // Registered as one provider among the host's — never through Serilog's factory replacement,
            // which would sideline pre-registered providers and bypass the level filters applied below.
            builder.Services.AddSingleton<ILoggerProvider>(
                _ => new SerilogLoggerProvider(serilogLogger, dispose: true));

            builder.Logging.SetMinimumLevel(options.Logging.Level);

            foreach (string category in _frameworkCategorySeeds)
            {
                builder.Logging.AddFilter(category, LogLevel.Warning);
            }

            foreach (KeyValuePair<string, LogLevel> levelOverride in options.Logging.LevelOverrides)
            {
                builder.Logging.AddFilter(levelOverride.Key, levelOverride.Value);
            }

            if (options.OpenTelemetry.IsActive)
            {
                if (observabilityOptions.PipelineMode == ObservabilityPipelineMode.Contribute)
                {
                    OpenTelemetryPipeline.ConfigureContribute(builder.Services, options, observabilityOptions);
                }
                else
                {
                    OpenTelemetryPipeline.ConfigureOwner(builder.Services, options, observabilityOptions, builder.Environment);
                }
            }

            return new CloudstrapObservabilityBuilder(builder.Services, options.OpenTelemetry, contributionMarker);
        }
    }
}
