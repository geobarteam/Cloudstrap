namespace Cloudstrap.Hangfire
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The startup summary lines, under the <c>Cloudstrap.Hangfire</c> category: one posture line, then one line
    /// per recurring job on a processing host. Keys and names only — never a connection string, server or
    /// database name.
    /// </summary>
    internal static partial class HangfireStartupSummaryLogger
    {
        /// <summary>The logger category of the summary lines.</summary>
        public const string Category = "Cloudstrap.Hangfire";

        /// <summary>Writes the posture line.</summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="state">The registration facts.</param>
        /// <param name="taskCount">The number of declared recurring tasks.</param>
        public static void LogPosture(ILogger logger, HangfireRegistrationState state, int taskCount)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(state);

            Posture(
                logger,
                state.StorageProvider,
                state.SchemaName,
                state.PrepareSchema,
                state.RunServer,
                taskCount,
                state.DashboardPath ?? "not mapped");
        }

        /// <summary>Writes one line per recurring job.</summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="jobs">The effective schedule of every declared task.</param>
        public static void LogJobs(ILogger logger, IEnumerable<ScheduledRecurringJob> jobs)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(jobs);

            foreach (ScheduledRecurringJob job in jobs)
            {
                if (job.Enabled)
                {
                    JobScheduled(logger, job.JobId, job.Cron, job.TimeZone.Id, job.Queue);
                }
                else
                {
                    JobDisabled(logger, job.JobId);
                }
            }
        }

        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Cloudstrap Hangfire: storage {StorageProvider}, schema '{Schema}', prepare schema {PrepareSchema}, " +
                      "run server {RunServer}, {TaskCount} recurring task(s), dashboard {DashboardPath}")]
        private static partial void Posture(
            ILogger logger,
            string storageProvider,
            string schema,
            bool prepareSchema,
            bool runServer,
            int taskCount,
            string dashboardPath);

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Information,
            Message = "Recurring job '{JobId}': cron '{Cron}', time zone '{TimeZone}', queue '{Queue}'")]
        private static partial void JobScheduled(ILogger logger, string jobId, string cron, string timeZone, string queue);

        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Information,
            Message = "Recurring job '{JobId}' disabled")]
        private static partial void JobDisabled(ILogger logger, string jobId);
    }
}
