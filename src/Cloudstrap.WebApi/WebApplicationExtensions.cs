namespace Cloudstrap.WebApi
{
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Builds the Cloudstrap Web API request pipeline.
    /// </summary>
    public static class WebApplicationExtensions
    {
        /// <summary>
        /// The key marking an application whose Cloudstrap pipeline has already been built.
        /// </summary>
        private const string _pipelineMarker = "Cloudstrap.WebApi.Pipeline";

        /// <summary>
        /// Adds the Cloudstrap Web API middleware and endpoints in the order a hardened, observable API
        /// needs them.
        /// </summary>
        /// <param name="app">The application to build the pipeline on.</param>
        /// <param name="configure">
        /// An optional hook placing the application's own middleware and endpoints inside the pipeline.
        /// </param>
        /// <returns>The same <paramref name="app"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The method has already been called on this application. Unlike the service registrations, which
        /// repeat safely, a request pipeline is built exactly once.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The order is the point of this call, and it is fixed:
        /// </para>
        /// <list type="number">
        ///   <item><description>the exception handler, in every environment,</description></item>
        ///   <item><description>HSTS, outside <c>Development</c> and when enabled,</description></item>
        ///   <item><description>the security-header middleware,</description></item>
        ///   <item><description>the path base, when <c>Cloudstrap:Application:PathBase</c> is set,</description></item>
        ///   <item><description><see cref="WebApiPipelineOptions.BeforeRouting"/>,</description></item>
        ///   <item><description>routing,</description></item>
        ///   <item><description>CORS, only when origins are configured,</description></item>
        ///   <item><description>correlation,</description></item>
        ///   <item><description>authentication, only when a scheme is registered,</description></item>
        ///   <item><description><see cref="WebApiPipelineOptions.BeforeAuthorization"/>,</description></item>
        ///   <item><description>authorization, under the same condition as authentication,</description></item>
        ///   <item><description><see cref="WebApiPipelineOptions.BeforeEndpoints"/>,</description></item>
        ///   <item><description>controllers, health probes and the API documentation endpoints,</description></item>
        ///   <item><description><see cref="WebApiPipelineOptions.ConfigureEndpoints"/>.</description></item>
        /// </list>
        /// <para>
        /// Correlation deliberately precedes authentication: every request that reaches routing carries an
        /// ambient correlation identifier, including the ones an authorization policy is about to reject, so
        /// a <c>401</c> is as traceable as a <c>200</c>.
        /// </para>
        /// <para>
        /// Every constituent piece stays independently callable — <c>AddCloudstrapJwtBearer</c>,
        /// <c>MapCloudstrapHealthChecks</c> and <c>UseCloudstrapCorrelation</c> — so a consumer who needs a
        /// different order simply does not call this method.
        /// </para>
        /// <para>
        /// A host that also serves a single-page application composes around the call rather than beside it:
        /// the framework's static-file middleware goes in
        /// <see cref="WebApiPipelineOptions.BeforeRouting"/> and the SPA fallback in
        /// <see cref="WebApiPipelineOptions.ConfigureEndpoints"/>, which leaves the API, the probes, the
        /// static files and the fallback all reachable.
        /// </para>
        /// </remarks>
        public static WebApplication UseCloudstrapWebApi(
            this WebApplication app,
            Action<WebApiPipelineOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(app);

            IDictionary<string, object?> properties = ((IApplicationBuilder)app).Properties;

            if (properties.ContainsKey(_pipelineMarker))
            {
                throw new InvalidOperationException(
                    $"{nameof(UseCloudstrapWebApi)} has already been called on this application. A request "
                    + "pipeline is built exactly once.");
            }

            properties[_pipelineMarker] = true;

            WebApiPipelineOptions hooks = new();
            configure?.Invoke(hooks);

            ApplicationOptions application = app.Services
                .GetRequiredService<IOptions<ApplicationOptions>>()
                .Value;
            WebApiOptions webApi = app.Services
                .GetRequiredService<IOptions<WebApiOptions>>()
                .Value;

            // First, in every environment: an unhandled exception must never escape as a stack-trace page.
            // The handler terminates rather than re-executing, so ApplicationOptions.ExceptionHandlerPath is
            // deliberately not consumed here.
            app.UseExceptionHandler();

            // Browsers only honour HSTS over HTTPS, and pinning a developer's localhost would be a nuisance
            // they have to clear by hand.
            if (webApi.Hsts.Enabled && !app.Environment.IsDevelopment())
            {
                app.UseHsts();
            }

            // Before the path base and before routing, so a short-circuited probe response carries them too.
            app.UseMiddleware<SecurityHeadersMiddleware>();

            if (!string.IsNullOrEmpty(application.PathBase))
            {
                app.UsePathBase(application.PathBase);
            }

            hooks.BeforeRouting?.Invoke(app);

            app.UseRouting();

            // Before correlation, so a preflight is answered by the CORS middleware and can never be
            // rejected for carrying no correlation identifier.
            if (webApi.Cors.AllowedOrigins.Count > 0)
            {
                app.UseCors();
            }

            // After routing, so endpoint metadata is visible; before authentication, so a request rejected by
            // an authorization policy is still correlated in the logs and in its problem-details response.
            app.UseCloudstrapCorrelation();

            hooks.BeforeAuthorization?.Invoke(app);

            hooks.BeforeEndpoints?.Invoke(app);

            if (hooks.MapControllers)
            {
                app.MapControllers();
            }

            app.MapCloudstrapHealthChecks();

            CloudstrapOpenApiOptions openApi = app.Services
                .GetRequiredService<IOptions<CloudstrapOpenApiOptions>>()
                .Value;

            if (openApi.Enabled)
            {
                // Anonymous by design: a require-authenticated fallback policy must not lock the API
                // description out of the reference UI that exists to render it. Consumers who want the
                // description protected switch it off in configuration or map it themselves.
                app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();

                CloudstrapScalarOptions scalar = app.Services
                    .GetRequiredService<IOptions<CloudstrapScalarOptions>>()
                    .Value;

                if (EnvironmentDefault.Resolve(scalar.Enabled, app.Environment.IsDevelopment()))
                {
                    ScalarRegistration.Map(app, scalar, openApi, application);
                }
            }

            hooks.ConfigureEndpoints?.Invoke(app);

            return app;
        }
    }
}
