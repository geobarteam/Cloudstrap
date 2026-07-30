namespace Cloudstrap.Observability
{
    /// <summary>
    /// The shared health-check tag vocabulary: hosting packages register checks with these tags, and probe
    /// endpoints filter on them, so liveness and readiness agree across every Cloudstrap package.
    /// </summary>
    public static class CloudstrapHealthCheckTags
    {
        /// <summary>
        /// The tag selecting checks served by the liveness probe.
        /// </summary>
        public const string Liveness = "live";

        /// <summary>
        /// The tag selecting checks served by the readiness probe.
        /// </summary>
        public const string Readiness = "ready";
    }
}
