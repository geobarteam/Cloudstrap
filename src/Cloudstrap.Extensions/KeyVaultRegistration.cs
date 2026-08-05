namespace Cloudstrap.Extensions
{
    using Azure.Core;
    using Azure.Extensions.AspNetCore.Configuration.Secrets;
    using Azure.Identity;
    using Cloudstrap.Core;

    /// <summary>
    /// Turns the bound settings and the code-level hook into the description of a KeyVault configuration
    /// source.
    /// </summary>
    internal static class KeyVaultRegistration
    {
        /// <summary>
        /// Composes the vault source: which vault, read with which credential, filtered by which prefix.
        /// </summary>
        /// <param name="options">The bound <c>Cloudstrap:KeyVault</c> settings, already validated.</param>
        /// <param name="application">
        /// The application identity, supplying the default secret prefix.
        /// </param>
        /// <param name="settings">The code-level overrides.</param>
        /// <returns>The composed source.</returns>
        /// <remarks>
        /// <c>DefaultAzureCredential</c> is constructed here and nowhere else. There is deliberately no
        /// credential-type exclusion list and no switching on the hosting environment: the same credential
        /// resolves a managed identity in Azure and a developer's sign-in on a laptop.
        /// </remarks>
        public static KeyVaultSource Compose(
            KeyVaultOptions options,
            ApplicationOptions application,
            KeyVaultConnectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(settings);

            string prefix = options.SecretPrefix ?? application.WorkloadName;
            TokenCredential credential = settings.Credential ?? new DefaultAzureCredential();

            AzureKeyVaultConfigurationOptions sourceOptions = new()
            {
                Manager = new PrefixKeyVaultSecretManager(prefix),
                ReloadInterval = settings.ReloadInterval,
            };

            return new KeyVaultSource(options.VaultUri!, credential, sourceOptions);
        }
    }
}
