namespace Cloudstrap.Mvc
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;

    /// <summary>
    /// The code-level hooks carried by
    /// <see cref="WebApplicationExtensions.UseCloudstrapMvc(WebApplication, System.Action{MvcPipelineOptions}?)"/>,
    /// for placing an application's own middleware and endpoints inside the Cloudstrap pipeline.
    /// </summary>
    /// <remarks>
    /// The hooks exist because middleware <em>order</em> is the knowledge this package encodes: an
    /// application that needs its own middleware still wants Cloudstrap's ordering around it. A consumer who
    /// wants full control skips the composite entirely — every constituent piece stays independently
    /// callable.
    /// </remarks>
    public sealed class MvcPipelineOptions
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
        /// Gets or sets the middleware added between authentication and authorization — the slot for anything
        /// that must see the authenticated principal before the authorization decision is taken.
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
        /// Gets or sets the endpoint registrations added after the Cloudstrap endpoints — the slot for
        /// minimal APIs, hubs, Razor Pages, or <c>MapStaticAssets</c> adopters' asset endpoints.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when no extra endpoints are mapped.</value>
        public Action<IEndpointRouteBuilder>? ConfigureEndpoints
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the pipeline maps the conventional default route
        /// <c>{controller=Home}/{action=Index}/{id?}</c> — which also maps every attribute-routed controller.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to map controllers over the conventional default route. Defaults to
        /// <see langword="true"/>; set it to <see langword="false"/> in a host that maps its own routes
        /// through <see cref="ConfigureEndpoints"/> — a host doing so and mapping nothing there serves
        /// static files and health probes only, which is a valid, deliberate configuration.
        /// </value>
        public bool MapDefaultControllerRoute { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the pipeline serves static files from the web root.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to serve <c>wwwroot</c> through the framework's static-file middleware.
        /// Defaults to <see langword="true"/>; set it to <see langword="false"/> in a host adopting
        /// endpoint-routed <c>MapStaticAssets</c> through <see cref="ConfigureEndpoints"/> instead.
        /// </value>
        public bool UseStaticFiles { get; set; } = true;
    }
}
