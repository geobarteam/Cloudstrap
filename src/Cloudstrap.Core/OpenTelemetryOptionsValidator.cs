namespace Cloudstrap.Core
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the conditional rules of <see cref="OpenTelemetryOptions"/> that data annotations cannot express.
    /// </summary>
    internal sealed class OpenTelemetryOptionsValidator : IValidateOptions<OpenTelemetryOptions>
    {
        /// <summary>
        /// Validates the supplied OpenTelemetry options. Only <see cref="OpenTelemetryMode.Otlp"/> carries
        /// extra requirements at the Core level; every other mode passes.
        /// </summary>
        /// <param name="name">The options instance name, unused: the rules do not vary per name.</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>The validation result.</returns>
        public ValidateOptionsResult Validate(string? name, OpenTelemetryOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.Mode != OpenTelemetryMode.Otlp)
            {
                return ValidateOptionsResult.Success;
            }

            if (options.Endpoint is null)
            {
                return ValidateOptionsResult.Fail(
                    $"Endpoint is required when Mode is {nameof(OpenTelemetryMode.Otlp)}.");
            }

            if (!options.Endpoint.IsAbsoluteUri
                || (options.Endpoint.Scheme != Uri.UriSchemeHttp && options.Endpoint.Scheme != Uri.UriSchemeHttps))
            {
                return ValidateOptionsResult.Fail(
                    $"Endpoint '{options.Endpoint.OriginalString}' must be an absolute http or https URI.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
