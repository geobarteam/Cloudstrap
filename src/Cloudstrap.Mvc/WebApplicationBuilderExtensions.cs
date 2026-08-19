namespace Cloudstrap.Mvc
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registers the Cloudstrap MVC services on a web application builder.
    /// </summary>
    public static class WebApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers everything a Cloudstrap server-rendered MVC application needs on the service side: the
        /// Cloudstrap settings model, correlation services, health checks on the stock builder, and
        /// controllers with views.
        /// </summary>
        /// <param name="builder">The web application builder to register into.</param>
        /// <param name="configure">
        /// An optional hook carrying the code-level overrides configuration cannot express — see
        /// <see cref="CloudstrapMvcConfigurator"/>.
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Pair it with
        /// <see cref="WebApplicationExtensions.UseCloudstrapMvc(WebApplication, Action{MvcPipelineOptions}?)"/>,
        /// which builds the matching request pipeline.
        /// </para>
        /// <para>
        /// Settings come from the <c>Cloudstrap:Mvc</c> configuration section and are validated at host
        /// startup, naming the offending key. Registration-time decisions (such as whether session state is
        /// wired at all) read that section eagerly from the builder's configuration: sources added
        /// <em>after</em> this call — a KeyVault provider, for example — do not affect them, so call
        /// configuration-adding extensions first.
        /// </para>
        /// <para>
        /// Session state is on by default, hardened by default, and flows entirely through stock
        /// <c>Microsoft.AspNetCore.Session</c>: the <c>.Cloudstrap.Session</c> cookie is always
        /// <c>Secure</c>, <c>HttpOnly</c>, <c>SameSite=Lax</c> and scoped to the configured path base. The
        /// backing store is any <c>IDistributedCache</c> you register before this call; without one, the
        /// framework's in-memory cache is the single-instance fallback.
        /// </para>
        /// <para>
        /// This call registers <strong>no</strong> authentication: pairing with a login package —
        /// <c>AddCloudstrapOpenIdConnect</c> or a scheme of your own — is a separate, deliberately visible
        /// call, and without one every endpoint is anonymous.
        /// </para>
        /// <para>
        /// Repeat calls are safe: every registration here is additive or idempotent.
        /// </para>
        /// </remarks>
        public static WebApplicationBuilder AddCloudstrapMvc(
            this WebApplicationBuilder builder,
            Action<CloudstrapMvcConfigurator>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            CloudstrapMvcConfigurator configurator = new();
            configure?.Invoke(configurator);

            IServiceCollection services = builder.Services;

            services.AddCloudstrapCore();
            services.AddCloudstrapCorrelation();
            services.AddHttpContextAccessor();

            // The stock builder, so a consumer's own checks and an Aspire host's checks land in the same set.
            services.AddHealthChecks();

            services.AddOptions<CloudstrapMvcOptions>()
                .BindConfiguration(CloudstrapMvcOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<CloudstrapMvcOptions>,
                    CloudstrapMvcOptionsValidator>());

            CloudstrapMvcOptions options = builder.Configuration
                .GetSection(CloudstrapMvcOptions.SectionName)
                .Get<CloudstrapMvcOptions>() ?? new CloudstrapMvcOptions();

            ApplicationOptions application = builder.Configuration
                .GetSection(ApplicationOptions.SectionName)
                .Get<ApplicationOptions>() ?? new ApplicationOptions();

            if (options.Session.Enabled)
            {
                // TryAdd semantics: an IDistributedCache the consumer registered wins by construction.
                services.AddDistributedMemoryCache();

                services.AddSession(session =>
                {
                    // Exactly the hardening delta; HttpOnly and SameSite stay at their stock defaults.
                    session.Cookie.Name = options.Session.CookieName;
                    session.Cookie.SecurePolicy = options.Session.CookieSecurePolicy;
                    session.Cookie.IsEssential = options.Session.IsEssential;
                    session.Cookie.Path = string.IsNullOrEmpty(application.PathBase)
                        ? "/"
                        : application.PathBase;
                    session.IdleTimeout = TimeSpan.FromMinutes(options.Session.IdleTimeoutMinutes);

                    // Last, so the hook always has the final say.
                    configurator.Session?.Invoke(session);
                });
            }

            IMvcBuilder mvc = services.AddControllersWithViews();

            // Last, so the hook always has the final say.
            configurator.Mvc?.Invoke(mvc);

            return builder;
        }
    }
}
