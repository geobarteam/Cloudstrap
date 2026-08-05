namespace Cloudstrap.Observability.AzureMonitor.Tests
{
    using System.Diagnostics;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using OpenTelemetry.Trace;

    /// <summary>
    /// Blazor Server hub suppression survives in the flagship mode, with the real Application Insights
    /// sampler installed — the case the base package cannot exercise, because it must never reference Azure.
    /// </summary>
    [TestFixture]
    public sealed class AzureMonitorHubScrubTests
    {
        private const string _dummyConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        [Test]
        public void AddAzureMonitor_ComponentHubSpan_DoesNotReachExportersWhileNormalSpanDoes()
        {
            // Arrange — AlwaysOnSampler makes the Application Insights sampler record everything, so the
            // scrub is the only suppression left in the pipeline
            List<Activity> exported = [];
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:OpenTelemetry:AlwaysOnSampler"] = "true";
            HostApplicationBuilder builder = CreateBuilder(values);
            builder
                .UseCloudstrapObservability(options => options.ConfigureTracing =
                    tracing => tracing.AddSource("Contoso.Test").AddInMemoryExporter(exported))
                .AddAzureMonitor(exporter => exporter.DisableOfflineStorage = true);
            using IHost host = builder.Build();
            TracerProvider tracerProvider = host.Services.GetRequiredService<TracerProvider>();

            // Act
            using (ActivitySource source = new("Contoso.Test"))
            {
                StartServerActivity(source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                StartServerActivity(source, "PlainSpan", []);
            }

            tracerProvider.ForceFlush();

            // Assert
            List<string> names = [.. exported.Select(activity => activity.DisplayName)];
            Assert.Multiple(() =>
            {
                Assert.That(names, Does.Not.Contain("HubSpan"));
                Assert.That(names, Does.Contain("PlainSpan"));
            });
        }

        private static void StartServerActivity(
            ActivitySource source,
            string name,
            List<KeyValuePair<string, object?>> tags)
        {
            using Activity? activity = source.StartActivity(
                name, ActivityKind.Server, parentContext: default, tags: tags);
        }

        private static Dictionary<string, string?> AzureMonitorModeValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
            ["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor",
            ["Cloudstrap:OpenTelemetry:EnableConsole"] = "false",
            ["Cloudstrap:AzureMonitor:ConnectionString"] = _dummyConnectionString,
        };

        private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);

            return builder;
        }
    }
}
