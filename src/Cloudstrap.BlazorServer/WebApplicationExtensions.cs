namespace Cloudstrap.BlazorServer
{
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Components;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Builds the Cloudstrap Blazor Server request pipeline.
    /// </summary>
    public static class WebApplicationExtensions
    {
        /// <summary>
        /// The key marking an application whose Cloudstrap Blazor Server pipeline has already been built.
        /// Deliberately distinct from the MVC and Web API composites' markers, so a host calling two
        /// composites fails on this method's own double-call rule rather than a cross-package collision.
        /// </summary>
        private const string _pipelineMarker = "Cloudstrap.BlazorServer.Pipeline";

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
        /// Adds the Cloudstrap Blazor Server middleware and endpoints in the order a hardened, observable
        /// Blazor Server application needs them, mapping <typeparamref name="TRootComponent"/> as the root.
        /// </summary>
        /// <typeparam name="TRootComponent">The application's root component, typically <c>App</c>.</typeparam>
        /// <param name="app">The application to build the pipeline on.</param>
        /// <param name="configure">
        /// An optional hook placing the application's own middleware and endpoints inside the pipeline.
        /// </param>
        /// <returns>The same <paramref name="app"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The method has already been called on this application — a request pipeline is built exactly
        /// once — or <c>AddCloudstrapBlazorServer</c> was never called on its builder.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The order is the point of this call, and it is fixed:
        /// </para>
        /// <list type="number">
        ///   <item><description>the error handling head: the developer exception page where selected, the
        ///   exception handler re-executing <c>Cloudstrap:Application:ExceptionHandlerPath</c> with a fresh
        ///   scope otherwise,</description></item>
        ///   <item><description>HSTS, outside <c>Development</c> and when enabled,</description></item>
        ///   <item><description>the security-header middleware,</description></item>
        ///   <item><description>the path base, when <c>Cloudstrap:Application:PathBase</c> is set,</description></item>
        ///   <item><description><see cref="BlazorServerPipelineOptions.BeforeRouting"/>,</description></item>
        ///   <item><description>routing,</description></item>
        ///   <item><description>correlation,</description></item>
        ///   <item><description>authentication, only when a scheme is registered,</description></item>
        ///   <item><description><see cref="BlazorServerPipelineOptions.BeforeAuthorization"/>,</description></item>
        ///   <item><description>authorization, under the same condition as authentication,</description></item>
        ///   <item><description>antiforgery,</description></item>
        ///   <item><description><see cref="BlazorServerPipelineOptions.BeforeEndpoints"/>,</description></item>
        ///   <item><description>the static-asset endpoints, when
        ///   <see cref="BlazorServerPipelineOptions.MapStaticAssets"/> is on,</description></item>
        ///   <item><description>the razor component endpoints for
        ///   <typeparamref name="TRootComponent"/> — with the Interactive Server render mode when the
        ///   registration-time <see cref="CloudstrapBlazorServerConfigurator.Interactivity"/> selected it,
        ///   the <see cref="BlazorServerPipelineOptions.AdditionalAssemblies"/>, and
        ///   <see cref="BlazorServerPipelineOptions.ConfigureComponentEndpoints"/> last,</description></item>
        ///   <item><description>health probes,</description></item>
        ///   <item><description><see cref="BlazorServerPipelineOptions.ConfigureEndpoints"/>.</description></item>
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
        /// in <see cref="BlazorServerPipelineOptions.BeforeRouting"/> — never a silent library default.
        /// </para>
        /// <para>
        /// Every constituent piece stays independently callable — <c>UseCloudstrapCorrelation</c>,
        /// <c>MapCloudstrapHealthChecks</c>, the framework's own component mapping — so a consumer who
        /// needs a different order simply does not call this method.
        /// </para>
        /// </remarks>
        public static WebApplication UseCloudstrapBlazorServer<TRootComponent>(
            this WebApplication app,
            Action<BlazorServerPipelineOptions>? configure = null)
            where TRootComponent : IComponent
        {
            ArgumentNullException.ThrowIfNull(app);

            IDictionary<string, object?> properties = ((IApplicationBuilder)app).Properties;

            if (properties.ContainsKey(_pipelineMarker))
            {
                throw new InvalidOperationException(
                    $"{nameof(UseCloudstrapBlazorServer)} has already been called on this application. A "
                    + "request pipeline is built exactly once.");
            }

            BlazorServerRegistrationState? state = app.Services.GetService<BlazorServerRegistrationState>();

            if (state is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(UseCloudstrapBlazorServer)} requires "
                    + $"{nameof(WebApplicationBuilderExtensions.AddCloudstrapBlazorServer)} to have been "
                    + "called on the application's builder first.");
            }

            properties[_pipelineMarker] = true;

            BlazorServerPipelineOptions hooks = new();
            configure?.Invoke(hooks);

            ApplicationOptions application = app.Services
                .GetRequiredService<IOptions<ApplicationOptions>>()
                .Value;
            CloudstrapBlazorServerOptions options = app.Services
                .GetRequiredService<IOptions<CloudstrapBlazorServerOptions>>()
                .Value;

            // The error handling head. When the developer page is selected it is the framework's: in
            // Development minimal hosting has already auto-inserted it as the outermost middleware, and
            // outside Development it is added explicitly here. Otherwise the exception handler re-executes
            // the consumer's own error path, with a fresh dependency injection scope for the re-execution —
            // the razor-components idiom.
            bool useDeveloperExceptionPage = EnvironmentDefault.Resolve(
                options.ExceptionHandling.UseDeveloperExceptionPage,
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
                    CreateScopeForErrors = true,
                });
            }

            // Browsers only honour HSTS over HTTPS, and pinning a developer's localhost would be a nuisance
            // they have to clear by hand.
            if (options.Hsts.Enabled && !app.Environment.IsDevelopment())
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

            // After routing, so endpoint metadata is visible; before authentication, so a request rejected by
            // an authorization policy is still correlated in the logs and in its problem-details response.
            app.UseCloudstrapCorrelation();

            // Whenever any authentication scheme is registered — Cloudstrap's login package or one the
            // consumer brought — the middleware belongs here, after routing. When none is, neither is added
            // and every endpoint is anonymous.
            //
            // The test is the registered scheme map, not the presence of IAuthenticationSchemeProvider: the
            // authentication core services exist even in an application that never configured a scheme.
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

            app.UseAntiforgery();

            hooks.BeforeEndpoints?.Invoke(app);

            if (hooks.MapStaticAssets)
            {
                app.MapStaticAssets();
            }

            RazorComponentsEndpointConventionBuilder components =
                app.MapRazorComponents<TRootComponent>();

            if (state.Interactivity == BlazorInteractivity.InteractiveServer)
            {
                components.AddInteractiveServerRenderMode();
            }

            if (hooks.AdditionalAssemblies.Count > 0)
            {
                components.AddAdditionalAssemblies([.. hooks.AdditionalAssemblies]);
            }

            // Last on the convention builder, so the hook always has the final say.
            hooks.ConfigureComponentEndpoints?.Invoke(components);

            app.MapCloudstrapHealthChecks();

            hooks.ConfigureEndpoints?.Invoke(app);

            ILogger logger = app.Services.GetRequiredService<ILogger<WebApplication>>();
            BlazorServerLog.PipelineBuilt(logger, state.Interactivity, hasAuthentication);

            return app;
        }
    }
}
