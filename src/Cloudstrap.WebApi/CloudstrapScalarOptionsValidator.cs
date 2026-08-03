namespace Cloudstrap.WebApi
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="CloudstrapScalarOptions"/>. The one rule spans two configuration sections, so it
    /// reads the other section through <see cref="IConfiguration"/> rather than being generated from
    /// attributes.
    /// </summary>
    /// <param name="configuration">The configuration the sibling document section is read from.</param>
    internal sealed class CloudstrapScalarOptionsValidator(IConfiguration configuration)
        : IValidateOptions<CloudstrapScalarOptions>
    {
        /// <summary>
        /// Validates the supplied settings, naming both offending configuration keys on failure.
        /// </summary>
        /// <param name="name">The options instance name, unused: these settings are never named.</param>
        /// <param name="options">The settings to validate.</param>
        /// <returns>The validation result.</returns>
        /// <remarks>
        /// Only an <em>explicit</em> request for the UI is an error when the documents are switched off. When
        /// exposure was merely implied by running in <c>Development</c>, the UI is quietly left unmapped
        /// instead — turning the documents off during local work is a reasonable thing to do, and should not
        /// stop the application from starting.
        /// </remarks>
        public ValidateOptionsResult Validate(string? name, CloudstrapScalarOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.Enabled != true)
            {
                return ValidateOptionsResult.Success;
            }

            bool documentsEnabled = configuration
                .GetSection(CloudstrapOpenApiOptions.SectionName)
                .Get<CloudstrapOpenApiOptions>()?.Enabled ?? true;

            if (!documentsEnabled)
            {
                return ValidateOptionsResult.Fail(
                    $"'{CloudstrapScalarOptions.SectionName}:Enabled' is true while "
                    + $"'{CloudstrapOpenApiOptions.SectionName}:Enabled' is false: the reference UI would "
                    + "have no documents to render. Enable the documents, or leave the UI setting unset to "
                    + "expose it in Development only.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
