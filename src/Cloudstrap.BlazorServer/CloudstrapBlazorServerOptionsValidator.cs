namespace Cloudstrap.BlazorServer
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="CloudstrapBlazorServerOptions"/>. The rules are conditional rather than
    /// attribute-shaped, so validation is written out rather than generated.
    /// </summary>
    internal sealed class CloudstrapBlazorServerOptionsValidator
        : IValidateOptions<CloudstrapBlazorServerOptions>
    {
        /// <summary>
        /// Validates the supplied settings, naming the offending configuration key on failure.
        /// </summary>
        /// <param name="name">The options instance name, unused: these settings are never named.</param>
        /// <param name="options">The settings to validate.</param>
        /// <returns>The validation result.</returns>
        public ValidateOptionsResult Validate(string? name, CloudstrapBlazorServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (options.Hsts.Enabled && options.Hsts.MaxAgeDays <= 0)
            {
                failures.Add(
                    $"'{CloudstrapBlazorServerOptions.SectionName}:Hsts:MaxAgeDays' must be greater than "
                    + $"zero when '{CloudstrapBlazorServerOptions.SectionName}:Hsts:Enabled' is true.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
