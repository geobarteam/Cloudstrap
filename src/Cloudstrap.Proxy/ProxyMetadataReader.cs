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

            return TryParseTokenMode(value, out ProxyTokenMode mode)
                ? mode
                : throw new InvalidOperationException(
                    $"Proxy cluster '{clusterId}' has '{ProxyMetadataKeys.TokenMode}' = '{value}', which is not a"
                    + $" supported token mode. Use one of: {AllowedTokenModes}.");
        }

        /// <summary>
        /// Gets the token mode names a setting accepts, comma-separated, for failure messages.
        /// </summary>
        public static string AllowedTokenModes => string.Join(", ", Enum.GetNames<ProxyTokenMode>());

        /// <summary>
        /// Parses a token mode setting. Only a <see cref="ProxyTokenMode"/> member name is accepted, in any case:
        /// unlike <see cref="Enum.TryParse{TEnum}(string?, bool, out TEnum)"/>, numeric values (<c>1</c>) and combined
        /// values (<c>Client, User</c>) are refused, so a setting can never select a mode by accident.
        /// </summary>
        /// <param name="value">The setting value.</param>
        /// <param name="mode">The parsed mode, when the method returns <see langword="true"/>.</param>
        /// <returns><see langword="true"/> when <paramref name="value"/> is a member name.</returns>
        public static bool TryParseTokenMode(string? value, out ProxyTokenMode mode)
        {
            foreach (ProxyTokenMode candidate in Enum.GetValues<ProxyTokenMode>())
            {
                if (string.Equals(value, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    mode = candidate;
                    return true;
                }
            }

            mode = default;
            return false;
        }

        /// <summary>
        /// Gets a value indicating whether a route opts out of the inbound-credential stripping: its
        /// <see cref="ProxyMetadataKeys.ForwardInboundCredentials"/> metadata is a case-insensitive <c>true</c>.
        /// </summary>
        /// <param name="route">The route.</param>
        /// <returns><see langword="true"/> when the route forwards the caller's credentials.</returns>
        public static bool ForwardsInboundCredentials(RouteConfig route)
        {
            return IsTrue(route, ProxyMetadataKeys.ForwardInboundCredentials);
        }

        /// <summary>
        /// Gets a value indicating whether an <c>Anonymous</c> route may forward to a token-bearing cluster: its
        /// <see cref="ProxyMetadataKeys.AllowAnonymousWithToken"/> metadata is a case-insensitive <c>true</c>.
        /// </summary>
        /// <param name="route">The route.</param>
        /// <returns><see langword="true"/> when the route is a deliberately public route that carries a token.</returns>
        public static bool AllowsAnonymousWithToken(RouteConfig route)
        {
            return IsTrue(route, ProxyMetadataKeys.AllowAnonymousWithToken);
        }

        /// <summary>Only a case-insensitive <c>true</c> counts; any other value, padded ones included, is false.</summary>
        private static bool IsTrue(RouteConfig route, string key)
        {
            return route.Metadata is not null
                && route.Metadata.TryGetValue(key, out string? value)
                && string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase);
        }
    }
}
