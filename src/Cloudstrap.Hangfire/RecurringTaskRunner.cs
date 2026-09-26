namespace Cloudstrap.Hangfire
{
    using System.ComponentModel;

    /// <summary>
    /// The single dispatcher every recurring job is stored against: Hangfire persists this type and the job id,
    /// never the task implementation, so a storage-only host can show the jobs without loading the processing
    /// host's assemblies.
    /// </summary>
    /// <remarks>
    /// This type, its namespace and the <see cref="RunAsync"/> signature are a persisted wire contract: renaming
    /// any of them orphans every stored recurring job. It is public only because Hangfire must activate it.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class RecurringTaskRunner
    {
        private readonly IEnumerable<IBackgroundRecurringTask> _tasks;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecurringTaskRunner"/> class.
        /// </summary>
        /// <param name="tasks">The recurring tasks registered on this host.</param>
        /// <exception cref="ArgumentNullException"><paramref name="tasks"/> is <see langword="null"/>.</exception>
        public RecurringTaskRunner(IEnumerable<IBackgroundRecurringTask> tasks)
        {
            ArgumentNullException.ThrowIfNull(tasks);

            _tasks = tasks;
        }

        /// <summary>
        /// Resolves the registered task whose <see cref="IBackgroundRecurringTask.JobId"/> is
        /// <paramref name="jobId"/> and runs it.
        /// </summary>
        /// <param name="jobId">The recurring job id the job was scheduled under.</param>
        /// <param name="cancellationToken">The job cancellation token Hangfire supplies at execution time.</param>
        /// <returns>A task that completes when the run has finished.</returns>
        /// <exception cref="ArgumentException"><paramref name="jobId"/> is <see langword="null"/> or whitespace.</exception>
        /// <exception cref="InvalidOperationException">
        /// No task with that id is registered on this host (typically a storage-only host that runs a server).
        /// </exception>
        public async Task RunAsync(string jobId, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

            IBackgroundRecurringTask task = _tasks.FirstOrDefault(candidate => string.Equals(candidate.JobId, jobId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No recurring task with job id '{jobId}' is registered on this host.");

            await task.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
