namespace Cloudstrap.Observability.Correlation
{
    /// <summary>
    /// Exempts an endpoint (or every endpoint on a controller) from the correlation requirement, even when
    /// <c>Cloudstrap:Correlation:Request:RequireForAllEndpoints</c> is on. A missing identifier is then
    /// generated instead of rejected.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class AllowNoCorrelationAttribute : Attribute
    {
    }
}
