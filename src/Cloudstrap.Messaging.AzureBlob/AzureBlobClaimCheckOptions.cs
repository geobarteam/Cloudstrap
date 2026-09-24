namespace Cloudstrap.Messaging.AzureBlob
{
    /// <summary>
    /// Settings of the Azure Blob claim check, bound from the <c>Cloudstrap:Messaging:ClaimCheck</c>
    /// configuration section. Every value has a working default: a host with no section at all offloads
    /// bodies larger than 200 KiB to the <c>{SystemName}-claimcheck</c> container.
    /// </summary>
    /// <remarks>
    /// No setting here carries a secret or an account. The storage account and its credential come from
    /// what the host already registered (<c>AddCloudstrapBlobStorage</c>, a <c>BlobServiceClient</c>, or a
    /// client handed in through <see cref="AzureBlobClaimCheckSettings.ContainerClient"/>); this section
    /// only decides <em>when</em> to offload and <em>where</em> on that account the payloads live.
    /// </remarks>
    public sealed class AzureBlobClaimCheckOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Messaging:ClaimCheck";

        /// <summary>
        /// Gets or sets the size, in bytes, above which a message's serialized body is stored in the
        /// container and only a reference travels through the transport.
        /// </summary>
        /// <value>
        /// The threshold. Defaults to <c>204800</c> (200 KiB). A body is offloaded when it is
        /// <em>strictly larger</em> than this value; the default leaves headroom under Azure Service Bus'
        /// 256 KB standard-tier message limit for the envelope's own headers. Must be positive.
        /// </value>
        public long OffloadThresholdBytes { get; set; } = 204_800;

        /// <summary>
        /// Gets or sets the name of the blob container the offloaded payloads are stored in.
        /// </summary>
        /// <value>
        /// The container name, or <see langword="null"/> to use <c>{SystemName}-claimcheck</c> in lower case
        /// (<c>contoso-claimcheck</c> for the system <c>Contoso</c>). The container is dedicated on purpose:
        /// a lifecycle-management policy that expires stale payloads can then target it without touching the
        /// application's own data. Pointing it at the application container
        /// (<c>Cloudstrap:Storage:ContainerName</c>) is allowed — the lifecycle risk is then yours. Must be
        /// a valid Azure container name: 3–63 lower-case letters, digits and single hyphens, starting and
        /// ending alphanumeric.
        /// </value>
        public string? ContainerName
        {
            get; set;
        }
    }
}
