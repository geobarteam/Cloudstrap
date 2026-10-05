namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>A task that records each run in <see cref="TaskRunLog"/>.</summary>
    public sealed class RecordingTask : IBackgroundRecurringTask
    {
        public string CronExpression => "0 0 1 1 *";

        public string JobId => "recording-task";

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            TaskRunLog.Record(JobId);
            return Task.CompletedTask;
        }
    }
}
