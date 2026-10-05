namespace Cloudstrap.Hangfire
{
    using Cloudstrap.Observability;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;

    /// <summary>
    /// Registers the Hangfire storage health check on the stock health-checks builder.
    /// </summary>
    public static class HealthChecksBuilderExtensions
    {
        /// <summary>
        /// Adds a check reporting whether Hangfire storage answers, with its server, recurring, enqueued and failed
        /// counts as data. It is opt-in and additive: checks registered before and after it are kept.
        /// </summary>
        /// <param name="builder">The health-checks builder, from <c>services.AddHealthChecks()</c>.</param>
        /// <param name="name">The check name. Defaults to <c>hangfire</c>.</param>
        /// <param name="failureStatus">
        /// The status reported when storage does not answer, or <see langword="null"/> for
        /// <see cref="HealthStatus.Unhealthy"/>. A storage-only host (a dashboard) typically passes
        /// <see cref="HealthStatus.Degraded"/> so it stays ready without the job storage.
        /// </param>
        /// <param name="tags">The check tags, or <see langword="null"/> for the readiness tag (<c>ready</c>).</param>
        /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or whitespace.</exception>
        /// <remarks>
        /// The result never carries a connection string, a server name or an exception message; the failure detail
        /// is logged at Warning instead. Requires <c>AddCloudstrapHangfire</c> on the host.
        /// </remarks>
        public static IHealthChecksBuilder AddCloudstrapHangfireHealthCheck(
            this IHealthChecksBuilder builder,
            string name = "hangfire",
            HealthStatus? failureStatus = null,
            IEnumerable<string>? tags = null)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            return builder.Add(new HealthCheckRegistration(
                name,
                services => ActivatorUtilities.CreateInstance<HangfireStorageHealthCheck>(services),
                failureStatus,
                tags ?? [CloudstrapHealthCheckTags.Readiness]));
        }
    }
}
