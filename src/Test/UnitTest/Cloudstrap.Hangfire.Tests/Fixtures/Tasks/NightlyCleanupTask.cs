namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>A public, well-behaved task that also implements <see cref="IDisposable"/> (discovery must not expose it as such).</summary>
    public sealed class NightlyCleanupTask : IBackgroundRecurringTask, IDisposable
    {
        public string CronExpression => "0 3 * * *";

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
