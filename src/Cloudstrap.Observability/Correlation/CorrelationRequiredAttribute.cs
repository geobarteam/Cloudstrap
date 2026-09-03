namespace Cloudstrap.Observability.Correlation
{
    /// <summary>
    /// Marks an endpoint (or every endpoint on a controller) as requiring an inbound correlation identifier,
    /// even when <c>Cloudstrap:Correlation:Request:RequireForAllEndpoints</c> is off. A request without the
    /// configured header is rejected with <c>400 application/problem+json</c>.
    /// </summary>
    /// <remarks>
    /// The same attribute marks a message handler (a handler method, its class, or a base class) as requiring
    /// a correlation identifier on the incoming message, even when
    /// <c>Cloudstrap:Correlation:Message:RequireForAllMessageHandlers</c> is off. A message without the
    /// configured header is then blocked and dead-lettered by <c>Cloudstrap.Messaging</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class CorrelationRequiredAttribute : Attribute
    {
    }
}
