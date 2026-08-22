namespace Cloudstrap.Demo.Worker
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// A trivial periodic background service — the "plain <see cref="BackgroundService"/>" the demo
    /// runs while its probes answer; its heartbeat is the stdout signal the E2E suite asserts.
    /// </summary>
    internal sealed class PeriodicWorker : BackgroundService
    {
        private readonly ILogger<PeriodicWorker> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PeriodicWorker"/> class.
        /// </summary>
        /// <param name="logger">The logger the heartbeat is written to.</param>
        public PeriodicWorker(ILogger<PeriodicWorker> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int beat = 0;
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            _logger.LogInformation("Demo worker heartbeat {Beat}", beat);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    beat++;
                    _logger.LogInformation("Demo worker heartbeat {Beat}", beat);
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown — nothing to flush beyond the host's own lifecycle logs.
            }
        }
    }
}
