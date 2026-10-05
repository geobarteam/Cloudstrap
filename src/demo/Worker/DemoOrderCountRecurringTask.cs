namespace Cloudstrap.Demo.Worker
{
    using Cloudstrap.Demo.Worker.Data;
    using Cloudstrap.Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The demo's recurring task (deliverable #16): every minute it logs how many orders <c>demo.Orders</c> holds.
    /// </summary>
    /// <remarks>
    /// Nothing registers this class by hand: <c>AddCloudstrapHangfire</c> discovers it in the entry assembly —
    /// internal implementations included — and schedules it at startup against the shared dispatcher, so the
    /// BlazorServer demo can show and trigger it without referencing this assembly. It is resolved per run in its
    /// own scope, so the <see cref="WorkerDbContext"/> lives for one run. It logs a count, never row contents.
    /// </remarks>
    internal sealed partial class DemoOrderCountRecurringTask(WorkerDbContext db, ILogger<DemoOrderCountRecurringTask> logger)
        : IBackgroundRecurringTask
    {
        /// <inheritdoc />
        public string CronExpression => "* * * * *";

        /// <inheritdoc />
        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            int orderCount = await db.Orders.CountAsync(cancellationToken);
            LogOrderCount(orderCount);
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Demo order count: {OrderCount} row(s) in demo.Orders")]
        private partial void LogOrderCount(int orderCount);
    }
}
