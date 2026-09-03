namespace Cloudstrap.Observability.Correlation
{
    /// <summary>
    /// Exempts an endpoint (or every endpoint on a controller) from the correlation requirement, even when
    /// <c>Cloudstrap:Correlation:Request:RequireForAllEndpoints</c> is on. A missing identifier is then
    /// generated instead of rejected.
    /// </summary>
    /// <remarks>
    /// The same attribute exempts a message handler (a handler method, its class, or a base class) from the
    /// correlation requirement, even when <c>Cloudstrap:Correlation:Message:RequireForAllMessageHandlers</c>
    /// is on. The handler then runs with a fresh (empty) correlation scope when no header arrived.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class AllowNoCorrelationAttribute : Attribute
    {
    }
}
