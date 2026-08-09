namespace Cloudstrap.Authentication.OpenIdConnect
{
    /// <summary>
    /// Settings of the interactive session cookie, bound from the
    /// <c>Cloudstrap:OpenIdConnect:Cookie</c> configuration section (decision D-1).
    /// </summary>
    /// <remarks>
    /// Only the name, lifetime and sliding behavior are configuration. The hardened trio —
    /// <c>HttpOnly</c>, <c>SecurePolicy=Always</c> and <c>SameSite=Lax</c> — are constants reachable
    /// only in code through <see cref="CloudstrapOpenIdConnectConfigurator.Cookie"/>, deliberately not
    /// configuration values: weakening them is a visible act in <c>Program.cs</c>, never config drift.
    /// </remarks>
    public sealed class CloudstrapAuthenticationCookieOptions
    {
        /// <summary>
        /// Gets or sets the session cookie's name.
        /// </summary>
        /// <value>
        /// The name. Defaults to <c>__Host-Cloudstrap</c> — the <c>__Host-</c> prefix makes the browser
        /// itself enforce <c>Secure</c>, <c>Path=/</c> and the absence of a <c>Domain</c> attribute.
        /// </value>
        public string Name { get; set; } = "__Host-Cloudstrap";

        /// <summary>
        /// Gets or sets the session's lifetime.
        /// </summary>
        /// <value>The lifetime. Defaults to 8 hours — a working day, not a browser lifetime.</value>
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(8);

        /// <summary>
        /// Gets or sets a value indicating whether activity extends the session.
        /// </summary>
        /// <value>
        /// <see langword="true"/> — the default — for sliding expiration; <see langword="false"/> for a
        /// fixed window from sign-in.
        /// </value>
        public bool SlidingExpiration { get; set; } = true;
    }
}
