namespace Cloudstrap.Observability.AzureMonitor.Tests
{
    using System.Diagnostics;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;
    using OpenTelemetry.Exporter;
    using OpenTelemetry.Trace;

    /// <summary>
    /// A consumer leaves <c>AddAzureMonitor()</c> in Program.cs permanently: outside
    /// <c>AzureMonitor</c> mode it changes nothing, yet the section binds in every mode.
    /// </summary>
    [TestFixture]
    public sealed class AddAzureMonitorInertModeTests
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
        public void AddAzureMonitor_InOtlpMode_ReturnsTheSameBuilderAndKeepsOtlpBehavior()
        {
            // Arrange
            List<OtlpExporterOptions> captured = [];
            HostApplicationBuilder builder = CreateBuilder(OtlpWithEndpointValid());
            CloudstrapObservabilityBuilder cloudstrap =
                builder.UseCloudstrapObservability(options => options.ConfigureOtlpExporter = captured.Add);

            // Act
            CloudstrapObservabilityBuilder returned = cloudstrap.AddAzureMonitor();

            // Assert — the OTLP configuration is exactly what the base package alone produces
            using IHost host = builder.Build();
            _ = host.Services.GetRequiredService<TracerProvider>();
            Assert.Multiple(() =>
            {
                Assert.That(returned, Is.SameAs(cloudstrap));
                Assert.That(
                    captured.Select(exporter => exporter.Endpoint.AbsoluteUri),
                    Has.Some.EndsWith("/v1/traces"));
            });
        }

        [Test]
        public async Task AddAzureMonitor_InConsoleMode_HostStartsAndExportsToConsole()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder
                .UseCloudstrapObservability(options =>
                    options.ConfigureTracing = tracing => tracing.AddSource("Contoso.Test"))
                .AddAzureMonitor();
            using IHost host = builder.Build();
            TracerProvider tracerProvider = host.Services.GetRequiredService<TracerProvider>();

            // Act
            await host.StartAsync();
            try
            {
                using ActivitySource source = new("Contoso.Test");
                using (source.StartActivity("Contoso.Test.Operation"))
                {
                }

                tracerProvider.ForceFlush();
            }
            finally
            {
                await host.StopAsync();
            }

            // Assert
            Assert.That(_consoleOutput.ToString(), Does.Contain("Contoso.Test.Operation"));
        }

        [Test]
        public void AddAzureMonitor_InDisabledMode_RegistersNoTracerProvider()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(MinimalValid());
            builder.UseCloudstrapObservability().AddAzureMonitor();
            using IHost host = builder.Build();

            // Act & Assert — the inert stack stays inert
            Assert.That(host.Services.GetService<TracerProvider>(), Is.Null);
        }

        [Test]
        public void AddAzureMonitor_InAnyMode_BindsTheAzureMonitorSection()
        {
            // Arrange
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:AzureMonitor:SamplingRatio"] = "0.5";
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability().AddAzureMonitor();
            using IHost host = builder.Build();

            // Act
            AzureMonitorOptions options = host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value;

            // Assert — the section binds even when the mode is not AzureMonitor
            Assert.That(options.SamplingRatio, Is.EqualTo(0.5f));
        }

        [Test]
        public void AddAzureMonitor_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            CloudstrapObservabilityBuilder builder = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.AddAzureMonitor());
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

        private static Dictionary<string, string?> OtlpWithEndpointValid()
        {
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["Cloudstrap:OpenTelemetry:Endpoint"] = "https://collector.example.com";
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
