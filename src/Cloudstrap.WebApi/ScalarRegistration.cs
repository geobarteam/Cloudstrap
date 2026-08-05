namespace Cloudstrap.WebApi
{
    using Asp.Versioning.ApiExplorer;
    using Cloudstrap.Core;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using Scalar.AspNetCore;

    /// <summary>
    /// Maps the Scalar reference UI over every discovered API version's document.
    /// </summary>
    internal static class ScalarRegistration
    {
        /// <summary>
        /// The security scheme name the published documents declare, which the UI's sign-in must match.
        /// </summary>
        private const string _securitySchemeName = "Bearer";

        /// <summary>
        /// Maps the reference UI.
        /// </summary>
        /// <param name="app">The application to map into.</param>
        /// <param name="scalar">The bound reference-UI settings.</param>
        /// <param name="openApi">The bound document settings, for the shared title.</param>
        /// <param name="application">The application identity backing the derived title.</param>
        public static void Map(
            WebApplication app,
            CloudstrapScalarOptions scalar,
            CloudstrapOpenApiOptions openApi,
            ApplicationOptions application)
        {
            IApiVersionDescriptionProvider versions = app.Services
                .GetRequiredService<IApiVersionDescriptionProvider>();
            string[] documents = [.. versions.ApiVersionDescriptions.Select(version => version.GroupName)];
            Action<ScalarOptions>? hook = app.Services.GetService<ScalarConfigurator>()?.Configure;

            app.MapScalarApiReference(
                    scalar.Path,
                    options =>
                    {
                        options.Title = OpenApiRegistration.ResolveTitle(openApi, application);
                        options.AddDocuments(documents);

                        if (!string.IsNullOrEmpty(scalar.OAuth.ClientId))
                        {
                            ConfigureSignIn(options, scalar, openApi);
                        }

                        // Last, so a consumer hook overrides anything above it.
                        hook?.Invoke(options);
                    })

                // Anonymous by design: the page exists to read the API description, so a require-authenticated
                // fallback policy must not answer it with a challenge the reader cannot satisfy yet.
                .AllowAnonymous();
        }

        /// <summary>
        /// Wires the reference UI's sign-in to the documented OAuth flow.
        /// </summary>
        /// <param name="options">The reference-UI options being built.</param>
        /// <param name="scalar">The bound reference-UI settings supplying the public client id.</param>
        /// <param name="openApi">The bound document settings supplying the flow's endpoints.</param>
        /// <remarks>
        /// The authorization code flow with PKCE is the only flow a page running in a browser can complete
        /// without holding a secret, so it is the only one Cloudstrap wires. A consumer who needs another
        /// flow reaches for the reference-UI hook and accepts what that implies.
        /// </remarks>
        private static void ConfigureSignIn(
            ScalarOptions options,
            CloudstrapScalarOptions scalar,
            CloudstrapOpenApiOptions openApi)
        {
            options.AddAuthorizationCodeFlow(_securitySchemeName, flow =>
            {
                flow.ClientId = scalar.OAuth.ClientId;
                flow.Pkce = Pkce.Sha256;

                if (openApi.OAuth.AuthorizationUrl is not null)
                {
                    flow.AuthorizationUrl = openApi.OAuth.AuthorizationUrl.ToString();
                }

                if (openApi.OAuth.TokenUrl is not null)
                {
                    flow.TokenUrl = openApi.OAuth.TokenUrl.ToString();
                }

                if (scalar.OAuth.SelectedScopes.Count > 0)
                {
                    flow.SelectedScopes = [.. scalar.OAuth.SelectedScopes];
                }
            });
        }
    }
}
