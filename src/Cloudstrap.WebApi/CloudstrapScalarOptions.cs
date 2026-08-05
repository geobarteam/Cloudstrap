namespace Cloudstrap.WebApi
{
    /// <summary>
    /// Reference-UI settings, bound from the <c>Cloudstrap:Scalar</c> configuration section.
    /// </summary>
    public sealed class CloudstrapScalarOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Scalar";

        /// <summary>
        /// Gets or sets a value indicating whether the reference UI is mapped.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to map it, <see langword="false"/> never to, or <see langword="null"/> —
        /// the default — to follow the environment: mapped in <c>Development</c> only. Setting it explicitly
        /// to <see langword="true"/> exposes the UI wherever the application runs, which is a conscious
        /// choice rather than an accident of the environment name.
        /// </value>
        public bool? Enabled
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the path the reference UI is served on.
        /// </summary>
        /// <value>The UI path. Defaults to <c>/scalar</c>.</value>
        public string Path { get; set; } = "/scalar";

        /// <summary>
        /// Gets or sets the OAuth settings the reference UI signs in with.
        /// </summary>
        /// <value>The UI's OAuth settings. Never <see langword="null"/>.</value>
        public ScalarOAuthSettings OAuth { get; set; } = new();
    }
}
