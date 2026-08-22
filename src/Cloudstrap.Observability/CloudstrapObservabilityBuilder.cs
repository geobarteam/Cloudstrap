namespace Cloudstrap.Observability
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// The result of <see cref="HostApplicationBuilderExtensions.UseCloudstrapObservability"/>: exposes the
    /// service collection and the telemetry settings the entry point made its registration decisions from,
    /// so packages that build on the observability pipeline can layer their own registrations on top.
    /// </summary>
    public sealed class CloudstrapObservabilityBuilder
    {
        private readonly ExporterContributionMarker _contributionMarker;

        internal CloudstrapObservabilityBuilder(
            IServiceCollection services,
            OpenTelemetryOptions telemetry,
            ExporterContributionMarker contributionMarker)
        {
            Services = services;
            Telemetry = telemetry;
            _contributionMarker = contributionMarker;
        }

        /// <summary>
        /// Gets the service collection of the host the observability services were registered into.
        /// </summary>
        /// <value>The host's service collection.</value>
        public IServiceCollection Services
        {
            get;
        }

        /// <summary>
        /// Gets the OpenTelemetry settings the registration decisions were made from.
        /// </summary>
        /// <value>The bound OpenTelemetry settings.</value>
        public OpenTelemetryOptions Telemetry
        {
            get;
        }

        /// <summary>
        /// Records that an exporter package has contributed an exporter to the pipeline. Exporter packages —
        /// <c>Cloudstrap.Observability.AzureMonitor</c> in particular — call this after registering their
        /// exporter; when <c>Cloudstrap:OpenTelemetry:Mode</c> is <c>AzureMonitor</c> and nothing was
        /// contributed, host startup fails rather than silently dropping telemetry.
        /// </summary>
        public void MarkExporterContributed() => _contributionMarker.Contributed = true;
    }
}
