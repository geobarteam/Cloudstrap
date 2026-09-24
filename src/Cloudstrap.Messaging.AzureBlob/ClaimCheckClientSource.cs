namespace Cloudstrap.Messaging.AzureBlob
{
    /// <summary>
    /// Where the claim check's container client came from — the rung of the resolution ladder that answered.
    /// </summary>
    internal enum ClaimCheckClientSource
    {
        /// <summary>A client handed in through <see cref="AzureBlobClaimCheckSettings.ContainerClient"/>.</summary>
        Code,

        /// <summary>A <c>BlobServiceClient</c> the host registered (its own, or a platform's).</summary>
        BlobServiceClient,

        /// <summary>The <c>BlobContainerClient</c> registered by <c>AddCloudstrapBlobStorage</c>.</summary>
        BlobContainerClient,
    }
}
