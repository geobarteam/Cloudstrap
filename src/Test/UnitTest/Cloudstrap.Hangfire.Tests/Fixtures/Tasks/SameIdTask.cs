namespace Cloudstrap.Hangfire.Tests.Fixtures.Tasks
{
    /// <summary>
    /// An open-generic task whose closed forms share one job id — the duplicate-id fixture. Discovery never
    /// registers open generic definitions, so the misbehaving pair only exists where a test registers it.
    /// </summary>
    /// <typeparam name="TMarker">A marker type that makes each closed form a distinct implementation.</typeparam>
    public sealed class SameIdTask<TMarker> : IBackgroundRecurringTask
    {
        public string CronExpression => "0 0 * * *";

        public string JobId => "duplicate-id";

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>The first <see cref="SameIdTask{TMarker}"/> marker.</summary>
    public sealed class First
    {
    }

    /// <summary>The second <see cref="SameIdTask{TMarker}"/> marker.</summary>
    public sealed class Second
    {
    }
}
