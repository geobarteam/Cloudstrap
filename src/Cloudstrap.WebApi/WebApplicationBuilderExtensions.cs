namespace Cloudstrap.WebApi
{
    using System.Text.Json.Serialization;
    using Asp.Versioning;
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
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
