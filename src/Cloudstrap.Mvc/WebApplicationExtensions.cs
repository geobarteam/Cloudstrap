namespace Cloudstrap.Mvc
{
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Builds the Cloudstrap MVC request pipeline.
    /// </summary>
    public static class WebApplicationExtensions
    {
        /// <summary>
        /// The key marking an application whose Cloudstrap MVC pipeline has already been built. Deliberately
        /// distinct from the Web API composite's marker, so a host calling both composites fails on this
        /// method's own double-call rule rather than a cross-package collision.
        /// </summary>
        private const string _pipelineMarker = "Cloudstrap.Mvc.Pipeline";

        /// <summary>
        /// The keys minimal hosting uses to decide whether it must insert the authentication and
        /// authorization middleware itself. It would place them <em>ahead of routing</em>, where no endpoint
        /// metadata is visible — which would make <c>[AllowAnonymous]</c> silently ineffective under a
        /// fallback policy. Claiming the keys hands placement to this method, which puts them after routing.
        /// The framework sets the same keys from <c>UseAuthentication</c> and <c>UseAuthorization</c>.
        /// </summary>
        private const string _authenticationMiddlewareMarker = "__AuthenticationMiddlewareSet";

        /// <inheritdoc cref="_authenticationMiddlewareMarker"/>
        private const string _authorizationMiddlewareMarker = "__AuthorizationMiddlewareSet";

        /// <summary>
        /// Adds the Cloudstrap MVC middleware and endpoints in the order a hardened, observable
        /// server-rendered application needs them.
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
        ///   <item><description>the error handling head: the developer exception page where selected, the
        ///   exception handler re-executing <c>Cloudstrap:Application:ExceptionHandlerPath</c>
        ///   otherwise,</description></item>
        ///   <item><description>HSTS, outside <c>Development</c> and when enabled,</description></item>
        ///   <item><description>the security-header middleware,</description></item>
        ///   <item><description>the path base, when <c>Cloudstrap:Application:PathBase</c> is set,</description></item>
        ///   <item><description>static files, when <see cref="MvcPipelineOptions.UseStaticFiles"/> is on,</description></item>
        ///   <item><description><see cref="MvcPipelineOptions.BeforeRouting"/>,</description></item>
        ///   <item><description>routing,</description></item>
        ///   <item><description>CORS, only when origins are configured,</description></item>
        ///   <item><description>correlation,</description></item>
        ///   <item><description>authentication, only when a scheme is registered,</description></item>
        ///   <item><description><see cref="MvcPipelineOptions.BeforeAuthorization"/>,</description></item>
        ///   <item><description>authorization, under the same condition as authentication,</description></item>
        ///   <item><description>session, when <c>Cloudstrap:Mvc:Session:Enabled</c> is on,</description></item>
        ///   <item><description>antiforgery,</description></item>
        ///   <item><description><see cref="MvcPipelineOptions.BeforeEndpoints"/>,</description></item>
        ///   <item><description>the conventional default route (attribute routes included), when
        ///   <see cref="MvcPipelineOptions.MapDefaultControllerRoute"/> is on,</description></item>
        ///   <item><description>health probes,</description></item>
        ///   <item><description><see cref="MvcPipelineOptions.ConfigureEndpoints"/>.</description></item>
        /// </list>
        /// <para>
        /// Correlation deliberately precedes authentication: every request that reaches routing carries an
        /// ambient correlation identifier, including the ones an authorization policy is about to reject, so
        /// a <c>401</c> is as traceable as a <c>200</c>.
        /// </para>
        /// <para>
        /// Authentication and authorization middleware appear exactly when an authentication scheme is
        /// registered — Cloudstrap's login package or one the consumer brought. There is no forced
        /// <c>RequireAuthorization()</c>: endpoint protection belongs to the auth package's fallback policy
        /// or your own attributes, and without a scheme every endpoint is anonymous.
        /// </para>
        /// <para>
        /// Forwarded headers are deliberately not configured here: behind a proxy, use the platform's
        /// <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> environment variable or place the middleware yourself
        /// in <see cref="MvcPipelineOptions.BeforeRouting"/> — never a silent library default.
        /// </para>
        /// <para>
        /// Every constituent piece stays independently callable — <c>UseStaticFiles</c>, <c>UseSession</c>,
        /// <c>UseCloudstrapCorrelation</c> and <c>MapCloudstrapHealthChecks</c> — so a consumer who needs a
        /// different order simply does not call this method.
        /// </para>
        /// </remarks>
        public static WebApplication UseCloudstrapMvc(
            this WebApplication app,
            Action<MvcPipelineOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(app);

            IDictionary<string, object?> properties = ((IApplicationBuilder)app).Properties;

            if (properties.ContainsKey(_pipelineMarker))
            {
                throw new InvalidOperationException(
                    $"{nameof(UseCloudstrapMvc)} has already been called on this application. A request "
                    + "pipeline is built exactly once.");
            }

            properties[_pipelineMarker] = true;

            MvcPipelineOptions hooks = new();
            configure?.Invoke(hooks);

            ApplicationOptions application = app.Services
                .GetRequiredService<IOptions<ApplicationOptions>>()
                .Value;
            CloudstrapMvcOptions mvc = app.Services
                .GetRequiredService<IOptions<CloudstrapMvcOptions>>()
                .Value;

            // The error handling head. When the developer page is selected it is the framework's: in
            // Development minimal hosting has already auto-inserted it as the outermost middleware, and
            // outside Development it is added explicitly here. Otherwise the exception handler answers —
            // Cloudstrap's negotiating handler terminally for JSON-preferring callers, a re-execution of
            // the consumer's page at Cloudstrap:Application:ExceptionHandlerPath for browsers. Sitting
            // inner to any auto-inserted developer page, it catches first, so that page never renders
            // when deselected (mechanic (i.3)).
            bool useDeveloperExceptionPage = EnvironmentDefault.Resolve(
                mvc.ExceptionHandling.UseDeveloperExceptionPage,
                app.Environment.IsDevelopment());

            if (useDeveloperExceptionPage)
            {
                if (!app.Environment.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }
            }
            else
            {
                app.UseExceptionHandler(new ExceptionHandlerOptions
                {
                    ExceptionHandlingPath = application.ExceptionHandlerPath,
                });
            }

            // Browsers only honour HSTS over HTTPS, and pinning a developer's localhost would be a nuisance
            // they have to clear by hand.
            if (mvc.Hsts.Enabled && !app.Environment.IsDevelopment())
            {
                app.UseHsts();
            }

            // Before the path base and before routing, so a short-circuited probe response carries them too.
            app.UseMiddleware<SecurityHeadersMiddleware>();

            if (!string.IsNullOrEmpty(application.PathBase))
            {
                app.UsePathBase(application.PathBase);
            }

            if (hooks.UseStaticFiles)
            {
                app.UseStaticFiles();
            }

            hooks.BeforeRouting?.Invoke(app);

            app.UseRouting();

            // Before correlation, so a preflight is answered by the CORS middleware and can never be
            // rejected for carrying no correlation identifier.
            if (mvc.Cors.AllowedOrigins.Count > 0)
            {
                app.UseCors();
            }

            // After routing, so endpoint metadata is visible; before authentication, so a request rejected by
            // an authorization policy is still correlated in the logs and in its problem-details response.
            app.UseCloudstrapCorrelation();

            // Whenever any authentication scheme is registered — Cloudstrap's login package or one the
            // consumer brought — the middleware belongs here, after routing. When none is, neither is added
            // and every endpoint is anonymous.
            //
            // The test is the registered scheme map, not the presence of IAuthenticationSchemeProvider: MVC
            // registers the authentication core services regardless, so the provider exists even in an
            // application that never configured a scheme.
            bool hasAuthentication = app.Services
                .GetService<IOptions<AuthenticationOptions>>()?.Value.SchemeMap.Count > 0;

            properties[_authenticationMiddlewareMarker] = true;
            properties[_authorizationMiddlewareMarker] = true;

            if (hasAuthentication)
            {
                app.UseAuthentication();
            }

            hooks.BeforeAuthorization?.Invoke(app);

            if (hasAuthentication)
            {
                app.UseAuthorization();
            }

            if (mvc.Session.Enabled)
            {
                app.UseSession();
            }

            app.UseAntiforgery();

            hooks.BeforeEndpoints?.Invoke(app);

            if (hooks.MapDefaultControllerRoute)
            {
                app.MapDefaultControllerRoute();
            }

            app.MapCloudstrapHealthChecks();

            hooks.ConfigureEndpoints?.Invoke(app);

            return app;
        }
    }
}
