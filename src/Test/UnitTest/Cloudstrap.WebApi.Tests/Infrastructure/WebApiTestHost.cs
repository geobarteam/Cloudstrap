namespace Cloudstrap.WebApi.Tests.Infrastructure
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// Boots a real ASP.NET Core pipeline in-process on <see cref="TestServer"/>, so every assertion in this
    /// suite is made over real HTTP against the same two calls a consumer writes.
    /// </summary>
    internal static class WebApiTestHost
    {
        /// <summary>
        /// The application name the host reports. It is deliberately <em>not</em> the test assembly, so MVC's
        /// default application-part discovery finds no controllers and the fixture controllers are reachable
        /// only through the documented <see cref="CloudstrapWebApiConfigurator.Mvc"/> hook.
        /// </summary>
        private const string _applicationName = "Cloudstrap.WebApi";

        /// <summary>
        /// Builds a configured but unstarted application.
        /// </summary>
        /// <param name="configuration">Configuration entries layered over the neutral defaults.</param>
        /// <param name="configure">Code-level configurator hooks.</param>
        /// <param name="configureMvc">Extra MVC builder work, applied after the fixture application part.</param>
        /// <param name="environment">The hosting environment name.</param>
        /// <param name="beforeBuild">Work applied to the builder before <c>AddCloudstrapWebApi</c>.</param>
        /// <param name="includeTestControllers">Whether the fixture controllers' application part is added.</param>
        /// <returns>The built application.</returns>
        public static WebApplication Build(
            IDictionary<string, string?>? configuration = null,
            Action<CloudstrapWebApiConfigurator>? configure = null,
            Action<IMvcBuilder>? configureMvc = null,
            string environment = "Production",
            Action<WebApplicationBuilder>? beforeBuild = null,
            bool includeTestControllers = true)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ApplicationName = _applicationName,
            });

            builder.Configuration.AddInMemoryCollection(Compose(configuration));
            builder.WebHost.UseTestServer();

            beforeBuild?.Invoke(builder);

            builder.AddCloudstrapWebApi(configurator =>
            {
                configure?.Invoke(configurator);

                Action<IMvcBuilder>? consumerMvc = configurator.Mvc;
                configurator.Mvc = mvc =>
                {
                    if (includeTestControllers)
                    {
                        mvc.AddApplicationPart(typeof(WebApiTestHost).Assembly);
                    }

                    configureMvc?.Invoke(mvc);
                    consumerMvc?.Invoke(mvc);
                };
            });

            return builder.Build();
        }

        /// <summary>
        /// Builds an application, adds the Cloudstrap pipeline and starts it.
        /// </summary>
        /// <param name="configuration">Configuration entries layered over the neutral defaults.</param>
        /// <param name="configure">Code-level configurator hooks.</param>
        /// <param name="configureMvc">Extra MVC builder work, applied after the fixture application part.</param>
        /// <param name="pipeline">The pipeline hooks passed to <c>UseCloudstrapWebApi</c>.</param>
        /// <param name="environment">The hosting environment name.</param>
        /// <param name="beforeBuild">Work applied to the builder before <c>AddCloudstrapWebApi</c>.</param>
        /// <param name="includeTestControllers">Whether the fixture controllers' application part is added.</param>
        /// <param name="afterUse">Work applied to the application after <c>UseCloudstrapWebApi</c>.</param>
        /// <returns>The started application.</returns>
        public static async Task<WebApplication> StartAsync(
            IDictionary<string, string?>? configuration = null,
            Action<CloudstrapWebApiConfigurator>? configure = null,
            Action<IMvcBuilder>? configureMvc = null,
            Action<WebApiPipelineOptions>? pipeline = null,
            string environment = "Production",
            Action<WebApplicationBuilder>? beforeBuild = null,
            bool includeTestControllers = true,
            Action<WebApplication>? afterUse = null)
        {
            WebApplication app = Build(
                configuration,
                configure,
                configureMvc,
                environment,
                beforeBuild,
                includeTestControllers);

            app.UseCloudstrapWebApi(pipeline);
            afterUse?.Invoke(app);
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);

            return app;
        }

        /// <summary>
        /// Layers the supplied entries over the neutral application identity every fixture needs.
        /// </summary>
        /// <param name="configuration">The entries to layer on top, if any.</param>
        /// <returns>The composed configuration dictionary.</returns>
        private static Dictionary<string, string?> Compose(IDictionary<string, string?>? configuration)
        {
            Dictionary<string, string?> composed = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "Catalog",
                ["Cloudstrap:Application:SubsystemType"] = "Api",

                // Keep the runner output readable; error-level assertions still see what they need.
                ["Logging:LogLevel:Default"] = "Warning",
            };

            if (configuration is not null)
            {
                foreach (KeyValuePair<string, string?> entry in configuration)
                {
                    composed[entry.Key] = entry.Value;
                }
            }

            return composed;
        }
    }
}
