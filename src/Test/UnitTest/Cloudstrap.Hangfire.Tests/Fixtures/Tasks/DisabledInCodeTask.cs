namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>A task disabled in code; configuration may enable it.</summary>
    public sealed class DisabledInCodeTask : IBackgroundRecurringTask
    {
        public string CronExpression => "0 * * * *";

        public bool IsEnabled => false;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
