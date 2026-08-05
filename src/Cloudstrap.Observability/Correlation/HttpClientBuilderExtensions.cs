namespace Cloudstrap.Observability.Correlation
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;

    /// <summary>
    /// Adds correlation propagation to typed and named HTTP clients.
    /// </summary>
    public static class HttpClientBuilderExtensions
    {
        /// <summary>
        /// Adds the <see cref="CorrelationHttpDelegatingHandler"/> to the client so outbound requests carry
        /// the ambient correlation identifier in the configured header. Idempotent per client: calling it
        /// again — or combining it with a defaults-level registration — never stacks a second handler.
        /// Typed-client registration (Cloudstrap.Extensions) is the intended caller.
        /// </summary>
        /// <param name="builder">The HTTP client builder to add the handler to.</param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        public static IHttpClientBuilder AddCloudstrapCorrelationHandler(this IHttpClientBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.Services.TryAddTransient<CorrelationHttpDelegatingHandler>();

            return builder.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
            {
                if (!handlers.Any(handler => handler is CorrelationHttpDelegatingHandler))
                {
                    handlers.Add(serviceProvider.GetRequiredService<CorrelationHttpDelegatingHandler>());
                }
            });
        }
    }
}
