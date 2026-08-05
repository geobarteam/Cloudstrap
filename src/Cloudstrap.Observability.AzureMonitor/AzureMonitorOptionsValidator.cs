namespace Cloudstrap.Observability.AzureMonitor
{
    using System.Globalization;
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="AzureMonitorOptions"/>. The range and mutual-exclusion rules apply in every mode,
    /// so a typo fails startup before the mode is flipped; the connection-string presence rule applies only
    /// when <c>Cloudstrap:OpenTelemetry:Mode</c> is <see cref="OpenTelemetryMode.AzureMonitor"/>. The supplied
    /// <see cref="IConfiguration"/> is consulted for the exporter's standard
    /// <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> variable, which satisfies that requirement on its own.
    /// </summary>
    internal sealed class AzureMonitorOptionsValidator : IValidateOptions<AzureMonitorOptions>
    {
        /// <summary>
        /// The configuration key of the Azure Monitor exporter's standard connection-string variable.
        /// </summary>
        internal const string StandardConnectionStringVariable = "APPLICATIONINSIGHTS_CONNECTION_STRING";

        private readonly IConfiguration _configuration;
        private readonly OpenTelemetryMode _mode;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureMonitorOptionsValidator"/> class.
        /// </summary>
        /// <param name="configuration">
        /// The configuration consulted for the <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> variable.
        /// </param>
        /// <param name="mode">
        /// The telemetry mode the registration decisions were made from, which decides whether a connection
        /// string is required.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public AzureMonitorOptionsValidator(IConfiguration configuration, OpenTelemetryMode mode)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            _configuration = configuration;
            _mode = mode;
        }

        /// <summary>
        /// Validates the supplied Azure Monitor options, reporting every broken rule at once.
        /// </summary>
        /// <param name="name">The options instance name, unused: the rules do not vary per name.</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>The validation result.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public ValidateOptionsResult Validate(string? name, AzureMonitorOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            // Negated range rather than an out-of-range test: NaN compares false against everything,
            // so `< 0 or > 1` would let a "NaN" setting through to the exporter.
            if (options.SamplingRatio is float ratio && ratio is not (>= 0.0f and <= 1.0f))
            {
                failures.Add(
                    $"{AzureMonitorOptions.SectionName}:SamplingRatio must be between 0.0 and 1.0 inclusive, " +
                    $"but was {ratio.ToString(CultureInfo.InvariantCulture)}.");
            }

            if (options.SamplingRatio is not null && options.TracesPerSecond is not null)
            {
                failures.Add(
                    $"{AzureMonitorOptions.SectionName}:SamplingRatio and " +
                    $"{AzureMonitorOptions.SectionName}:TracesPerSecond are mutually exclusive — " +
                    "configure at most one of them.");
            }

            if (_mode == OpenTelemetryMode.AzureMonitor && !HasConnectionString(options))
            {
                failures.Add(
                    $"A connection string is required when {OpenTelemetryOptions.SectionName}:Mode is " +
                    $"{nameof(OpenTelemetryMode.AzureMonitor)}. Set " +
                    $"{AzureMonitorOptions.SectionName}:ConnectionString, or supply the " +
                    $"{StandardConnectionStringVariable} configuration value — telemetry is never silently " +
                    "dropped.");
            }

            return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
        }

        private bool HasConnectionString(AzureMonitorOptions options)
            => !string.IsNullOrWhiteSpace(options.ConnectionString)
                || !string.IsNullOrWhiteSpace(_configuration[StandardConnectionStringVariable]);
    }
}
