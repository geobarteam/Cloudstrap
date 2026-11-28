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
    /// <para>
    /// A route that forwards the caller's credentials to a cluster in <see cref="ProxyTokenMode.Client"/> mode, through
    /// its <see cref="ProxyMetadataKeys.ForwardInboundCredentials"/> metadata or the global
    /// <see cref="CloudstrapProxyOptions.StripInboundCredentials"/> = <see langword="false"/>, is refused (fail closed):
    /// the client-credentials handler keeps an <c>Authorization</c> header already on the request, so the caller's
    /// token would reach the destination instead of the application's. YARP reports the exception, which fails startup
    /// for the initial configuration and rejects a reloaded one.
    /// </para>
    /// <para>
    /// The confused-deputy guard: a route with policy <c>Anonymous</c> on a cluster whose effective mode is
    /// <see cref="ProxyTokenMode.Client"/> or <see cref="ProxyTokenMode.User"/> is refused the same way, unless its
    /// <see cref="ProxyMetadataKeys.AllowAnonymousWithToken"/> metadata is <c>true</c>. The filter is the only
    /// mechanism needed: YARP's initial load rethrows a filter exception, so no separate startup check exists.
    /// </para>
    /// </remarks>
    /// <param name="options">The proxy options, for <see cref="CloudstrapProxyOptions.DefaultTokenMode"/>.</param>
    internal sealed class ProxyRouteConfigFilter(IOptions<CloudstrapProxyOptions> options) : IProxyConfigFilter
    {
        /// <summary>YARP's reserved policy name for the application's default authorization policy.</summary>
        internal const string DefaultPolicyName = "Default";

        /// <summary>YARP's reserved policy name for a route that anyone may call.</summary>
        internal const string AnonymousPolicyName = "Anonymous";

        /// <summary>The configuration key of <see cref="CloudstrapProxyOptions.StripInboundCredentials"/>.</summary>
        internal const string StripInboundCredentialsKey = $"{CloudstrapProxyOptions.SectionName}:{nameof(CloudstrapProxyOptions.StripInboundCredentials)}";

        /// <inheritdoc />
        public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
        {
            return new ValueTask<ClusterConfig>(cluster);
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">
        /// The route forwards the caller's credentials to a cluster in <see cref="ProxyTokenMode.Client"/> mode, or the
        /// cluster's token mode metadata is not a supported token mode, or the route is <c>Anonymous</c> on a cluster
        /// that attaches a token without the <see cref="ProxyMetadataKeys.AllowAnonymousWithToken"/> override.
        /// </exception>
        public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(route);

            if (cluster is not null)
            {
                ProxyTokenMode mode = ProxyMetadataReader.ResolveTokenMode(
                    cluster.ClusterId,
                    cluster.Metadata,
                    options.Value.DefaultTokenMode);
                RejectInboundCredentialsOnClientCluster(route, cluster, mode);
                RejectAnonymousRouteWithToken(route, cluster, mode);
            }

            return string.IsNullOrEmpty(route.AuthorizationPolicy)
                ? new ValueTask<RouteConfig>(route with
                {
                    AuthorizationPolicy = DefaultPolicyName
                })
                : new ValueTask<RouteConfig>(route);
        }

        /// <summary>
        /// The confused-deputy guard (AC-PX5): a route anyone can call must not forward with the application's or a
        /// user's token, or an anonymous caller borrows that identity. YARP's reserved policy names are
        /// case-insensitive, so <c>anonymous</c> counts too.
        /// </summary>
        private static void RejectAnonymousRouteWithToken(RouteConfig route, ClusterConfig cluster, ProxyTokenMode mode)
        {
            if (mode == ProxyTokenMode.None
                || !string.Equals(route.AuthorizationPolicy, AnonymousPolicyName, StringComparison.OrdinalIgnoreCase)
                || ProxyMetadataReader.AllowsAnonymousWithToken(route))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Proxy route '{route.RouteId}' has the authorization policy '{AnonymousPolicyName}', but its cluster"
                + $" '{cluster.ClusterId}' attaches a token ('{ProxyMetadataKeys.TokenMode}' = '{mode}'). Any caller could"
                + $" then reach the destination with that identity. Require a signed-in user on the route, set the"
                + $" cluster's '{ProxyMetadataKeys.TokenMode}' to '{nameof(ProxyTokenMode.None)}', or, for a deliberately"
                + $" public route, set the route metadata '{ProxyMetadataKeys.AllowAnonymousWithToken}' to 'true'.");
        }

        private void RejectInboundCredentialsOnClientCluster(RouteConfig route, ClusterConfig cluster, ProxyTokenMode mode)
        {
            if (mode != ProxyTokenMode.Client)
            {
                return;
            }

            if (!options.Value.StripInboundCredentials)
            {
                throw new InvalidOperationException(
                    $"'{StripInboundCredentialsKey}' is false, but proxy cluster '{cluster.ClusterId}' (route"
                    + $" '{route.RouteId}') attaches the application's token ('{ProxyMetadataKeys.TokenMode}' ="
                    + $" '{nameof(ProxyTokenMode.Client)}'). The client-credentials handler keeps an Authorization header"
                    + " already on the request, so the caller's credentials would be sent instead of the application's"
                    + $" token. Remove '{StripInboundCredentialsKey}', or set the cluster's"
                    + $" '{ProxyMetadataKeys.TokenMode}' to '{nameof(ProxyTokenMode.None)}'.");
            }

            if (!ProxyMetadataReader.ForwardsInboundCredentials(route))
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
