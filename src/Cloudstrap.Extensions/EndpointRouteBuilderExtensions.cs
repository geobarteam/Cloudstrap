namespace Cloudstrap.Extensions
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Diagnostics.HealthChecks;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;

    /// <summary>
    /// Maps the Cloudstrap health probe endpoints.
    /// </summary>
    public static class EndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the liveness and readiness probes described by the <c>Cloudstrap:HealthChecks</c>
        /// configuration section: <c>/healthz</c> answers with the checks tagged
        /// <see cref="CloudstrapHealthCheckTags.Liveness"/> and <c>/ready</c> with those tagged
        /// <see cref="CloudstrapHealthCheckTags.Readiness"/>.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder to map into.</param>
        /// <returns>The same <paramref name="endpoints"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Both probes are anonymous and short-circuited, so an orchestrator reaches them without
        /// authentication and without running the rest of the middleware pipeline. The framework's own
        /// response writer is used: probes answer with a status code and a one-word body, which is what
        /// orchestrators read.
        /// </para>
        /// <para>
        /// Tagging is what separates the two: a failing dependency check tagged <c>ready</c> takes the
        /// instance out of the load balancer without letting the orchestrator restart it.
        /// </para>
        /// <para>
        /// Setting <c>Cloudstrap:HealthChecks:Enabled</c> to <see langword="false"/> maps nothing, and the
        /// call is safe to repeat: the probes are mapped once per application. Consumers wanting a custom
        /// response body map their own endpoints with the framework's <c>MapHealthChecks</c> instead.
        /// </para>
        /// </remarks>
        public static IEndpointRouteBuilder MapCloudstrapHealthChecks(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            if (endpoints.DataSources.Any(source => source is CloudstrapHealthProbeMarker))
            {
                return endpoints;
            }

            HealthChecksOptions options = endpoints.ServiceProvider
                .GetRequiredService<IConfiguration>()
                .GetSection(HealthChecksOptions.SectionName)
                .Get<HealthChecksOptions>() ?? new HealthChecksOptions();

            if (!options.Enabled)
            {
                return endpoints;
            }

            endpoints.DataSources.Add(new CloudstrapHealthProbeMarker());

            MapProbe(endpoints, options.LivenessPath, CloudstrapHealthCheckTags.Liveness);
            MapProbe(endpoints, options.ReadinessPath, CloudstrapHealthCheckTags.Readiness);

            return endpoints;
        }

        /// <summary>
        /// Maps one probe endpoint serving the checks carrying a given tag.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder to map into.</param>
        /// <param name="path">The path the probe is served on.</param>
        /// <param name="tag">The tag selecting the checks the probe reports.</param>
        private static void MapProbe(IEndpointRouteBuilder endpoints, string path, string tag)
        {
            endpoints.MapHealthChecks(
                    path,
                    new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(tag) })
                .AllowAnonymous()
                .ShortCircuit();
        }
    }
}
