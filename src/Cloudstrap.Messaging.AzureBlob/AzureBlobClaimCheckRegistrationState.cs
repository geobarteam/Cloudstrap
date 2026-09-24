namespace Cloudstrap.Messaging.AzureBlob
{
    /// <summary>
    /// The registration-time facts of the one claim check a messaging node carries: its presence in the
    /// service collection is what makes a second <c>UseAzureBlobClaimCheck</c> call fail fast; it carries the
    /// eagerly bound options, the code-level hooks and the effective container name to the engine
    /// contribution, which records the client resolution it made.
    /// </summary>
    internal sealed class AzureBlobClaimCheckRegistrationState
    {
        public AzureBlobClaimCheckRegistrationState(
            AzureBlobClaimCheckOptions options,
            AzureBlobClaimCheckSettings settings,
            string containerName)
        {
            Options = options;
            Settings = settings;
            ContainerName = containerName;
        }

        /// <summary>Gets the <c>Cloudstrap:Messaging:ClaimCheck</c> section as bound at the registration call.</summary>
        public AzureBlobClaimCheckOptions Options
        {
            get;
        }

        /// <summary>Gets the code-level hooks the consumer supplied at registration.</summary>
        public AzureBlobClaimCheckSettings Settings
        {
            get;
        }

        /// <summary>Gets the effective container name: the configured one or <c>{SystemName}-claimcheck</c>.</summary>
        public string ContainerName
        {
            get;
        }

        /// <summary>
        /// Gets or sets the client resolution the engine contribution made at host start, or
        /// <see langword="null"/> until then.
        /// </summary>
        public ClaimCheckClientResolution? Resolution
        {
            get; set;
        }
    }
}
