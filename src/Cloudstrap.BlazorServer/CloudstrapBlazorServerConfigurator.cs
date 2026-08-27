namespace Cloudstrap.BlazorServer
{
    using Microsoft.AspNetCore.Antiforgery;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// The code-level hooks carried by
    /// <see cref="WebApplicationBuilderExtensions.AddCloudstrapBlazorServer(Microsoft.AspNetCore.Builder.WebApplicationBuilder, System.Action{CloudstrapBlazorServerConfigurator}?)"/>,
    /// for the things configuration cannot express.
    /// </summary>
    /// <remarks>
    /// Every hook runs <em>after</em> the matching Cloudstrap defaults, so a hook always has the final say.
    /// </remarks>
    public sealed class CloudstrapBlazorServerConfigurator
    {
        /// <summary>
        /// Gets or sets the interactivity the application runs with. The decision is made here, once, and
        /// <see cref="WebApplicationExtensions.UseCloudstrapBlazorServer{TRootComponent}(Microsoft.AspNetCore.Builder.WebApplication, System.Action{BlazorServerPipelineOptions}?)"/>
        /// follows it — the pipeline call has no render-mode knob of its own.
        /// </summary>
        /// <value><see cref="BlazorInteractivity.InteractiveServer"/> by default.</value>
        public BlazorInteractivity Interactivity { get; set; } = BlazorInteractivity.InteractiveServer;

        /// <summary>
        /// Gets or sets the hook applied to the framework's <see cref="AntiforgeryOptions"/> <em>after</em>
        /// the Cloudstrap hardening — full access, final say.
        /// </summary>
        /// <value>The antiforgery hook, or <see langword="null"/> when the hardened defaults stand.</value>
        /// <remarks>
        /// The hardened defaults are an <c>HttpOnly</c> cookie, <c>SecurePolicy=Always</c> and
        /// <c>SameSite=Strict</c>. The override ladder is: hardened defaults → this hook.
        /// </remarks>
        public Action<AntiforgeryOptions>? Antiforgery
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook applied to the razor-components builder <em>after</em> the Cloudstrap
        /// registrations — the place to configure circuit options, add Interactive WebAssembly components
        /// from a consumer-referenced package, or attach any other component-service configuration.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when no extra component configuration is needed.</value>
        public Action<IRazorComponentsBuilder>? RazorComponents
        {
            get; set;
        }
    }
}
