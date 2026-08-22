namespace Cloudstrap.Observability.Correlation
{
    using Microsoft.AspNetCore.Builder;

    /// <summary>
    /// Adds the Cloudstrap correlation middleware to an application pipeline.
    /// </summary>
    public static class ApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds the correlation middleware: it establishes the ambient correlation identifier for every
        /// request and enforces the correlation requirement configured under <c>Cloudstrap:Correlation</c>.
        /// </summary>
        /// <param name="app">The application builder to add the middleware to.</param>
        /// <returns>The same <paramref name="app"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// Placement is not automatic: call this after routing so endpoint metadata
        /// (<see cref="CorrelationRequiredAttribute"/>, <see cref="AllowNoCorrelationAttribute"/>, health-check
        /// metadata) is visible to the middleware. The <c>400 application/problem+json</c> response is backed
        /// by the framework's problem-details services, registered by <c>AddCloudstrapCorrelation</c>.
        /// </remarks>
        public static IApplicationBuilder UseCloudstrapCorrelation(this IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);

            return app.UseMiddleware<CloudstrapCorrelationMiddleware>();
        }
    }
}
