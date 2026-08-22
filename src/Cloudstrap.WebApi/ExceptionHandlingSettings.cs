namespace Cloudstrap.WebApi
{
    /// <summary>
    /// Error-response settings, bound from the <c>Cloudstrap:WebApi:ExceptionHandling</c> configuration
    /// section.
    /// </summary>
    public sealed class ExceptionHandlingSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether the error response carries the exception's type, message,
        /// stack trace and inner-exception chain.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to include the detail, <see langword="false"/> to withhold it, or
        /// <see langword="null"/> — the default — to follow the environment: detail in <c>Development</c>
        /// only. An explicit value wins in both directions.
        /// </value>
        /// <remarks>
        /// <para>
        /// The response is <c>application/problem+json</c> either way, and the exception is logged
        /// server-side either way. What this setting governs is only what the <em>caller</em> is told.
        /// </para>
        /// <para>
        /// <strong>Never enable this on a public production API.</strong> Stack traces and exception messages
        /// describe internal structure, file paths and sometimes data; they belong in the log, which the
        /// operator can read and the caller cannot.
        /// </para>
        /// <para>
        /// Setting it explicitly is nevertheless the right move in two cases: <see langword="false"/> in a
        /// <c>Development</c> run whose tests assert the hardened shape, and <see langword="true"/> in a
        /// short-lived private diagnostic instance.
        /// </para>
        /// </remarks>
        public bool? IncludeDetails
        {
            get; set;
        }
    }
}
