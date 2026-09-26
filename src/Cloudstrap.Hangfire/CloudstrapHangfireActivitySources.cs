namespace Cloudstrap.Hangfire
{
    /// <summary>
    /// The <c>ActivitySource</c> names this package emits spans under, published so a pipeline owner outside
    /// dependency injection can add them with <c>AddSource</c>.
    /// </summary>
    public static class CloudstrapHangfireActivitySources
    {
        /// <summary>
        /// The source of the <c>RecurringTask {jobId}</c> span wrapping each recurring-task run.
        /// </summary>
        public const string RecurringTask = "Cloudstrap.Hangfire";
    }
}
