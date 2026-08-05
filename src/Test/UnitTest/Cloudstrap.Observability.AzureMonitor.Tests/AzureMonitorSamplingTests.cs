namespace Cloudstrap.Observability.AzureMonitor.Tests
{
    using System.Diagnostics;
    using global::Azure.Monitor.OpenTelemetry.Exporter;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using OpenTelemetry.Trace;

    /// <summary>
    /// The decided sampling policy, observable end to end: the platform default is left untouched, the two
    /// overrides are applied, and the diagnosis flag records everything with a stamped sample rate so
    /// Application Insights can renormalize counts.
    /// </summary>
    [TestFixture]
    public sealed class AzureMonitorSamplingTests
    {
        private const string _dummyConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        [Test]
        public void AddAzureMonitor_WithNeitherSamplingSetting_LeavesExporterSamplingAtSdkDefaults()
        {
            // Arrange
            AzureMonitorExporterOptions untouched = new();
            using IHost host = BuildHost(AzureMonitorModeValid(), out List<AzureMonitorExporterOptions> captured);

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert — Cloudstrap set nothing, so the platform's rate-limited default governs
            Assert.That(captured, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                Assert.That(
                    captured.Select(exporter => exporter.SamplingRatio),
                    Has.All.EqualTo(untouched.SamplingRatio));
                Assert.That(
                    captured.Select(exporter => exporter.TracesPerSecond),
                    Has.All.EqualTo(untouched.TracesPerSecond));
            });
        }

        [Test]
        public void AddAzureMonitor_WithSamplingRatio_AppliesFixedPercentage()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:AzureMonitor:SamplingRatio"] = "0.25";
            using IHost host = BuildHost(values, out List<AzureMonitorExporterOptions> captured);

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert — the rate limit must be cleared, or the exporter keeps rate-limiting and the
            // configured percentage never takes effect
            Assert.That(captured, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                Assert.That(captured.Select(exporter => exporter.SamplingRatio), Has.All.EqualTo(0.25f));
                Assert.That(captured.Select(exporter => exporter.TracesPerSecond), Has.All.Null);
            });
        }

        [Test]
        public void AddAzureMonitor_WithTracesPerSecond_AppliesRateLimit()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:AzureMonitor:TracesPerSecond"] = "2.5";
            using IHost host = BuildHost(values, out List<AzureMonitorExporterOptions> captured);

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert
            Assert.That(captured, Is.Not.Empty);
            Assert.That(captured.Select(exporter => exporter.TracesPerSecond), Has.All.EqualTo(2.5));
        }

        [Test]
        public void AddAzureMonitor_WithAlwaysOnSamplerFlag_ForcesFullSamplingAndIgnoresBothSettings()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:OpenTelemetry:AlwaysOnSampler"] = "true";
            values["Cloudstrap:AzureMonitor:SamplingRatio"] = "0.1";
            using IHost host = BuildHost(values, out List<AzureMonitorExporterOptions> captured);

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert — the diagnosis flag beats the configured policy, and the platform's rate limit is
            // cleared so "record everything" really means everything
            Assert.That(captured, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                Assert.That(captured.Select(exporter => exporter.SamplingRatio), Has.All.EqualTo(1.0f));
                Assert.That(captured.Select(exporter => exporter.TracesPerSecond), Has.All.Null);
            });
        }

        [Test]
        public void AddAzureMonitor_WithFixedPercentageSampling_DropsAProportionOfTraces()
        {
            // Arrange — proof that the Application Insights sampler is installed and honoring the
            // configured percentage. The sample rate it stamps for portal renormalization is written during
            // conversion to the Application Insights wire format, downstream of any in-process exporter,
            // so that half is covered by the documented manual verification (AC-O1), not from here.
            const int activityCount = 200;
            List<Activity> exported = [];
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:AzureMonitor:SamplingRatio"] = "0.5";
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
                for (int index = 0; index < activityCount; index++)
                {
                    using (source.StartActivity($"Contoso.Test.Operation{index}"))
                    {
                    }
                }
            }

            tracerProvider.ForceFlush();

            // Assert — some but not all traces survive the sampler
            Assert.Multiple(() =>
            {
                Assert.That(exported, Is.Not.Empty);
                Assert.That(exported, Has.Count.LessThan(activityCount));
            });
        }

        [Test]
        public void AddAzureMonitor_WithFullSampling_ExportsEveryTrace()
        {
            // Arrange
            const int activityCount = 5;
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
                for (int index = 0; index < activityCount; index++)
                {
                    using (source.StartActivity($"Contoso.Test.Operation{index}"))
                    {
                    }
                }
            }

            tracerProvider.ForceFlush();

            // Assert — the diagnosis flag really does record everything: the platform's 5-per-second rate
            // limit was cleared, so no trace is dropped. At 100% there is no sample rate to stamp, because
            // there is nothing for the portal to renormalize.
            Assert.That(exported, Has.Count.EqualTo(activityCount));
        }

        private static IHost BuildHost(
            Dictionary<string, string?> values,
            out List<AzureMonitorExporterOptions> captured)
        {
            List<AzureMonitorExporterOptions> capturedOptions = [];
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability().AddAzureMonitor(exporter =>
            {
                exporter.DisableOfflineStorage = true;
                capturedOptions.Add(exporter);
            });
            captured = capturedOptions;

            return builder.Build();
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
