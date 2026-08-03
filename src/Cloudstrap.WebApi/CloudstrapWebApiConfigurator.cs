namespace Cloudstrap.WebApi
{
    using Asp.Versioning;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.DependencyInjection;

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
