namespace Cloudstrap.Proxy
{
    using Microsoft.Extensions.Options;
    using Yarp.ReverseProxy.Configuration;

    /// <summary>
    /// Applies Cloudstrap's route rules to every route YARP loads, at startup and on each configuration reload.
    /// A route without an authorization policy gets YARP's reserved <c>Default</c> policy, the application's default
    /// authorization policy (a signed-in user unless the application changed it), so a proxied route is never
    /// reachable anonymously by omission. Named policies and an explicit <c>Anonymous</c> are kept.
    /// </summary>
    /// <remarks>
    /// A route that forwards the caller's credentials (<see cref="ProxyMetadataKeys.ForwardInboundCredentials"/>) to a
    /// cluster in <see cref="ProxyTokenMode.Client"/> mode is refused (fail closed): the client-credentials handler
    /// keeps an <c>Authorization</c> header already on the request, so the caller's token would reach the destination
    /// instead of the application's. YARP reports the exception, which fails startup for the initial configuration
    /// and rejects a reloaded one.
    /// </remarks>
    /// <param name="options">The proxy options, for <see cref="CloudstrapProxyOptions.DefaultTokenMode"/>.</param>
    internal sealed class ProxyRouteConfigFilter(IOptions<CloudstrapProxyOptions> options) : IProxyConfigFilter
    {
        /// <summary>YARP's reserved policy name for the application's default authorization policy.</summary>
        internal const string DefaultPolicyName = "Default";

        /// <inheritdoc />
        public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
        {
            return new ValueTask<ClusterConfig>(cluster);
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">
        /// The route forwards the caller's credentials to a cluster in <see cref="ProxyTokenMode.Client"/> mode.
        /// </exception>
        public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(route);

            if (cluster is not null && ProxyMetadataReader.ForwardsInboundCredentials(route))
            {
                RejectInboundCredentialsOnClientCluster(route, cluster);
            }

            return string.IsNullOrEmpty(route.AuthorizationPolicy)
                ? new ValueTask<RouteConfig>(route with
                {
                    AuthorizationPolicy = DefaultPolicyName
                })
                : new ValueTask<RouteConfig>(route);
        }

        private void RejectInboundCredentialsOnClientCluster(RouteConfig route, ClusterConfig cluster)
        {
            ProxyTokenMode mode = ProxyMetadataReader.ResolveTokenMode(
                cluster.ClusterId,
                cluster.Metadata,
                options.Value.DefaultTokenMode);
            if (mode != ProxyTokenMode.Client)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Proxy route '{route.RouteId}' sets '{ProxyMetadataKeys.ForwardInboundCredentials}' = 'true', but its"
                + $" cluster '{cluster.ClusterId}' attaches the application's token ('{ProxyMetadataKeys.TokenMode}' ="
                + $" '{nameof(ProxyTokenMode.Client)}'). The client-credentials handler keeps an Authorization header"
                + " already on the request, so the caller's credentials would be sent instead of the application's"
                + $" token. Remove '{ProxyMetadataKeys.ForwardInboundCredentials}' from the route, or set the cluster's"
                + $" '{ProxyMetadataKeys.TokenMode}' to '{nameof(ProxyTokenMode.None)}'.");
        }
    }
}
