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
        /// <returns>The settings.</returns>
        public static Dictionary<string, string?> SingleRoute(Uri destination, string section = "ReverseProxy")
        {
            ArgumentNullException.ThrowIfNull(destination);

            return new Dictionary<string, string?>
            {
                [$"{section}:Routes:{RouteId}:ClusterId"] = ClusterId,
                [$"{section}:Routes:{RouteId}:Match:Path"] = "/api/{**catch-all}",
                [$"{section}:Clusters:{ClusterId}:Destinations:primary:Address"] = destination.ToString(),
            };
        }
    }
}
