namespace Cloudstrap.Mvc.Tests.Infrastructure
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// Boots a real ASP.NET Core pipeline in-process on <see cref="TestServer"/>, so every assertion in this
    /// suite is made over real HTTP against the same two calls a consumer writes.
    /// </summary>
    internal static class MvcTestHost
    {
        /// <summary>
        /// The application name the host reports. It is deliberately <em>not</em> the test assembly, so MVC's
        /// default application-part discovery finds no controllers and the fixture controllers are reachable
        /// only through the documented <see cref="CloudstrapMvcConfigurator.Mvc"/> hook.
        /// </summary>
        private const string _applicationName = "Cloudstrap.Mvc";

        /// <summary>
        /// The lazily provisioned web root carrying <c>site.css</c>, so static-file serving is testable
        /// against a real asset on disk.
        /// </summary>
        private static readonly Lazy<string> _webRoot = new(ProvisionWebRoot);

        /// <summary>
        /// Builds a configured but unstarted application.
        /// </summary>
        /// <param name="configuration">Configuration entries layered over the neutral defaults.</param>
        /// <param name="configure">Code-level configurator hooks.</param>
        /// <param name="configureMvc">Extra MVC builder work, applied after the fixture application part.</param>
        /// <param name="environment">The hosting environment name.</param>
        /// <param name="beforeBuild">Work applied to the builder before <c>AddCloudstrapMvc</c>.</param>
        /// <param name="includeTestControllers">Whether the fixture controllers' application part is added.</param>
        /// <param name="loggerProvider">A capturing logger provider, when a test asserts over log entries.</param>
        /// <returns>The built application.</returns>
        public static WebApplication Build(
            IDictionary<string, string?>? configuration = null,
            Action<CloudstrapMvcConfigurator>? configure = null,
            Action<IMvcBuilder>? configureMvc = null,
            string environment = "Production",
            Action<WebApplicationBuilder>? beforeBuild = null,
            bool includeTestControllers = true,
            ILoggerProvider? loggerProvider = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ApplicationName = _applicationName,
                WebRootPath = _webRoot.Value,
            });

            builder.Configuration.AddInMemoryCollection(Compose(configuration));
            builder.WebHost.UseTestServer();

            if (loggerProvider is not null)
            {
                builder.Logging.AddProvider(loggerProvider);
            }

            beforeBuild?.Invoke(builder);

            builder.AddCloudstrapMvc(configurator =>
            {
                configure?.Invoke(configurator);

                Action<IMvcBuilder>? consumerMvc = configurator.Mvc;
                configurator.Mvc = mvc =>
                {
                    if (includeTestControllers)
                    {
                        mvc.AddApplicationPart(typeof(MvcTestHost).Assembly);
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
        /// <param name="pipeline">The pipeline hooks passed to <c>UseCloudstrapMvc</c>.</param>
        /// <param name="environment">The hosting environment name.</param>
        /// <param name="beforeBuild">Work applied to the builder before <c>AddCloudstrapMvc</c>.</param>
        /// <param name="includeTestControllers">Whether the fixture controllers' application part is added.</param>
        /// <param name="afterUse">Work applied to the application after <c>UseCloudstrapMvc</c>.</param>
        /// <param name="loggerProvider">A capturing logger provider, when a test asserts over log entries.</param>
        /// <returns>The started application.</returns>
        public static async Task<WebApplication> StartAsync(
            IDictionary<string, string?>? configuration = null,
            Action<CloudstrapMvcConfigurator>? configure = null,
            Action<IMvcBuilder>? configureMvc = null,
            Action<MvcPipelineOptions>? pipeline = null,
            string environment = "Production",
            Action<WebApplicationBuilder>? beforeBuild = null,
            bool includeTestControllers = true,
            Action<WebApplication>? afterUse = null,
            ILoggerProvider? loggerProvider = null)
        {
            WebApplication app = Build(
                configuration,
                configure,
                configureMvc,
                environment,
                beforeBuild,
                includeTestControllers,
                loggerProvider);

            app.UseCloudstrapMvc(pipeline);
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
                ["Cloudstrap:Application:SubsystemType"] = "Web",

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

        /// <summary>
        /// Creates the fixture web root under the temp directory, carrying one real <c>site.css</c>.
        /// </summary>
        /// <returns>The web root path.</returns>
        private static string ProvisionWebRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "Cloudstrap.Mvc.Tests", "wwwroot");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "site.css"), "body { font-family: sans-serif; }");

            return root;
        }
    }
}
