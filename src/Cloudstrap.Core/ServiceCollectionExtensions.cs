namespace Cloudstrap.Core
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registers the Cloudstrap settings model in a dependency injection container.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Binds the <c>Cloudstrap</c> configuration section and every one of its subsections to its options
        /// type, and validates all of them at host startup.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// After this call <see cref="IOptions{TOptions}"/> resolves for <see cref="CloudstrapOptions"/> and for
        /// each section type: <see cref="ApplicationOptions"/>, <see cref="LoggingOptions"/>,
        /// <see cref="OpenTelemetryOptions"/>, <see cref="CorrelationOptions"/> and
        /// <see cref="HealthChecksOptions"/>. Invalid configuration fails host startup with an
        /// <see cref="OptionsValidationException"/> naming the offending settings — the same rules
        /// <see cref="ConfigurationExtensions.GetCloudstrapOptions"/> applies on the eager path.
        /// </para>
        /// <para>
        /// Binding reads the <see cref="IConfiguration"/> registered in the container, which the standard host
        /// builders do automatically. There is deliberately no configure callback: layer code-level overrides
        /// with the framework idiom, <c>services.Configure&lt;ApplicationOptions&gt;(...)</c> or
        /// <c>services.PostConfigure&lt;ApplicationOptions&gt;(...)</c>, after calling this method.
        /// </para>
        /// <para>
        /// Calling this method more than once is safe: the validator registrations are idempotent.
        /// </para>
        /// </remarks>
        public static IServiceCollection AddCloudstrapCore(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddOptions<CloudstrapOptions>()
                .BindConfiguration(CloudstrapOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<CloudstrapOptions>, CloudstrapOptionsValidator>());

            services.AddOptions<ApplicationOptions>()
                .BindConfiguration(ApplicationOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<ApplicationOptions>, ApplicationOptionsValidator>());

            services.AddOptions<LoggingOptions>()
                .BindConfiguration(LoggingOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<LoggingOptions>, LoggingOptionsValidator>());

            services.AddOptions<OpenTelemetryOptions>()
                .BindConfiguration(OpenTelemetryOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<OpenTelemetryOptions>, OpenTelemetryOptionsValidator>());

            // Correlation and health checks carry no rules today. ValidateOnStart is applied anyway, so a rule
            // added later activates without changing this public API.
            services.AddOptions<CorrelationOptions>()
                .BindConfiguration(CorrelationOptions.SectionName)
                .ValidateOnStart();

            services.AddOptions<HealthChecksOptions>()
                .BindConfiguration(HealthChecksOptions.SectionName)
                .ValidateOnStart();

            return services;
        }
    }
}
