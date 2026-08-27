namespace Cloudstrap.BlazorServer
{
    /// <summary>
    /// The Blazor Server bootstrap settings, bound from <c>Cloudstrap:BlazorServer</c> and validated at
    /// host startup. The section is optional: with it absent, every default below applies and startup
    /// validation passes.
    /// </summary>
    public sealed class CloudstrapBlazorServerOptions
    {
        /// <summary>
        /// The configuration section these settings bind from.
        /// </summary>
        public const string SectionName = "Cloudstrap:BlazorServer";

        /// <summary>
        /// Gets or sets the HSTS settings, bound from <c>Cloudstrap:BlazorServer:Hsts</c>.
        /// </summary>
        /// <value>The HSTS settings; the hardened defaults when the section is absent.</value>
        public HstsSettings Hsts { get; set; } = new();

        /// <summary>
        /// Gets or sets the error-handling settings, bound from
        /// <c>Cloudstrap:BlazorServer:ExceptionHandling</c>.
        /// </summary>
        /// <value>The error-handling settings; the environment defaults when the section is absent.</value>
        public ExceptionHandlingSettings ExceptionHandling { get; set; } = new();

        /// <summary>
        /// Gets or sets a value indicating whether the <c>X-Frame-Options: SAMEORIGIN</c> response header
        /// is emitted.
        /// </summary>
        /// <value>
        /// <see langword="true"/> by default (D-12): a Blazor Server page has no business being framed by
        /// another origin unless the application says so. Set it to <see langword="false"/> for an
        /// application that is deliberately embedded, or one shipping a content security policy with
        /// <c>frame-ancestors</c> through its own middleware.
        /// </value>
        public bool EnableFrameOptions { get; set; } = true;
    }
}
