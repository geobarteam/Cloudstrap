namespace Cloudstrap.Observability.AzureMonitor
{
    using Cloudstrap.Core;
    using global::Azure.Monitor.OpenTelemetry.Exporter;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;
    using OpenTelemetry;
    using OpenTelemetry.Logs;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;

    /// <summary>
    /// The only place Azure types are touched. The entry point calls this on the
    /// <see cref="OpenTelemetryMode.AzureMonitor"/> arm alone, so a host in any other mode never loads the
    /// Azure Monitor exporter assembly.
    /// </summary>
    internal static class AzureMonitorRegistration
    {
        /// <summary>
        /// Contributes the per-signal Azure Monitor exporters to the pipeline, gated by the
        /// <c>Cloudstrap:OpenTelemetry</c> signal flags, and lifts the base package's
        /// exporter-contribution guard so the host may start.
        /// </summary>
        /// <param name="builder">The observability builder to contribute to.</param>
        /// <param name="configure">
        /// The consumer's exporter callback, applied by the exporter after
        /// <see cref="AzureMonitorExporterSetup"/>, so it always wins.
        /// </param>
        public static void Register(
            CloudstrapObservabilityBuilder builder,
            Action<AzureMonitorExporterOptions>? configure)
        {
            OpenTelemetryOptions telemetry = builder.Telemetry;

            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IConfigureOptions<AzureMonitorExporterOptions>, AzureMonitorExporterSetup>());

            // Idempotent: contributes to the pipeline the base package already built
            OpenTelemetryBuilder openTelemetry = builder.Services.AddOpenTelemetry();

            if (telemetry.EnableTracing)
            {
                openTelemetry.WithTracing(tracing => tracing.AddAzureMonitorTraceExporter(configure));
            }

            if (telemetry.EnableMetrics)
            {
                openTelemetry.WithMetrics(metrics => metrics.AddAzureMonitorMetricExporter(configure));
            }

            if (telemetry.EnableLogs)
            {
                openTelemetry.WithLogging(
                    configureBuilder: logging => logging.AddAzureMonitorLogExporter(configure),
                    configureOptions: null);
            }

            builder.MarkExporterContributed();
        }
    }
}
