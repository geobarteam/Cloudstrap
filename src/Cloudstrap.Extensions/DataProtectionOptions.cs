namespace Cloudstrap.Extensions
{
    /// <summary>
    /// Data protection settings, bound from the <c>Cloudstrap:DataProtection</c> configuration section: where
    /// the key ring lives and which key encrypts it.
    /// </summary>
    /// <remarks>
    /// This type shares its simple name with <c>Microsoft.AspNetCore.DataProtection.DataProtectionOptions</c>.
    /// Qualify or alias whichever one is meant when both namespaces are in scope.
    /// </remarks>
    public sealed class DataProtectionOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:DataProtection";

        /// <summary>
        /// Gets or sets a value indicating whether the key ring is persisted to Azure.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to persist and protect keys in Azure. Defaults to <see langword="false"/>,
        /// which leaves the framework's own key storage in place.
        /// </value>
        public bool Enabled
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the blob holding the key ring. Required when <see cref="Enabled"/> is set.
        /// </summary>
        /// <value>
        /// The full blob URI, for example
        /// <c>https://contosostore.blob.core.windows.net/keys/keys.xml</c>, or <see langword="null"/> when
        /// data protection is not configured.
        /// </value>
        public Uri? KeysBlobUri
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the KeyVault key encrypting the key ring at rest. Required when
        /// <see cref="Enabled"/> is set.
        /// </summary>
        /// <value>
        /// The key identifier, for example <c>https://contoso-vault.vault.azure.net/keys/dp-key</c>, or
        /// <see langword="null"/> when data protection is not configured.
        /// </value>
        /// <remarks>
        /// It is required rather than optional so keys are never written unencrypted by omission. An
        /// application that genuinely wants blob persistence without envelope encryption configures the
        /// framework's own <c>AddDataProtection()</c> chain instead.
        /// </remarks>
        public Uri? KeyVaultKeyId
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the name isolating this application's protected payloads from others sharing the
        /// same key ring storage.
        /// </summary>
        /// <value>
        /// The application name, or <see langword="null"/> to use the application's workload name. Set the
        /// same value in two applications to let them read each other's payloads deliberately.
        /// </value>
        public string? ApplicationName
        {
            get; set;
        }
    }
}
