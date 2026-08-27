namespace Cloudstrap.BlazorServer
{
    /// <summary>
    /// The activity source names the Cloudstrap Blazor Server package emits spans from, published so
    /// consumers who own their own OpenTelemetry pipeline can subscribe with <c>AddSource</c> themselves.
    /// </summary>
    public static class BlazorServerActivitySources
    {
        /// <summary>
        /// The source interaction root spans started through <see cref="IBlazorInteractionTrace"/> are
        /// emitted from. <c>AddCloudstrapBlazorServer</c> contributes it to any pipeline built from the
        /// application's service collection automatically.
        /// </summary>
        public const string Interaction = "Cloudstrap.BlazorServer.Interaction";
    }
}
