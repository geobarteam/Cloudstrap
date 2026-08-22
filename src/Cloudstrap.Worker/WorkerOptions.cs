namespace Cloudstrap.Worker
{
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// Worker settings, bound from the <c>Cloudstrap:Worker</c> configuration section.
    /// </summary>
    /// <remarks>
    /// The section carries only the health listener's endpoint knobs. The probe paths and the kill
    /// switch live in Core's <c>Cloudstrap:HealthChecks</c> section (<c>Enabled</c>,
    /// <c>LivenessPath</c>, <c>ReadinessPath</c>) and are never duplicated here — the worker's
    /// probes are the same probes every Cloudstrap web host serves.
    /// </remarks>
    public sealed class WorkerOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Worker";

        /// <summary>
        /// Gets or sets the TCP port the health listener serves the probe endpoints on.
        /// </summary>
        /// <value>The health listener port. Defaults to <c>9000</c>.</value>
        [Range(1, 65535)]
        public int HealthPort { get; set; } = 9000;

        /// <summary>
        /// Gets or sets the address the health listener binds.
        /// </summary>
        /// <value>
        /// <c>"*"</c> (the default) binds all interfaces — the container-probe reality, where the
        /// orchestrator reaches the pod's address, not loopback. Set <c>"localhost"</c> for the
        /// explicit development-time loopback override; there is no environment sniffing — the
        /// bind decision is this option and nothing else.
        /// </value>
        [Required]
        public string HealthListenAddress { get; set; } = "*";
    }
}
