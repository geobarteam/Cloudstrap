namespace Cloudstrap.Extensions
{
    using Azure.Extensions.AspNetCore.Configuration.Secrets;
    using Azure.Security.KeyVault.Secrets;
    using Microsoft.Extensions.Configuration;

    /// <summary>
    /// Loads the secrets belonging to one workload out of a vault that may hold many, and maps their flat
    /// names onto nested configuration keys: <c>{prefix}-Section--Key</c> becomes <c>Section:Key</c>.
    /// </summary>
    /// <remarks>
    /// Sharing one vault across workloads is the reason this exists — without the filter, every workload
    /// would load every other workload's secrets. An empty prefix turns filtering off, leaving the name
    /// mapping in place.
    /// </remarks>
    internal sealed class PrefixKeyVaultSecretManager : KeyVaultSecretManager
    {
        private readonly string _prefix;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrefixKeyVaultSecretManager"/> class.
        /// </summary>
        /// <param name="prefix">
        /// The workload prefix, without its trailing separator. Empty loads every secret in the vault.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
        public PrefixKeyVaultSecretManager(string prefix)
        {
            ArgumentNullException.ThrowIfNull(prefix);

            _prefix = prefix.Length == 0 ? string.Empty : $"{prefix}-";
        }

        /// <inheritdoc/>
        public override bool Load(SecretProperties secret)
        {
            ArgumentNullException.ThrowIfNull(secret);

            return secret.Name.StartsWith(_prefix, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override string GetKey(KeyVaultSecret secret)
        {
            ArgumentNullException.ThrowIfNull(secret);

            return secret.Name[_prefix.Length..]
                .Replace("--", ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
        }
    }
}
