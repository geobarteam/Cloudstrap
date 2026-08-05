namespace Cloudstrap.Core
{
    /// <summary>
    /// OpenTelemetry settings, bound from the <c>Cloudstrap:OpenTelemetry</c> configuration section.
    /// </summary>
    public sealed class OpenTelemetryOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:OpenTelemetry";

        /// <summary>
        /// Gets or sets the operating mode of the OpenTelemetry pipeline.
        /// </summary>
        /// <value>The pipeline mode. Defaults to <see cref="OpenTelemetryMode.Disabled"/>.</value>
        public OpenTelemetryMode Mode { get; set; } = OpenTelemetryMode.Disabled;

        /// <summary>
        /// Gets or sets the endpoint telemetry is exported to. Required when <see cref="Mode"/> is
        /// <see cref="OpenTelemetryMode.Otlp"/>, unless the OpenTelemetry SDK's standard
        /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> variable is present in configuration. When set, the value must
        /// be an absolute <c>http</c> or <c>https</c> URI — an explicit endpoint is validated even when the
        /// standard variable is also present.
        /// </summary>
        /// <value>The OTLP endpoint, or <see langword="null"/> when no endpoint is configured.</value>
        public Uri? Endpoint
        {
            get; set;
        }

        /// <summary>
        /// Gets the headers sent with every export request, keyed by header name. This is the sanctioned way
        /// to pass an ingestion token or tenant key to an OTLP receiver.
        /// </summary>
        /// <value>The export header map. Empty when no headers are configured.</value>
        public Dictionary<string, string> Headers { get; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether distributed tracing is collected.
        /// </summary>
        /// <value><see langword="true"/> when tracing is enabled. Defaults to <see langword="true"/>.</value>
        public bool EnableTracing { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether metrics are collected.
        /// </summary>
        /// <value><see langword="true"/> when metrics are enabled. Defaults to <see langword="true"/>.</value>
        public bool EnableMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether logs are routed through the OpenTelemetry pipeline.
        /// </summary>
        /// <value><see langword="true"/> when log export is enabled. Defaults to <see langword="true"/>.</value>
        public bool EnableLogs { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether a console exporter is added alongside the exporter selected by
        /// <see cref="Mode"/>, so human-readable telemetry stays visible locally. Has no effect when
        /// <see cref="Mode"/> is <see cref="OpenTelemetryMode.Disabled"/>.
        /// </summary>
        /// <value><see langword="true"/> when the additional console exporter is enabled. Defaults to <see langword="true"/>.</value>
        public bool EnableConsole { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether .NET runtime metrics (GC, thread pool, JIT) are collected.
        /// </summary>
        /// <value><see langword="true"/> when runtime metrics are enabled. Defaults to <see langword="true"/>.</value>
        public bool EnableRuntimeMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether outbound HTTP client metrics are collected.
        /// </summary>
        /// <value><see langword="true"/> when HTTP client metrics are enabled. Defaults to <see langword="true"/>.</value>
        public bool EnableHttpClientMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether ASP.NET Core request metrics (duration, count, errors)
        /// are collected. Set to <see langword="false"/> for non-web workloads such as background workers.
        /// </summary>
        /// <value><see langword="true"/> when ASP.NET Core metrics are enabled. Defaults to <see langword="true"/>.</value>
        public bool EnableAspNetCoreMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether messaging metrics are collected.
        /// Set to <see langword="false"/> for hosts that send or handle no messages.
        /// </summary>
        /// <value><see langword="true"/> when messaging metrics are enabled. Defaults to <see langword="true"/>.</value>
        public bool EnableMessagingMetrics { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether SQL client instrumentation is added. Off by default because
        /// it is only useful for workloads that talk to a SQL database.
        /// </summary>
        /// <value><see langword="true"/> when SQL client instrumentation is enabled. Defaults to <see langword="false"/>.</value>
        public bool EnableSqlClientInstrumentation
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether Blazor Server SignalR component-hub spans are exported.
        /// Off by default: these per-interaction round-trips have very high volume and low diagnostic value,
        /// and the inbound calls they trigger are traced regardless. Turn on to diagnose circuit issues.
        /// </summary>
        /// <value><see langword="true"/> when hub tracing is enabled. Defaults to <see langword="false"/>.</value>
        public bool EnableBlazorHubTracing
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the always-on sampler is used, so every trace is recorded.
        /// Intended for development and diagnosis, not for production volume.
        /// </summary>
        /// <value><see langword="true"/> when the always-on sampler is used. Defaults to <see langword="false"/>.</value>
        public bool AlwaysOnSampler
        {
            get; set;
        }

        /// <summary>
        /// Gets a value indicating whether the OpenTelemetry pipeline is active, that is
        /// <see cref="Mode"/> is not <see cref="OpenTelemetryMode.Disabled"/>.
        /// </summary>
        /// <value><see langword="true"/> when the pipeline is active.</value>
        public bool IsActive => Mode != OpenTelemetryMode.Disabled;

        /// <summary>
        /// Gets a value indicating whether a console exporter should be added: <see langword="true"/> when the
        /// pipeline is active and either <see cref="Mode"/> is <see cref="OpenTelemetryMode.Console"/> or
        /// <see cref="EnableConsole"/> is <see langword="true"/>. Always <see langword="false"/> when the
        /// pipeline is disabled.
        /// </summary>
        /// <value><see langword="true"/> when a console exporter should be added.</value>
        public bool IsConsoleEnabled => IsActive && (Mode == OpenTelemetryMode.Console || EnableConsole);
    }
}
