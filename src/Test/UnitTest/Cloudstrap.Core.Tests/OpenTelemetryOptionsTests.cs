namespace Cloudstrap.Core.Tests
{
    using Microsoft.Extensions.Configuration;
    using NUnit.Framework;

    [TestFixture]
    public sealed class OpenTelemetryOptionsTests
    {
        [Test]
        public void Mode_WithAzureMonitorString_BindsReservedEnumValue()
        {
            // Arrange
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor",
                })
                .Build();

            // Act
            OpenTelemetryOptions options = configuration
                .GetSection(OpenTelemetryOptions.SectionName)
                .Get<OpenTelemetryOptions>()!;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(options.Mode, Is.EqualTo(OpenTelemetryMode.AzureMonitor));
                Assert.That((int)OpenTelemetryMode.AzureMonitor, Is.EqualTo(3));
            });
        }

        [TestCase(OpenTelemetryMode.Disabled, false)]
        [TestCase(OpenTelemetryMode.Console, true)]
        [TestCase(OpenTelemetryMode.Otlp, true)]
        [TestCase(OpenTelemetryMode.AzureMonitor, true)]
        public void IsActive_ForEachMode_TracksModeNotDisabled(OpenTelemetryMode mode, bool expected)
        {
            // Arrange
            var options = new OpenTelemetryOptions { Mode = mode };

            // Act
            bool isActive = options.IsActive;

            // Assert
            Assert.That(isActive, Is.EqualTo(expected));
        }

        [TestCase(OpenTelemetryMode.Disabled, true, false)]
        [TestCase(OpenTelemetryMode.Disabled, false, false)]
        [TestCase(OpenTelemetryMode.Console, true, true)]
        [TestCase(OpenTelemetryMode.Console, false, true)]
        [TestCase(OpenTelemetryMode.Otlp, true, true)]
        [TestCase(OpenTelemetryMode.Otlp, false, false)]
        [TestCase(OpenTelemetryMode.AzureMonitor, true, true)]
        [TestCase(OpenTelemetryMode.AzureMonitor, false, false)]
        public void IsConsoleEnabled_ForModeAndFlagCombinations_ComposesCorrectly(
            OpenTelemetryMode mode,
            bool enableConsole,
            bool expected)
        {
            // Arrange
            var options = new OpenTelemetryOptions { Mode = mode, EnableConsole = enableConsole };

            // Act
            bool isConsoleEnabled = options.IsConsoleEnabled;

            // Assert
            Assert.That(isConsoleEnabled, Is.EqualTo(expected));
        }
    }
}
