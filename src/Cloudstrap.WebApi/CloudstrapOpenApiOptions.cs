namespace Cloudstrap.WebApi
{
    /// <summary>
    /// OpenAPI document settings, bound from the <c>Cloudstrap:OpenApi</c> configuration section.
    /// </summary>
    /// <remarks>
    /// These settings describe the <em>documents</em>. What version an unversioned request is served under is
    /// a routing decision and lives in <c>Cloudstrap:WebApi:ApiVersioning</c> — documentation settings never
    /// steer routing behavior.
    /// </remarks>
    public sealed class CloudstrapOpenApiOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:OpenApi";

        /// <summary>
        /// Gets or sets a value indicating whether documents are generated and served.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to publish one document per discovered API version at
        /// <c>/openapi/v{n}.json</c>. Defaults to <see langword="true"/>; setting it to
        /// <see langword="false"/> is the usual production posture for an API whose description is not
        /// public.
        /// </value>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the document title.
        /// </summary>
        /// <value>
        /// The title, or <see langword="null"/> — the default — to derive it from
        /// <c>Cloudstrap:Application:WorkloadName</c>.
        /// </value>
        public string? Title
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the document description.
        /// </summary>
        /// <value>
        /// The description, or <see langword="null"/> — the default — to derive a neutral sentence from the
        /// configured system, subsystem and subsystem type.
        /// </value>
        public string? Description
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the OAuth flow described in the documents.
        /// </summary>
        /// <value>The documented OAuth settings. Never <see langword="null"/>.</value>
        public OpenApiOAuthSettings OAuth { get; set; } = new();
    }
}
