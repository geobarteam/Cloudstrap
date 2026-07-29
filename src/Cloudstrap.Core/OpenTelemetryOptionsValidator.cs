namespace Cloudstrap.Core
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates the conditional rules of <see cref="OpenTelemetryOptions"/> that data annotations cannot
    /// express. The validator consults the supplied <see cref="IConfiguration"/> for the OpenTelemetry SDK's
    /// standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> variable, which satisfies the
    /// <see cref="OpenTelemetryMode.Otlp"/> endpoint requirement when no explicit endpoint is configured.
    /// </summary>
    internal sealed class OpenTelemetryOptionsValidator : IValidateOptions<OpenTelemetryOptions>
    {
        /// <summary>
        /// The configuration key of the OpenTelemetry SDK's standard OTLP endpoint variable.
        /// </summary>
        internal const string StandardEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenTelemetryOptionsValidator"/> class.
        /// </summary>
        /// <param name="configuration">
        /// The configuration consulted for the <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> variable. On the dependency
        /// injection path the container supplies the host's configuration; the eager path passes its own.
        /// </param>
        public OpenTelemetryOptionsValidator(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _configuration = configuration;
        }

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
                return string.IsNullOrWhiteSpace(_configuration[StandardEndpointVariable])
                    ? ValidateOptionsResult.Fail(
                        $"Endpoint is required when Mode is {nameof(OpenTelemetryMode.Otlp)} and the " +
                        $"{StandardEndpointVariable} configuration value is not set.")
                    : ValidateOptionsResult.Success;
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
