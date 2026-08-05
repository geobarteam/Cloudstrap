namespace Cloudstrap.WebApi
{
    using System.Text.Json.Serialization;
    using Asp.Versioning;
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registers the Cloudstrap Web API services on a web application builder.
    /// </summary>
    public static class WebApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers everything a Cloudstrap Web API needs on the service side: the Cloudstrap settings
        /// model, correlation services, controllers with the Cloudstrap JSON and routing opinions, and API
        /// versioning with per-version route and header conventions.
        /// </summary>
        /// <param name="builder">The web application builder to register into.</param>
        /// <param name="configure">
        /// An optional hook carrying the code-level overrides configuration cannot express: versioning
        /// readers and conventions, the JSON serializer, and the MVC builder.
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Pair it with
        /// <see cref="WebApplicationExtensions.UseCloudstrapWebApi(WebApplication, Action{WebApiPipelineOptions}?)"/>,
        /// which builds the matching request pipeline.
        /// </para>
        /// <para>
        /// Settings come from <c>Cloudstrap:WebApi</c> and are validated at host startup, naming the
        /// offending key. The Cloudstrap opinions applied here are: null properties omitted from JSON
        /// responses, lowercase generated URLs and query strings, the configured default API version assumed
        /// for requests and controllers that name none, and <c>api-supported-versions</c> reported on every
        /// response. Each has a configuration override, and each hook on
        /// <see cref="CloudstrapWebApiConfigurator"/> runs after the corresponding default.
        /// </para>
        /// <para>
        /// This call registers <strong>no</strong> authentication. Token validation is the separate,
        /// deliberately visible <c>AddCloudstrapJwtBearer</c> call.
        /// </para>
        /// <para>
        /// Repeat calls are safe: every registration here is additive or idempotent.
        /// </para>
        /// </remarks>
        public static WebApplicationBuilder AddCloudstrapWebApi(
            this WebApplicationBuilder builder,
            Action<CloudstrapWebApiConfigurator>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            CloudstrapWebApiConfigurator configurator = new();
            configure?.Invoke(configurator);

            IServiceCollection services = builder.Services;

            services.AddCloudstrapCore();
            services.AddCloudstrapCorrelation();
            services.AddHttpContextAccessor();

            // The stock builder, so a consumer's own checks and an Aspire host's checks land in the same set.
            services.AddHealthChecks();

            services.AddOptions<WebApiOptions>()
                .BindConfiguration(WebApiOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<WebApiOptions>, WebApiOptionsValidator>());

            WebApiOptions options = builder.Configuration
                .GetSection(WebApiOptions.SectionName)
                .Get<WebApiOptions>() ?? new WebApiOptions();

            services.Configure<RouteOptions>(route =>
            {
                route.LowercaseUrls = options.LowercaseUrls;
                route.LowercaseQueryStrings = options.LowercaseUrls;
            });

            IMvcBuilder mvc = services.AddControllers();

            services.Configure<JsonOptions>(json =>
                json.JsonSerializerOptions.DefaultIgnoreCondition = options.Json.IgnoreNullValues
                    ? JsonIgnoreCondition.WhenWritingNull
                    : JsonIgnoreCondition.Never);

            if (configurator.Json is not null)
            {
                // Registered after the Cloudstrap default, so the hook wins.
                mvc.AddJsonOptions(configurator.Json);
            }

            services.AddHsts(hsts =>
            {
                hsts.MaxAge = TimeSpan.FromDays(options.Hsts.MaxAgeDays);
                hsts.IncludeSubDomains = options.Hsts.IncludeSubDomains;
                hsts.Preload = options.Hsts.Preload;
            });

            ConfigureCors(services, options.Cors);

            services.AddProblemDetails();

            // Registered last, so any handler the consumer added before this call gets the first attempt.
            services.AddExceptionHandler<CloudstrapWebApiExceptionHandler>();

            IApiVersioningBuilder versioning = ApiVersioningRegistration.Configure(
                services,
                options,
                configurator.ApiVersioning);

            services.AddOptions<CloudstrapOpenApiOptions>()
                .BindConfiguration(CloudstrapOpenApiOptions.SectionName)
                .ValidateOnStart();

            CloudstrapOpenApiOptions openApi = builder.Configuration
                .GetSection(CloudstrapOpenApiOptions.SectionName)
                .Get<CloudstrapOpenApiOptions>() ?? new CloudstrapOpenApiOptions();

            if (openApi.Enabled)
            {
                ApplicationOptions application = builder.Configuration
                    .GetSection(ApplicationOptions.SectionName)
                    .Get<ApplicationOptions>() ?? new ApplicationOptions();

                OpenApiRegistration.Configure(versioning, openApi, application, configurator.OpenApi);
            }

            services.AddOptions<CloudstrapScalarOptions>()
                .BindConfiguration(CloudstrapScalarOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<CloudstrapScalarOptions>,
                    CloudstrapScalarOptionsValidator>());
            services.AddSingleton(new ScalarConfigurator(configurator.Scalar));

            configurator.Mvc?.Invoke(mvc);

            return builder;
        }

        /// <summary>
        /// Adds inbound JWT bearer validation, hardened beyond the framework's defaults, and — unless told
        /// otherwise — makes every endpoint require an authenticated caller.
        /// </summary>
        /// <param name="builder">The web application builder to register into.</param>
        /// <param name="configure">
        /// An optional hook applied to the bearer options <em>after</em> the Cloudstrap defaults, for
        /// anything the settings type does not model — additional valid issuers, a custom token validator,
        /// event handlers.
        /// </param>
        /// <returns>The same <paramref name="builder"/> instance, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// This call is deliberately separate from <see cref="AddCloudstrapWebApi"/> and deliberately not a
        /// configuration flag: whether an API requires authentication should be visible in the code that
        /// composes it, not buried in a settings file where a deployment mistake can silently switch it off.
        /// </para>
        /// <para>
        /// Four defaults are stricter than the framework's, and every one of them is a settable property of
        /// <see cref="CloudstrapJwtBearerOptions"/>: audience validation is on and its value required; the
        /// clock skew is 60 seconds rather than 300; metadata must be fetched over HTTPS outside
        /// <c>Development</c>; and inbound claim names are left as the token spelled them. Registering the
        /// bearer also installs a require-authenticated fallback authorization policy — see
        /// <see cref="CloudstrapJwtBearerOptions.RequireAuthenticatedEndpoints"/> for the two opt-outs.
        /// </para>
        /// <para>
        /// This validates tokens the API <em>receives</em>. It acquires none, holds no client credentials and
        /// logs no token contents; the handler's own <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> log
        /// category carries the diagnostics for a rejected token.
        /// </para>
        /// <para>
        /// A consumer who builds their own pipeline instead of calling
        /// <see cref="WebApplicationExtensions.UseCloudstrapWebApi(WebApplication, Action{WebApiPipelineOptions}?)"/>
        /// must place <c>UseAuthentication</c> and <c>UseAuthorization</c> themselves, after routing.
        /// </para>
        /// </remarks>
        public static WebApplicationBuilder AddCloudstrapJwtBearer(
            this WebApplicationBuilder builder,
            Action<JwtBearerOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            IServiceCollection services = builder.Services;

            services.AddOptions<CloudstrapJwtBearerOptions>()
                .BindConfiguration(CloudstrapJwtBearerOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<CloudstrapJwtBearerOptions>,
                    CloudstrapJwtBearerOptionsValidator>());

            CloudstrapJwtBearerOptions options = builder.Configuration
                .GetSection(CloudstrapJwtBearerOptions.SectionName)
                .Get<CloudstrapJwtBearerOptions>() ?? new CloudstrapJwtBearerOptions();

            bool requireHttpsMetadata = EnvironmentDefault.Resolve(
                options.RequireHttpsMetadata,
                !builder.Environment.IsDevelopment());

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(bearer =>
                {
                    bearer.Authority = options.Authority;
                    bearer.Audience = options.Audience;
                    bearer.RequireHttpsMetadata = requireHttpsMetadata;
                    bearer.MapInboundClaims = options.MapInboundClaims;

                    bearer.TokenValidationParameters.ValidateAudience = true;
                    bearer.TokenValidationParameters.ValidateIssuer = true;
                    bearer.TokenValidationParameters.ValidateLifetime = true;
                    bearer.TokenValidationParameters.ClockSkew =
                        TimeSpan.FromSeconds(options.ClockSkewSeconds);

                    // Last, so the hook overrides anything above it.
                    configure?.Invoke(bearer);
                });

            if (options.RequireAuthenticatedEndpoints)
            {
                services.AddAuthorization(authorization =>
                    authorization.FallbackPolicy = new AuthorizationPolicyBuilder()
                        .RequireAuthenticatedUser()
                        .Build());
            }

            return builder;
        }

        /// <summary>
        /// Registers the default CORS policy — but only once at least one origin is configured.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="cors">The configured cross-origin settings.</param>
        /// <remarks>
        /// With no origins configured nothing at all is registered, so no <c>Access-Control-Allow-Origin</c>
        /// header can be emitted and browsers keep their same-origin default. There is deliberately no
        /// allow-any-origin fallback.
        /// </remarks>
        private static void ConfigureCors(IServiceCollection services, CorsSettings cors)
        {
            if (cors.AllowedOrigins.Count == 0)
            {
                return;
            }

            string[] origins = [.. cors.AllowedOrigins];
            bool hasWildcardSubdomain = origins.Any(origin => origin.Contains('*', StringComparison.Ordinal));

            services.AddCors(options => options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(origins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();

                if (hasWildcardSubdomain)
                {
                    policy.SetIsOriginAllowedToAllowWildcardSubdomains();
                }
            }));
        }
    }
}
