namespace Cloudstrap.Mvc
{
    /// <summary>
    /// Cross-origin settings, bound from <c>Cloudstrap:Mvc:Cors</c>.
    /// </summary>
    /// <remarks>
    /// With no origins configured, nothing at all is registered: no
    /// <c>Access-Control-Allow-Origin</c> header can be emitted and browsers keep their same-origin
    /// default. There is deliberately no allow-any-origin fallback. Because the list is get-only
    /// initialized, configured values <em>append</em> to the (empty) default rather than replacing it.
    /// </remarks>
    public sealed class CorsSettings
    {
        /// <summary>
        /// Gets the origins allowed to call this application cross-origin. An entry may carry a
        /// <c>*</c> to allow wildcard subdomains, e.g. <c>https://*.contoso.example</c>.
        /// </summary>
        /// <value>Empty by default — CORS stays entirely unregistered until an origin is configured.</value>
        public IList<string> AllowedOrigins { get; } = [];
    }
}
