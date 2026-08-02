namespace Cloudstrap.Observability.AzureMonitor.Tests
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// Every mistake in the <c>Cloudstrap:AzureMonitor</c> section fails fast naming the offending
    /// setting keys — including the ones a consumer makes before flipping the mode.
    /// </summary>
    [TestFixture]
    public sealed class AzureMonitorOptionsValidationTests
    {
        private const string _dummyConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        private const string _standardConnectionStringVariable = "APPLICATIONINSIGHTS_CONNECTION_STRING";

        [Test]
        public void Options_WithSamplingRatioAboveOne_FailsValidationEvenInConsoleMode()
        {
            // Arrange — a typo caught before the mode ever flips to AzureMonitor
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:AzureMonitor:SamplingRatio"] = "1.5";
            using IHost host = BuildHost(values);

            // Act — ValidateOnStart surfaces the failure at host startup
            OptionsValidationException? exception =
                Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

            // Assert
            Assert.That(exception!.Message, Does.Contain("SamplingRatio"));
        }

        [Test]
        public void Options_WithSamplingRatioBelowZero_FailsValidation()
        {
            // Arrange
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:AzureMonitor:SamplingRatio"] = "-0.1";
            using IHost host = BuildHost(values);

            // Act
            OptionsValidationException? exception = Assert.Throws<OptionsValidationException>(
                () => _ = host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value);

            // Assert
            Assert.That(exception!.Message, Does.Contain("SamplingRatio"));
        }

        [Test]
        public void Options_WithNotANumberSamplingRatio_FailsValidation()
        {
            // Arrange — the binder parses "NaN" into a float, and every NaN comparison is false,
            // so a plain range check would let it through to the exporter
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:AzureMonitor:SamplingRatio"] = "NaN";
            using IHost host = BuildHost(values);

            // Act
            OptionsValidationException? exception = Assert.Throws<OptionsValidationException>(
                () => _ = host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value);

            // Assert
            Assert.That(exception!.Message, Does.Contain("SamplingRatio"));
        }

        [Test]
        public void Options_WithBothSamplingSettings_FailsValidationNamingBoth()
        {
            // Arrange
            Dictionary<string, string?> values = ConsoleModeValid();
            values["Cloudstrap:AzureMonitor:SamplingRatio"] = "0.5";
            values["Cloudstrap:AzureMonitor:TracesPerSecond"] = "3";
            using IHost host = BuildHost(values);

            // Act
            OptionsValidationException? exception = Assert.Throws<OptionsValidationException>(
                () => _ = host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value);

            // Assert — the message names both mutually exclusive settings
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("SamplingRatio"));
                Assert.That(exception.Message, Does.Contain("TracesPerSecond"));
            });
        }

        [Test]
        public void Options_AzureMonitorModeWithNoConnectionStringAnywhere_FailsNamingBothSources()
        {
            // Act — in AzureMonitor mode the exporters resolve these options while the host builds its
            // logging pipeline, so the failure surfaces at Build(); asserting around both steps keeps the
            // test honest about the behavior rather than the timing
            OptionsValidationException? exception = Assert.Throws<OptionsValidationException>(
                () => ResolveOptions(AzureMonitorModeValid()));

            // Assert — both places a connection string can come from are named
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("Cloudstrap:AzureMonitor:ConnectionString"));
                Assert.That(exception.Message, Does.Contain(_standardConnectionStringVariable));
            });
        }

        [Test]
        public void Options_AzureMonitorModeWithWhitespaceConnectionString_TreatedAsAbsent()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:AzureMonitor:ConnectionString"] = "   ";

            // Act
            OptionsValidationException? exception = Assert.Throws<OptionsValidationException>(
                () => ResolveOptions(values));

            // Assert
            Assert.That(exception!.Message, Does.Contain("Cloudstrap:AzureMonitor:ConnectionString"));
        }

        [Test]
        public void Options_AzureMonitorModeWithStandardVariableInConfiguration_PassesValidation()
        {
            // Arrange — the platform's own variable satisfies the requirement
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values[_standardConnectionStringVariable] = _dummyConnectionString;
            using IHost host = BuildHost(values);

            // Act & Assert
            Assert.DoesNotThrow(
                () => _ = host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value);
        }

        [Test]
        public void Options_OtlpModeWithNoConnectionString_PassesValidation()
        {
            // Arrange — the presence rule applies to AzureMonitor mode only
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["Cloudstrap:OpenTelemetry:Endpoint"] = "https://collector.example.com";
            using IHost host = BuildHost(values);

            // Act & Assert
            Assert.DoesNotThrow(
                () => _ = host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value);
        }

        /// <summary>
        /// Builds a host from the supplied configuration and resolves the Azure Monitor options, so a
        /// validation failure is observed wherever it surfaces — while the host builds, or on resolution.
        /// </summary>
        private static void ResolveOptions(Dictionary<string, string?> values)
        {
            using IHost host = BuildHost(values);
            _ = host.Services.GetRequiredService<IOptions<AzureMonitorOptions>>().Value;
        }

        private static IHost BuildHost(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);
            builder.UseCloudstrapObservability().AddAzureMonitor();

            return builder.Build();
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

        private static Dictionary<string, string?> AzureMonitorModeValid()
        {
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor";
            return values;
        }
    }
}
