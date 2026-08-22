namespace Cloudstrap.Observability.AzureMonitor
{
    using Cloudstrap.Core;
    using global::Azure.Identity;
    using global::Azure.Monitor.OpenTelemetry.Exporter;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Applies the <c>Cloudstrap:AzureMonitor</c> settings to every signal's exporter options. Registered
    /// before the per-signal exporter calls, so the consumer's own callback — which the exporter applies
    /// afterwards — always has the final say.
    /// </summary>
    /// <remarks>
    /// Settings the consumer did not configure are deliberately left untouched rather than defaulted, so the
    /// exporter's own behavior governs: an absent connection string is resolved by the SDK from the standard
    /// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> variable. This mirrors the base package's OTLP posture.
    /// </remarks>
    internal sealed class AzureMonitorExporterSetup : IConfigureOptions<AzureMonitorExporterOptions>
    {
        private readonly IOptions<AzureMonitorOptions> _options;
        private readonly IOptions<OpenTelemetryOptions> _telemetry;

        // One credential for all three signals: a credential per signal would keep three independent token
        // caches and triple the token traffic. Created on first use, so the default path constructs none.
        private readonly Lazy<DefaultAzureCredential> _defaultCredential =
            new(static () => new DefaultAzureCredential(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureMonitorExporterSetup"/> class.
        /// </summary>
        /// <param name="options">The bound <c>Cloudstrap:AzureMonitor</c> settings.</param>
        /// <param name="telemetry">
        /// The bound <c>Cloudstrap:OpenTelemetry</c> settings, consulted for the always-on sampler flag.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> or <paramref name="telemetry"/> is <see langword="null"/>.
        /// </exception>
        public AzureMonitorExporterSetup(
            IOptions<AzureMonitorOptions> options,
            IOptions<OpenTelemetryOptions> telemetry)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(telemetry);

            _options = options;
            _telemetry = telemetry;
        }

        /// <summary>
        /// Configures one signal's Azure Monitor exporter options from the Cloudstrap settings.
        /// </summary>
        /// <param name="options">The exporter options to configure.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public void Configure(AzureMonitorExporterOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            AzureMonitorOptions cloudstrap = _options.Value;

            if (!string.IsNullOrWhiteSpace(cloudstrap.ConnectionString))
            {
                options.ConnectionString = cloudstrap.ConnectionString;
            }

            ApplySampling(options, cloudstrap, _telemetry.Value.AlwaysOnSampler);

            if (cloudstrap.UseDefaultAzureCredential)
            {
                // Constructing the credential contacts nothing; tokens are acquired lazily, at first export
                options.Credential = _defaultCredential.Value;
            }
        }

        /// <summary>
        /// Applies the sampling policy. The exporter expresses its rate-limited platform default as a
        /// non-null <see cref="AzureMonitorExporterOptions.TracesPerSecond"/>, which takes precedence over
        /// <see cref="AzureMonitorExporterOptions.SamplingRatio"/> — so choosing a fixed percentage means
        /// clearing the rate limit, or the percentage would never take effect. When nothing is configured
        /// Cloudstrap touches neither, leaving the platform default in force.
        /// </summary>
        private static void ApplySampling(
            AzureMonitorExporterOptions options,
            AzureMonitorOptions cloudstrap,
            bool alwaysOnSampler)
        {
            if (alwaysOnSampler)
            {
                options.SamplingRatio = 1.0f;
                options.TracesPerSecond = null;

                return;
            }

            if (cloudstrap.SamplingRatio is float ratio)
            {
                options.SamplingRatio = ratio;
                options.TracesPerSecond = null;

                return;
            }

            if (cloudstrap.TracesPerSecond is double tracesPerSecond)
            {
                options.TracesPerSecond = tracesPerSecond;
            }
        }
    }
}
