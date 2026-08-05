namespace Cloudstrap.Observability
{
    using Cloudstrap.Core;
    using Microsoft.AspNetCore.Http;

    /// <summary>
    /// The default trace noise predicates: probe endpoints (from <see cref="HealthChecksOptions"/>, never
    /// hard-coded), Blazor infrastructure paths, static assets and consumer-configured segments are dropped;
    /// everything else is traced.
    /// </summary>
    internal static class TraceNoiseFilter
    {
        private static readonly string[] _noisePathSegments = ["/_blazor", "/_framework/", "/_content/"];

        private static readonly HashSet<string> _staticAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".css", ".js", ".map", ".ico", ".png", ".jpg", ".svg", ".gif", ".woff", ".woff2",
        };

        /// <summary>
        /// Decides whether an inbound request is traced: probe paths, Blazor infrastructure, static assets
        /// and ignored segments are dropped.
        /// </summary>
        public static bool ShouldTrace(
            HttpContext context,
            HealthChecksOptions healthChecks,
            IList<string> ignoredPathSegments)
        {
            string path = context.Request.Path.Value ?? string.Empty;

            if (PathEquals(path, healthChecks.LivenessPath) || PathEquals(path, healthChecks.ReadinessPath))
            {
                return false;
            }

            return ShouldTracePath(path, ignoredPathSegments);
        }

        /// <summary>
        /// Decides whether an outbound HTTP request is traced: static assets and ignored segments are dropped.
        /// </summary>
        public static bool ShouldTrace(HttpRequestMessage request, IList<string> ignoredPathSegments)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;

            return ShouldTracePath(path, ignoredPathSegments);
        }

        private static bool ShouldTracePath(string path, IList<string> ignoredPathSegments)
        {
            foreach (string segment in _noisePathSegments)
            {
                if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (_staticAssetExtensions.Contains(Path.GetExtension(path)))
            {
                return false;
            }

            foreach (string segment in ignoredPathSegments)
            {
                if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PathEquals(string path, string probePath) =>
            string.Equals(path, probePath, StringComparison.OrdinalIgnoreCase);
    }
}
