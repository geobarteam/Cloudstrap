namespace Cloudstrap.Hangfire
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The per-run log lines of <see cref="RecurringTaskRunner"/>: the job id and the elapsed time only, never a
    /// task argument or payload. A failure carries its exception as the entry exception, not in the message.
    /// </summary>
    internal static partial class RecurringTaskLog
    {
        [LoggerMessage(EventId = 20, Level = LogLevel.Information, Message = "Recurring task {JobId} started")]
        public static partial void Started(ILogger logger, string jobId);

        [LoggerMessage(EventId = 21, Level = LogLevel.Information, Message = "Recurring task {JobId} completed in {ElapsedMilliseconds} ms")]
        public static partial void Completed(ILogger logger, string jobId, double elapsedMilliseconds);

        [LoggerMessage(EventId = 22, Level = LogLevel.Error, Message = "Recurring task {JobId} failed after {ElapsedMilliseconds} ms")]
        public static partial void Failed(ILogger logger, Exception exception, string jobId, double elapsedMilliseconds);

        [LoggerMessage(EventId = 23, Level = LogLevel.Warning, Message = "Recurring task {JobId} skipped: the previous run still holds the lock")]
        public static partial void Skipped(ILogger logger, string jobId);
    }
}
