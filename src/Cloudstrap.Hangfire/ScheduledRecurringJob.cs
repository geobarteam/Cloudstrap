namespace Cloudstrap.Hangfire
{
    /// <summary>
    /// The effective schedule of one declared task after configuration overrides, as written at startup and
    /// stated in the startup summary.
    /// </summary>
    /// <param name="JobId">The recurring job id.</param>
    /// <param name="Enabled">Whether the job was scheduled (otherwise it was removed).</param>
    /// <param name="Cron">The effective cron expression.</param>
    /// <param name="TimeZone">The effective time zone.</param>
    /// <param name="Queue">The queue the job is enqueued on.</param>
    internal sealed record ScheduledRecurringJob(string JobId, bool Enabled, string Cron, TimeZoneInfo TimeZone, string Queue);
}
