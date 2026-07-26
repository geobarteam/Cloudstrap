namespace Cloudstrap.Core.Tests
{
    using Microsoft.Extensions.Configuration;
    using NUnit.Framework;

    [TestFixture]
    public sealed class ApplicationOptionsTests
    {
        [Test]
        public void WorkloadName_WithoutExplicitValue_ComputesLowercaseSystemSubsystemType()
        {
            // Arrange
            ApplicationOptions options = Bind(new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "Orders",
                ["Cloudstrap:Application:SubsystemType"] = "Api",
            });

            // Act
            string workloadName = options.WorkloadName;

            // Assert
            Assert.That(workloadName, Is.EqualTo("contoso-orders-api"));
        }

        [Test]
        public void WorkloadName_WithExplicitConfigValue_OverridesComputation()
        {
            // Arrange
            ApplicationOptions options = Bind(new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "Orders",
                ["Cloudstrap:Application:SubsystemType"] = "Api",
                ["Cloudstrap:Application:WorkloadName"] = "my-workload",
            });

            // Act
            string workloadName = options.WorkloadName;

            // Assert
            Assert.That(workloadName, Is.EqualTo("my-workload"));
        }

        [TestCase("myapp", "/myapp")]
        [TestCase("/myapp/", "/myapp")]
        [TestCase("", "")]
        public void PathBase_WithConfiguredValue_NormalizesToLeadingSlashNoTrailing(string configured, string expected)
        {
            // Arrange
            ApplicationOptions options = Bind(new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:PathBase"] = configured,
            });

            // Act
            string pathBase = options.PathBase;

            // Assert
            Assert.That(pathBase, Is.EqualTo(expected));
        }

        [Test]
        public void SectionValues_WhenBound_PopulateIdentityProperties()
        {
            // Arrange
            ApplicationOptions options = Bind(new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "Orders",
                ["Cloudstrap:Application:SubsystemType"] = "Api",
                ["Cloudstrap:Application:EnvironmentTier"] = "acceptance",
                ["Cloudstrap:Application:ExceptionHandlerPath"] = "/oops",
            });

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(options.SystemName, Is.EqualTo("Contoso"));
                Assert.That(options.SubsystemName, Is.EqualTo("Orders"));
                Assert.That(options.SubsystemType, Is.EqualTo("Api"));
                Assert.That(options.EnvironmentTier, Is.EqualTo("acceptance"));
                Assert.That(options.ExceptionHandlerPath, Is.EqualTo("/oops"));
            });
        }

        [Test]
        public void Defaults_WithoutConfiguredValues_HoldDocumentedValues()
        {
            // Arrange
            var options = new ApplicationOptions();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(options.ExceptionHandlerPath, Is.EqualTo("/error"));
                Assert.That(options.EnvironmentTier, Is.Null);
                Assert.That(options.PathBase, Is.Empty);
                Assert.That(ApplicationOptions.SectionName, Is.EqualTo("Cloudstrap:Application"));
            });
        }

        private static ApplicationOptions Bind(Dictionary<string, string?> values)
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(values)
                .Build();

            return configuration.GetSection(ApplicationOptions.SectionName).Get<ApplicationOptions>()
                ?? new ApplicationOptions();
        }
    }
}
