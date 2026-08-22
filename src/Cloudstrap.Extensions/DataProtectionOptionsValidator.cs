namespace Cloudstrap.Extensions
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="DataProtectionOptions"/>. Both URIs become required the moment the section is
    /// enabled — a conditional rule, so validation is written out rather than generated from attributes —
    /// and every missing key is reported, not just the first.
    /// </summary>
    internal sealed class DataProtectionOptionsValidator : IValidateOptions<DataProtectionOptions>
    {
        /// <summary>
        /// Validates the supplied settings, naming each offending configuration key.
        /// </summary>
        /// <param name="name">The options instance name, unused: these settings are never named.</param>
        /// <param name="options">The settings to validate.</param>
        /// <returns>The validation result.</returns>
        public ValidateOptionsResult Validate(string? name, DataProtectionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!options.Enabled)
            {
                return ValidateOptionsResult.Success;
            }

            List<string> failures = [];

            if (options.KeysBlobUri is null || !options.KeysBlobUri.IsAbsoluteUri)
            {
                failures.Add(
                    $"'{DataProtectionOptions.SectionName}:KeysBlobUri' must be an absolute URI when "
                    + $"'{DataProtectionOptions.SectionName}:Enabled' is true.");
            }

            if (options.KeyVaultKeyId is null || !options.KeyVaultKeyId.IsAbsoluteUri)
            {
                failures.Add(
                    $"'{DataProtectionOptions.SectionName}:KeyVaultKeyId' must be an absolute URI when "
                    + $"'{DataProtectionOptions.SectionName}:Enabled' is true.");
            }

            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }
    }
}
