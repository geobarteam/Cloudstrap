namespace Cloudstrap.Worker.Tests.Infrastructure
{
    using Microsoft.Extensions.Diagnostics.HealthChecks;

    /// <summary>
    /// A test-owned health check whose result a test flips at runtime — the process-internal
    /// equivalent of the demo app's sentinel-file outage check.
    /// </summary>
    internal sealed class ToggleHealthCheck : IHealthCheck
    {
        /// <summary>Gets or sets the status the check reports.</summary>
        public HealthStatus Status { get; set; } = HealthStatus.Healthy;

        /// <inheritdoc/>
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HealthCheckResult(Status));
        }
    }
}
