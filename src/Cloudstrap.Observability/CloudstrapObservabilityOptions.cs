namespace Cloudstrap.Observability
{
    using OpenTelemetry.Exporter;
    using OpenTelemetry.Logs;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Resources;
    using OpenTelemetry.Trace;
    using Serilog;

    /// <summary>
    /// Code-level options for <see cref="HostApplicationBuilderExtensions.UseCloudstrapObservability"/>.
    /// Everything driven by settings lives under the <c>Cloudstrap:</c> configuration section; these options
    /// carry only what cannot be expressed in configuration, such as callbacks.
    /// </summary>
    public sealed class CloudstrapObservabilityOptions
    {
        /// <summary>
        /// Gets or sets how Cloudstrap relates to the host's OpenTelemetry pipeline: own it, or contribute
        /// samplers, filters and <c>cloudstrap.*</c> enrichment to a pipeline the host already owns.
        /// </summary>
        /// <value>The pipeline mode. Defaults to <see cref="ObservabilityPipelineMode.Owner"/>.</value>
        public ObservabilityPipelineMode PipelineMode
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a callback that runs over the Serilog configuration after Cloudstrap has applied its
        /// own shape, giving the consumer the final say — add sinks, change levels, or override anything
        /// Cloudstrap configured.
        /// </summary>
        /// <value>The Serilog configuration callback, or <see langword="null"/> when not used.</value>
        public Action<LoggerConfiguration>? ConfigureSerilog
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a callback that runs over the telemetry resource after Cloudstrap has applied its
        /// identity, so consumers can add or override resource attributes.
        /// </summary>
        /// <value>The resource configuration callback, or <see langword="null"/> when not used.</value>
        public Action<ResourceBuilder>? ConfigureResource
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a callback that runs over the tracing pipeline after Cloudstrap has registered its
        /// instrumentation and exporters — the place to add sources, processors or extra exporters.
        /// </summary>
        /// <value>The tracing configuration callback, or <see langword="null"/> when not used.</value>
        public Action<TracerProviderBuilder>? ConfigureTracing
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a callback that runs over the metrics pipeline after Cloudstrap has registered its
        /// meters and exporters — the place to add meters, views or extra exporters.
        /// </summary>
        /// <value>The metrics configuration callback, or <see langword="null"/> when not used.</value>
        public Action<MeterProviderBuilder>? ConfigureMetrics
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a callback that runs over the OpenTelemetry logging options after Cloudstrap has
        /// registered its exporters — the place to add extra log exporters or tweak record shaping.
        /// </summary>
        /// <value>The logging configuration callback, or <see langword="null"/> when not used.</value>
        public Action<OpenTelemetryLoggerOptions>? ConfigureLogging
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a callback that runs over each enabled signal's OTLP exporter options after Cloudstrap
        /// has applied its own configuration, giving the consumer the final say. Only invoked when
        /// <c>Cloudstrap:OpenTelemetry:Mode</c> is <c>Otlp</c>.
        /// </summary>
        /// <value>The OTLP exporter configuration callback, or <see langword="null"/> when not used.</value>
        public Action<OtlpExporterOptions>? ConfigureOtlpExporter
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the default trace noise filter is applied: probe endpoints
        /// (from <c>Cloudstrap:HealthChecks</c>), Blazor infrastructure paths, static assets and
        /// <see cref="IgnoredPathSegments"/> are dropped from traces. When <see langword="false"/> Cloudstrap
        /// composes no filter at all and every request is traced unless the host filters it.
        /// </summary>
        /// <value><see langword="true"/> when the default filter is applied. Defaults to <see langword="true"/>.</value>
        public bool EnableDefaultTraceNoiseFilter { get; set; } = true;

        /// <summary>
        /// Gets the path segments appended to the default trace noise list: a request whose path contains any
        /// of these segments is dropped from traces. Only effective while
        /// <see cref="EnableDefaultTraceNoiseFilter"/> is <see langword="true"/>.
        /// </summary>
        /// <value>The extra ignored path segments. Empty by default.</value>
        public IList<string> IgnoredPathSegments { get; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether Cloudstrap installs its sampler chain (parent-based or
        /// always-on per <c>Cloudstrap:OpenTelemetry:AlwaysOnSampler</c>, wrapped in the Blazor hub sampler
        /// unless <c>EnableBlazorHubTracing</c>). Set to <see langword="false"/> when the host owns the
        /// sampler — <c>SetSampler</c> is last-wins, so Cloudstrap steps aside entirely.
        /// </summary>
        /// <value><see langword="true"/> when Cloudstrap installs its sampler. Defaults to <see langword="true"/>.</value>
        public bool ApplySampler { get; set; } = true;
    }
}
