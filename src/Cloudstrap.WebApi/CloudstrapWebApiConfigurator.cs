namespace Cloudstrap.WebApi
{
    using Asp.Versioning;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.OpenApi;
    using Microsoft.Extensions.DependencyInjection;
    using Scalar.AspNetCore;

    /// <summary>
    /// The code-level hooks carried by
    /// <see cref="WebApplicationBuilderExtensions.AddCloudstrapWebApi(Microsoft.AspNetCore.Builder.WebApplicationBuilder, System.Action{CloudstrapWebApiConfigurator}?)"/>,
    /// for the things configuration cannot express.
    /// </summary>
    /// <remarks>
    /// Every hook runs <em>after</em> the matching Cloudstrap defaults, so a hook always has the final say.
    /// </remarks>
    public sealed class CloudstrapWebApiConfigurator
    {
        /// <summary>
        /// Gets or sets the hook applied to the API versioning options after the Cloudstrap defaults — the
        /// place to add version readers or conventions.
        /// </summary>
        /// <value>The versioning hook, or <see langword="null"/> when the defaults are used as they are.</value>
        public Action<ApiVersioningOptions>? ApiVersioning
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook applied to the MVC JSON options after the Cloudstrap defaults.
        /// </summary>
        /// <value>The JSON hook, or <see langword="null"/> when the defaults are used as they are.</value>
        public Action<JsonOptions>? Json
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook applied to each generated OpenAPI document after the Cloudstrap defaults —
        /// the place to add document, operation or schema transformers.
        /// </summary>
        /// <value>The document hook, or <see langword="null"/> when the defaults are used as they are.</value>
        /// <remarks>
        /// It runs once per discovered API version, against that version's own document options.
        /// </remarks>
        public Action<OpenApiOptions>? OpenApi
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook applied to the reference UI after the Cloudstrap defaults — a straight
        /// passthrough to <c>Scalar.AspNetCore</c>'s own options.
        /// </summary>
        /// <value>The reference-UI hook, or <see langword="null"/> when the defaults are used as they are.</value>
        public Action<ScalarOptions>? Scalar
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook applied to the MVC builder — the place to add application parts, formatters
        /// or filters.
        /// </summary>
        /// <value>The MVC hook, or <see langword="null"/> when no extra MVC configuration is needed.</value>
        public Action<IMvcBuilder>? Mvc
        {
            get; set;
        }
    }
}
