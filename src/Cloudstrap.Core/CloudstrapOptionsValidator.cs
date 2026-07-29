namespace Cloudstrap.Core
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates a whole <see cref="CloudstrapOptions"/> graph. This is the single validation cascade both
    /// entry points share: it runs each per-section validator, prefixes the failures it collects with the
    /// section path they came from, and adds the rules that only exist at the root — one per named HTTP client.
    /// </summary>
    internal sealed class CloudstrapOptionsValidator : IValidateOptions<CloudstrapOptions>
    {
        private readonly ApplicationOptionsValidator _applicationValidator = new();
        private readonly LoggingOptionsValidator _loggingValidator = new();
        private readonly OpenTelemetryOptionsValidator _openTelemetryValidator;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudstrapOptionsValidator"/> class.
        /// </summary>
        /// <param name="configuration">
        /// The configuration forwarded to the OpenTelemetry validator, which consults it for the standard
        /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> variable.
        /// </param>
        public CloudstrapOptionsValidator(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _openTelemetryValidator = new OpenTelemetryOptionsValidator(configuration);
        }

        /// <summary>
        /// Validates the supplied options graph, reporting every failure rather than stopping at the first.
        /// </summary>
        /// <param name="name">The options instance name, forwarded to the per-section validators.</param>
        /// <param name="options">The options graph to validate.</param>
        /// <returns>The validation result, carrying one failure per broken rule.</returns>
        public ValidateOptionsResult Validate(string? name, CloudstrapOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            Collect(failures, "Application", _applicationValidator.Validate(name, options.Application));
            Collect(failures, "Logging", _loggingValidator.Validate(name, options.Logging));
            Collect(failures, "OpenTelemetry", _openTelemetryValidator.Validate(name, options.OpenTelemetry));

            foreach (KeyValuePair<string, HttpClientServiceOptions> client in options.HttpClients)
            {
                if (client.Value.BaseAddress is null || !client.Value.BaseAddress.IsAbsoluteUri)
                {
                    failures.Add($"HttpClients:{client.Key}:BaseAddress must be an absolute URI.");
                }
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        private static void Collect(List<string> failures, string sectionName, ValidateOptionsResult result)
        {
            if (!result.Failed || result.Failures is null)
            {
                return;
            }

            foreach (string failure in result.Failures)
            {
                failures.Add($"{sectionName}:{failure}");
            }
        }
    }
}
