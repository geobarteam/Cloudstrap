namespace Cloudstrap.BlazorServer
{
    using System.Reflection;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;

    /// <summary>
    /// The code-level hooks carried by
    /// <see cref="WebApplicationExtensions.UseCloudstrapBlazorServer{TRootComponent}(WebApplication, System.Action{BlazorServerPipelineOptions}?)"/>,
    /// for placing an application's own middleware and endpoints inside the Cloudstrap pipeline.
    /// </summary>
    /// <remarks>
    /// The hooks exist because middleware <em>order</em> is the knowledge this package encodes: an
    /// application that needs its own middleware still wants Cloudstrap's ordering around it. A consumer
    /// who wants full control skips the composite entirely — every constituent piece stays independently
    /// callable. There is deliberately no render-mode knob here: interactivity is decided once, on
    /// <see cref="CloudstrapBlazorServerConfigurator.Interactivity"/>.
    /// </remarks>
    public sealed class BlazorServerPipelineOptions
    {
        /// <summary>
        /// Gets or sets the middleware added immediately before routing — the slot for anything that should
        /// short-circuit before endpoint selection, such as a security-headers bundle or a rewrite rule.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when nothing is added there.</value>
        public Action<IApplicationBuilder>? BeforeRouting
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the middleware added between authentication and authorization — the slot for
        /// anything that must see the authenticated principal before the authorization decision is taken.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when nothing is added there.</value>
        /// <remarks>
        /// When no authentication scheme is registered, no authentication or authorization middleware is
        /// added and this hook runs in their place, keeping its position in the order.
        /// </remarks>
        public Action<IApplicationBuilder>? BeforeAuthorization
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the middleware added immediately before the endpoints are mapped.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when nothing is added there.</value>
        public Action<IApplicationBuilder>? BeforeEndpoints
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook applied to the component endpoint convention builder returned by
        /// <c>MapRazorComponents</c>, <em>after</em> every Cloudstrap convention — final say. This is the
        /// seam a consumer-referenced Interactive WebAssembly render mode attaches through (D-7), and the
        /// place for any other component-endpoint metadata.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when no extra conventions are added.</value>
        public Action<RazorComponentsEndpointConventionBuilder>? ConfigureComponentEndpoints
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the endpoint registrations added after the Cloudstrap endpoints — the slot for
        /// minimal APIs, hubs, or a login package's authentication endpoints.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when no extra endpoints are mapped.</value>
        public Action<IEndpointRouteBuilder>? ConfigureEndpoints
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the pipeline maps the build-time static-asset endpoints
        /// through the framework's <c>MapStaticAssets</c>.
        /// </summary>
        /// <value>
        /// <see langword="true"/> by default. Set it to <see langword="false"/> in a host serving assets
        /// some other way — or one whose build produces no static-asset manifest.
        /// </value>
        public bool MapStaticAssets { get; set; } = true;

        /// <summary>
        /// Gets the additional assemblies whose routable components join the root component's assembly —
        /// the composite's replacement for any registration-based component catalog.
        /// </summary>
        /// <value>Empty by default.</value>
        /// <remarks>
        /// Entries are passed through to the framework's <c>AddAdditionalAssemblies</c> as they are;
        /// duplicate entries follow the framework's own semantics.
        /// </remarks>
        public IList<Assembly> AdditionalAssemblies { get; } = [];
    }
}
