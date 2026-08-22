namespace Cloudstrap.WebApi
{
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.OpenApi;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using Microsoft.OpenApi;

    /// <summary>
    /// Makes the published documents describe the authentication the pipeline actually enforces: a bearer
    /// security scheme, and a requirement on every operation that is not anonymous.
    /// </summary>
    /// <param name="options">The bound document settings supplying the documented flow's URLs and scopes.</param>
    internal sealed class OpenApiSecurityTransformer(CloudstrapOpenApiOptions options)
        : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
    {
        /// <summary>
        /// The name the security scheme is published under, and the name operations reference.
        /// </summary>
        private const string _schemeName = "Bearer";

        /// <summary>
        /// Adds the security scheme to a document, when the application takes tokens at all.
        /// </summary>
        /// <param name="document">The document being generated.</param>
        /// <param name="context">The generation context.</param>
        /// <param name="cancellationToken">A token that cancels the transformation.</param>
        /// <returns>A completed task.</returns>
        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(context);

            if (!AuthenticationIsRegistered(context.ApplicationServices))
            {
                return Task.CompletedTask;
            }

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(
                StringComparer.Ordinal);
            document.Components.SecuritySchemes[_schemeName] = BuildScheme();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Attaches a security requirement to an operation, unless its endpoint is anonymous.
        /// </summary>
        /// <param name="operation">The operation being generated.</param>
        /// <param name="context">The generation context.</param>
        /// <param name="cancellationToken">A token that cancels the transformation.</param>
        /// <returns>A completed task.</returns>
        public Task TransformAsync(
            OpenApiOperation operation,
            OpenApiOperationTransformerContext context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(context);

            if (!AuthenticationIsRegistered(context.ApplicationServices) || IsAnonymous(context))
            {
                return Task.CompletedTask;
            }

            OpenApiSecurityRequirement requirement = new()
            {
                [new OpenApiSecuritySchemeReference(_schemeName, context.Document, externalResource: null)] =
                    [.. options.OAuth.Scopes.Keys],
            };

            operation.Security ??= [];
            operation.Security.Add(requirement);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Determines whether the application registered any authentication scheme.
        /// </summary>
        /// <param name="services">The application's services.</param>
        /// <returns><see langword="true"/> when at least one scheme is registered.</returns>
        /// <remarks>
        /// Asked at generation time rather than registration time, because <c>AddCloudstrapJwtBearer</c> may
        /// be called after <c>AddCloudstrapWebApi</c> — the documents must describe the application as it
        /// finally is, not as it was halfway through being composed.
        /// </remarks>
        private static bool AuthenticationIsRegistered(IServiceProvider services)
        {
            return services.GetService<IOptions<AuthenticationOptions>>()?.Value.SchemeMap.Count > 0;
        }

        /// <summary>
        /// Determines whether an operation's endpoint opted out of authorization.
        /// </summary>
        /// <param name="context">The generation context carrying the endpoint's metadata.</param>
        /// <returns><see langword="true"/> when the endpoint is anonymous.</returns>
        private static bool IsAnonymous(OpenApiOperationTransformerContext context)
        {
            return context.Description.ActionDescriptor.EndpointMetadata
                .OfType<IAllowAnonymous>()
                .Any();
        }

        /// <summary>
        /// Builds the security scheme from the configured flow.
        /// </summary>
        /// <returns>
        /// An OAuth2 scheme describing the authorization code flow when URLs are configured; otherwise a
        /// plain HTTP bearer scheme. No URL is ever derived from the identity provider's authority.
        /// </returns>
        private OpenApiSecurityScheme BuildScheme()
        {
            if (options.OAuth.TokenUrl is null && options.OAuth.AuthorizationUrl is null)
            {
                return new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "A JSON Web Token issued by the configured identity provider.",
                };
            }

            return new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description = "A JSON Web Token issued by the configured identity provider.",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = options.OAuth.AuthorizationUrl,
                        TokenUrl = options.OAuth.TokenUrl,
                        Scopes = new Dictionary<string, string>(options.OAuth.Scopes, StringComparer.Ordinal),
                    },
                },
            };
        }
    }
}
