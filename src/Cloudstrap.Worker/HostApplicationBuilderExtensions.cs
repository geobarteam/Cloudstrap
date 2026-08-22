namespace Cloudstrap.Worker
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Bootstraps a Cloudstrap worker service on the generic host.
    /// </summary>
    public static class HostApplicationBuilderExtensions
    {
        private const string _workerMarker = "Cloudstrap.Worker";

        /// <summary>
        /// Adds the Cloudstrap worker bootstrap: the validated <c>Cloudstrap</c> settings model,
        /// correlation services, the stock health-check builder, and a health listener serving the
        /// standard probe endpoints from the host's registered checks on a configurable port.
        /// </summary>
        /// <param name="builder">The host application builder to configure.</param>
        /// <param name="configure">
        /// Optional code-level overrides for <see cref="WorkerOptions"/>; runs after configuration
        /// binding, so its values win.
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <exception cref="ConfigurationValidationException">The <c>Cloudstrap</c> section is missing or invalid.</exception>
        /// <remarks>
        /// <para>
        /// This call owns the <c>Cloudstrap:Worker</c> section (<see cref="WorkerOptions.HealthPort"/>,
        /// <see cref="WorkerOptions.HealthListenAddress"/>) and consumes <c>Cloudstrap:HealthChecks</c>
        /// (<c>Enabled</c>, <c>LivenessPath</c>, <c>ReadinessPath</c>) — probe paths and the kill
        /// switch are defined once, never redefined here. Setting
        /// <c>Cloudstrap:HealthChecks:Enabled</c> to <see langword="false"/> registers no listener
        /// and never binds the port. The enabled decision is read at this call: add late
        /// configuration sources (for example <c>AddCloudstrapKeyVault</c>) <em>before</em> calling
        /// this method.
        /// </para>
        /// <para>
        /// Deliberately not registered here (the suite's composition convention): observability —
        /// call <c>UseCloudstrapObservability()</c> as an explicit sibling so its returned builder
        /// stays reachable for <c>.AddAzureMonitor()</c> and the owner/contribute mode choice — and
        /// KeyVault configuration — call <c>AddCloudstrapKeyVault()</c> explicitly so configuration
        /// ownership stays visible (Cloudstrap's or the platform's, not both).
        /// </para>
        /// <para>
        /// The configuration is read and validated eagerly: an invalid <c>Cloudstrap</c> section
        /// throws at this call, before the host is built. Health checks registered on the stock
        /// <c>AddHealthChecks()</c> builder before or after this call all feed the probes. Calling
        /// this method more than once is a no-op after the first call.
        /// </para>
        /// </remarks>
        public static IHostApplicationBuilder AddCloudstrapWorker(
            this IHostApplicationBuilder builder,
            Action<WorkerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (builder.Properties.ContainsKey(_workerMarker))
            {
                return builder;
            }

            builder.Properties[_workerMarker] = true;

            // Fail fast: an invalid Cloudstrap section aborts at the call, before the host is built.
            _ = builder.Configuration.GetCloudstrapOptions();

            builder.Services.AddCloudstrapCore();
            builder.Services.AddCloudstrapCorrelation();

            builder.Services.AddOptions<WorkerOptions>()
                .BindConfiguration(WorkerOptions.SectionName)
                .ValidateOnStart();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>());
            if (configure is not null)
            {
                builder.Services.PostConfigure(configure);
            }

            // Additive on the stock builder: consumer checks registered before or after this call
            // all land in the same registry (the Aspire-coexistence posture).
            builder.Services.AddHealthChecks();

            HealthChecksOptions healthChecks = builder.Configuration
                .GetSection(HealthChecksOptions.SectionName)
                .Get<HealthChecksOptions>() ?? new HealthChecksOptions();
            if (healthChecks.Enabled)
            {
                builder.Services.AddHostedService<WorkerHealthListener>();
            }

            return builder;
        }
    }
}
