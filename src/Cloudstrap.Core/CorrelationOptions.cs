namespace Cloudstrap.Core
{
    /// <summary>
    /// Correlation settings, bound from the <c>Cloudstrap:Correlation</c> configuration section.
    /// </summary>
    public sealed class CorrelationOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Correlation";

        /// <summary>
        /// Gets or sets the HTTP header carrying the correlation identifier. W3C <c>traceparent</c> remains the
        /// tracing backbone; this header carries the business correlation identifier alongside it.
        /// </summary>
        /// <value>The correlation header name. Defaults to <c>X-Correlation-ID</c>.</value>
        public string HeaderName { get; set; } = "X-Correlation-ID";

        /// <summary>
        /// Gets or sets the correlation settings that apply to inbound HTTP requests.
        /// </summary>
        /// <value>The request correlation settings. Never <see langword="null"/>.</value>
        public CorrelationRequestOptions Request { get; set; } = new();

        /// <summary>
        /// Gets or sets the correlation settings that apply to message handlers.
        /// </summary>
        /// <value>The message correlation settings. Never <see langword="null"/>.</value>
        public CorrelationMessageOptions Message { get; set; } = new();
    }
}
