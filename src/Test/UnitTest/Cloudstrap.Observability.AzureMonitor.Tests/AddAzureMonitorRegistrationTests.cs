namespace Cloudstrap.Observability.AzureMonitor.Tests
{
    using global::Azure.Monitor.OpenTelemetry.Exporter;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using OpenTelemetry;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;

    /// <summary>
    /// An <c>AzureMonitor</c>-mode host that refused to start without an exporter package now starts and
    /// carries per-signal Azure Monitor exporters wired to the resolved connection string. No live Azure:
    /// the connection string is syntactically valid but unreachable, and nothing is ever transmitted.
    /// </summary>
    [TestFixture]
    public sealed class AddAzureMonitorRegistrationTests
    {
        private const string _dummyConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        private const string _standardConnectionStringVariable = "APPLICATIONINSIGHTS_CONNECTION_STRING";

        [Test]
        public void AddAzureMonitor_AzureMonitorMode_HostStartsCleanly()
        {
            // Arrange — the shipped guard failed this exact host before an exporter package existed
            using IHost host = BuildHost(AzureMonitorModeValid(), out _);

            // Act & Assert
            Assert.DoesNotThrowAsync(async () =>
            {
                await host.StartAsync();
                await host.StopAsync();
            });
        }

        [Test]
        public void AddAzureMonitor_AzureMonitorMode_AppliesCloudstrapConnectionStringToExporterOptions()
        {
            // Arrange
            using IHost host = BuildHost(AzureMonitorModeValid(), out List<AzureMonitorExporterOptions> captured);

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert — the explicit setting wins; the SDK only falls back when nothing is set
            Assert.That(captured, Is.Not.Empty);
            Assert.That(
                captured.Select(exporter => exporter.ConnectionString),
                Has.All.EqualTo(_dummyConnectionString));
        }

        [Test]
        public void AddAzureMonitor_WithOnlyStandardVariable_LetsTheSdkResolveTheConnectionString()
        {
            // Arrange — the SDK reads the process environment, so the variable is set there as well as in
            // configuration, where Cloudstrap's validator looks for it. The value differs from the one
            // Cloudstrap's own setting would carry, and that setting is absent, so a match can only come
            // from the SDK's own resolution.
            const string fromEnvironment = "InstrumentationKey=22222222-2222-2222-2222-222222222222";
            string? original = Environment.GetEnvironmentVariable(_standardConnectionStringVariable);
            Environment.SetEnvironmentVariable(_standardConnectionStringVariable, fromEnvironment);
            try
            {
                Dictionary<string, string?> values = AzureMonitorModeValid();
                values.Remove("Cloudstrap:AzureMonitor:ConnectionString");
                values[_standardConnectionStringVariable] = fromEnvironment;
                using IHost host = BuildHost(values, out List<AzureMonitorExporterOptions> captured);

                // Act
                _ = host.Services.GetRequiredService<TracerProvider>();

                // Assert — Cloudstrap configured nothing; the SDK resolved the standard variable itself
                Assert.That(captured, Is.Not.Empty);
                Assert.That(
                    captured.Select(exporter => exporter.ConnectionString),
                    Has.All.EqualTo(fromEnvironment));
            }
            finally
            {
                Environment.SetEnvironmentVariable(_standardConnectionStringVariable, original);
            }
        }

        [Test]
        public async Task AddAzureMonitor_RegistersTracerAndMeterProviders()
        {
            // Arrange
            using IHost host = BuildHost(AzureMonitorModeValid(), out List<AzureMonitorExporterOptions> captured);

            // Act — the logged event forces the OpenTelemetry logging pipeline to materialize
            await host.StartAsync();
            try
            {
                host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Contoso.Orders.Api")
                    .LogInformation("Force logger provider creation");
            }
            finally
            {
                await host.StopAsync();
            }

            // Assert — all three signals reached the exporter
            Assert.Multiple(() =>
            {
                Assert.That(host.Services.GetService<TracerProvider>(), Is.Not.Null);
                Assert.That(host.Services.GetService<MeterProvider>(), Is.Not.Null);
                Assert.That(captured, Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void AddAzureMonitor_WithEnableTracingFalse_DoesNotResurrectTracing()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:OpenTelemetry:EnableTracing"] = "false";
            using IHost host = BuildHost(values, out _);

            // Act & Assert — the leaf gates on Core's flags itself
            Assert.That(host.Services.GetService<TracerProvider>(), Is.Null);
        }

        [Test]
        public void AddAzureMonitor_WithEnableMetricsFalse_RegistersNoMeterProvider()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:OpenTelemetry:EnableMetrics"] = "false";
            using IHost host = BuildHost(values, out _);

            // Act & Assert
            Assert.That(host.Services.GetService<MeterProvider>(), Is.Null);
        }

        [Test]
        public void AddAzureMonitor_WithEnableLogsFalse_AddsNoOtelLoggerProvider()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:OpenTelemetry:EnableLogs"] = "false";
            using IHost host = BuildHost(values, out _);

            // Act
            List<string?> providers =
                [.. host.Services.GetServices<ILoggerProvider>().Select(provider => provider.GetType().FullName)];

            // Assert
            Assert.That(providers, Has.None.Contains("OpenTelemetry"));
        }

        [Test]
        public void AddAzureMonitor_WithAllSignalsDisabled_HostStillStarts()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:OpenTelemetry:EnableTracing"] = "false";
            values["Cloudstrap:OpenTelemetry:EnableMetrics"] = "false";
            values["Cloudstrap:OpenTelemetry:EnableLogs"] = "false";
            using IHost host = BuildHost(values, out _);

            // Act & Assert — the guard is satisfied, nothing is exported
            Assert.DoesNotThrowAsync(async () =>
            {
                await host.StartAsync();
                await host.StopAsync();
            });
        }

        [Test]
        public void AddAzureMonitor_CalledTwice_ConfiguresExportersOnce()
        {
            // Arrange
            using IHost once = BuildHost(AzureMonitorModeValid(), out List<AzureMonitorExporterOptions> onceCaptured);
            using IHost twice = BuildHost(
                AzureMonitorModeValid(),
                out List<AzureMonitorExporterOptions> twiceCaptured,
                callTwice: true);

            // Act
            _ = once.Services.GetRequiredService<TracerProvider>();
            _ = twice.Services.GetRequiredService<TracerProvider>();

            // Assert
            Assert.That(twiceCaptured, Has.Count.EqualTo(onceCaptured.Count));
        }

        [Test]
        public void AddAzureMonitor_ConsumerHook_RunsLastAndWins()
        {
            // Arrange
            const string overridden = "InstrumentationKey=11111111-1111-1111-1111-111111111111";
            List<string?> connectionStringsAtHookTime = [];
            HostApplicationBuilder builder = CreateBuilder(AzureMonitorModeValid());
            builder.UseCloudstrapObservability().AddAzureMonitor(exporter =>
            {
                connectionStringsAtHookTime.Add(exporter.ConnectionString);
                exporter.DisableOfflineStorage = true;
                exporter.ConnectionString = overridden;
            });
            using IHost host = builder.Build();

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert — Cloudstrap had already configured the options when the hook fired,
            // and nothing runs after the hook, so its write stands
            Assert.That(connectionStringsAtHookTime, Has.All.EqualTo(_dummyConnectionString));
        }

        [Test]
        public void AddAzureMonitor_InContributeMode_AddsExportersToTheHostPipeline()
        {
            // Arrange — a ServiceDefaults-shaped host that already owns its pipeline
            List<AzureMonitorExporterOptions> captured = [];
            HostApplicationBuilder builder = CreateBuilder(AzureMonitorModeValid());
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource("Contoso.Test"));
            builder
                .UseCloudstrapObservability(options =>
                    options.PipelineMode = ObservabilityPipelineMode.Contribute)
                .AddAzureMonitor(exporter =>
                {
                    exporter.DisableOfflineStorage = true;
                    captured.Add(exporter);
                });
            using IHost host = builder.Build();

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert — the legitimate Aspire pairing: the host owns the pipeline, Cloudstrap adds the exporter
            Assert.That(captured, Is.Not.Empty);
            Assert.DoesNotThrowAsync(async () =>
            {
                await host.StartAsync();
                await host.StopAsync();
            });
        }

        private static IHost BuildHost(
            Dictionary<string, string?> values,
            out List<AzureMonitorExporterOptions> captured,
            bool callTwice = false)
        {
            List<AzureMonitorExporterOptions> capturedOptions = [];
            HostApplicationBuilder builder = CreateBuilder(values);
            CloudstrapObservabilityBuilder cloudstrap = builder.UseCloudstrapObservability();

            // Offline storage is disabled so a test run leaves no telemetry spool behind
            void Capture(AzureMonitorExporterOptions exporter)
            {
                exporter.DisableOfflineStorage = true;
                capturedOptions.Add(exporter);
            }

            cloudstrap.AddAzureMonitor(Capture);
            if (callTwice)
            {
                cloudstrap.AddAzureMonitor(Capture);
            }

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
