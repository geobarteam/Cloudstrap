namespace Cloudstrap.Core
{
    /// <summary>
    /// Additional parameters applied when requesting an access token for an outbound HTTP client, bound from the
    /// <c>Cloudstrap:HttpClients:{name}:TokenRequestParameters</c> configuration section.
    /// </summary>
    public sealed class TokenRequestOptions
    {
        /// <summary>
        /// Gets or sets the statically configured scope requested with the token.
        /// </summary>
        /// <value>The scope, or <see langword="null"/> to use the token client's configured scope.</value>
        public string? Scope
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the resource the token is requested for.
        /// </summary>
        /// <value>The resource, or <see langword="null"/> when no resource is requested.</value>
        public string? Resource
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the authentication scheme holding the stored tokens.
        /// </summary>
        /// <value>The sign-in scheme, or <see langword="null"/> to use the default scheme.</value>
        public string? SignInScheme
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the challenge scheme token service configuration is derived from.
        /// </summary>
        /// <value>The challenge scheme, or <see langword="null"/> to use the default scheme.</value>
        public string? ChallengeScheme
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether a fresh token is requested even when a cached one is still valid.
        /// </summary>
        /// <value><see langword="true"/> to force renewal. Defaults to <see langword="false"/>.</value>
        public bool ForceRenewal
        {
            get; set;
        }
    }
}
