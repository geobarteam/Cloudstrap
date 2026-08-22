namespace Cloudstrap.Core.Tests
{
    using Microsoft.Extensions.Configuration;
    using NUnit.Framework;

    [TestFixture]
    public sealed class ConfigurationExtensionsTests
    {
        [Test]
        public void GetCloudstrapOptions_WithValidSection_ReturnsBoundOptions()
        {
            // Arrange
            IConfiguration configuration = Build(MinimalValid());

            // Act
            CloudstrapOptions options = configuration.GetCloudstrapOptions();

            // Assert
            Assert.That(options.Application.SystemName, Is.EqualTo("Contoso"));
        }

        [Test]
        public void GetCloudstrapOptions_WithoutCloudstrapSection_ThrowsNamingMissingSection()
        {
            // Arrange
            IConfiguration configuration = Build([]);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.That(exception.Message, Does.Contain(CloudstrapOptions.SectionName));
        }

        [Test]
        public void GetCloudstrapOptions_WithMissingSystemName_ThrowsWithFailureNamingSystemName()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values.Remove("Cloudstrap:Application:SystemName");
            IConfiguration configuration = Build(values);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.That(exception.Failures, Has.Some.Contains("SystemName"));
        }

        [Test]
        public void GetCloudstrapOptions_WithMultipleViolations_ListsEveryFailure()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values.Remove("Cloudstrap:Application:SystemName");
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            IConfiguration configuration = Build(values);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(exception.Failures, Has.Some.Contains("SystemName"));
                Assert.That(exception.Failures, Has.Some.Contains("Endpoint"));
                Assert.That(exception.Failures, Has.Count.GreaterThanOrEqualTo(2));
            });
        }

        [Test]
        public void GetCloudstrapOptions_WithOtlpModeAndNeitherEndpointSource_ThrowsNamingEndpointAndVariable()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            IConfiguration configuration = Build(values);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(exception.Failures, Has.Some.Contains("OpenTelemetry:Endpoint"));
                Assert.That(exception.Failures, Has.Some.Contains("OTEL_EXPORTER_OTLP_ENDPOINT"));
            });
        }

        [Test]
        public void GetCloudstrapOptions_WithOtlpModeNoEndpointAndStandardVariable_ReturnsOptions()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://collector.example.com";
            IConfiguration configuration = Build(values);

            // Act
            CloudstrapOptions options = configuration.GetCloudstrapOptions();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(options.OpenTelemetry.Mode, Is.EqualTo(OpenTelemetryMode.Otlp));
                Assert.That(options.OpenTelemetry.Endpoint, Is.Null);
            });
        }

        [Test]
        public void GetCloudstrapOptions_WithNonHttpEndpointAndStandardVariable_StillThrows()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["Cloudstrap:OpenTelemetry:Endpoint"] = "ftp://collector.example.com";
            values["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://collector.example.com";
            IConfiguration configuration = Build(values);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.That(exception.Failures, Has.Some.Contains("OpenTelemetry:Endpoint"));
        }

        [Test]
        public void GetCloudstrapOptions_WithOtlpModeAndNonHttpEndpoint_ThrowsNamingEndpoint()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["Cloudstrap:OpenTelemetry:Endpoint"] = "ftp://collector.example.com";
            IConfiguration configuration = Build(values);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.That(exception.Failures, Has.Some.Contains("OpenTelemetry:Endpoint"));
        }

        [Test]
        public void GetCloudstrapOptions_WithOtlpModeAndHttpsEndpoint_ReturnsOptions()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["Cloudstrap:OpenTelemetry:Endpoint"] = "https://collector.example.com/";
            IConfiguration configuration = Build(values);

            // Act
            CloudstrapOptions options = configuration.GetCloudstrapOptions();

            // Assert
            Assert.That(options.OpenTelemetry.Endpoint, Is.EqualTo(new Uri("https://collector.example.com/")));
        }

        [Test]
        public void GetCloudstrapOptions_WithAzureMonitorModeAndNothingElse_ReturnsOptions()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor";
            IConfiguration configuration = Build(values);

            // Act
            CloudstrapOptions options = configuration.GetCloudstrapOptions();

            // Assert
            Assert.That(options.OpenTelemetry.Mode, Is.EqualTo(OpenTelemetryMode.AzureMonitor));
        }

        [Test]
        public void GetCloudstrapOptions_WithFileLoggingEnabledAndNoPath_ThrowsNamingFilePath()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Logging:File:Enabled"] = "true";
            IConfiguration configuration = Build(values);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.That(exception.Failures, Has.Some.Contains("Logging:File:Path"));
        }

        [Test]
        public void GetCloudstrapOptions_WithHttpClientMissingBaseAddress_ThrowsNamingClientEntry()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:HttpClients:CatalogApi:Timeout"] = "00:00:10";
            IConfiguration configuration = Build(values);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.That(exception.Failures, Has.Some.Contains("HttpClients:CatalogApi:BaseAddress"));
        }

        [Test]
        public void GetCloudstrapOptions_WithRelativeHttpClientBaseAddress_ThrowsNamingClientEntry()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:HttpClients:CatalogApi:BaseAddress"] = "/catalog";
            IConfiguration configuration = Build(values);

            // Act
            ConfigurationValidationException exception =
                Assert.Throws<ConfigurationValidationException>(() => configuration.GetCloudstrapOptions())!;

            // Assert
            Assert.That(exception.Failures, Has.Some.Contains("HttpClients:CatalogApi:BaseAddress"));
        }

        [Test]
        public void GetCloudstrapOptions_WithNullConfiguration_ThrowsArgumentNullException()
        {
            // Arrange
            IConfiguration configuration = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => configuration.GetCloudstrapOptions());
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static IConfiguration Build(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
