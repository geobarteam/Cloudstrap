namespace Cloudstrap.WebApi
{
    /// <summary>
    /// The OAuth settings the reference UI uses when a developer signs in to try an endpoint, bound from the
    /// <c>Cloudstrap:Scalar:OAuth</c> configuration section.
    /// </summary>
    /// <remarks>
    /// There is deliberately <strong>no client-secret property</strong>. Anything this type carries is
    /// rendered into a page a browser downloads, so a secret here would be a published secret. The library
    /// this package replaces had one and guarded it with a production-only validation rule; making the
    /// property not exist removes the mistake instead of policing it. Use a public client with PKCE.
    /// </remarks>
    public sealed class ScalarOAuthSettings
    {
        /// <summary>
        /// Gets or sets the public client identifier the reference UI authenticates with.
        /// </summary>
        /// <value>The client id, or <see langword="null"/> when the UI does not sign in.</value>
        public string? ClientId
        {
            get; set;
        }

        /// <summary>
        /// Gets the scopes the reference UI pre-selects when starting a sign-in.
        /// </summary>
        /// <value>The pre-selected scope names. Empty by default.</value>
        /// <remarks>
        /// Get-only initialized, so configured values <em>append</em> to whatever the default holds. The
        /// default is empty precisely so that caveat never bites.
        /// </remarks>
        public IList<string> SelectedScopes { get; } = [];
    }
}
