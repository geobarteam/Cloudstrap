namespace Cloudstrap.Mvc
{
    /// <summary>
    /// The MVC bootstrap settings, bound from <c>Cloudstrap:Mvc</c> and validated at host startup.
    /// </summary>
    public sealed class CloudstrapMvcOptions
    {
        /// <summary>
        /// The configuration section these settings bind from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Mvc";

        /// <summary>
        /// Gets or sets the session-state settings, bound from <c>Cloudstrap:Mvc:Session</c>.
        /// </summary>
        /// <value>The session settings; the hardened defaults when the section is absent.</value>
        public SessionSettings Session { get; set; } = new();

        /// <summary>
        /// Gets or sets the error-handling settings, bound from <c>Cloudstrap:Mvc:ExceptionHandling</c>.
        /// </summary>
        /// <value>The error-handling settings; the environment defaults when the section is absent.</value>
        public ExceptionHandlingSettings ExceptionHandling { get; set; } = new();

        /// <summary>
        /// Gets or sets the HSTS settings, bound from <c>Cloudstrap:Mvc:Hsts</c>.
        /// </summary>
        /// <value>The HSTS settings; the hardened defaults when the section is absent.</value>
        public HstsSettings Hsts { get; set; } = new();

        /// <summary>
        /// Gets the cross-origin settings, bound from <c>Cloudstrap:Mvc:Cors</c>.
        /// </summary>
        /// <value>The CORS settings; no origins — and therefore no CORS at all — by default.</value>
        public CorsSettings Cors { get; } = new();
    }
}
