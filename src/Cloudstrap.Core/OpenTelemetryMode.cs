namespace Cloudstrap.Core
{
    /// <summary>
    /// Defines the operating mode of the OpenTelemetry pipeline.
    /// </summary>
    public enum OpenTelemetryMode
    {
        /// <summary>
        /// The OpenTelemetry pipeline is off. No telemetry is collected or exported.
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// The pipeline is on and exports to the console only. No endpoint is required;
        /// useful for local development.
        /// </summary>
        Console = 1,

        /// <summary>
        /// The pipeline is on and exports over OTLP.
        /// <see cref="OpenTelemetryOptions.Endpoint"/> is required and must be an absolute
        /// <c>http</c> or <c>https</c> URI.
        /// </summary>
        Otlp = 2,

        /// <summary>
        /// The pipeline is on and exports to Azure Monitor. Exporter settings live in the
        /// Azure Monitor package; no additional value is required here.
        /// </summary>
        AzureMonitor = 3,
    }
}
