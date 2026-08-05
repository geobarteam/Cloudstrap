namespace Cloudstrap.Extensions
{
    using Azure.Core;
    using Azure.Extensions.AspNetCore.Configuration.Secrets;

    /// <summary>
    /// Everything needed to add the vault as a configuration source, composed from settings and hooks before
    /// anything Azure-touching happens. Keeping the decisions in one value is what makes them assertable
    /// without a vault.
    /// </summary>
    /// <param name="VaultUri">The vault the secrets are read from.</param>
    /// <param name="Credential">The credential the vault is read with.</param>
    /// <param name="Options">The source settings, carrying the secret manager and the reload interval.</param>
    internal sealed record KeyVaultSource(
        Uri VaultUri,
        TokenCredential Credential,
        AzureKeyVaultConfigurationOptions Options);
}
