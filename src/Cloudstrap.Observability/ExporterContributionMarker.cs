namespace Cloudstrap.Observability
{
    /// <summary>
    /// The shared marker exporter packages flip through
    /// <see cref="CloudstrapObservabilityBuilder.MarkExporterContributed"/> so the
    /// <see cref="AzureMonitorContributionGuard"/> lets the host start.
    /// </summary>
    internal sealed class ExporterContributionMarker
    {
        /// <summary>
        /// Gets or sets a value indicating whether an exporter package has contributed an exporter.
        /// </summary>
        public bool Contributed
        {
            get; set;
        }
    }
}
