namespace Cloudstrap.Observability.Tests
{
    using System.Diagnostics;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using OpenTelemetry.Trace;

    [TestFixture]
    public sealed class BlazorHubSamplingTests
    {
        private TextWriter _originalConsoleOutput = null!;
        private StringWriter _consoleOutput = null!;

        [SetUp]
        public void SetUp()
        {
            _originalConsoleOutput = Console.Out;
            _consoleOutput = new StringWriter();
            Console.SetOut(_consoleOutput);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(_originalConsoleOutput);
            _consoleOutput.Dispose();
        }

        [Test]
        public void Tracing_WithComponentHubTagByDefault_ExportsNoSpan()
        {
            // Arrange
            (IHost host, List<Activity> exported) = BuildHost(ConsoleModeValid());
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                StartServerActivity(source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                StartServerActivity(source, "PlainSpan", []);
                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert
            List<string> names = [.. exported.Select(activity => activity.DisplayName)];
            Assert.Multiple(() =>
            {
                Assert.That(names, Does.Not.Contain("HubSpan"));
                Assert.That(names, Does.Contain("PlainSpan"));
            });
        }

        [Test]
        public void Tracing_WithEnableBlazorHubTracing_ExportsHubSpan()
        {
            // Arrange
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:OpenTelemetry:EnableBlazorHubTracing"] = "true";
            (IHost host, List<Activity> exported) = BuildHost(values);
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                StartServerActivity(source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert
            Assert.That(exported.Select(activity => activity.DisplayName), Does.Contain("HubSpan"));
        }

        [Test]
        public void Tracing_WithAlwaysOnSamplerFlag_RecordsAllSpans()
        {
            // Arrange
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:OpenTelemetry:AlwaysOnSampler"] = "true";
            (IHost host, List<Activity> exported) = BuildHost(values);
            using (host)
            {
                // Act — a child of an unsampled remote parent is dropped by ParentBased but kept by AlwaysOn
                ActivityContext unsampledParent = new(
                    ActivityTraceId.CreateRandom(),
                    ActivitySpanId.CreateRandom(),
                    ActivityTraceFlags.None,
                    isRemote: true);
                using ActivitySource source = new("Contoso.Test");
                using (source.StartActivity("ChildOfUnsampled", ActivityKind.Server, unsampledParent))
                {
                }

                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert
            Assert.That(exported.Select(activity => activity.DisplayName), Does.Contain("ChildOfUnsampled"));
        }

        [Test]
        public void Tracing_WithApplySamplerFalse_LeavesHostSamplerAlone()
        {
            // Arrange
            (IHost host, List<Activity> exported) = BuildHost(
                ConsoleModeValid(),
                options => options.ApplySampler = false);
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                StartServerActivity(source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert — Cloudstrap did not install its sampler, so the hub-tagged span is exported
            Assert.That(exported.Select(activity => activity.DisplayName), Does.Contain("HubSpan"));
        }

        [Test]
        public void AzureMonitorMode_ComponentHubActivity_IsSampledInButNotExported()
        {
            // Arrange — in AzureMonitor mode the exporter owns the sampler, so suppression moves to an
            // export-time scrub: the span is sampled in, then dropped before any exporter sees it
            (IHost host, List<Activity> exported) = BuildHost(AzureMonitorModeValid());
            bool recordedAtStart;
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                recordedAtStart = StartServerActivityAndReportRecorded(
                    source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                StartServerActivity(source, "PlainSpan", []);
                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert
            List<string> names = [.. exported.Select(activity => activity.DisplayName)];
            Assert.Multiple(() =>
            {
                Assert.That(recordedAtStart, Is.True, "no Cloudstrap sampler should have dropped the hub span");
                Assert.That(names, Does.Not.Contain("HubSpan"));
                Assert.That(names, Does.Contain("PlainSpan"));
            });
        }

        [Test]
        public void AzureMonitorMode_WithEnableBlazorHubTracing_ExportsHubSpans()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:OpenTelemetry:EnableBlazorHubTracing"] = "true";
            (IHost host, List<Activity> exported) = BuildHost(values);
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                StartServerActivity(source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert — the override lifts the scrub
            Assert.That(exported.Select(activity => activity.DisplayName), Does.Contain("HubSpan"));
        }

        [Test]
        public void AzureMonitorMode_WithApplySamplerFalse_StillScrubsHubSpans()
        {
            // Arrange — ApplySampler governs sampler installation, and in AzureMonitor mode Cloudstrap
            // installs no sampler at all; hub suppression is a noise-filter concern owned by
            // EnableBlazorHubTracing, so switching the sampler off must not resurrect hub spans
            (IHost host, List<Activity> exported) = BuildHost(
                AzureMonitorModeValid(),
                options => options.ApplySampler = false);
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                StartServerActivity(source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                StartServerActivity(source, "PlainSpan", []);
                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert
            List<string> names = [.. exported.Select(activity => activity.DisplayName)];
            Assert.Multiple(() =>
            {
                Assert.That(names, Does.Not.Contain("HubSpan"));
                Assert.That(names, Does.Contain("PlainSpan"));
            });
        }

        [Test]
        public void AzureMonitorMode_WithConsoleExporterAlongside_ScrubsHubSpanFromConsoleToo()
        {
            // Arrange — EnableConsole defaults to true, so the flagship mode normally runs a console exporter
            // alongside the Azure Monitor one; the scrub is registered ahead of it
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor";
            (IHost host, _) = BuildHost(values);
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                StartServerActivity(source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                StartServerActivity(source, "PlainSpan", []);
                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert
            string console = _consoleOutput.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(console, Does.Not.Contain("HubSpan"));
                Assert.That(console, Does.Contain("PlainSpan"));
            });
        }

        [Test]
        public void ConsoleMode_KeepsSamplerBasedHubSuppression()
        {
            // Arrange — regression pin: outside AzureMonitor mode the shipped sampler chain is unchanged,
            // so the hub span is dropped at sampling time rather than at export time
            (IHost host, List<Activity> exported) = BuildHost(ConsoleModeValid());
            bool recordedAtStart;
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                recordedAtStart = StartServerActivityAndReportRecorded(
                    source, "HubSpan", [new("rpc.service", "ComponentHub")]);
                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(recordedAtStart, Is.False, "the Cloudstrap sampler should have dropped the hub span");
                Assert.That(exported.Select(activity => activity.DisplayName), Does.Not.Contain("HubSpan"));
            });
        }

        private static void StartServerActivity(
            ActivitySource source,
            string name,
            List<KeyValuePair<string, object?>> tags)
        {
            using Activity? activity = source.StartActivity(name, ActivityKind.Server, parentContext: default, tags: tags);
        }

        private static bool StartServerActivityAndReportRecorded(
            ActivitySource source,
            string name,
            List<KeyValuePair<string, object?>> tags)
        {
            using Activity? activity = source.StartActivity(name, ActivityKind.Server, parentContext: default, tags: tags);

            return activity?.Recorded ?? false;
        }

        private static (IHost Host, List<Activity> Exported) BuildHost(
            Dictionary<string, string?> values,
            Action<CloudstrapObservabilityOptions>? extraOptions = null)
        {
            List<Activity> exported = [];
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);
            CloudstrapObservabilityBuilder cloudstrap = builder.UseCloudstrapObservability(options =>
            {
                options.ConfigureTracing = tracing => tracing.AddSource("Contoso.Test").AddInMemoryExporter(exported);
                extraOptions?.Invoke(options);
            });

            // Stands in for an exporter package, so an AzureMonitor-mode host is allowed to start
            cloudstrap.MarkExporterContributed();

            IHost host = builder.Build();
            _ = host.Services.GetRequiredService<TracerProvider>();

            return (host, exported);
        }

        private static Dictionary<string, string?> ConsoleModeValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
            ["Cloudstrap:OpenTelemetry:Mode"] = "Console",
        };

        private static Dictionary<string, string?> AzureMonitorModeValid()
        {
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor";
            values["Cloudstrap:OpenTelemetry:EnableConsole"] = "false";
            return values;
        }
    }
}
