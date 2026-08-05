namespace Cloudstrap.Core
{
    /// <summary>
    /// The root Cloudstrap settings model, bound from the <c>Cloudstrap</c> configuration section.
    /// Aggregates the cross-cutting settings every Cloudstrap package reads.
    /// </summary>
    public sealed class CloudstrapOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap";

        /// <summary>
        /// Gets or sets the application identity settings.
        /// </summary>
        /// <value>The application identity settings. Never <see langword="null"/>.</value>
        public ApplicationOptions Application { get; set; } = new();

        /// <summary>
        /// Gets or sets the logging settings.
        /// </summary>
        /// <value>The logging settings. Never <see langword="null"/>.</value>
        public LoggingOptions Logging { get; set; } = new();

        /// <summary>
        /// Gets or sets the OpenTelemetry settings.
        /// </summary>
        /// <value>The OpenTelemetry settings. Never <see langword="null"/>.</value>
        public OpenTelemetryOptions OpenTelemetry { get; set; } = new();

        /// <summary>
        /// Gets or sets the correlation settings.
        /// </summary>
        /// <value>The correlation settings. Never <see langword="null"/>.</value>
        public CorrelationOptions Correlation { get; set; } = new();

        /// <summary>
        /// Gets or sets the health check settings.
        /// </summary>
        /// <value>The health check settings. Never <see langword="null"/>.</value>
        public HealthChecksOptions HealthChecks { get; set; } = new();

        /// <summary>
        /// Gets the outbound HTTP client registry, keyed by logical client name. Each entry is bound from
        /// <c>Cloudstrap:HttpClients:{name}</c>.
        /// </summary>
        /// <value>The named HTTP client settings. Empty when no clients are configured.</value>
        public Dictionary<string, HttpClientServiceOptions> HttpClients { get; } = [];
    }
}
