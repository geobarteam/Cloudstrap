namespace Cloudstrap.Messaging.AzureBlob
{
    using Azure.Storage.Blobs;

    /// <summary>
    /// The outcome of resolving the claim check's container client at host start.
    /// </summary>
    /// <param name="Container">The container client the payloads are stored through.</param>
    /// <param name="Source">The rung of the ladder that supplied it.</param>
    internal sealed record ClaimCheckClientResolution(BlobContainerClient Container, ClaimCheckClientSource Source)
    {
        /// <summary>Gets the label the posture log line uses for the source.</summary>
        public string SourceLabel => Source switch
        {
            ClaimCheckClientSource.Code => "code",
            ClaimCheckClientSource.BlobServiceClient => nameof(BlobServiceClient),
            _ => "AddCloudstrapBlobStorage",
        };
    }
}
