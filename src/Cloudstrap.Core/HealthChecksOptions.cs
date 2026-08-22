namespace Cloudstrap.Core
{
    /// <summary>
    /// Health check settings, bound from the <c>Cloudstrap:HealthChecks</c> configuration section.
    /// </summary>
    public sealed class HealthChecksOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:HealthChecks";

        /// <summary>
        /// Gets or sets a value indicating whether health checks and their probe endpoints are registered.
        /// </summary>
        /// <value><see langword="true"/> when health checks are enabled. Defaults to <see langword="true"/>.</value>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the path serving the liveness probe.
        /// </summary>
        /// <value>The liveness probe path. Defaults to <c>/healthz</c>.</value>
        public string LivenessPath { get; set; } = "/healthz";

        /// <summary>
        /// Gets or sets the path serving the readiness probe.
        /// </summary>
        /// <value>The readiness probe path. Defaults to <c>/ready</c>.</value>
        public string ReadinessPath { get; set; } = "/ready";
    }
}
