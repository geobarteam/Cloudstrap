namespace Cloudstrap.Proxy
{
    using Yarp.ReverseProxy.Configuration;

    /// <summary>
    /// Applies Cloudstrap's route rules to every route YARP loads, at startup and on each configuration reload.
    /// A route without an authorization policy gets YARP's reserved <c>Default</c> policy, the application's default
    /// authorization policy (a signed-in user unless the application changed it), so a proxied route is never
    /// reachable anonymously by omission. Named policies and an explicit <c>Anonymous</c> are kept.
    /// </summary>
    internal sealed class ProxyRouteConfigFilter : IProxyConfigFilter
    {
        /// <summary>YARP's reserved policy name for the application's default authorization policy.</summary>
        internal const string DefaultPolicyName = "Default";

        /// <inheritdoc />
        public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
        {
            return new ValueTask<ClusterConfig>(cluster);
        }

        /// <inheritdoc />
        public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(route);

            return string.IsNullOrEmpty(route.AuthorizationPolicy)
                ? new ValueTask<RouteConfig>(route with
                {
                    AuthorizationPolicy = DefaultPolicyName
                })
                : new ValueTask<RouteConfig>(route);
        }
    }
}
