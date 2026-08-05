namespace Cloudstrap.Observability
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;

    /// <summary>
    /// Registers the Cloudstrap business tracing services in a dependency injection container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds <see cref="IBusinessTrace"/> as a singleton. Registered regardless of the telemetry pipeline
        /// state: with telemetry disabled, recorded spans are safe no-ops.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        public static IServiceCollection AddCloudstrapBusinessTrace(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<IBusinessTrace, BusinessTrace>();

            return services;
        }
    }
}
