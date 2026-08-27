namespace Cloudstrap.BlazorServer
{
    /// <summary>
    /// The registration-time decisions the pipeline call follows, resolved once in
    /// <see cref="WebApplicationBuilderExtensions.AddCloudstrapBlazorServer(Microsoft.AspNetCore.Builder.WebApplicationBuilder, System.Action{CloudstrapBlazorServerConfigurator}?)"/>.
    /// Its absence from the container doubles as the missing-<c>Add</c> detection.
    /// </summary>
    /// <param name="interactivity">The interactivity decided at registration time.</param>
    internal sealed class BlazorServerRegistrationState(BlazorInteractivity interactivity)
    {
        /// <summary>
        /// Gets the interactivity decided at registration time.
        /// </summary>
        public BlazorInteractivity Interactivity { get; } = interactivity;
    }
}
