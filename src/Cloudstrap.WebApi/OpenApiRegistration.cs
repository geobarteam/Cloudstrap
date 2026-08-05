namespace Cloudstrap.WebApi
{
    using Asp.Versioning;
    using Cloudstrap.Core;
    using Microsoft.AspNetCore.OpenApi;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Registers one OpenAPI document per discovered API version, with neutral metadata derived from the
    /// application's identity.
    /// </summary>
    /// <remarks>
    /// The per-version behavior comes from <c>Asp.Versioning.OpenApi</c>, which bridges the API explorer's
    /// version descriptions to the built-in document generator. This package therefore owns <strong>no</strong>
    /// version-filtering transformer of its own.
    /// </remarks>
    internal static class OpenApiRegistration
    {
        /// <summary>
        /// Adds the versioned OpenAPI documents to an API versioning registration.
        /// </summary>
        /// <param name="versioning">The API versioning builder to extend.</param>
        /// <param name="options">The bound document settings.</param>
        /// <param name="application">The application identity the neutral defaults are derived from.</param>
        /// <param name="configure">The consumer hook, invoked per generated document after the defaults.</param>
        public static void Configure(
            IApiVersioningBuilder versioning,
            CloudstrapOpenApiOptions options,
            ApplicationOptions application,
            Action<OpenApiOptions>? configure)
        {
            string title = ResolveTitle(options, application);
            string description = options.Description ?? DescribeApplication(application);

            versioning.AddOpenApi(versioned =>
            {
                versioned.Document.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Info.Title = title;
                    document.Info.Description = description;

                    return Task.CompletedTask;
                });

                OpenApiSecurityTransformer security = new(options);
                versioned.Document.AddDocumentTransformer(security);
                versioned.Document.AddOperationTransformer(security);

                // Registered after the Cloudstrap defaults, so a consumer transformer wins.
                configure?.Invoke(versioned.Document);
            });
        }

        /// <summary>
        /// Resolves the document title, so the documents and the reference UI always agree on it.
        /// </summary>
        /// <param name="options">The bound document settings.</param>
        /// <param name="application">The application identity the neutral default is derived from.</param>
        /// <returns>The configured title, or the workload-derived default.</returns>
        public static string ResolveTitle(CloudstrapOpenApiOptions options, ApplicationOptions application)
        {
            return options.Title ?? $"{application.WorkloadName} API";
        }

        /// <summary>
        /// Builds the neutral description used when none is configured.
        /// </summary>
        /// <param name="application">The application identity.</param>
        /// <returns>A one-sentence description naming the system, subsystem and workload kind.</returns>
        private static string DescribeApplication(ApplicationOptions application)
        {
            return $"The {application.SubsystemType} interface of the {application.SubsystemName} subsystem "
                + $"in the {application.SystemName} system.";
        }
    }
}
