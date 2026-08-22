namespace Cloudstrap.TestIdentityProvider
{
    /// <summary>
    /// One user the test identity provider can sign in interactively — a neutral, declarative test
    /// fixture, never a real person.
    /// </summary>
    /// <remarks>
    /// Fixture values are always neutral: <c>contoso</c>-style usernames, obviously fake placeholder
    /// passwords, and claim values carrying no personal data.
    /// </remarks>
    public sealed class TestIdentityProviderUser
    {
        /// <summary>
        /// Gets or sets the username typed into the login form. It also becomes the <c>sub</c> claim of
        /// every token issued for this user.
        /// </summary>
        /// <value>The username. Empty by default.</value>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the password typed into the login form.
        /// </summary>
        /// <value>
        /// The password — an obvious placeholder only; this provider exists for tests, never for real
        /// credentials.
        /// </value>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Gets the claims stamped into tokens issued for this user, next to the per-client claim sets.
        /// Multi-valued claims are JSON arrays.
        /// </summary>
        /// <value>Claim values by claim type. Empty by default.</value>
        public IDictionary<string, IList<string>> Claims
        {
            get;
        } =
            new Dictionary<string, IList<string>>(StringComparer.Ordinal);
    }
}
