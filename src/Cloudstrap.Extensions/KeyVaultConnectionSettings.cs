namespace Cloudstrap.Extensions
{
    using Azure.Core;

    /// <summary>
    /// Code-level overrides for the KeyVault configuration source — the settings that cannot sensibly live in
    /// configuration because they are objects rather than values.
    /// </summary>
    public sealed class KeyVaultConnectionSettings
    {
        /// <summary>
        /// Gets or sets the credential used to read secrets.
        /// </summary>
        /// <value>
        /// The credential, or <see langword="null"/> to use <c>DefaultAzureCredential</c> — which covers
        /// managed identity in Azure and a developer's own sign-in locally.
        /// </value>
        public TokenCredential? Credential
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets how often secrets are re-read from the vault.
        /// </summary>
        /// <value>
        /// The reload interval, or <see langword="null"/> to read once at startup — the default, and the
        /// cheaper choice for applications that are redeployed when their secrets rotate.
        /// </value>
        public TimeSpan? ReloadInterval
        {
            get; set;
        }
    }
}
