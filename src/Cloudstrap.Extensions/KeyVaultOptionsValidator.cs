namespace Cloudstrap.Extensions
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Validates <see cref="KeyVaultOptions"/>. The single rule is conditional — a vault URI is required
    /// only once the section is enabled — so validation is written out rather than generated from
    /// attributes.
    /// </summary>
    internal sealed class KeyVaultOptionsValidator : IValidateOptions<KeyVaultOptions>
    {
        /// <summary>
        /// Validates the supplied settings, naming the offending configuration key on failure.
        /// </summary>
        /// <param name="name">The options instance name, unused: these settings are never named.</param>
        /// <param name="options">The settings to validate.</param>
        /// <returns>The validation result.</returns>
        public ValidateOptionsResult Validate(string? name, KeyVaultOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!options.Enabled)
            {
                return ValidateOptionsResult.Success;
            }

            if (options.VaultUri is null || !options.VaultUri.IsAbsoluteUri)
            {
                return ValidateOptionsResult.Fail(
                    $"'{KeyVaultOptions.SectionName}:VaultUri' must be an absolute URI when "
                    + $"'{KeyVaultOptions.SectionName}:Enabled' is true.");
            }

            return ValidateOptionsResult.Success;
        }
    }
}
