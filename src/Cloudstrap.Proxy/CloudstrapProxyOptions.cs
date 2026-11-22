namespace Cloudstrap.Proxy
{
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// Settings for the trusted-subsystem proxy, bound from the <c>Cloudstrap:Proxy</c> configuration section.
    /// Routes and clusters are not here: they stay in YARP's own configuration section.
    /// </summary>
    public sealed class CloudstrapProxyOptions
    {
        /// <summary>
        /// The configuration section the options bind from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Proxy";

        /// <summary>
        /// The default name of the YARP configuration section.
        /// </summary>
        public const string DefaultConfigurationSection = "ReverseProxy";

        /// <summary>
        /// Gets or sets the name of the configuration section holding YARP's routes and clusters.
        /// </summary>
        /// <value>
        /// The section name. Defaults to <see cref="DefaultConfigurationSection"/>. The section must exist: a missing
        /// section fails host startup naming this key.
        /// </value>
        [Required(ErrorMessage = "'Cloudstrap:Proxy:ConfigurationSection' is required — the name of the configuration"
            + " section holding the YARP routes and clusters ('ReverseProxy' by default).")]
        public string ConfigurationSection { get; set; } = DefaultConfigurationSection;

        /// <summary>
        /// Gets or sets the token mode of clusters that carry no <see cref="ProxyMetadataKeys.TokenMode"/> metadata.
        /// </summary>
        /// <value>The default token mode. Defaults to <see cref="ProxyTokenMode.Client"/>.</value>
        public ProxyTokenMode DefaultTokenMode { get; set; } = ProxyTokenMode.Client;

        /// <summary>
        /// Gets or sets a value indicating whether the caller's <c>Cookie</c> and <c>Authorization</c> headers are
        /// removed before a request is forwarded.
        /// </summary>
        /// <value>
        /// <see langword="true"/> (the default) to keep the caller's credentials at the front door. Setting it to
        /// <see langword="false"/> is unsafe for the trusted-subsystem pattern.
        /// </value>
        public bool StripInboundCredentials { get; set; } = true;
    }
}
