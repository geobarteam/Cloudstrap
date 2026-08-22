namespace Cloudstrap.TestIdentityProvider
{
    /// <summary>
    /// One client the test identity provider recognises for the client-credentials grant.
    /// </summary>
    /// <remarks>
    /// Fixture values are always neutral: <c>contoso-*</c> client identifiers, <c>example.com</c>-style
    /// audiences, and obviously fake placeholder secrets — never a real credential.
    /// </remarks>
    public sealed class TestIdentityProviderClient
    {
        /// <summary>
        /// Gets or sets the client identifier presented as <c>client_id</c>.
        /// </summary>
        /// <value>The client identifier. Empty by default.</value>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the client secret presented as <c>client_secret</c>.
        /// </summary>
        /// <value>
        /// The secret — an obvious placeholder only; this provider exists for tests, never for real
        /// credentials.
        /// </value>
        public string? ClientSecret
        {
            get; set;
        }

        /// <summary>
        /// Gets the scopes this client may request. Requesting any other scope fails standards-shaped.
        /// </summary>
        /// <value>The grantable scopes. Empty by default.</value>
        public IList<string> Scopes { get; } = [];

        /// <summary>
        /// Gets the audiences (<c>aud</c>) stamped into tokens issued to this client.
        /// </summary>
        /// <value>The audiences. Empty by default.</value>
        public IList<string> Audiences { get; } = [];

        /// <summary>
        /// Gets the redirect URIs registered for the authorization-code flow. This list is the
        /// whitelist: an authorization request naming any other <c>redirect_uri</c> is rejected. A
        /// client with no redirect URIs stays a client-credentials-only client with exactly its
        /// pre-interactive permission set.
        /// </summary>
        /// <value>The registered redirect URIs. Empty by default.</value>
        public IList<Uri> RedirectUris { get; } = [];

        /// <summary>
        /// Gets the post-logout redirect URIs registered for RP-initiated sign-out. This list is the
        /// whitelist for <c>post_logout_redirect_uri</c>.
        /// </summary>
        /// <value>The registered post-logout redirect URIs. Empty by default.</value>
        public IList<Uri> PostLogoutRedirectUris { get; } = [];

        /// <summary>
        /// Gets the additional claims stamped into tokens issued to this client.
        /// </summary>
        /// <value>The claim sets, keyed by token kind.</value>
        public TestIdentityProviderClaims TokenClaims { get; } = new();
    }
}
