namespace Cloudstrap.Hangfire
{
    using global::Hangfire;
    using global::Hangfire.Storage.Monitoring;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Reports whether Hangfire storage answers, with its server, recurring, enqueued and failed counts as data.
    /// On failure the result carries only the exception type: provider messages can name servers or databases,
    /// so the exception itself is logged at Warning for operators and never attached to the probe result.
    /// </summary>
    internal sealed partial class HangfireStorageHealthCheck : IHealthCheck
    {
        private readonly JobStorage _storage;
        private readonly ILogger<HangfireStorageHealthCheck> _logger;

        public HangfireStorageHealthCheck(JobStorage storage, ILogger<HangfireStorageHealthCheck> logger)
        {
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(logger);

            _storage = storage;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            try
            {
                StatisticsDto statistics = _storage.GetMonitoringApi().GetStatistics();
                Dictionary<string, object> data = new(StringComparer.Ordinal)
                {
                    ["servers"] = statistics.Servers,
                    ["recurring"] = statistics.Recurring,
                    ["enqueued"] = statistics.Enqueued,
                    ["failed"] = statistics.Failed,
                };
                return Task.FromResult(HealthCheckResult.Healthy("Hangfire storage answers.", data));
            }
#pragma warning disable CA1031 // A health check reports every storage failure as a status, never throws.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogUnreachable(exception, context.Registration.Name);
                return Task.FromResult(new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"Hangfire storage is unreachable ({exception.GetType().Name})."));
            }
        }

        [LoggerMessage(
            EventId = 30,
            Level = LogLevel.Warning,
            Message = "Health check '{HealthCheckName}': Hangfire storage is unreachable")]
        private partial void LogUnreachable(Exception exception, string healthCheckName);
    }
}
