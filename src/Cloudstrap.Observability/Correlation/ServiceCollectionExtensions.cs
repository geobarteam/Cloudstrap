namespace Cloudstrap.Observability.Correlation
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;

    /// <summary>
    /// Registers the Cloudstrap correlation services in a dependency injection container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the ambient correlation services: <see cref="ICorrelationContextAccessor"/> and
        /// <see cref="ICorrelationSource"/>, plus the framework's problem-details services backing the
        /// middleware's <c>400 application/problem+json</c> responses.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Registrations are idempotent and never override consumer registrations: an
        /// <see cref="ICorrelationSource"/> registered before this call wins, and the framework's
        /// <c>AddProblemDetails</c> is additive.
        /// </remarks>
        public static IServiceCollection AddCloudstrapCorrelation(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
            services.TryAddSingleton<ICorrelationSource, TraceIdCorrelationSource>();
            services.AddProblemDetails();

            return services;
        }
    }
}
