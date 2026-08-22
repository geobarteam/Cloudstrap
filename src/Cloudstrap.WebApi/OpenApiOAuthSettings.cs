namespace Cloudstrap.WebApi
{
    /// <summary>
    /// The OAuth flow described in the published OpenAPI documents, bound from the
    /// <c>Cloudstrap:OpenApi:OAuth</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Every URL here is explicit. Cloudstrap never derives one from the identity provider's authority,
    /// because the path layout under an authority is provider-specific — the enterprise library this package
    /// replaces hard-coded one vendor's layout and could describe no other.
    /// </remarks>
    public sealed class OpenApiOAuthSettings
    {
        /// <summary>
        /// Gets or sets the token endpoint the documented flow exchanges credentials at.
        /// </summary>
        /// <value>
        /// The absolute token endpoint URL, or <see langword="null"/> when no flow is documented.
        /// </value>
        public Uri? TokenUrl
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the authorization endpoint the documented flow starts at.
        /// </summary>
        /// <value>
        /// The absolute authorization endpoint URL, or <see langword="null"/> when the documented flow needs
        /// none.
        /// </value>
        public Uri? AuthorizationUrl
        {
            get; set;
        }

        /// <summary>
        /// Gets the scopes the documented flow may request, keyed by scope name with a human description as
        /// the value.
        /// </summary>
        /// <value>The scope map. Empty by default.</value>
        /// <remarks>
        /// Get-only initialized, so configured entries <em>add to</em> whatever the default holds. The
        /// default is empty precisely so that caveat never bites.
        /// </remarks>
        public IDictionary<string, string> Scopes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
