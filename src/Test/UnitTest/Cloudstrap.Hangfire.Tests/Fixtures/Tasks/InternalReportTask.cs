namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>An internal task: discovery includes non-public implementations (the suite is internal by default).</summary>
    internal sealed class InternalReportTask : IBackgroundRecurringTask
    {
        public string CronExpression => "30 6 * * 1";

        public string Queue => "reports";

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
