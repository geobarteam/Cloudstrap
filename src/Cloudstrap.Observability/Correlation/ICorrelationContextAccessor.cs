namespace Cloudstrap.Observability.Correlation
{
    /// <summary>
    /// Ambient access to the current correlation identifier. The value is the identifier itself — there is
    /// no context object graph to manage — and it is readable anywhere in the process, with or without an
    /// <c>HttpContext</c>: message handlers, background jobs and plain services all see the same ambient
    /// value that the middleware or handler established for the logical flow.
    /// </summary>
    public interface ICorrelationContextAccessor
    {
        /// <summary>
        /// Gets or sets the correlation identifier of the current logical flow. Setting is last-write-wins
        /// and never throws; the value flows across <see langword="await"/> points and stays isolated
        /// between parallel flows.
        /// </summary>
        /// <value>The correlation identifier, or <see langword="null"/> when none has been established.</value>
        string? CorrelationId
        {
            get; set;
        }
    }
}
