namespace Cloudstrap.Observability.AzureMonitor
{
    /// <summary>
    /// Azure Monitor exporter settings, bound from the <c>Cloudstrap:AzureMonitor</c> configuration section.
    /// The section is bound and validated in every <c>Cloudstrap:OpenTelemetry:Mode</c>, so a mistake is
    /// caught before the mode is ever flipped to <c>AzureMonitor</c>.
    /// </summary>
    public sealed class AzureMonitorOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:AzureMonitor";

        /// <summary>
        /// Gets or sets the Application Insights connection string telemetry is sent to. When this setting is
        /// absent, the exporter resolves the standard <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> environment
        /// variable itself. One of the two is required when <c>Cloudstrap:OpenTelemetry:Mode</c> is
        /// <c>AzureMonitor</c>; host startup fails naming both sources when neither is present.
        /// </summary>
        /// <value>The connection string, or <see langword="null"/> when the standard variable supplies it.</value>
        public string? ConnectionString
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the fraction of traces sampled, from <c>0.0</c> (none) to <c>1.0</c> (all). Mutually
        /// exclusive with <see cref="TracesPerSecond"/>.
        /// </summary>
        /// <value>The sampling ratio, or <see langword="null"/> to leave sampling at the exporter's own default.</value>
        /// <remarks>
        /// When neither this setting nor <see cref="TracesPerSecond"/> is configured, Cloudstrap sets nothing and
        /// the Azure Monitor exporter's platform default applies: rate-limited sampling at 5 traces per second,
        /// NOT 100%. Raise the volume by setting this ratio to <c>1.0</c> or by giving
        /// <see cref="TracesPerSecond"/> a higher rate. Application Insights renormalizes counts in the portal
        /// from the sample rate stamped on every exported item, so aggregates stay accurate either way.
        /// </remarks>
        public float? SamplingRatio
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the maximum number of traces sampled per second. Mutually exclusive with
        /// <see cref="SamplingRatio"/>.
        /// </summary>
        /// <value>
        /// The rate limit, or <see langword="null"/> to leave sampling at the exporter's own default — see the
        /// remarks on <see cref="SamplingRatio"/> for what that default is.
        /// </value>
        public double? TracesPerSecond
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether Entra ID (Microsoft Entra) credentials authenticate telemetry
        /// ingestion, for workspaces that disable local (connection-string key) authentication. When enabled, a
        /// <c>DefaultAzureCredential</c> is attached, which behaves identically across Azure Web Apps,
        /// containers and developer machines. A credential supplied through the registration callback always
        /// wins over this flag.
        /// </summary>
        /// <value><see langword="true"/> when Entra ID authentication is used. Defaults to <see langword="false"/>.</value>
        public bool UseDefaultAzureCredential
        {
            get; set;
        }
    }
}
