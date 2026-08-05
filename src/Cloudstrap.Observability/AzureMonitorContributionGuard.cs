namespace Cloudstrap.Observability
{
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// Fails host startup when <c>Cloudstrap:OpenTelemetry:Mode</c> is <c>AzureMonitor</c> but no exporter
    /// package contributed an exporter: telemetry is never silently dropped. Registered in owner mode only —
    /// in contribute mode the host owns exporter selection.
    /// </summary>
    internal sealed class AzureMonitorContributionGuard : IHostedService
    {
        private readonly ExporterContributionMarker _marker;

        public AzureMonitorContributionGuard(ExporterContributionMarker marker)
        {
            _marker = marker;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_marker.Contributed)
            {
                throw new InvalidOperationException(
                    "Cloudstrap:OpenTelemetry:Mode is AzureMonitor, but no Azure Monitor exporter was " +
                    "contributed to the pipeline. Reference the Cloudstrap.Observability.AzureMonitor package " +
                    "and call its registration, or change the mode — telemetry is never silently dropped.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
