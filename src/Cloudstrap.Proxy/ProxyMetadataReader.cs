namespace Cloudstrap.Proxy
{
    using Yarp.ReverseProxy.Configuration;

    /// <summary>
    /// Reads the Cloudstrap keys of YARP route and cluster metadata (<see cref="ProxyMetadataKeys"/>), so the
    /// forwarder factory, the route filter and the stripping transform all apply the same rules.
    /// </summary>
    internal static class ProxyMetadataReader
    {
        /// <summary>
        /// Gets the raw <see cref="ProxyMetadataKeys.TokenMode"/> value of a cluster's metadata.
        /// </summary>
        /// <param name="metadata">The cluster metadata, possibly <see langword="null"/>.</param>
        /// <returns>The value, or <see langword="null"/> when the key is absent.</returns>
        public static string? ReadTokenMode(IReadOnlyDictionary<string, string>? metadata)
        {
            return metadata is not null && metadata.TryGetValue(ProxyMetadataKeys.TokenMode, out string? value) ? value : null;
        }

        /// <summary>
        /// Resolves a cluster's effective token mode: its <see cref="ProxyMetadataKeys.TokenMode"/> metadata, else the
        /// application's default.
        /// </summary>
        /// <param name="clusterId">The cluster id, for the failure message.</param>
        /// <param name="metadata">The cluster metadata, possibly <see langword="null"/>.</param>
        /// <param name="defaultMode">The mode of a cluster without the metadata key.</param>
        /// <returns>The effective token mode.</returns>
        /// <exception cref="InvalidOperationException">The metadata value is not a supported token mode.</exception>
        public static ProxyTokenMode ResolveTokenMode(
            string clusterId,
            IReadOnlyDictionary<string, string>? metadata,
            ProxyTokenMode defaultMode)
        {
            string? value = ReadTokenMode(metadata);
            if (value is null)
            {
                return defaultMode;
            }

            return Enum.TryParse(value, ignoreCase: true, out ProxyTokenMode mode)
                ? mode
                : throw UnsupportedTokenMode(clusterId, value);
        }

        /// <summary>
        /// Creates the failure for a token mode value that is not supported.
        /// </summary>
        /// <param name="clusterId">The cluster id.</param>
        /// <param name="value">The unsupported value.</param>
        /// <returns>The exception to throw.</returns>
        public static InvalidOperationException UnsupportedTokenMode(string clusterId, string value)
        {
            return new InvalidOperationException(
                $"Proxy cluster '{clusterId}' has '{ProxyMetadataKeys.TokenMode}' = '{value}', which is not a supported"
                + " token mode.");
        }

        /// <summary>
        /// Gets a value indicating whether a route opts out of the inbound-credential stripping: its
        /// <see cref="ProxyMetadataKeys.ForwardInboundCredentials"/> metadata is a case-insensitive <c>true</c>.
        /// </summary>
        /// <param name="route">The route.</param>
        /// <returns><see langword="true"/> when the route forwards the caller's credentials.</returns>
        public static bool ForwardsInboundCredentials(RouteConfig route)
        {
            return route.Metadata is not null
                && route.Metadata.TryGetValue(ProxyMetadataKeys.ForwardInboundCredentials, out string? value)
                && string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase);
        }
    }
}
