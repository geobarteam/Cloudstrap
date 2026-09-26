namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>A task that signals it started, then blocks until released — the overlap-lock fixture.</summary>
    public sealed class GatedTask : IBackgroundRecurringTask
    {
        /// <summary>Gets the gate the running task waits on; release it to let the run finish.</summary>
        public static SemaphoreSlim Gate { get; } = new(0);

        /// <summary>Gets the signal set when a run has started.</summary>
        public static SemaphoreSlim Started { get; } = new(0);

        public string CronExpression => "0 0 1 1 *";

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            Started.Release();
            await Gate.WaitAsync(cancellationToken);
        }
    }
}
