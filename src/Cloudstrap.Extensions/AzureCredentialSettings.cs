namespace Cloudstrap.Extensions
{
    using Azure.Core;

    /// <summary>
    /// The code-level credential override shared by the Azure-backed registrations that take no other
    /// object-valued settings.
    /// </summary>
    public sealed class AzureCredentialSettings
    {
        /// <summary>
        /// Gets or sets the credential used to reach the resource.
        /// </summary>
        /// <value>
        /// The credential, or <see langword="null"/> to use <c>DefaultAzureCredential</c> — which covers
        /// managed identity in Azure and a developer's own sign-in locally.
        /// </value>
        public TokenCredential? Credential
        {
            get; set;
        }
    }
}
