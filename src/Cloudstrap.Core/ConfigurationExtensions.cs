namespace Cloudstrap.Core
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Reads Cloudstrap settings straight from an <see cref="IConfiguration"/>, for code that runs before the
    /// host — and therefore before dependency injection — exists.
    /// </summary>
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Binds and validates the <c>Cloudstrap</c> configuration section eagerly.
        /// </summary>
        /// <param name="configuration">The configuration to read the <c>Cloudstrap</c> section from.</param>
        /// <returns>The bound and validated settings.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        /// <exception cref="ConfigurationValidationException">
        /// The <c>Cloudstrap</c> section is absent, or one or more validation rules fail. The exception message
        /// lists every failure and <see cref="ConfigurationValidationException.Failures"/> exposes them individually.
        /// </exception>
        /// <remarks>
        /// Consumers that want a tolerant read can bind without validating:
        /// <c>configuration.GetSection(CloudstrapOptions.SectionName).Get&lt;CloudstrapOptions&gt;()</c>.
        /// Hosts should prefer <c>AddCloudstrapCore</c>, which applies the same rules at startup.
        /// </remarks>
        public static CloudstrapOptions GetCloudstrapOptions(this IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            IConfigurationSection section = configuration.GetSection(CloudstrapOptions.SectionName);

            if (!section.Exists())
            {
                throw new ConfigurationValidationException(
                    $"The '{CloudstrapOptions.SectionName}' configuration section was not found. " +
                    $"Add a '{CloudstrapOptions.SectionName}' section to your configuration.");
            }

            CloudstrapOptions options = section.Get<CloudstrapOptions>() ?? new CloudstrapOptions();

            CloudstrapOptionsValidator validator = new(configuration);
            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

            if (result.Failed)
            {
                throw new ConfigurationValidationException(
                    $"The '{CloudstrapOptions.SectionName}' configuration section is invalid.",
                    result.Failures ?? []);
            }

            return options;
        }
    }
}
