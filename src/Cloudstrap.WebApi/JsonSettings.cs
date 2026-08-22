namespace Cloudstrap.WebApi
{
    /// <summary>
    /// JSON serialization settings, bound from the <c>Cloudstrap:WebApi:Json</c> configuration section.
    /// </summary>
    /// <remarks>
    /// Only the one opinion Cloudstrap holds is modelled here. Everything else is stock
    /// <c>System.Text.Json</c> web behavior, and the full serializer is reachable through the
    /// <see cref="CloudstrapWebApiConfigurator.Json"/> hook.
    /// </remarks>
    public sealed class JsonSettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether properties holding <see langword="null"/> are omitted from
        /// responses.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to omit null properties. Defaults to <see langword="true"/>; setting it to
        /// <see langword="false"/> restores the serializer's stock behavior of writing them.
        /// </value>
        public bool IgnoreNullValues { get; set; } = true;
    }
}
