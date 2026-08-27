namespace Cloudstrap.BlazorServer
{
    /// <summary>
    /// The interactivity a Cloudstrap Blazor Server application runs with. The decision is made once, on
    /// <see cref="CloudstrapBlazorServerConfigurator.Interactivity"/>, and the pipeline call follows it —
    /// there is deliberately no second knob on <see cref="BlazorServerPipelineOptions"/>.
    /// </summary>
    public enum BlazorInteractivity
    {
        /// <summary>
        /// Interactive Server rendering over a SignalR circuit: the interactive component services are
        /// registered and the component endpoints carry the Interactive Server render mode. The default.
        /// </summary>
        InteractiveServer,

        /// <summary>
        /// Static server-side rendering only: no circuit services, no interactive render mode. A component
        /// declaring <c>@rendermode InteractiveServer</c> fails with the framework's own error — the
        /// package adds no detection of its own.
        /// </summary>
        StaticServer,
    }
}
