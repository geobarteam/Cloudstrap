namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>A task that opts out of overlap prevention: it runs without taking the distributed lock.</summary>
    public sealed class NoOverlapGuardTask : IBackgroundRecurringTask
    {
        public string CronExpression => "*/5 * * * *";

        public bool PreventOverlappingRuns => false;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            InvocationRecorder.Record(nameof(NoOverlapGuardTask));
            return Task.CompletedTask;
        }
    }
}
