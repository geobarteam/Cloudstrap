namespace Cloudstrap.Messaging.AzureBlob
{
    using Azure.Storage.Blobs;
    using Wolverine.Persistence;

    /// <summary>
    /// Code-level hooks over the claim check's configuration-driven defaults, supplied to
    /// <see cref="CloudstrapMessagingBuilderExtensions.UseAzureBlobClaimCheck"/>.
    /// </summary>
    public sealed class AzureBlobClaimCheckSettings
    {
        /// <summary>
        /// Gets or sets the container client the payloads are stored through.
        /// </summary>
        /// <value>
        /// The client, or <see langword="null"/> to resolve one at host start from what the host registered:
        /// a <see cref="BlobServiceClient"/> first, else the <see cref="BlobContainerClient"/> registered by
        /// <c>AddCloudstrapBlobStorage</c> — the claim-check container is then opened on that same account.
        /// A client handed in here wins over both, and <see cref="AzureBlobClaimCheckOptions.ContainerName"/>
        /// is ignored: the client already names its container.
        /// </value>
        public BlobContainerClient? ContainerClient
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the delegate that adjusts Wolverine's claim-check configuration after the store and
        /// the threshold have been applied.
        /// </summary>
        /// <value>
        /// The delegate, or <see langword="null"/> to keep the defaults. It runs last inside
        /// <c>UseClaimCheck</c>, so per-message stores (<c>StoreForMessage&lt;T&gt;</c>), per-envelope routing
        /// (<c>StoreWhen</c>) or a different threshold for one message type can be layered on top. Do not
        /// assign <c>Store</c> (the Azure store is already the node's <c>IClaimCheckStore</c>) or a payload
        /// time to live (<c>DeletePayloadsOlderThan</c>): both make Wolverine register services, which its
        /// bootstrap forbids at this point, and the Azure store does not sweep — expire payloads with a
        /// lifecycle-management policy on the container instead.
        /// </value>
        public Action<ClaimCheckConfiguration>? ClaimCheck
        {
            get; set;
        }
    }
}
