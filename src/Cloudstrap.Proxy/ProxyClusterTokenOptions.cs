namespace Cloudstrap.Proxy
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;

    /// <summary>
    /// Mechanic (d): the per-cluster token request parameters. YARP cluster metadata is a flat string map and cannot
    /// carry the <see cref="TokenRequestOptions"/> shape, so the parameters bind from
    /// <c>Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters</c> instead.
    /// </summary>
    internal static class ProxyClusterTokenOptions
    {
        /// <summary>The subsection, under a cluster's <c>Cloudstrap:Proxy:Clusters:{clusterId}</c> section.</summary>
        internal const string TokenRequestParametersSection = "TokenRequestParameters";

        /// <summary>Gets the configuration path of a cluster's token request parameters.</summary>
        /// <param name="clusterId">The YARP cluster id.</param>
        /// <returns>The configuration path.</returns>
        public static string SectionPath(string clusterId)
        {
            return $"{CloudstrapProxyOptions.SectionName}:Clusters:{clusterId}:{TokenRequestParametersSection}";
        }

        /// <summary>Reads a cluster's token request parameters.</summary>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="clusterId">The YARP cluster id.</param>
        /// <returns>
        /// The parameters, or <see langword="null"/> when the subsection is absent (the provider's defaults apply).
        /// </returns>
        public static TokenRequestOptions? Read(IConfiguration configuration, string clusterId)
        {
            IConfigurationSection section = configuration.GetSection(SectionPath(clusterId));
            return section.Exists() ? section.Get<TokenRequestOptions>() : null;
        }
    }
}
