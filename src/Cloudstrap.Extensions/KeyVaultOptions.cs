namespace Cloudstrap.Extensions
{
    /// <summary>
    /// KeyVault-backed configuration settings, bound from the <c>Cloudstrap:KeyVault</c> configuration
    /// section.
    /// </summary>
    /// <remarks>
    /// Activation is explicit rather than inferred from the environment: the registration call belongs in
    /// every <c>Program.cs</c> unconditionally, and configuration decides where it does something.
    /// </remarks>
    public sealed class KeyVaultOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:KeyVault";

        /// <summary>
        /// Gets or sets a value indicating whether the vault is added as a configuration source.
        /// </summary>
        /// <value><see langword="true"/> to load secrets. Defaults to <see langword="false"/>.</value>
        public bool Enabled
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the vault the secrets are read from. Required, and must be absolute, when
        /// <see cref="Enabled"/> is set.
        /// </summary>
        /// <value>
        /// The vault URI, for example <c>https://contoso-vault.vault.azure.net/</c>, or
        /// <see langword="null"/> when no vault is used.
        /// </value>
        public Uri? VaultUri
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the prefix selecting this workload's secrets in a vault that may serve several.
        /// A secret named <c>{prefix}-Section--Key</c> is loaded as configuration key <c>Section:Key</c>.
        /// </summary>
        /// <value>
        /// The prefix without its trailing separator; <see langword="null"/> to use the application's
        /// workload name; or an empty string to load every secret in the vault unfiltered.
        /// </value>
        public string? SecretPrefix
        {
            get; set;
        }
    }
}
