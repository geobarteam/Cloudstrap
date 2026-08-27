namespace Cloudstrap.BlazorServer
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;
    using OpenTelemetry.Trace;

    /// <summary>
    /// Registers the Cloudstrap Blazor Server services on a web application builder.
    /// </summary>
    public static class WebApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers everything a Cloudstrap Blazor Server application needs on the service side: the
        /// Cloudstrap settings model, correlation services, health checks on the stock builder, razor
        /// components with the interactivity decided here, cascading authentication state, and a hardened
        /// antiforgery cookie.
        /// </summary>
        /// <param name="builder">The web application builder to register into.</param>
        /// <param name="configure">
        /// An optional hook carrying the code-level overrides configuration cannot express — see
        /// <see cref="CloudstrapBlazorServerConfigurator"/>.
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Pair it with
        /// <see cref="WebApplicationExtensions.UseCloudstrapBlazorServer{TRootComponent}(WebApplication, Action{BlazorServerPipelineOptions}?)"/>,
        /// which builds the matching request pipeline and follows the
        /// <see cref="CloudstrapBlazorServerConfigurator.Interactivity"/> decision made here — the decision
        /// is made once, in one place.
        /// </para>
        /// <para>
        /// Settings come from the <c>Cloudstrap:BlazorServer</c> configuration section and are validated at
        /// host startup, naming the offending key; the section is optional and every default applies
        /// without it.
        /// </para>
        /// <para>
        /// This call registers <strong>no</strong> authentication: pairing with a login package —
        /// <c>AddCloudstrapOpenIdConnect</c> or a scheme of your own — is a separate, deliberately visible
        /// call, and without one every endpoint is anonymous. It registers <strong>no</strong> observability
        /// pipeline either: <c>UseCloudstrapObservability</c> is a separate, visible call, and the package
        /// only contributes its activity source to whatever pipeline the host owns.
        /// </para>
        /// <para>
        /// Repeat calls are safe: every registration here is additive or idempotent.
        /// </para>
        /// </remarks>
        public static WebApplicationBuilder AddCloudstrapBlazorServer(
            this WebApplicationBuilder builder,
            Action<CloudstrapBlazorServerConfigurator>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            CloudstrapBlazorServerConfigurator configurator = new();
            configure?.Invoke(configurator);

            IServiceCollection services = builder.Services;

            services.AddCloudstrapCore();
            services.AddCloudstrapCorrelation();
            services.AddHttpContextAccessor();

            // The stock builder, so a consumer's own checks and an Aspire host's checks land in the same set.
            services.AddHealthChecks();

            services.AddOptions<CloudstrapBlazorServerOptions>()
                .BindConfiguration(CloudstrapBlazorServerOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<CloudstrapBlazorServerOptions>,
                    CloudstrapBlazorServerOptionsValidator>());

            CloudstrapBlazorServerOptions options = builder.Configuration
                .GetSection(CloudstrapBlazorServerOptions.SectionName)
                .Get<CloudstrapBlazorServerOptions>() ?? new CloudstrapBlazorServerOptions();

            IRazorComponentsBuilder razorComponents = services.AddRazorComponents();

            if (configurator.Interactivity == BlazorInteractivity.InteractiveServer)
            {
                razorComponents.AddInteractiveServerComponents();
            }

            services.AddCascadingAuthenticationState();

            services.AddAntiforgery(antiforgery =>
            {
                // Exactly the hardening delta over the framework defaults.
                antiforgery.Cookie.HttpOnly = true;
                antiforgery.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                antiforgery.Cookie.SameSite = SameSiteMode.Strict;

                // Last, so the hook always has the final say.
                configurator.Antiforgery?.Invoke(antiforgery);
            });

            services.AddHsts(hsts =>
            {
                hsts.MaxAge = TimeSpan.FromDays(options.Hsts.MaxAgeDays);
                hsts.IncludeSubDomains = options.Hsts.IncludeSubDomains;
                hsts.Preload = options.Hsts.Preload;
            });

            // The once-made interactivity decision the pipeline call follows; first registration wins.
            services.TryAddSingleton(new BlazorServerRegistrationState(configurator.Interactivity));

            services.TryAddSingleton<IBlazorInteractionTrace, BlazorInteractionTrace>();

            // A deferred contribution, not a pipeline: the source reaches whatever tracer pipeline is built
            // from this service collection — Cloudstrap-owned, contribute-mode or an Aspire-style host's —
            // and registers no provider and no exporter of its own when none is.
            services.ConfigureOpenTelemetryTracerProvider(tracing =>
                tracing.AddSource(BlazorServerActivitySources.Interaction));

            // Last, so the hook always has the final say.
            configurator.RazorComponents?.Invoke(razorComponents);

            return builder;
        }
    }
}
