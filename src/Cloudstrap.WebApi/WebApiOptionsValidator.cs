namespace Cloudstrap.WebApi
{
    using Asp.Versioning;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="WebApiOptions"/>. The rules are parse-shaped rather than attribute-shaped, so
    /// validation is written out rather than generated.
    /// </summary>
    internal sealed class WebApiOptionsValidator : IValidateOptions<WebApiOptions>
    {
        /// <summary>
        /// Validates the supplied settings, naming the offending configuration key on failure.
        /// </summary>
        /// <param name="name">The options instance name, unused: these settings are never named.</param>
        /// <param name="options">The settings to validate.</param>
        /// <returns>The validation result.</returns>
        public ValidateOptionsResult Validate(string? name, WebApiOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (!ApiVersionParser.Default.TryParse(options.ApiVersioning.DefaultVersion, out ApiVersion? _))
            {
                failures.Add(
                    $"'{WebApiOptions.SectionName}:ApiVersioning:DefaultVersion' must be a parsable API "
                    + $"version such as '1.0'; '{options.ApiVersioning.DefaultVersion}' is not.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
