namespace Cloudstrap.Observability
{
    /// <summary>
    /// Records business-level spans — a domain operation, not an HTTP or database call — through the
    /// telemetry pipeline. Keep both arguments low-cardinality: operation and component name a <em>kind</em>
    /// of work (<c>SubmitOrder</c>, <c>OrderService</c>), never a user, document or entity identifier.
    /// </summary>
    public interface IBusinessTrace
    {
        /// <summary>
        /// Starts a business span. When no telemetry listener is active the returned scope is a safe no-op
        /// with <see cref="IBusinessTraceScope.IsRecording"/> <see langword="false"/> — consumers never need
        /// to guard the call.
        /// </summary>
        /// <param name="operation">The low-cardinality operation name; becomes the span name.</param>
        /// <param name="component">The low-cardinality component name; becomes the component tag.</param>
        /// <returns>The scope that ends the span when disposed.</returns>
        /// <exception cref="ArgumentException">Either argument is <see langword="null"/> or whitespace.</exception>
        IBusinessTraceScope StartSpan(string operation, string component);
    }
}
