namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>A task whose run fails — the failure-path fixture.</summary>
    public sealed class ThrowingTask : IBackgroundRecurringTask
    {
        public string CronExpression => "0 0 1 1 *";

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The fixture task failed on purpose.");
        }
    }
}
