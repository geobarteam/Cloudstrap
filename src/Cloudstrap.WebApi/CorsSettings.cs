namespace Cloudstrap.WebApi
{
    /// <summary>
    /// Cross-origin settings, bound from the <c>Cloudstrap:WebApi:Cors</c> configuration section.
    /// </summary>
    public sealed class CorsSettings
    {
        /// <summary>
        /// Gets the browser origins allowed to call this API.
        /// </summary>
        /// <value>
        /// The allowed origins, each an absolute scheme-and-authority such as
        /// <c>https://app.example.com</c>. A leading <c>*.</c> label matches any subdomain
        /// (<c>https://*.example.com</c>). Empty by default.
        /// </value>
        /// <remarks>
        /// <para>
        /// While this list is empty <strong>no CORS policy is registered at all</strong> and no
        /// <c>Access-Control-Allow-Origin</c> header is ever emitted, so browsers apply their own
        /// same-origin default. There is deliberately no "allow any origin" fallback: an API that allows
        /// every origin because nobody configured one is a security defect, not a convenience.
        /// </para>
        /// <para>
        /// Once at least one origin is configured, Cloudstrap registers a default policy allowing exactly
        /// those origins, with credentials, any header and any method. Consumers needing several named
        /// policies use the framework's own <c>AddCors</c> and <c>RequireCors</c>: Cloudstrap's policy is
        /// additive, not exclusive.
        /// </para>
        /// <para>
        /// The property is get-only initialized, so configured values <em>append</em> to whatever the
        /// default holds. The default is empty precisely so that caveat never bites.
        /// </para>
        /// </remarks>
        public IList<string> AllowedOrigins { get; } = [];
    }
}
