namespace Cloudstrap.Observability.AzureMonitor
{
    /// <summary>
    /// The marker registered by the first
    /// <see cref="CloudstrapObservabilityBuilderExtensions.AddAzureMonitor"/> call, so a second call
    /// short-circuits instead of contributing a duplicate set of exporters.
    /// </summary>
    internal sealed class AzureMonitorRegistrationMarker
    {
    }
}
