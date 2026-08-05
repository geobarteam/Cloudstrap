namespace Cloudstrap.WebApi
{
    /// <summary>
    /// HTTP Strict Transport Security settings, bound from the <c>Cloudstrap:WebApi:Hsts</c> configuration
    /// section.
    /// </summary>
    /// <remarks>
    /// The header is emitted outside <c>Development</c> only, and browsers only honour it on HTTPS
    /// responses. Configuring it in a local run therefore changes nothing — which is the point: a developer
    /// should never have to clear an HSTS pin from their browser because a library set one on localhost.
    /// </remarks>
    public sealed class HstsSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether the <c>Strict-Transport-Security</c> header is emitted.
        /// </summary>
        /// <value><see langword="true"/> to emit the header. Defaults to <see langword="true"/>.</value>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets how long, in days, a browser should remember to reach this host over HTTPS only.
        /// </summary>
        /// <value>
        /// The max-age in days. Defaults to 365. Must be greater than zero while
        /// <see cref="Enabled"/> is set, or host startup fails naming
        /// <c>Cloudstrap:WebApi:Hsts:MaxAgeDays</c>.
        /// </value>
        public int MaxAgeDays { get; set; } = 365;

        /// <summary>
        /// Gets or sets a value indicating whether the policy covers every subdomain of this host.
        /// </summary>
        /// <value><see langword="true"/> to include subdomains. Defaults to <see langword="true"/>.</value>
        public bool IncludeSubDomains { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the header asks for inclusion in the browser preload
        /// lists.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to request preloading. Defaults to <see langword="false"/>.
        /// </value>
        /// <remarks>
        /// Preloading is a commitment the domain owner makes, not one a library may make on their behalf:
        /// entries are baked into browser builds and are slow and painful to undo, and they cover the whole
        /// domain rather than this one application. Turn it on deliberately, once the whole domain is
        /// HTTPS-only.
        /// </remarks>
        public bool Preload
        {
            get; set;
        }
    }
}
