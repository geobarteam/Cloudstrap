namespace Cloudstrap.Hangfire
{
    /// <summary>
    /// Per-job overrides, bound from <c>Cloudstrap:Hangfire:Jobs:{JobId}</c>. A set value wins over the task's own
    /// declaration; an unset or empty value falls back to it.
    /// </summary>
    public sealed class HangfireJobOptions
    {
        /// <summary>
        /// Gets or sets the cron expression that replaces <see cref="IBackgroundRecurringTask.CronExpression"/>.
        /// </summary>
        /// <value>A Hangfire (Cronos) cron expression, or <see langword="null"/> to keep the task's own.</value>
        public string? Cron
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the time zone that replaces <see cref="IBackgroundRecurringTask.TimeZone"/>.
        /// </summary>
        /// <value>An IANA time zone id such as <c>Europe/Brussels</c>, or <see langword="null"/> to keep the task's own.</value>
        public string? TimeZone
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value that replaces <see cref="IBackgroundRecurringTask.IsEnabled"/>.
        /// </summary>
        /// <value>
        /// <see langword="false"/> to remove the job, <see langword="true"/> to schedule it, or
        /// <see langword="null"/> to keep the task's own value.
        /// </value>
        public bool? Enabled
        {
            get; set;
        }
    }
}
