namespace Cloudstrap.BlazorServer
{
    /// <summary>
    /// HTTP Strict-Transport-Security settings, bound from <c>Cloudstrap:BlazorServer:Hsts</c>. The header
    /// is emitted outside <c>Development</c> only — browsers ignore HSTS over plain HTTP, and pinning a
    /// developer's localhost would be a nuisance cleared by hand.
    /// </summary>
    /// <remarks>
    /// Deliberately re-expressed package-locally rather than shared with <c>Cloudstrap.Mvc</c> or
    /// <c>Cloudstrap.WebApi</c>: a cross-package settings reference would couple otherwise independent
    /// closures for four properties (the D-2 precedent).
    /// </remarks>
    public sealed class HstsSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether the HSTS header is emitted at all.
        /// </summary>
        /// <value><see langword="true"/> by default.</value>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the <c>max-age</c> the header advertises, in days.
        /// </summary>
        /// <value>Defaults to 365.</value>
        public int MaxAgeDays { get; set; } = 365;

        /// <summary>
        /// Gets or sets a value indicating whether subdomains are included in the policy.
        /// </summary>
        /// <value><see langword="true"/> by default.</value>
        public bool IncludeSubDomains { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the <c>preload</c> token is claimed.
        /// </summary>
        /// <value>
        /// <see langword="false"/> by default: submitting a domain to the browser preload lists is a
        /// commitment only the domain owner can make — never a library default.
        /// </value>
        public bool Preload
        {
            get; set;
        }
    }
}
