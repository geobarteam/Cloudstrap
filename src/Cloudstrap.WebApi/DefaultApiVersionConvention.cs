namespace Cloudstrap.WebApi
{
    using Asp.Versioning;
    using Asp.Versioning.Conventions;
    using Microsoft.AspNetCore.Mvc.ApplicationModels;

    /// <summary>
    /// Assigns the default API version to controllers that carry no version metadata at all — neither an
    /// <c>[ApiVersion]</c>-style attribute nor a version-neutral marker, and no namespace convention match.
    /// </summary>
    /// <remarks>
    /// Without this convention an unattributed controller belongs to no version and is unreachable, which
    /// makes adding versioning to an existing API a breaking change. With it, an existing controller keeps
    /// answering under the configured default version.
    /// </remarks>
    /// <param name="defaultVersion">The API version assigned to unversioned controllers.</param>
    internal sealed class DefaultApiVersionConvention(ApiVersion defaultVersion) : IControllerConvention
    {
        /// <summary>
        /// Applies the convention to one controller.
        /// </summary>
        /// <param name="builder">The controller convention builder.</param>
        /// <param name="controller">The controller model being configured.</param>
        /// <returns>
        /// <see langword="true"/> when the default version was assigned; <see langword="false"/> when the
        /// controller already declares its own versioning.
        /// </returns>
        public bool Apply(IControllerConventionBuilder builder, ControllerModel controller)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(controller);

            if (controller.Attributes.OfType<IApiVersionProvider>().Any()
                || controller.Attributes.OfType<IApiVersionNeutral>().Any())
            {
                return false;
            }

            builder.HasApiVersion(defaultVersion);

            return true;
        }
    }
}
