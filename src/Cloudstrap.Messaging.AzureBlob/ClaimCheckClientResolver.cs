namespace Cloudstrap.Messaging.AzureBlob
{
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Specialized;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// The fixed ladder that turns "what the host already registered" into the claim check's container
    /// client, without constructing a credential or a client against another account: a client handed in
    /// through the settings, else a registered <see cref="BlobServiceClient"/>, else the
    /// <see cref="BlobContainerClient"/> registered by <c>AddCloudstrapBlobStorage</c> — whose account the
    /// claim-check container is opened on — else a failure naming both routes.
    /// </summary>
    internal static class ClaimCheckClientResolver
    {
        /// <summary>
        /// Resolves the container client.
        /// </summary>
        /// <param name="services">The host's service provider.</param>
        /// <param name="settings">The code-level hooks.</param>
        /// <param name="containerName">The effective claim-check container name (ignored when the settings hand in a client).</param>
        /// <returns>The resolution: the client and where it came from.</returns>
        /// <exception cref="InvalidOperationException">No route supplied a client.</exception>
        public static ClaimCheckClientResolution Resolve(
            IServiceProvider services,
            AzureBlobClaimCheckSettings settings,
            string containerName)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

            if (settings.ContainerClient is not null)
            {
                return new ClaimCheckClientResolution(settings.ContainerClient, ClaimCheckClientSource.Code);
            }

            BlobServiceClient? service = services.GetService<BlobServiceClient>();
            if (service is not null)
            {
                return new ClaimCheckClientResolution(
                    service.GetBlobContainerClient(containerName),
                    ClaimCheckClientSource.BlobServiceClient);
            }

            BlobContainerClient? registered = services.GetService<BlobContainerClient>();
            if (registered is not null)
            {
                return new ClaimCheckClientResolution(
                    registered.GetParentBlobServiceClient().GetBlobContainerClient(containerName),
                    ClaimCheckClientSource.BlobContainerClient);
            }

            throw new InvalidOperationException(
                "The Azure Blob claim check has no storage account to store payloads on: call " +
                "AddCloudstrapBlobStorage() on the host (Cloudstrap:Storage), or register a BlobServiceClient or " +
                $"BlobContainerClient, or hand a client in through {nameof(AzureBlobClaimCheckSettings)}." +
                $"{nameof(AzureBlobClaimCheckSettings.ContainerClient)}.");
        }
    }
}
