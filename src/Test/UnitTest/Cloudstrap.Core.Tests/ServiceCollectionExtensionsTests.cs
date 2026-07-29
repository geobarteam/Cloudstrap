namespace Cloudstrap.Core.Tests
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    [TestFixture]
    public sealed class ServiceCollectionExtensionsTests
    {
        [Test]
        public void AddCloudstrapCore_WithValidConfiguration_ResolvesRootAndAllSectionOptions()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Logging:Level"] = "Warning";
            values["Cloudstrap:HealthChecks:LivenessPath"] = "/alive";
            values["Cloudstrap:Correlation:HeaderName"] = "X-Request-ID";
            values["Cloudstrap:OpenTelemetry:Mode"] = "Console";
            values["Cloudstrap:HttpClients:CatalogApi:BaseAddress"] = "https://catalog.example.com/";
            using ServiceProvider provider = BuildProvider(values);

            // Act
            CloudstrapOptions root = provider.GetRequiredService<IOptions<CloudstrapOptions>>().Value;
            ApplicationOptions application = provider.GetRequiredService<IOptions<ApplicationOptions>>().Value;
            LoggingOptions logging = provider.GetRequiredService<IOptions<LoggingOptions>>().Value;
            OpenTelemetryOptions openTelemetry = provider.GetRequiredService<IOptions<OpenTelemetryOptions>>().Value;
            CorrelationOptions correlation = provider.GetRequiredService<IOptions<CorrelationOptions>>().Value;
            HealthChecksOptions healthChecks = provider.GetRequiredService<IOptions<HealthChecksOptions>>().Value;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(root.Application.WorkloadName, Is.EqualTo("contoso-orders-api"));
                Assert.That(root.HttpClients["CatalogApi"].BaseAddress, Is.EqualTo(new Uri("https://catalog.example.com/")));
                Assert.That(application.SystemName, Is.EqualTo("Contoso"));
                Assert.That(logging.Level, Is.EqualTo(LogLevel.Warning));
                Assert.That(openTelemetry.Mode, Is.EqualTo(OpenTelemetryMode.Console));
                Assert.That(correlation.HeaderName, Is.EqualTo("X-Request-ID"));
                Assert.That(healthChecks.LivenessPath, Is.EqualTo("/alive"));
            });
        }

        [Test]
        public void AddCloudstrapCore_WithValidConfiguration_StartupValidationSucceeds()
        {
            // Arrange
            using ServiceProvider provider = BuildProvider(MinimalValid());
            IStartupValidator validator = provider.GetRequiredService<IStartupValidator>();

            // Act & Assert
            Assert.DoesNotThrow(validator.Validate);
        }

        [Test]
        public void AddCloudstrapCore_WithMissingSystemName_StartupValidationThrowsNamingMember()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values.Remove("Cloudstrap:Application:SystemName");
            using ServiceProvider provider = BuildProvider(values);

            // Act
            IEnumerable<string> failures = StartupValidationFailures(provider);

            // Assert
            Assert.That(failures, Has.Some.Contains("SystemName"));
        }

        [Test]
        public void AddCloudstrapCore_WithOtlpModeAndNoEndpoint_StartupValidationThrowsNamingEndpoint()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            using ServiceProvider provider = BuildProvider(values);

            // Act
            IEnumerable<string> failures = StartupValidationFailures(provider);

            // Assert
            Assert.That(failures, Has.Some.Contains("Endpoint"));
        }

        [Test]
        public void AddCloudstrapCore_WithOtlpModeAndStandardVariable_StartupValidationSucceeds()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Otlp";
            values["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://collector.example.com";
            using ServiceProvider provider = BuildProvider(values);
            IStartupValidator validator = provider.GetRequiredService<IStartupValidator>();

            // Act & Assert
            Assert.DoesNotThrow(validator.Validate);
        }

        [Test]
        public void AddCloudstrapCore_WithInvalidHttpClient_StartupValidationThrowsNamingClientEntry()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:HttpClients:CatalogApi:Timeout"] = "00:00:10";
            using ServiceProvider provider = BuildProvider(values);

            // Act
            IEnumerable<string> failures = StartupValidationFailures(provider);

            // Assert
            Assert.That(failures, Has.Some.Contains("HttpClients:CatalogApi:BaseAddress"));
        }

        [Test]
        public void AddCloudstrapCore_CalledTwice_ResolvesSingleValidatorPerOptionsType()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(BuildConfiguration(MinimalValid()));

            // Act
            services.AddCloudstrapCore();
            services.AddCloudstrapCore();

            // Assert
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetServices<IValidateOptions<CloudstrapOptions>>().Count(), Is.EqualTo(1));
                Assert.That(provider.GetServices<IValidateOptions<ApplicationOptions>>().Count(), Is.EqualTo(1));
                Assert.That(provider.GetServices<IValidateOptions<LoggingOptions>>().Count(), Is.EqualTo(1));
                Assert.That(provider.GetServices<IValidateOptions<OpenTelemetryOptions>>().Count(), Is.EqualTo(1));
            });
        }

        [Test]
        public void AddCloudstrapCore_WithNullServices_ThrowsArgumentNullException()
        {
            // Arrange
            IServiceCollection services = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => services.AddCloudstrapCore());
        }

        [Test]
        public void AddCloudstrapCore_WhenCalled_ReturnsSameServiceCollection()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            IServiceCollection returned = services.AddCloudstrapCore();

            // Assert
            Assert.That(returned, Is.SameAs(services));
        }

        /// <summary>
        /// Runs startup validation and returns the failure messages. When more than one options type is invalid
        /// the framework aggregates the individual <see cref="OptionsValidationException"/>s, which is what a
        /// real host surfaces; this flattens either shape.
        /// </summary>
        private static IEnumerable<string> StartupValidationFailures(ServiceProvider provider)
        {
            IStartupValidator validator = provider.GetRequiredService<IStartupValidator>();
            Exception thrown = Assert.Catch(validator.Validate)!;

            IEnumerable<Exception> validationExceptions = thrown is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions
                : [thrown];

            // AC-C2: every startup failure is the framework's own OptionsValidationException.
            Assert.That(validationExceptions, Is.All.InstanceOf<OptionsValidationException>());

            return validationExceptions.Select(exception => exception.Message);
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
        {
            var services = new ServiceCollection();
            services.AddSingleton(BuildConfiguration(values));
            services.AddCloudstrapCore();

            return services.BuildServiceProvider();
        }
    }
}
