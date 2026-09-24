namespace Cloudstrap.Messaging.AzureBlob
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="AzureBlobClaimCheckOptions"/>: a positive threshold and a container name —
    /// the configured one, else the <c>{SystemName}-claimcheck</c> default — that satisfies Azure's naming
    /// rules. Every failure names the offending configuration key and never echoes a value; the rules are
    /// written out (rather than generated from attributes) so the key, not the property, is what a failure
    /// names.
    /// </summary>
    internal sealed class AzureBlobClaimCheckOptionsValidator : IValidateOptions<AzureBlobClaimCheckOptions>
    {
        private const string _thresholdKey = $"{AzureBlobClaimCheckOptions.SectionName}:OffloadThresholdBytes";
        private const string _containerKey = $"{AzureBlobClaimCheckOptions.SectionName}:ContainerName";
        private const string _systemNameKey = $"{ApplicationOptions.SectionName}:SystemName";

        private readonly ApplicationOptions _application;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureBlobClaimCheckOptionsValidator"/> class.
        /// </summary>
        /// <param name="application">The bound application identity, supplying the default container name.</param>
        public AzureBlobClaimCheckOptionsValidator(ApplicationOptions application)
        {
            ArgumentNullException.ThrowIfNull(application);

            _application = application;
        }

        /// <summary>
        /// Computes the effective container name: the configured value, else the system-derived default.
        /// </summary>
        /// <param name="options">The bound options.</param>
        /// <param name="application">The bound application identity.</param>
        /// <returns>The effective container name (not yet validated).</returns>
        public static string EffectiveContainerName(AzureBlobClaimCheckOptions options, ApplicationOptions application)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(application);

            return string.IsNullOrWhiteSpace(options.ContainerName)
                ? ContainerNames.Default(application.SystemName)
                : options.ContainerName;
        }

        /// <summary>
        /// Validates the supplied options, reporting every failure rather than stopping at the first.
        /// </summary>
        /// <param name="name">The options instance name, unused: the rules do not vary per name.</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>The validation result, carrying one failure per broken rule.</returns>
        public ValidateOptionsResult Validate(string? name, AzureBlobClaimCheckOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            List<string> failures = [];

            if (options.OffloadThresholdBytes <= 0)
            {
                failures.Add($"'{_thresholdKey}' must be a positive number of bytes (the default is 204800).");
            }

            if (!ContainerNames.IsValid(EffectiveContainerName(options, _application)))
            {
                failures.Add(string.IsNullOrWhiteSpace(options.ContainerName)
                    ? $"The default claim-check container name derived from '{_systemNameKey}' is not a valid Azure " +
                      $"container name; set '{_containerKey}' to 3-63 lower-case letters, digits and single hyphens, " +
                      "starting and ending alphanumeric."
                    : $"'{_containerKey}' is not a valid Azure container name: 3-63 lower-case letters, digits and " +
                      "single hyphens, starting and ending alphanumeric.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
