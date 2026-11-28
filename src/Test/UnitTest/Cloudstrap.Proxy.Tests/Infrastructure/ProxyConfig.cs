namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    /// <summary>
    /// Builds YARP's own <c>ReverseProxy</c> configuration schema as in-memory key/value pairs (mechanic (h)).
    /// </summary>
    internal static class ProxyConfig
    {
        /// <summary>The route id the helpers use.</summary>
        public const string RouteId = "downstream-route";

        /// <summary>The cluster id the helpers use.</summary>
        public const string ClusterId = "downstream-api";

        /// <summary>
        /// One route matching <c>/api/{**catch-all}</c> (path forwarded unchanged) to one cluster whose single
        /// destination is <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">The destination address, typically <see cref="EchoServer.Address"/>.</param>
        /// <param name="section">The configuration section holding the routes and clusters.</param>
        /// <param name="tokenMode">
        /// The cluster's <see cref="ProxyMetadataKeys.TokenMode"/> metadata, or <see langword="null"/> to leave it
        /// unset (the cluster then uses <see cref="CloudstrapProxyOptions.DefaultTokenMode"/>). Defaults to
        /// <c>None</c>, so suites that are not about tokens need no token provider.
        /// </param>
        /// <returns>The settings.</returns>
        public static Dictionary<string, string?> SingleRoute(Uri destination, string section = "ReverseProxy", string? tokenMode = nameof(ProxyTokenMode.None))
        {
            ArgumentNullException.ThrowIfNull(destination);

            Dictionary<string, string?> settings = new()
            {
                [$"{section}:Routes:{RouteId}:ClusterId"] = ClusterId,
                [$"{section}:Routes:{RouteId}:Match:Path"] = "/api/{**catch-all}",
                [$"{section}:Clusters:{ClusterId}:Destinations:primary:Address"] = destination.ToString(),
            };

            if (tokenMode is not null)
            {
                settings[$"{section}:Clusters:{ClusterId}:Metadata:{ProxyMetadataKeys.TokenMode}"] = tokenMode;
            }

            return settings;
        }
    }
}
