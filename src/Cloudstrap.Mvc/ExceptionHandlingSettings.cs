namespace Cloudstrap.Mvc
{
    /// <summary>
    /// Error-handling settings, bound from <c>Cloudstrap:Mvc:ExceptionHandling</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The error contract is content-negotiated: a request is HTML-preferring exactly when its
    /// <c>Accept</c> header contains the <c>text/html</c> or <c>application/xhtml+xml</c> media type —
    /// browsers always send <c>text/html</c> on navigation. Everything else — <c>application/json</c>,
    /// <c>*/*</c> alone, an absent or unparsable <c>Accept</c> header — is JSON-preferring and receives
    /// RFC 9457 <c>application/problem+json</c>. HTML-preferring callers get the consumer's own error
    /// page, re-executed at <c>Cloudstrap:Application:ExceptionHandlerPath</c> (default <c>/error</c>);
    /// this package ships no default page — supply a minimal action there, for example
    /// <c>[Route("/error")] public IActionResult Get() =&gt; View();</c>.
    /// </para>
    /// </remarks>
    public sealed class ExceptionHandlingSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether the problem-details payload carries the exception type,
        /// message, stack trace and a depth-bounded inner-exception chain.
        /// </summary>
        /// <value>
        /// Unset (<see langword="null"/>) means <c>Development</c> only. Applies to the <strong>JSON path
        /// only</strong>: the re-executed HTML error page never carries exception content, whatever this
        /// switch says. Never enable details on a public production application.
        /// </value>
        public bool? IncludeDetails
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the framework's developer exception page handles
        /// unhandled exceptions instead of the Cloudstrap error contract.
        /// </summary>
        /// <value>
        /// Unset (<see langword="null"/>) means <c>Development</c> only. Set it to
        /// <see langword="false"/> to keep the hardened error page and problem details even in
        /// <c>Development</c> — the posture the SUT demo host pins.
        /// </value>
        public bool? UseDeveloperExceptionPage
        {
            get; set;
        }
    }
}
