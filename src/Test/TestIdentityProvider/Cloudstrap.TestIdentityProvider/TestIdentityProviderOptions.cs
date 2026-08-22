namespace Cloudstrap.TestIdentityProvider
{
    /// <summary>
    /// Settings for the in-repo test identity provider (D-5): a real, lightweight OpenID Connect server
    /// issuing client-credentials tokens for tests to verify against. Everything about the issued tokens —
    /// clients, scopes, audiences, claims, lifetime — is configuration data on this options model.
    /// </summary>
    public sealed class TestIdentityProviderOptions
    {
        /// <summary>
        /// Gets or sets the lifetime of issued access tokens.
        /// </summary>
        /// <value>
        /// The lifetime. Defaults to 5 minutes — deliberately short; renewal tests override it shorter.
        /// </value>
        public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the lifetime of issued refresh tokens.
        /// </summary>
        /// <value>
        /// The lifetime. Defaults to 30 minutes — short; renewal-expiry tests shorten it further.
        /// </value>
        public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Gets or sets the issuer advertised in the discovery document and stamped into issued tokens.
        /// </summary>
        /// <value>
        /// The issuer, or <see langword="null"/> to derive it from the host's own address.
        /// </value>
        public Uri? Issuer
        {
            get; set;
        }

        /// <summary>
        /// Gets the clients the provider recognises. A token request from any other client identifier —
        /// or with a wrong secret — fails with the standard <c>invalid_client</c> error.
        /// </summary>
        /// <value>The configured clients. Empty by default.</value>
        public IList<TestIdentityProviderClient> Clients { get; } = [];

        /// <summary>
        /// Gets the users the provider can sign in interactively through its minimal login form.
        /// Configuring at least one user — or one client with redirect URIs — enables the
        /// authorization-code (PKCE-required) and refresh-token grants next to the always-on
        /// client-credentials grant.
        /// </summary>
        /// <value>The configured users. Empty by default.</value>
        public IList<TestIdentityProviderUser> Users { get; } = [];
    }
}
