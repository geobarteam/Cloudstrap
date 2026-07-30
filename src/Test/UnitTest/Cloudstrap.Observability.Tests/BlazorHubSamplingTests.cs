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

        private static void StartServerActivity(
            ActivitySource source,
            string name,
            List<KeyValuePair<string, object?>> tags)
        {
            using Activity? activity = source.StartActivity(name, ActivityKind.Server, parentContext: default, tags: tags);
        }

        private static (IHost Host, List<Activity> Exported) BuildHost(
            Dictionary<string, string?> values,
            Action<CloudstrapObservabilityOptions>? extraOptions = null)
        {
            List<Activity> exported = [];
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);
            builder.UseCloudstrapObservability(options =>
            {
                options.ConfigureTracing = tracing => tracing.AddSource("Contoso.Test").AddInMemoryExporter(exported);
                extraOptions?.Invoke(options);
            });

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
    }
}
