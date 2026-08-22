namespace Cloudstrap.Demo.Worker
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Diagnostics.HealthChecks;

    /// <summary>
    /// The demo's outage drill: reports Unhealthy while the sentinel file at
    /// <c>Demo:OutageSentinelPath</c> exists — a process-external toggle, so the E2E suite (or a
    /// curious developer) can flip readiness on the running host without touching the process.
    /// Consumer code, deliberately in the demo app — the package ships no health checks.
    /// </summary>
    internal sealed class DemoOutageHealthCheck : IHealthCheck
    {
        private readonly string _sentinelPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="DemoOutageHealthCheck"/> class.
        /// </summary>
        /// <param name="configuration">The configuration carrying <c>Demo:OutageSentinelPath</c>.</param>
        public DemoOutageHealthCheck(IConfiguration configuration)
        {
            _sentinelPath = configuration["Demo:OutageSentinelPath"] ?? "demo-outage.sentinel";
        }

        /// <inheritdoc/>
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(File.Exists(_sentinelPath)
                ? HealthCheckResult.Unhealthy($"Outage sentinel present: {_sentinelPath}")
                : HealthCheckResult.Healthy());
        }
    }
}
