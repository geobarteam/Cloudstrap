namespace Cloudstrap.Mvc
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="CloudstrapMvcOptions"/>. The rules are conditional rather than
    /// attribute-shaped, so validation is written out rather than generated.
    /// </summary>
    internal sealed class CloudstrapMvcOptionsValidator : IValidateOptions<CloudstrapMvcOptions>
    {
        /// <summary>
        /// Validates the supplied settings, naming the offending configuration key on failure.
        /// </summary>
        /// <param name="name">The options instance name, unused: these settings are never named.</param>
        /// <param name="options">The settings to validate.</param>
        /// <returns>The validation result.</returns>
        public ValidateOptionsResult Validate(string? name, CloudstrapMvcOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (options.Session.Enabled && options.Session.IdleTimeoutMinutes <= 0)
            {
                failures.Add(
                    $"'{CloudstrapMvcOptions.SectionName}:Session:IdleTimeoutMinutes' must be greater than "
                    + $"zero when '{CloudstrapMvcOptions.SectionName}:Session:Enabled' is true.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
