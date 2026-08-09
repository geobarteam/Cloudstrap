namespace Cloudstrap.TestIdentityProvider
{
    /// <summary>
    /// Additional claims stamped into issued tokens, declared as plain data. Multi-valued claims are
    /// JSON arrays — there is no pipe-separator convention and no <c>schemas.</c> prefix rewriting.
    /// </summary>
    public sealed class TestIdentityProviderClaims
    {
        /// <summary>
        /// Gets the claims added to every token the provider issues.
        /// </summary>
        /// <value>Claim values by claim type. Empty by default.</value>
        public IDictionary<string, IList<string>> Common
        {
            get;
        } =
            new Dictionary<string, IList<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Gets the claims added to access tokens.
        /// </summary>
        /// <value>Claim values by claim type. Empty by default.</value>
        public IDictionary<string, IList<string>> AccessToken
        {
            get;
        } =
            new Dictionary<string, IList<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Gets the claims added to tokens issued through the client-credentials grant.
        /// </summary>
        /// <value>Claim values by claim type. Empty by default.</value>
        public IDictionary<string, IList<string>> ClientCredentialsToken
        {
            get;
        } =
            new Dictionary<string, IList<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Gets the claims added only to id tokens issued through the interactive flows.
        /// </summary>
        /// <value>Claim values by claim type. Empty by default.</value>
        public IDictionary<string, IList<string>> IdToken
        {
            get;
        } =
            new Dictionary<string, IList<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Gets the claims returned by the userinfo endpoint for tokens issued to this client.
        /// </summary>
        /// <value>Claim values by claim type. Empty by default.</value>
        public IDictionary<string, IList<string>> UserInfo
        {
            get;
        } =
            new Dictionary<string, IList<string>>(StringComparer.Ordinal);
    }
}
