namespace Cloudstrap.WebApi
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The log messages this package writes. Source-generated, so logging costs nothing when the level is
    /// disabled and the message template is checked at compile time.
    /// </summary>
    internal static partial class WebApiLog
    {
        /// <summary>
        /// Records an exception no part of the application handled, immediately before the caller is answered
        /// with problem details. This is the single server-side record of the failure.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="method">The request method.</param>
        /// <param name="path">The request path.</param>
        /// <param name="exception">The unhandled exception.</param>
        [LoggerMessage(
            EventId = 1000,
            Level = LogLevel.Error,
            Message = "Unhandled exception while processing {Method} {Path}.")]
        public static partial void UnhandledException(
            ILogger logger,
            string method,
            string? path,
            Exception exception);
    }
}
