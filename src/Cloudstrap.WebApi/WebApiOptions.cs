namespace Cloudstrap.WebApi
{
    /// <summary>
    /// Web API settings, bound from the <c>Cloudstrap:WebApi</c> configuration section.
    /// </summary>
    public sealed class WebApiOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:WebApi";

        /// <summary>
        /// Gets or sets the API versioning settings.
        /// </summary>
        /// <value>The API versioning settings. Never <see langword="null"/>.</value>
        public ApiVersioningSettings ApiVersioning { get; set; } = new();

        /// <summary>
        /// Gets or sets the JSON serialization settings.
        /// </summary>
        /// <value>The JSON settings. Never <see langword="null"/>.</value>
        public JsonSettings Json { get; set; } = new();

        /// <summary>
        /// Gets or sets a value indicating whether generated URLs and query strings are lowercased.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to generate lowercase URLs and query strings. Defaults to
        /// <see langword="true"/>. Route <em>matching</em> is case-insensitive either way; this setting only
        /// governs the links the application generates.
        /// </value>
        public bool LowercaseUrls { get; set; } = true;
    }
}
