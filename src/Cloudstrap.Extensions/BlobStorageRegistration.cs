namespace Cloudstrap.Extensions
{
    using Azure.Core;
    using Azure.Identity;
    using Azure.Storage.Blobs;
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;

    /// <summary>
    /// Turns the bound storage settings into the container client the application resolves.
    /// </summary>
    internal static class BlobStorageRegistration
    {
        /// <summary>
        /// Creates the container client: by connection string when one is configured, otherwise against the
        /// blob service URI with a credential.
        /// </summary>
        /// <param name="options">The bound <c>Cloudstrap:Storage</c> settings, already validated.</param>
        /// <param name="application">The application identity, supplying the default container name.</param>
        /// <param name="settings">The code-level credential override.</param>
        /// <param name="configuration">
        /// The configuration consulted for the standard <c>ConnectionStrings:</c> entry.
        /// </param>
        /// <returns>The container client.</returns>
        /// <remarks>
        /// Creating a client contacts nothing. The container is created only when the settings ask for it,
        /// and a failure to create it is surfaced rather than swallowed.
        /// </remarks>
        public static BlobContainerClient CreateClient(
            StorageOptions options,
            ApplicationOptions application,
            AzureCredentialSettings settings,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(application);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(configuration);

            string containerName = ResolveContainerName(options, application);
            string? connectionString = ResolveConnectionString(options, configuration);

            BlobContainerClient client = connectionString is not null
                ? new BlobContainerClient(connectionString, containerName)
                : new BlobContainerClient(
                    new Uri(options.BlobServiceUri!, containerName),
                    SelectCredential(settings));

            if (options.CreateContainerIfNotExists)
            {
                client.CreateIfNotExists();
            }

            return client;
        }

        /// <summary>
        /// Resolves the connection string from this package's own setting, falling back to the standard
        /// <c>ConnectionStrings:</c> entry so platform tooling can supply it.
        /// </summary>
        /// <param name="options">The bound settings.</param>
        /// <param name="configuration">The configuration consulted for the standard entry.</param>
        /// <returns>The connection string, or <see langword="null"/> when none is configured.</returns>
        public static string? ResolveConnectionString(StorageOptions options, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(configuration);

            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                return options.ConnectionString;
            }

            string? platformConnectionString =
                configuration.GetConnectionString(StorageOptions.ConnectionStringName);

            return string.IsNullOrWhiteSpace(platformConnectionString) ? null : platformConnectionString;
        }

        /// <summary>
        /// Selects the credential: the one the consumer supplied, or <c>DefaultAzureCredential</c>.
        /// </summary>
        /// <param name="settings">The code-level credential override.</param>
        /// <returns>The credential.</returns>
        public static TokenCredential SelectCredential(AzureCredentialSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            return settings.Credential ?? new DefaultAzureCredential();
        }

        /// <summary>
        /// Resolves the container name, defaulting to the application's system name in lower case.
        /// </summary>
        /// <param name="options">The bound settings.</param>
        /// <param name="application">The application identity.</param>
        /// <returns>The container name.</returns>
        private static string ResolveContainerName(StorageOptions options, ApplicationOptions application) =>
            string.IsNullOrWhiteSpace(options.ContainerName)
                ? application.SystemName.ToLowerInvariant()
                : options.ContainerName;
    }
}
