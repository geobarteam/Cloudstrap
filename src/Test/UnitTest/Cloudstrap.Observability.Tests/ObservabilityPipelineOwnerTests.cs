namespace Cloudstrap.Observability.Tests
{
    using System.Diagnostics;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using OpenTelemetry;
    using OpenTelemetry.Logs;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Resources;
    using OpenTelemetry.Trace;

    [TestFixture]
    public sealed class ObservabilityPipelineOwnerTests
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
        public async Task UseCloudstrapObservability_WithModeDisabled_RegistersNoTracerOrMeterProvider()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(MinimalValid());
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            try
            {
                // Assert
                List<ILoggerProvider> providers = [.. host.Services.GetServices<ILoggerProvider>()];
                Assert.Multiple(() =>
                {
                    Assert.That(host.Services.GetService<TracerProvider>(), Is.Null);
                    Assert.That(host.Services.GetService<MeterProvider>(), Is.Null);
                    Assert.That(providers.Select(provider => provider.GetType().FullName), Has.Some.Contains("Serilog"));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public void UseCloudstrapObservability_WithModeConsole_RegistersTracerAndMeterProviders()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability();

            // Act
            using IHost host = builder.Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(host.Services.GetService<TracerProvider>(), Is.Not.Null);
                Assert.That(host.Services.GetService<MeterProvider>(), Is.Not.Null);
            });
        }

        [Test]
        public void UseCloudstrapObservability_WithModeConsole_WritesSpanToConsoleExporter()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability(options =>
                options.ConfigureTracing = tracing => tracing.AddSource("Contoso.Test"));
            using IHost host = builder.Build();
            TracerProvider tracerProvider = host.Services.GetRequiredService<TracerProvider>();

            // Act
            using ActivitySource source = new("Contoso.Test");
            using (source.StartActivity("Contoso.Test.Operation"))
            {
            }

            tracerProvider.ForceFlush();

            // Assert
            Assert.That(_consoleOutput.ToString(), Does.Contain("Contoso.Test.Operation"));
        }

        [Test]
        public void UseCloudstrapObservability_OwnerMode_SetsCloudstrapResourceAttributes()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            Dictionary<string, object> attributes = ResourceAttributes(host);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(attributes["service.name"], Is.EqualTo("contoso-orders-api"));
                Assert.That(attributes["deployment.environment.name"], Is.EqualTo(builder.Environment.EnvironmentName));
                Assert.That(attributes["host.name"], Is.EqualTo(Environment.MachineName));
                Assert.That(attributes["cloudstrap.system.name"], Is.EqualTo("Contoso"));
                Assert.That(attributes["cloudstrap.subsystem.name"], Is.EqualTo("Orders"));
                Assert.That(attributes["cloudstrap.subsystem.type"], Is.EqualTo("Api"));
                Assert.That(attributes.Keys, Has.None.StartsWith("nihdi."));
            });
        }

        [Test]
        public void UseCloudstrapObservability_WithEnvironmentTier_AddsTierAttribute()
        {
            // Arrange
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:Application:EnvironmentTier"] = "Acceptance";
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            Dictionary<string, object> attributes = ResourceAttributes(host);

            // Assert
            Assert.That(attributes["cloudstrap.environment.tier"], Is.EqualTo("Acceptance"));
        }

        [Test]
        public void UseCloudstrapObservability_WithoutEnvironmentTier_OmitsTierAttribute()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            Dictionary<string, object> attributes = ResourceAttributes(host);

            // Assert
            Assert.That(attributes.ContainsKey("cloudstrap.environment.tier"), Is.False);
        }

        [Test]
        public void UseCloudstrapObservability_WithConfigureResource_AppliesConsumerOverride()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability(options =>
                options.ConfigureResource = resource =>
                    resource.AddAttributes([new KeyValuePair<string, object>("contoso.custom.attribute", "custom-value")]));
            using IHost host = builder.Build();

            // Act
            Dictionary<string, object> attributes = ResourceAttributes(host);

            // Assert
            Assert.That(attributes["contoso.custom.attribute"], Is.EqualTo("custom-value"));
        }

        [Test]
        public void UseCloudstrapObservability_WithEnableTracingFalse_RegistersNoTracerProviderButKeepsMetrics()
        {
            // Arrange
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:OpenTelemetry:EnableTracing"] = "false";
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability();

            // Act
            using IHost host = builder.Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(host.Services.GetService<TracerProvider>(), Is.Null);
                Assert.That(host.Services.GetService<MeterProvider>(), Is.Not.Null);
            });
        }

        [Test]
        public void UseCloudstrapObservability_WithEnableLogs_ExportsLogRecordsThroughOtelProvider()
        {
            // Arrange
            List<LogRecord> records = [];
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability(options =>
                options.ConfigureLogging = logging => logging.AddInMemoryExporter(records));

            // Act
            using (IHost host = builder.Build())
            {
                host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Contoso.Orders.Api")
                    .LogInformation("Otel log record event");
            }

            // Assert
            Assert.That(records, Is.Not.Empty);
        }

        [Test]
        public void UseCloudstrapObservability_WithSqlClientInstrumentationDisabled_DoesNotThrow()
        {
            // Arrange, Act & Assert — the flag builds and starts in both positions
            Assert.DoesNotThrowAsync(async () =>
            {
                string[] toggles = ["false", "true"];
                foreach (string enabled in toggles)
                {
                    Dictionary<string, string?> values = ConsoleModeValid();
                    values["Cloudstrap:OpenTelemetry:EnableSqlClientInstrumentation"] = enabled;
                    HostApplicationBuilder builder = CreateBuilder(values);
                    builder.UseCloudstrapObservability();
                    using IHost host = builder.Build();
                    await host.StartAsync();
                    await host.StopAsync();
                }
            });
        }

        private static Dictionary<string, object> ResourceAttributes(IHost host) =>
            host.Services.GetRequiredService<TracerProvider>().GetResource()
                .Attributes.ToDictionary(attribute => attribute.Key, attribute => attribute.Value);

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

        private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);

            return builder;
        }
    }
}
