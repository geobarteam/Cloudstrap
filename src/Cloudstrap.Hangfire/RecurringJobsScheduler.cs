namespace Cloudstrap.Hangfire
{
    using Cloudstrap.Core;
    using global::Hangfire;
    using global::Hangfire.Common;
    using global::Hangfire.Storage;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Schedules the declared recurring tasks against the <see cref="RecurringTaskRunner"/> dispatcher on a
    /// processing host's start, in three phases: validate every task, write, then reconcile jobs whose task is
    /// no longer declared. A validation failure writes nothing.
    /// </summary>
    internal sealed partial class RecurringJobsScheduler
    {
        private readonly IRecurringJobManager _recurringJobManager;
        private readonly IReadOnlyList<IBackgroundRecurringTask> _tasks;
        private readonly JobStorage _storage;
        private readonly HangfireOptions _options;
        private readonly ILogger<RecurringJobsScheduler> _logger;

        public RecurringJobsScheduler(
            IRecurringJobManager recurringJobManager,
            IEnumerable<IBackgroundRecurringTask> tasks,
            JobStorage storage,
            IOptions<HangfireOptions> options,
            ILogger<RecurringJobsScheduler> logger)
        {
            ArgumentNullException.ThrowIfNull(recurringJobManager);
            ArgumentNullException.ThrowIfNull(tasks);
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(logger);

            _recurringJobManager = recurringJobManager;
            _tasks = [.. tasks];
            _storage = storage;
            _options = options.Value;
            _logger = logger;
        }

        /// <summary>Validates, schedules and reconciles every declared task.</summary>
        /// <returns>The effective schedule of every declared task, for the startup summary.</returns>
        /// <exception cref="ConfigurationValidationException">
        /// Two tasks share a job id, a configured time zone is unknown, or a cron expression is rejected.
        /// </exception>
        public IReadOnlyList<ScheduledRecurringJob> ScheduleAll()
        {
            // Phase 1: validate everything before the first write, so a misconfiguration never half-applies.
            RejectDuplicateJobIds();
            List<ScheduledRecurringJob> plan = [.. _tasks.Select(Resolve)];
            WarnAboutUndeclaredOverrides();

            // Phase 2: write.
            foreach (ScheduledRecurringJob job in plan)
            {
                Write(job);
            }

            // Phase 3: reconcile.
            RemoveOrphans(plan);
            return plan;
        }

        private static string JobsKey(string jobId, string setting)
        {
            return $"{HangfireOptions.SectionName}:Jobs:{jobId}:{setting}";
        }

        private static TimeZoneInfo FindTimeZone(string jobId, string timeZoneId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new ConfigurationValidationException(
                    $"Recurring task '{jobId}' has an unknown time zone '{timeZoneId}' at " +
                    $"'{JobsKey(jobId, nameof(HangfireJobOptions.TimeZone))}': use an IANA id such as 'Europe/Brussels'.",
                    exception);
            }
        }

        private void RejectDuplicateJobIds()
        {
            IGrouping<string, IBackgroundRecurringTask>? duplicate = _tasks
                .GroupBy(task => task.JobId, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicate is not null)
            {
                string types = string.Join(", ", duplicate.Select(task => task.GetType().FullName));
                throw new ConfigurationValidationException(
                    $"Recurring tasks {types} share the job id '{duplicate.Key}'. Job ids must be unique: override " +
                    $"{nameof(IBackgroundRecurringTask.JobId)} on one of them.");
            }
        }

        private ScheduledRecurringJob Resolve(IBackgroundRecurringTask task)
        {
            string jobId = task.JobId;
            HangfireJobOptions? overrides = FindOverrides(jobId);

            bool enabled = overrides?.Enabled ?? task.IsEnabled;
            string cron = string.IsNullOrWhiteSpace(overrides?.Cron) ? task.CronExpression : overrides.Cron;
            TimeZoneInfo timeZone = string.IsNullOrWhiteSpace(overrides?.TimeZone)
                ? task.TimeZone
                : FindTimeZone(jobId, overrides.TimeZone);

            return new ScheduledRecurringJob(jobId, enabled, cron, timeZone, task.Queue);
        }

        private HangfireJobOptions? FindOverrides(string jobId)
        {
            // Looked up case-insensitively here: the binder's dictionary comparer is not relied on.
            return _options.Jobs
                .FirstOrDefault(entry => string.Equals(entry.Key, jobId, StringComparison.OrdinalIgnoreCase))
                .Value;
        }

        private void WarnAboutUndeclaredOverrides()
        {
            foreach (string configuredId in _options.Jobs.Keys)
            {
                if (!_tasks.Any(task => string.Equals(task.JobId, configuredId, StringComparison.OrdinalIgnoreCase)))
                {
                    LogUndeclaredOverride(configuredId);
                }
            }
        }

        private void Write(ScheduledRecurringJob job)
        {
            if (!job.Enabled)
            {
                _recurringJobManager.RemoveIfExists(job.JobId);
                return;
            }

            // Stored against the shared dispatcher and the job id, never the implementation type: a storage-only
            // host renders the job without loading the processing host's assemblies. Hangfire substitutes the
            // real job cancellation token for the placeholder at execution time.
            string jobId = job.JobId;
            Job dispatch = Job.FromExpression<RecurringTaskRunner>(runner => runner.RunAsync(jobId, CancellationToken.None));
            Job queued = new(dispatch.Type, dispatch.Method, dispatch.Args, job.Queue);

            try
            {
                _recurringJobManager.AddOrUpdate(jobId, queued, job.Cron, new RecurringJobOptions { TimeZone = job.TimeZone });
            }
            catch (ArgumentException exception)
            {
                throw new ConfigurationValidationException(
                    $"Recurring task '{jobId}' could not be scheduled: its cron expression '{job.Cron}' was rejected. " +
                    $"Fix the task's {nameof(IBackgroundRecurringTask.CronExpression)} or " +
                    $"'{JobsKey(jobId, nameof(HangfireJobOptions.Cron))}'.",
                    exception);
            }
        }

        private void RemoveOrphans(List<ScheduledRecurringJob> plan)
        {
            // The zero-task guard: a processing host that declares nothing never removes another host's jobs.
            if (plan.Count == 0)
            {
                LogReconciliationSkipped();
                return;
            }

            HashSet<string> declared = new(plan.Select(job => job.JobId), StringComparer.OrdinalIgnoreCase);

            using IStorageConnection connection = _storage.GetConnection();
            foreach (RecurringJobDto stored in connection.GetRecurringJobs())
            {
                // Only dispatcher-owned jobs are reconciled. A job of another type, or one whose type cannot be
                // loaded (Job is null), belongs to some other component and is never touched.
                if (stored.Job?.Type != typeof(RecurringTaskRunner) || declared.Contains(stored.Id))
                {
                    continue;
                }

                _recurringJobManager.RemoveIfExists(stored.Id);
                LogOrphanRemoved(stored.Id);
            }
        }

        [LoggerMessage(
            EventId = 10,
            Level = LogLevel.Warning,
            Message = "'Cloudstrap:Hangfire:Jobs:{JobId}' overrides a recurring job this host does not declare; the override is ignored")]
        private partial void LogUndeclaredOverride(string jobId);

        [LoggerMessage(
            EventId = 11,
            Level = LogLevel.Information,
            Message = "Removed recurring job '{JobId}': its task is no longer declared on this processing host")]
        private partial void LogOrphanRemoved(string jobId);

        [LoggerMessage(
            EventId = 12,
            Level = LogLevel.Information,
            Message = "Recurring job reconciliation skipped: this processing host declares no recurring tasks")]
        private partial void LogReconciliationSkipped();
    }
}
