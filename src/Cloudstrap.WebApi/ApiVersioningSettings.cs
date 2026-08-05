namespace Cloudstrap.WebApi
{
    /// <summary>
    /// API versioning settings, bound from the <c>Cloudstrap:WebApi:ApiVersioning</c> configuration section.
    /// </summary>
    /// <remarks>
    /// These settings steer request routing, never documentation. The version an unversioned caller gets is a
    /// routing decision and lives here, not in <c>Cloudstrap:OpenApi</c> or <c>Cloudstrap:Scalar</c>.
    /// </remarks>
    public sealed class ApiVersioningSettings
    {
        /// <summary>
        /// Gets or sets the API version assumed when a request names none, and assigned to controllers that
        /// carry no version metadata of their own.
        /// </summary>
        /// <value>
        /// The default API version, in the library's textual form (for example <c>1.0</c>, <c>2</c> or
        /// <c>2026-08-02</c>). Defaults to <c>1.0</c>. A value that does not parse fails host startup naming
        /// <c>Cloudstrap:WebApi:ApiVersioning:DefaultVersion</c>.
        /// </value>
        public string DefaultVersion { get; set; } = "1.0";

        /// <summary>
        /// Gets or sets a value indicating whether a request that names no API version is served with
        /// <see cref="DefaultVersion"/> instead of being rejected.
        /// </summary>
        /// <value><see langword="true"/> to assume the default version. Defaults to <see langword="true"/>.</value>
        public bool AssumeDefaultVersionWhenUnspecified { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether responses carry the <c>api-supported-versions</c> and
        /// <c>api-deprecated-versions</c> headers.
        /// </summary>
        /// <value><see langword="true"/> to report API versions. Defaults to <see langword="true"/>.</value>
        public bool ReportApiVersions { get; set; } = true;
    }
}
