namespace Cloudstrap.Observability.Correlation
{
    using System.Diagnostics;

    /// <summary>
    /// The default <see cref="ICorrelationSource"/>: the current activity's W3C trace identifier — so logs,
    /// traces and the correlation header agree by construction — or a new GUID when no activity is current.
    /// </summary>
    internal sealed class TraceIdCorrelationSource : ICorrelationSource
    {
        public string GenerateCorrelation() =>
            Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString();
    }
}
