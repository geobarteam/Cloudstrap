namespace Cloudstrap.Observability.Correlation
{
    /// <summary>
    /// Marks an endpoint (or every endpoint on a controller) as requiring an inbound correlation identifier,
    /// even when <c>Cloudstrap:Correlation:Request:RequireForAllEndpoints</c> is off. A request without the
    /// configured header is rejected with <c>400 application/problem+json</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class CorrelationRequiredAttribute : Attribute
    {
    }
}
