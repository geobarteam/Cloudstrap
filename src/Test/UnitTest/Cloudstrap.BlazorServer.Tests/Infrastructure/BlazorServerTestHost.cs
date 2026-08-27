namespace Cloudstrap.BlazorServer.Tests.Infrastructure
{
    using Cloudstrap.BlazorServer.Tests.Fixtures;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using NUnit.Framework;

    /// <summary>
    /// Boots a real ASP.NET Core pipeline in-process on <see cref="TestServer"/>, so every assertion in this
    /// suite is made over real HTTP against the same two calls a consumer writes.
    /// </summary>
    internal static class BlazorServerTestHost
    {
        /// <summary>
        /// Builds a configured but unstarted application, with <c>AddCloudstrapBlazorServer</c> applied.
        /// </summary>
        /// <param name="configuration">Configuration entries layered over the neutral defaults.</param>
        /// <param name="configure">Code-level configurator hooks.</param>
        /// <param name="environment">The hosting environment name.</param>
        /// <param name="beforeBuild">Work applied to the builder before <c>AddCloudstrapBlazorServer</c>.</param>
        /// <returns>The built application.</returns>
        public static WebApplication Build(
            IDictionary<string, string?>? configuration = null,
            Action<CloudstrapBlazorServerConfigurator>? configure = null,
            string environment = "Production",
            Action<WebApplicationBuilder>? beforeBuild = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ApplicationName = "Cloudstrap.BlazorServer",
            });

            builder.Configuration.AddInMemoryCollection(Compose(configuration));
            builder.WebHost.UseTestServer();

            beforeBuild?.Invoke(builder);

            builder.AddCloudstrapBlazorServer(configure);

            return builder.Build();
        }

        /// <summary>
        /// Builds an application, adds the Cloudstrap Blazor Server pipeline over the fixture root component
        /// and starts it.
        /// </summary>
        /// <param name="configuration">Configuration entries layered over the neutral defaults.</param>
        /// <param name="configure">Code-level configurator hooks.</param>
        /// <param name="pipeline">The pipeline hooks passed to <c>UseCloudstrapBlazorServer</c>.</param>
        /// <param name="environment">The hosting environment name.</param>
        /// <param name="beforeBuild">Work applied to the builder before <c>AddCloudstrapBlazorServer</c>.</param>
        /// <param name="afterUse">Work applied to the application after <c>UseCloudstrapBlazorServer</c>.</param>
        /// <returns>The started application.</returns>
        /// <remarks>
        /// <c>MapStaticAssets</c> is off by default here: the fixture assembly carries no built static-asset
        /// manifest on <see cref="TestServer"/>, so the flag's default-on behavior is exercised by the demo
        /// application and its E2E tests instead (plan mechanic (d)). A test opting back in simply sets the
        /// flag in its <paramref name="pipeline"/> hook, which runs after this default.
        /// </remarks>
        public static async Task<WebApplication> StartAsync(
            IDictionary<string, string?>? configuration = null,
            Action<CloudstrapBlazorServerConfigurator>? configure = null,
            Action<BlazorServerPipelineOptions>? pipeline = null,
            string environment = "Production",
            Action<WebApplicationBuilder>? beforeBuild = null,
            Action<WebApplication>? afterUse = null)
        {
            WebApplication app = Build(configuration, configure, environment, beforeBuild);

            app.UseCloudstrapBlazorServer<App>(options =>
            {
                options.MapStaticAssets = false;
                pipeline?.Invoke(options);
            });
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
    }
}
