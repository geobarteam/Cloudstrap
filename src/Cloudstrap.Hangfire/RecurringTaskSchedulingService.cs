namespace Cloudstrap.Hangfire
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Schedules the declared recurring tasks when a processing host starts, then writes the startup summary.
    /// It is registered before the Hangfire server, so scheduling completes before any job is dequeued; a
    /// failure propagates and faults startup, so the server never starts on a misconfigured task set.
    /// </summary>
    internal sealed class RecurringTaskSchedulingService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly HangfireRegistrationState _state;
        private readonly ILogger _logger;

        public RecurringTaskSchedulingService(
            IServiceScopeFactory scopeFactory,
            HangfireRegistrationState state,
            ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _scopeFactory = scopeFactory;
            _state = state;
            _logger = loggerFactory.CreateLogger(HangfireStartupSummaryLogger.Category);
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<ScheduledRecurringJob> jobs;
            using (IServiceScope scope = _scopeFactory.CreateScope())
            {
                jobs = scope.ServiceProvider.GetRequiredService<RecurringJobsScheduler>().ScheduleAll();
            }

            HangfireStartupSummaryLogger.LogPosture(_logger, _state, jobs.Count);
            HangfireStartupSummaryLogger.LogJobs(_logger, jobs);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
