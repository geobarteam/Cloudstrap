namespace Cloudstrap.Observability.Tests
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    [TestFixture]
    public sealed class CloudstrapBootstrapLoggerTests
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
        public void Create_WithDefaults_WritesInformationToConsole()
        {
            // Arrange
            CloudstrapOptions options = BindOptions(MinimalValid());

            // Act
            using (ILoggerFactory factory = CloudstrapBootstrapLogger.Create(options))
            {
                factory.CreateLogger("Contoso.Orders.Api").LogInformation("Bootstrap information event");
            }

            // Assert
            string output = _consoleOutput.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.Contain("Bootstrap information event"));
                Assert.That(output, Does.Contain("INF"));
            });
        }

        [Test]
        public void Create_WithDefaults_SuppressesDebugBelowConfiguredLevel()
        {
            // Arrange
            CloudstrapOptions options = BindOptions(MinimalValid());

            // Act
            using (ILoggerFactory factory = CloudstrapBootstrapLogger.Create(options))
            {
                factory.CreateLogger("Contoso.Orders.Api").LogDebug("Bootstrap debug event");
            }

            // Assert
            Assert.That(_consoleOutput.ToString(), Does.Not.Contain("Bootstrap debug event"));
        }

        [Test]
        public void Create_WithLevelOverride_AppliesSourceContextOverride()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Logging:LevelOverrides:Contoso.Noisy"] = "Error";
            CloudstrapOptions options = BindOptions(values);

            // Act
            using (ILoggerFactory factory = CloudstrapBootstrapLogger.Create(options))
            {
                factory.CreateLogger("Contoso.Noisy.Component").LogInformation("Noisy information event");
                factory.CreateLogger("Contoso.Orders.Api").LogInformation("Quiet information event");
            }

            // Assert
            string output = _consoleOutput.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.Not.Contain("Noisy information event"));
                Assert.That(output, Does.Contain("Quiet information event"));
            });
        }

        [Test]
        public void Create_WithFileLoggingEnabled_WritesUnderExactlyTheConfiguredPath()
        {
            // Arrange
            string tempDirectory = Path.Combine(Path.GetTempPath(), $"cloudstrap-bootstrap-{Guid.NewGuid():N}");
            try
            {
                Dictionary<string, string?> values = MinimalValid();
                values["Cloudstrap:Logging:File:Enabled"] = "true";
                values["Cloudstrap:Logging:File:Path"] = tempDirectory;
                CloudstrapOptions options = BindOptions(values);

                // Act
                using (ILoggerFactory factory = CloudstrapBootstrapLogger.Create(options))
                {
                    factory.CreateLogger("Contoso.Orders.Api").LogInformation("Bootstrap file event");
                }

                // Assert
                string[] logFiles = Directory.GetFiles(tempDirectory, "*.log", SearchOption.TopDirectoryOnly);
                Assert.Multiple(() =>
                {
                    Assert.That(logFiles, Has.Length.EqualTo(1));
                    Assert.That(File.ReadAllText(logFiles[0]), Does.Contain("Bootstrap file event"));
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
        public void Create_WithConsoleDisabled_WritesNothingToConsole()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Logging:Console:Enabled"] = "false";
            CloudstrapOptions options = BindOptions(values);

            // Act
            using (ILoggerFactory factory = CloudstrapBootstrapLogger.Create(options))
            {
                factory.CreateLogger("Contoso.Orders.Api").LogInformation("Suppressed console event");
            }

            // Assert
            Assert.That(_consoleOutput.ToString(), Is.Empty);
        }

        [Test]
        public void Create_WithLevelNone_WritesNothing()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Logging:Level"] = "None";
            CloudstrapOptions options = BindOptions(values);

            // Act
            using (ILoggerFactory factory = CloudstrapBootstrapLogger.Create(options))
            {
                factory.CreateLogger("Contoso.Orders.Api").LogCritical("Critical event under None");
            }

            // Assert
            Assert.That(_consoleOutput.ToString(), Is.Empty);
        }

        [Test]
        public void Create_WithNullOptions_ThrowsArgumentNullException()
        {
            // Arrange
            CloudstrapOptions options = null!;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => CloudstrapBootstrapLogger.Create(options));
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static CloudstrapOptions BindOptions(Dictionary<string, string?> values) =>
            new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetCloudstrapOptions();
    }
}
