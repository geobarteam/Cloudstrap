namespace Cloudstrap.Observability.AzureMonitor
{
    using Azure.Monitor.OpenTelemetry.Exporter;
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Adds Application Insights export to the Cloudstrap observability pipeline.
    /// </summary>
    public static class CloudstrapObservabilityBuilderExtensions
    {
        /// <summary>
        /// Adds the Azure Monitor (Application Insights) exporters to the observability pipeline when
        /// <c>Cloudstrap:OpenTelemetry:Mode</c> is <see cref="OpenTelemetryMode.AzureMonitor"/>, and binds and
        /// validates the <c>Cloudstrap:AzureMonitor</c> section in every mode.
        /// </summary>
        /// <param name="builder">The builder returned by <c>UseCloudstrapObservability</c>.</param>
        /// <param name="configure">
        /// An optional callback applied to the exporter options of every signal, after Cloudstrap's own
        /// configuration, so it always has the final say. Use it for the exporter settings Cloudstrap does not
        /// surface as configuration — offline storage, standard metrics, a code-supplied credential.
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// The call is safe to leave in place permanently: in any mode other than
        /// <see cref="OpenTelemetryMode.AzureMonitor"/> it contributes no exporter and changes no pipeline
        /// behavior, so the same binary moves between environments on configuration alone. It is also
        /// idempotent — calling it twice registers one set of exporters.
        /// </para>
        /// <para>
        /// The <c>Cloudstrap:AzureMonitor</c> section is bound and validated regardless of mode, so a bad
        /// sampling value or a missing connection string fails host startup naming the offending keys rather
        /// than surfacing the day the mode is flipped.
        /// </para>
        /// <para>
        /// When neither <see cref="AzureMonitorOptions.SamplingRatio"/> nor
        /// <see cref="AzureMonitorOptions.TracesPerSecond"/> is configured, Cloudstrap sets no sampling policy
        /// and the exporter's platform default applies: rate-limited sampling at 5 traces per second, not 100%.
        /// </para>
        /// </remarks>
        public static CloudstrapObservabilityBuilder AddAzureMonitor(
            this CloudstrapObservabilityBuilder builder,
            Action<AzureMonitorExporterOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (builder.Services.Any(
                descriptor => descriptor.ServiceType == typeof(AzureMonitorRegistrationMarker)))
            {
                return builder;
            }

            builder.Services.TryAddSingleton<AzureMonitorRegistrationMarker>();

            OpenTelemetryMode mode = builder.Telemetry.Mode;

            builder.Services.AddOptions<AzureMonitorOptions>()
                .BindConfiguration(AzureMonitorOptions.SectionName)
                .ValidateOnStart();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<AzureMonitorOptions>, AzureMonitorOptionsValidator>(
                    provider => new AzureMonitorOptionsValidator(
                        provider.GetRequiredService<IConfiguration>(), mode)));

            if (mode != OpenTelemetryMode.AzureMonitor)
            {
                return builder;
            }

            AzureMonitorRegistration.Register(builder, configure);

            return builder;
        }
    }
}
