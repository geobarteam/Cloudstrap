namespace Cloudstrap.Observability.Tests
{
    using System.Globalization;
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using Serilog;
    using ILogger = Microsoft.Extensions.Logging.ILogger;

    [TestFixture]
    public sealed class HostApplicationBuilderExtensionsTests
    {
        [Test]
        public void UseCloudstrapObservability_WithProviderRegisteredBefore_KeepsThatProvider()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(MinimalValid());
            builder.Logging.AddProvider(new FakeLoggerProvider());

            // Act
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Assert
            List<ILoggerProvider> providers = [.. host.Services.GetServices<ILoggerProvider>()];
            Assert.Multiple(() =>
            {
                Assert.That(providers.OfType<FakeLoggerProvider>(), Is.Not.Empty);
                Assert.That(providers.Select(provider => provider.GetType().FullName), Has.Some.Contains("Serilog"));
            });
        }

        [Test]
        public void UseCloudstrapObservability_WithDefaultLevel_DisablesDebugForApplicationCategories()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(MinimalValid());
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Contoso.Orders.Service");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(logger.IsEnabled(LogLevel.Information), Is.True);
                Assert.That(logger.IsEnabled(LogLevel.Debug), Is.False);
            });
        }

        [Test]
        public void UseCloudstrapObservability_SeedsFrameworkCategoriesAtWarning()
        {
            // Arrange
            HostApplicationBuilder builder = CreateBuilder(MinimalValid());
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Microsoft.AspNetCore.Routing.Matching");

            // Assert
            Assert.That(logger.IsEnabled(LogLevel.Information), Is.False);
        }

        [Test]
        public void UseCloudstrapObservability_WithLevelOverrideOnSeededCategory_ConsumerOverrideWins()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Logging:LevelOverrides:Microsoft.AspNetCore"] = "Debug";
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("Microsoft.AspNetCore.Routing.Matching");

            // Assert
            Assert.That(logger.IsEnabled(LogLevel.Debug), Is.True);
        }

        [Test]
        public void UseCloudstrapObservability_WithFileLoggingEnabled_WritesUnderExactlyTheConfiguredPath()
        {
            // Arrange
            string tempDirectory = Path.Combine(Path.GetTempPath(), $"cloudstrap-host-{Guid.NewGuid():N}");
            try
            {
                Dictionary<string, string?> values = MinimalValid();
                values["Cloudstrap:Logging:File:Enabled"] = "true";
                values["Cloudstrap:Logging:File:Path"] = tempDirectory;
                HostApplicationBuilder builder = CreateBuilder(values);
                builder.UseCloudstrapObservability();

                // Act
                using (IHost host = builder.Build())
                {
                    host.Services.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Contoso.Orders.Api")
                        .LogInformation("Host file event");
                }

                // Assert
                string[] logFiles = Directory.GetFiles(tempDirectory, "*.log", SearchOption.TopDirectoryOnly);
                Assert.Multiple(() =>
                {
                    Assert.That(logFiles, Has.Length.EqualTo(1));
                    Assert.That(File.ReadAllText(logFiles[0]), Does.Contain("Host file event"));
                });
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void UseCloudstrapObservability_WithInvalidCloudstrapSection_ThrowsConfigurationValidationException()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values.Remove("Cloudstrap:Application:SystemName");
            HostApplicationBuilder builder = CreateBuilder(values);

            // Act & Assert
            Assert.Throws<ConfigurationValidationException>(() => builder.UseCloudstrapObservability());
        }

        [Test]
        public void UseCloudstrapObservability_WithConfigureSerilog_HasFinalSay()
        {
            // Arrange
            CollectingSink sink = new();
            HostApplicationBuilder builder = CreateBuilder(MinimalValid());
            builder.UseCloudstrapObservability(options =>
                options.ConfigureSerilog = loggerConfiguration => loggerConfiguration.WriteTo.Sink(sink));

            // Act
            using (IHost host = builder.Build())
            {
                host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Contoso.Orders.Api")
                    .LogInformation("Consumer sink event");
            }

            // Assert
            Assert.That(sink.Messages, Has.Some.Contains("Consumer sink event"));
        }

        [Test]
        public void UseCloudstrapObservability_CalledOnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            IHostApplicationBuilder builder = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.UseCloudstrapObservability());
        }

        [Test]
        public void UseCloudstrapObservability_ReturnsBuilderExposingServicesAndTelemetry()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Console";
            HostApplicationBuilder builder = CreateBuilder(values);

            // Act
            CloudstrapObservabilityBuilder result = builder.UseCloudstrapObservability();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(result.Services, Is.SameAs(builder.Services));
                Assert.That(result.Telemetry.Mode, Is.EqualTo(OpenTelemetryMode.Console));
            });
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);

            return builder;
        }

        private sealed class FakeLoggerProvider : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

            public void Dispose()
            {
            }
        }

        private sealed class CollectingSink : Serilog.Core.ILogEventSink
        {
            public List<string> Messages { get; } = [];

            public void Emit(Serilog.Events.LogEvent logEvent) =>
                Messages.Add(logEvent.RenderMessage(CultureInfo.InvariantCulture));
        }
    }
}
