namespace Cloudstrap.Proxy
{
    /// <summary>
    /// The YARP route and cluster <c>Metadata</c> keys Cloudstrap reads. Values are strings, as YARP metadata is a
    /// flat string map.
    /// </summary>
    public static class ProxyMetadataKeys
    {
        /// <summary>
        /// Cluster metadata: the <see cref="ProxyTokenMode"/> of the cluster's requests (<c>None</c>, <c>Client</c>
        /// or <c>User</c>, case-insensitive). Absent means <see cref="CloudstrapProxyOptions.DefaultTokenMode"/>.
        /// </summary>
        public const string TokenMode = "Cloudstrap.TokenMode";

        /// <summary>
        /// Route metadata: <c>true</c> forwards the caller's <c>Cookie</c> and <c>Authorization</c> headers to the
        /// destination. Unsafe for the trusted-subsystem pattern; the default removes both.
        /// </summary>
        public const string ForwardInboundCredentials = "Cloudstrap.ForwardInboundCredentials";

        /// <summary>
        /// Route metadata: <c>true</c> allows a route whose authorization policy is <c>Anonymous</c> to forward to a
        /// cluster that attaches a token, a deliberately public route. Without it, such a route fails startup.
        /// </summary>
        public const string AllowAnonymousWithToken = "Cloudstrap.AllowAnonymousWithToken";
    }
}
