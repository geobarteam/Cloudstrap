namespace Cloudstrap.BlazorCommon
{
    using System.Reflection;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Knobs for the
    /// <see cref="ServiceCollectionExtensions.AddCloudstrapBlazorCommon{TAssemblyMarker}"/>
    /// convention scan.
    /// </summary>
    /// <remarks>
    /// This is a code-level knob object consumed at the call — it is never registered in the
    /// container and never bound to <c>IConfiguration</c>; no <c>Cloudstrap:</c> configuration
    /// section exists for this package.
    /// </remarks>
    public sealed class BlazorCommonOptions
    {
        /// <summary>
        /// Gets the class-name suffixes the scan matches (ordinal comparison). Defaults to
        /// <c>ViewModel</c> and <c>Service</c>; replace the contents to change the conventions, or
        /// clear the list to scan nothing.
        /// </summary>
        public IList<string> ConventionSuffixes { get; } = ["ViewModel", "Service"];

        /// <summary>
        /// Gets or sets the lifetime applied to every convention registration.
        /// </summary>
        /// <value>Defaults to <see cref="ServiceLifetime.Transient"/>.</value>
        public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;

        /// <summary>
        /// Gets the assemblies scanned in addition to the marker type's assembly.
        /// </summary>
        public IList<Assembly> AdditionalAssemblies { get; } = [];
    }
}
