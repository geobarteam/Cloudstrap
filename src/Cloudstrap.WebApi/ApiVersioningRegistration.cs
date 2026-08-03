namespace Cloudstrap.WebApi
{
    using Asp.Versioning;
    using Asp.Versioning.Conventions;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Registers API versioning with the Cloudstrap defaults: the configured default version assumed for
    /// unversioned requests and unattributed controllers, supported versions reported, and the two stock
    /// readers (query string and URL segment) live.
    /// </summary>
    internal static class ApiVersioningRegistration
    {
        /// <summary>
        /// Adds and configures API versioning, the MVC conventions and the API explorer.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="options">The bound Web API settings.</param>
        /// <param name="configure">The consumer hook, invoked after the Cloudstrap defaults.</param>
        public static void Configure(
            IServiceCollection services,
            WebApiOptions options,
            Action<ApiVersioningOptions>? configure)
        {
            ApiVersion defaultVersion = ResolveDefaultVersion(options.ApiVersioning.DefaultVersion);

            services.AddApiVersioning(versioning =>
                {
                    versioning.DefaultApiVersion = defaultVersion;
                    versioning.AssumeDefaultVersionWhenUnspecified =
                        options.ApiVersioning.AssumeDefaultVersionWhenUnspecified;
                    versioning.ReportApiVersions = options.ApiVersioning.ReportApiVersions;
                    versioning.ApiVersionReader = ApiVersionReader.Combine(
                        new QueryStringApiVersionReader(),
                        new UrlSegmentApiVersionReader());

                    configure?.Invoke(versioning);
                })
                .AddMvc(mvc =>
                {
                    // A controller in a versioned namespace takes its version from that namespace…
                    mvc.Conventions.Add(new VersionByNamespaceConvention());

                    // …and one with no version metadata at all takes the configured default.
                    mvc.Conventions.Add(new DefaultApiVersionConvention(defaultVersion));
                })
                .AddApiExplorer(explorer =>
                {
                    explorer.GroupNameFormat = "'v'VVV";
                    explorer.SubstituteApiVersionInUrl = true;
                });
        }

        /// <summary>
        /// Parses the configured default version, falling back to <c>1.0</c> when it does not parse.
        /// </summary>
        /// <param name="value">The configured default version.</param>
        /// <returns>The parsed version, or <c>1.0</c> when parsing failed.</returns>
        /// <remarks>
        /// An unparsable value is a configuration error reported by <see cref="WebApiOptionsValidator"/> at
        /// host startup, naming the key. Falling back here keeps that message the one the operator sees,
        /// instead of a parse exception thrown from a registration call.
        /// </remarks>
        private static ApiVersion ResolveDefaultVersion(string value)
        {
            return ApiVersionParser.Default.TryParse(value, out ApiVersion? parsed) && parsed is not null
                ? parsed
                : new ApiVersion(1, 0);
        }
    }
}
