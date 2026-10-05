namespace Cloudstrap.Hangfire
{
    using System.ComponentModel;
    using System.Diagnostics;
    using global::Hangfire;
    using global::Hangfire.Storage;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The single dispatcher every recurring job is stored against: Hangfire persists this type and the job id,
    /// never the task implementation, so a storage-only host can show the jobs without loading the processing
    /// host's assemblies.
    /// </summary>
    /// <remarks>
    /// <b>Wire contract.</b> Hangfire persists this type, its namespace and the <see cref="RunAsync"/> method by
    /// name for every recurring job: never rename or move any of them, or every stored job is orphaned. The type
    /// is public only because Hangfire must activate it; it is not meant to be called directly.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class RecurringTaskRunner
    {
        private static readonly ActivitySource _activitySource = new(CloudstrapHangfireActivitySources.RecurringTask);

        private readonly IEnumerable<IBackgroundRecurringTask> _tasks;
        private readonly JobStorage _storage;
        private readonly ILogger<RecurringTaskRunner> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecurringTaskRunner"/> class.
        /// </summary>
        /// <param name="tasks">The recurring tasks registered on this host.</param>
        /// <param name="storage">The job storage, which provides the overlap-prevention lock.</param>
        /// <param name="logger">The logger for the per-run lines.</param>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public RecurringTaskRunner(IEnumerable<IBackgroundRecurringTask> tasks, JobStorage storage, ILogger<RecurringTaskRunner> logger)
        {
            ArgumentNullException.ThrowIfNull(tasks);
            ArgumentNullException.ThrowIfNull(storage);
            ArgumentNullException.ThrowIfNull(logger);

            _tasks = tasks;
            _storage = storage;
            _logger = logger;
        }

        /// <summary>
        /// Resolves the registered task whose <see cref="IBackgroundRecurringTask.JobId"/> is
        /// <paramref name="jobId"/> and runs it inside a <c>RecurringTask {jobId}</c> span. When the task prevents
        /// overlapping runs, the run holds the <c>recurring-task:{jobId}</c> distributed lock; a trigger that finds
        /// the lock taken is skipped with a warning.
        /// </summary>
        /// <param name="jobId">The recurring job id the job was scheduled under.</param>
        /// <param name="cancellationToken">The job cancellation token Hangfire supplies at execution time.</param>
        /// <returns>A task that completes when the run has finished or was skipped.</returns>
        /// <exception cref="ArgumentException"><paramref name="jobId"/> is <see langword="null"/> or whitespace.</exception>
        /// <exception cref="InvalidOperationException">
        /// No task with that id is registered on this host (typically a storage-only host that runs a server).
        /// </exception>
        [JobDisplayName("{0}")]
        public async Task RunAsync(string jobId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

            IBackgroundRecurringTask task = _tasks.FirstOrDefault(candidate => string.Equals(candidate.JobId, jobId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No registered recurring task with id '{jobId}' was found on this host.");

            if (!task.PreventOverlappingRuns)
            {
                await ExecuteAsync(task, jobId, cancellationToken).ConfigureAwait(false);
                return;
            }

            // The lock lives on this connection, so the connection must outlive the run.
            using IStorageConnection connection = _storage.GetConnection();
            IDisposable distributedLock;
            try
            {
                // Zero wait: if a previous run still holds the lock, skip this trigger instead of queuing behind it.
                distributedLock = connection.AcquireDistributedLock($"recurring-task:{jobId}", TimeSpan.Zero);
            }
            catch (DistributedLockTimeoutException)
            {
                RecurringTaskLog.Skipped(_logger, jobId);
                return;
            }

            using (distributedLock)
            {
                await ExecuteAsync(task, jobId, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ExecuteAsync(IBackgroundRecurringTask task, string jobId, CancellationToken cancellationToken)
        {
            using Activity? activity = _activitySource.StartActivity($"RecurringTask {jobId}");
            activity?.SetTag("cloudstrap.hangfire.job_id", jobId);

            long started = Stopwatch.GetTimestamp();
            RecurringTaskLog.Started(_logger, jobId);

            try
            {
                await task.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                double failedAfter = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
                RecurringTaskLog.Failed(_logger, exception, jobId, failedAfter);
                throw;
            }

            double completedIn = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            activity?.SetStatus(ActivityStatusCode.Ok);
            RecurringTaskLog.Completed(_logger, jobId, completedIn);
        }
    }
}
