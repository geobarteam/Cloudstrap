namespace Cloudstrap.Observability.Tests
{
    using System.Diagnostics;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;
    using OpenTelemetry;
    using OpenTelemetry.Exporter;
    using OpenTelemetry.Instrumentation.AspNetCore;
    using OpenTelemetry.Resources;
    using OpenTelemetry.Trace;

    [TestFixture]
    public sealed class ObservabilityContributeModeTests
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
        public void ContributeMode_WithHostPipeline_ExportsEachSpanExactlyOnce()
        {
            // Arrange
            (IHost host, List<Activity> exported) = BuildContributeHost(ConsoleModeValid());
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                using (source.StartActivity("Contoso.Test.Operation"))
                {
                }

                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert — one activity, one exporter, exactly one exported span
            Assert.That(exported, Has.Count.EqualTo(1));
        }

        [Test]
        public void ContributeMode_AddsCloudstrapResourceAttributesButLeavesServiceName()
        {
            // Arrange
            (IHost host, _) = BuildContributeHost(ConsoleModeValid());
            using (host)
            {
                // Act
                Dictionary<string, object> attributes = host.Services.GetRequiredService<TracerProvider>()
                    .GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(attributes["cloudstrap.system.name"], Is.EqualTo("Contoso"));
                    Assert.That(attributes["cloudstrap.subsystem.name"], Is.EqualTo("Orders"));
                    Assert.That(attributes["cloudstrap.subsystem.type"], Is.EqualTo("Api"));
                    Assert.That(attributes["service.name"], Is.EqualTo("host-owned-name"));
                });
            }
        }

        [Test]
        public void ContributeMode_AppliesBlazorHubSampler()
        {
            // Arrange
            (IHost host, List<Activity> exported) = BuildContributeHost(ConsoleModeValid());
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                using (source.StartActivity(
                    "HubSpan", ActivityKind.Server, parentContext: default,
                    tags: [new("rpc.service", "ComponentHub")]))
                {
                }

                using (source.StartActivity("PlainSpan"))
                {
                }

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
        public void ContributeMode_WithApplySamplerFalse_LeavesHostSamplerAlone()
        {
            // Arrange
            (IHost host, List<Activity> exported) = BuildContributeHost(
                ConsoleModeValid(),
                options => options.ApplySampler = false);
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                using (source.StartActivity(
                    "HubSpan", ActivityKind.Server, parentContext: default,
                    tags: [new("rpc.service", "ComponentHub")]))
                {
                }

                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert — Cloudstrap did not install its sampler on the host pipeline
            Assert.That(exported.Select(activity => activity.DisplayName), Does.Contain("HubSpan"));
        }

        [Test]
        public void ContributeMode_ComposesNoiseFilterWithHostInstrumentationOptions()
        {
            // Arrange
            (IHost host, _) = BuildContributeHost(ConsoleModeValid());
            using (host)
            {
                // Act
                Func<HttpContext, bool> filter = host.Services
                    .GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value.Filter!;
                DefaultHttpContext context = new();
                context.Request.Path = "/healthz";

                // Assert — composed even though Cloudstrap added no instrumentation
                Assert.That(filter(context), Is.False);
            }
        }

        [Test]
        public void ContributeMode_DoesNotConfigureOtlpExporterOrConsoleExporter()
        {
            // Arrange
            List<OtlpExporterOptions> captured = [];
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["Cloudstrap:OpenTelemetry:Endpoint"] = "https://collector.example.com";
            values["Cloudstrap:OpenTelemetry:EnableConsole"] = "true";
            (IHost host, List<Activity> exported) = BuildContributeHost(
                values,
                options => options.ConfigureOtlpExporter = captured.Add);
            using (host)
            {
                // Act
                using ActivitySource source = new("Contoso.Test");
                using (source.StartActivity("Contoso.Test.Operation"))
                {
                }

                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert — exporter selection is entirely the host's in contribute mode
            Assert.Multiple(() =>
            {
                Assert.That(captured, Is.Empty);
                Assert.That(exported, Has.Count.EqualTo(1));
                Assert.That(_consoleOutput.ToString(), Does.Not.Contain("Contoso.Test.Operation"));
            });
        }

        [Test]
        public void ContributeMode_DoesNotRegisterAzureMonitorGuard()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor";
            (IHost host, _) = BuildContributeHost(values);
            using (host)
            {
                // Act & Assert — the host owns exporters; AC-B7 is an owner-mode contract
                Assert.DoesNotThrowAsync(async () =>
                {
                    await host.StartAsync();
                    await host.StopAsync();
                });
            }
        }

        [Test]
        public void ContributeMode_InAzureMonitorMode_AddsNoExportTimeScrub()
        {
            // Arrange — the owner-mode hub scrub must not leak into contribute mode, where suppression and
            // exporter selection are the host's. ApplySampler is off so Cloudstrap contributes no sampler
            // either, and the second exporter is registered AFTER Cloudstrap: a scrub processor would sit
            // ahead of it and the hub span would be missing there
            List<Activity> hostExported = [];
            List<Activity> afterCloudstrap = [];
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor";

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddSource("Contoso.Test").AddInMemoryExporter(hostExported));
            builder.UseCloudstrapObservability(options =>
            {
                options.PipelineMode = ObservabilityPipelineMode.Contribute;
                options.ApplySampler = false;
            });
            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddInMemoryExporter(afterCloudstrap));

            using IHost host = builder.Build();
            using (ActivitySource source = new("Contoso.Test"))
            {
                _ = host.Services.GetRequiredService<TracerProvider>();

                // Act
                using (source.StartActivity(
                    "HubSpan", ActivityKind.Server, parentContext: default,
                    tags: [new("rpc.service", "ComponentHub")]))
                {
                }

                host.Services.GetRequiredService<TracerProvider>().ForceFlush();
            }

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(hostExported.Select(activity => activity.DisplayName), Does.Contain("HubSpan"));
                Assert.That(afterCloudstrap.Select(activity => activity.DisplayName), Does.Contain("HubSpan"));
            });
        }

        [Test]
        public void ContributeMode_StillAddsSerilogAndLevels()
        {
            // Arrange
            (IHost host, _) = BuildContributeHost(ConsoleModeValid());
            using (host)
            {
                // Act
                List<string?> providers =
                    [.. host.Services.GetServices<ILoggerProvider>().Select(p => p.GetType().FullName)];
                ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Microsoft.AspNetCore.Routing.Matching");

                // Assert — the Slice-2 logging behavior is mode-independent
                Assert.Multiple(() =>
                {
                    Assert.That(providers, Has.Some.Contains("Serilog"));
                    Assert.That(logger.IsEnabled(LogLevel.Information), Is.False);
                });
            }
        }

        private static (IHost Host, List<Activity> Exported) BuildContributeHost(
            Dictionary<string, string?> values,
            Action<CloudstrapObservabilityOptions>? extraOptions = null)
        {
            List<Activity> exported = [];
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);

            // A ServiceDefaults-shaped pipeline, registered by the host BEFORE Cloudstrap
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService("host-owned-name"))
                .WithTracing(tracing => tracing.AddSource("Contoso.Test").AddInMemoryExporter(exported));

            builder.UseCloudstrapObservability(options =>
            {
                options.PipelineMode = ObservabilityPipelineMode.Contribute;
                extraOptions?.Invoke(options);
            });

            IHost host = builder.Build();
            _ = host.Services.GetRequiredService<TracerProvider>();

            return (host, exported);
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static Dictionary<string, string?> ConsoleModeValid()
        {
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Console";
            return values;
        }
    }
}
