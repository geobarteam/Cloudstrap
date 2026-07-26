namespace Cloudstrap.Core
{
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// Describes the identity of the hosted application: who owns it, what it is, and how it is
    /// addressed. Bound from the <c>Cloudstrap:Application</c> configuration section.
    /// </summary>
    public sealed class ApplicationOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Application";

        private string _pathBase = string.Empty;
        private string? _workloadName;

        /// <summary>
        /// Gets or sets the name of the business system the application belongs to (for example <c>Contoso</c>).
        /// </summary>
        /// <value>The business system name. Required.</value>
        [Required]
        public string SystemName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the subsystem within <see cref="SystemName"/> (for example <c>Orders</c>).
        /// </summary>
        /// <value>The subsystem name. Required.</value>
        [Required]
        public string SubsystemName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the kind of workload the subsystem is (for example <c>Api</c>, <c>Worker</c> or <c>Web</c>).
        /// </summary>
        /// <value>The subsystem type. Required.</value>
        [Required]
        public string SubsystemType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the workload name used by conventions such as queue names, resource prefixes and
        /// telemetry service names. When no explicit value is configured it is computed as the lowercase
        /// <c>{SystemName}-{SubsystemName}-{SubsystemType}</c>; an explicitly configured value wins verbatim.
        /// </summary>
        /// <value>The workload name.</value>
        public string WorkloadName
        {
            get
            {
                return string.IsNullOrEmpty(_workloadName)
                    ? $"{SystemName}-{SubsystemName}-{SubsystemType}".ToLowerInvariant()
                    : _workloadName;
            }

            set
            {
                _workloadName = value;
            }
        }

        /// <summary>
        /// Gets or sets an optional free-form environment tier label, for organizations whose tiers do not map
        /// onto the standard ASP.NET Core environments. Cloudstrap attaches no behavior to this value; consuming
        /// packages may key explicit options off it.
        /// </summary>
        /// <value>The environment tier label, or <see langword="null"/> when not configured.</value>
        public string? EnvironmentTier
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the path base the application is hosted under. The value is normalized to a single
        /// leading slash without a trailing slash (<c>myapp</c> and <c>/myapp/</c> both yield <c>/myapp</c>);
        /// an empty value yields an empty string.
        /// </summary>
        /// <value>The normalized path base, or an empty string when the application is hosted at the root.</value>
        public string PathBase
        {
            get
            {
                return _pathBase;
            }

            set
            {
                _pathBase = string.IsNullOrEmpty(value) ? string.Empty : $"/{value.Trim('/')}";
            }
        }

        /// <summary>
        /// Gets or sets the path the exception handler middleware re-executes on for unhandled exceptions.
        /// </summary>
        /// <value>The exception handler path. Defaults to <c>/error</c>.</value>
        public string ExceptionHandlerPath { get; set; } = "/error";
    }
}
