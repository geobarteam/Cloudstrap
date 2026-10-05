namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>A task that records the cancellation token of its last run.</summary>
    public sealed class TokenCapturingTask : IBackgroundRecurringTask
    {
        /// <summary>Gets or sets the token passed to the last run.</summary>
        public static CancellationToken LastToken
        {
            get; set;
        }

        /// <summary>Gets the payload-like value the task holds: it must never reach a log line.</summary>
        public static string Sentinel => "sentinel-payload-7f3a";

        public string CronExpression => "0 0 1 1 *";

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
