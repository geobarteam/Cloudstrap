namespace Cloudstrap.Hangfire
{
    /// <summary>
    /// A unit of recurring background work. Implement it in the processing host and it is discovered, scheduled
    /// at startup and run on its cron schedule; the implementation needs no Hangfire type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every member except <see cref="CronExpression"/> and <see cref="ExecuteAsync"/> has a default.
    /// Configuration under <c>Cloudstrap:Hangfire:Jobs:{JobId}</c> can override the cron expression, the time
    /// zone and the enabled state without a redeploy.
    /// </para>
    /// <para>
    /// The task is resolved from dependency injection for each run, in its own scope, so it may depend on scoped
    /// services such as a <c>DbContext</c>. Honor the cancellation token: it is signalled on host shutdown.
    /// </para>
    /// </remarks>
    public interface IBackgroundRecurringTask
    {
        /// <summary>
        /// Gets the schedule, as a Hangfire cron expression (Cronos syntax: five fields, or six with seconds).
        /// </summary>
        /// <value>For example <c>0 3 * * *</c> for every day at 03:00.</value>
        string CronExpression
        {
            get;
        }

        /// <summary>
        /// Gets the stable identifier of this task's recurring job: shown in the dashboard and used as the
        /// <c>Cloudstrap:Hangfire:Jobs:{JobId}</c> configuration key.
        /// </summary>
        /// <value>Defaults to the implementation type name.</value>
        /// <remarks>
        /// The default is rename-sensitive: renaming the class creates a new job, and the old one is reconciled
        /// away with its history. Override it for any task operators reference in configuration.
        /// </remarks>
        string JobId => GetType().Name;

        /// <summary>
        /// Gets the time zone <see cref="CronExpression"/> is evaluated in.
        /// </summary>
        /// <value>Defaults to <see cref="TimeZoneInfo.Utc"/>. Prefer IANA ids such as <c>Europe/Brussels</c> for portability.</value>
        TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        /// <summary>
        /// Gets the queue the job is enqueued on.
        /// </summary>
        /// <value>Defaults to <c>default</c>. A processing server only runs jobs from the queues it listens to.</value>
        string Queue => "default";

        /// <summary>
        /// Gets a value indicating whether the task is scheduled. A disabled task's recurring job is removed.
        /// </summary>
        /// <value>Defaults to <see langword="true"/>.</value>
        bool IsEnabled => true;

        /// <summary>
        /// Gets a value indicating whether a trigger that fires while a previous run is still in progress is
        /// skipped. A distributed lock guards each run.
        /// </summary>
        /// <value>Defaults to <see langword="true"/>.</value>
        bool PreventOverlappingRuns => true;

        /// <summary>
        /// Runs the task once.
        /// </summary>
        /// <param name="cancellationToken">A token signalled when the host shuts down; honor it.</param>
        /// <returns>A task that completes when the run has finished.</returns>
        Task ExecuteAsync(CancellationToken cancellationToken);
    }
}
