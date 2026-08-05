namespace Cloudstrap.Observability
{
    /// <summary>
    /// The activity source names Cloudstrap emits spans from, published so consumers who own their own
    /// OpenTelemetry pipeline can subscribe with <c>AddSource</c> themselves.
    /// </summary>
    public static class CloudstrapActivitySources
    {
        /// <summary>
        /// The source business spans recorded through <see cref="IBusinessTrace"/> are emitted from.
        /// Cloudstrap-managed pipelines — owner and contribute — subscribe to it automatically.
        /// </summary>
        public const string Business = "Cloudstrap.Business";
    }
}
