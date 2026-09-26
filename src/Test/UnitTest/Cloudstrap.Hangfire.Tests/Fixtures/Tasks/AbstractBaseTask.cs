namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>An abstract implementation: never registered by discovery.</summary>
    public abstract class AbstractBaseTask : IBackgroundRecurringTask
    {
        public string CronExpression => "0 0 * * *";

        public abstract Task ExecuteAsync(CancellationToken cancellationToken);
    }
}
