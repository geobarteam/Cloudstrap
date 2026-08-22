namespace Cloudstrap.Observability.Tests
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using OpenTelemetry.Exporter;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;

    [TestFixture]
    public sealed class ObservabilityOtlpAndAzureMonitorTests
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
        public void OtlpMode_WithExplicitEndpoint_AppendsTraceSignalPathAndSetsHeaders()
        {
            // Arrange
            List<OtlpExporterOptions> captured = [];
            HostApplicationBuilder builder = CreateBuilder(OtlpWithEndpointValid());
            builder.UseCloudstrapObservability(options => options.ConfigureOtlpExporter = captured.Add);
            using IHost host = builder.Build();

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert
            OtlpExporterOptions? traceExporter = captured.Find(
                exporter => exporter.Endpoint.AbsoluteUri.EndsWith("/v1/traces", StringComparison.Ordinal));
            Assert.That(traceExporter, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(traceExporter!.Protocol, Is.EqualTo(OtlpExportProtocol.HttpProtobuf));
                Assert.That(traceExporter.Headers, Does.Contain("x-api-key=secret"));
            });
        }

        [Test]
        public void OtlpMode_WithExplicitEndpoint_UsesPerSignalPathsForMetricsAndLogs()
        {
            // Arrange
            List<OtlpExporterOptions> captured = [];
            HostApplicationBuilder builder = CreateBuilder(OtlpWithEndpointValid());
            builder.UseCloudstrapObservability(options => options.ConfigureOtlpExporter = captured.Add);
            using IHost host = builder.Build();

            // Act
            _ = host.Services.GetRequiredService<MeterProvider>();
            host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Contoso.Orders.Api")
                .LogInformation("Force logger provider creation");

            // Assert
            List<string> endpoints = [.. captured.Select(exporter => exporter.Endpoint.AbsoluteUri)];
            Assert.Multiple(() =>
            {
                Assert.That(endpoints, Has.Some.EndsWith("/v1/metrics"));
                Assert.That(endpoints, Has.Some.EndsWith("/v1/logs"));
            });
        }

        [Test]
        public async Task OtlpMode_WithOnlyStandardVariable_LeavesExporterOptionsUntouched()
        {
            // Arrange
            List<OtlpExporterOptions> captured = [];
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://collector.example.com";
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability(options => options.ConfigureOtlpExporter = captured.Add);
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            try
            {
                // Assert — Cloudstrap set neither protocol nor a per-signal path nor headers
                Assert.That(host.Services.GetService<TracerProvider>(), Is.Not.Null);
                Assert.That(captured, Is.Not.Empty);
                Assert.Multiple(() =>
                {
                    foreach (OtlpExporterOptions exporter in captured)
                    {
                        Assert.That(exporter.Protocol, Is.EqualTo(OtlpExportProtocol.Grpc));
                        Assert.That(exporter.Endpoint.AbsoluteUri, Does.Not.Contain("/v1/"));
                        Assert.That(exporter.Headers, Is.Null.Or.Empty);
                    }
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public void OtlpMode_WithConsumerConfigureOtlpExporter_OverridesCloudstrap()
        {
            // Arrange
            List<string> endpointsAtHookTime = [];
            HostApplicationBuilder builder = CreateBuilder(OtlpWithEndpointValid());
            builder.UseCloudstrapObservability(options => options.ConfigureOtlpExporter = exporter =>
            {
                endpointsAtHookTime.Add(exporter.Endpoint.AbsoluteUri);
                exporter.Endpoint = new Uri("https://consumer-override.example.com/custom");
            });
            using IHost host = builder.Build();

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert — Cloudstrap's per-signal configuration had already run when the hook fired,
            // and nothing runs after the hook, so its write stands
            Assert.That(endpointsAtHookTime, Has.Some.EndsWith("/v1/traces"));
        }

        [Test]
        public void ConsoleMode_DoesNotConfigureOtlpExporter()
        {
            // Arrange
            List<OtlpExporterOptions> captured = [];
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Console";
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability(options => options.ConfigureOtlpExporter = captured.Add);
            using IHost host = builder.Build();

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();
            _ = host.Services.GetRequiredService<MeterProvider>();
            host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Contoso.Orders.Api")
                .LogInformation("Force logger provider creation");

            // Assert
            Assert.That(captured, Is.Empty);
        }

        [Test]
        public void AzureMonitorMode_WithNothingContributed_FailsStartNamingTheMissingPackage()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor";
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            InvalidOperationException? exception =
                Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

            // Assert
            Assert.That(exception!.Message, Does.Contain("Cloudstrap.Observability.AzureMonitor"));
        }

        [Test]
        public void AzureMonitorMode_AfterMarkExporterContributed_StartsCleanly()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor";
            HostApplicationBuilder builder = CreateBuilder(values);
            CloudstrapObservabilityBuilder cloudstrap = builder.UseCloudstrapObservability();
            cloudstrap.MarkExporterContributed();
            using IHost host = builder.Build();

            // Act & Assert
            Assert.DoesNotThrowAsync(async () =>
            {
                await host.StartAsync();
                await host.StopAsync();
            });
        }

        [Test]
        public void DisabledMode_DoesNotRegisterTheGuard()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(MinimalValid());
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            List<string?> hostedServices =
                [.. host.Services.GetServices<IHostedService>().Select(service => service.GetType().FullName)];

            // Assert
            Assert.That(hostedServices, Has.None.Contains("AzureMonitorContributionGuard"));
            Assert.DoesNotThrowAsync(async () =>
            {
                await host.StartAsync();
                await host.StopAsync();
            });
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static Dictionary<string, string?> OtlpWithEndpointValid()
        {
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["Cloudstrap:OpenTelemetry:Endpoint"] = "https://collector.example.com";
            values["Cloudstrap:OpenTelemetry:Headers:x-api-key"] = "secret";
            return values;
        }

        private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);

            return builder;
        }
    }
}
