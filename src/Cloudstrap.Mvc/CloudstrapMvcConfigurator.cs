namespace Cloudstrap.Mvc
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// The code-level hooks carried by
    /// <see cref="WebApplicationBuilderExtensions.AddCloudstrapMvc(Microsoft.AspNetCore.Builder.WebApplicationBuilder, System.Action{CloudstrapMvcConfigurator}?)"/>,
    /// for the things configuration cannot express.
    /// </summary>
    /// <remarks>
    /// Every hook runs <em>after</em> the matching Cloudstrap defaults, so a hook always has the final say.
    /// </remarks>
    public sealed class CloudstrapMvcConfigurator
    {
        /// <summary>
        /// Gets or sets the hook applied to the MVC builder — the place to add application parts, formatters
        /// or filters.
        /// </summary>
        /// <value>The MVC hook, or <see langword="null"/> when no extra MVC configuration is needed.</value>
        public Action<IMvcBuilder>? Mvc
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook applied to the framework's <see cref="SessionOptions"/> <em>after</em> the
        /// Cloudstrap defaults and the <c>Cloudstrap:Mvc:Session</c> configuration — full access, final say.
        /// </summary>
        /// <value>The session hook, or <see langword="null"/> when the defaults are used as they are.</value>
        /// <remarks>
        /// The override ladder is: hardened defaults → <c>Cloudstrap:Mvc:Session</c> → this hook. It runs
        /// only when session state is enabled; disabling it removes session entirely instead.
        /// </remarks>
        public Action<SessionOptions>? Session
        {
            get; set;
        }
    }
}
