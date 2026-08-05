namespace Cloudstrap.WebApi
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Routing;

    /// <summary>
    /// The code-level hooks carried by
    /// <see cref="WebApplicationExtensions.UseCloudstrapWebApi(WebApplication, System.Action{WebApiPipelineOptions}?)"/>,
    /// for placing an application's own middleware and endpoints inside the Cloudstrap pipeline.
    /// </summary>
    /// <remarks>
    /// The hooks exist because middleware <em>order</em> is the knowledge this package encodes: an
    /// application that needs its own middleware still wants Cloudstrap's ordering around it. A consumer who
    /// wants full control skips the composite entirely — every constituent piece stays independently
    /// callable.
    /// </remarks>
    public sealed class WebApiPipelineOptions
    {
        /// <summary>
        /// Gets or sets the middleware added immediately before routing — the slot for static files, a SPA
        /// framework's files, or anything that should short-circuit before endpoint selection.
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
        /// minimal APIs, hubs, or a SPA fallback.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> when no extra endpoints are mapped.</value>
        public Action<IEndpointRouteBuilder>? ConfigureEndpoints
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the pipeline maps attribute-routed controllers.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to map controllers. Defaults to <see langword="true"/>; set it to
        /// <see langword="false"/> in a host that serves minimal APIs only and maps them through
        /// <see cref="ConfigureEndpoints"/>.
        /// </value>
        public bool MapControllers { get; set; } = true;
    }
}
