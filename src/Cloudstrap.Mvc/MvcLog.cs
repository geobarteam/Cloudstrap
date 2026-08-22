namespace Cloudstrap.Mvc
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The log messages this package writes. Source-generated, so logging costs nothing when the level is
    /// disabled and the message template is checked at compile time.
    /// </summary>
    internal static partial class MvcLog
    {
        /// <summary>
        /// Records an exception no part of the application handled, immediately before a JSON-preferring
        /// caller is answered with problem details. This is the single server-side record of the failure on
        /// that path; on the HTML path the framework's exception-handler middleware writes the single
        /// record instead.
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
