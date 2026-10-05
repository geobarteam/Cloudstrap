namespace Cloudstrap.TestInfrastructure.Tests
{
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// #8 D-6: the hoisted host builder supplies the neutral, valid <c>Cloudstrap:Application</c> section the
    /// messaging suites build their generic hosts on.
    /// </summary>
    [TestFixture]
    public sealed class CloudstrapTestHostTests
    {
        [Test]
        public void ValidSettings_IsTheNeutralContosoOrdersWorkerApplicationSection()
        {
            // Act
            Dictionary<string, string?> settings = CloudstrapTestHost.ValidSettings();

            // Assert
            Assert.That(settings, Is.EquivalentTo(new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "contoso",
                ["Cloudstrap:Application:SubsystemName"] = "orders",
                ["Cloudstrap:Application:SubsystemType"] = "worker",
            }));
        }

        [Test]
        public void ValidSettings_ReturnsAFreshDictionaryPerCall()
        {
            // Arrange
            Dictionary<string, string?> first = CloudstrapTestHost.ValidSettings();

            // Act
            first["Cloudstrap:Application:SystemName"] = "mutated";
            Dictionary<string, string?> second = CloudstrapTestHost.ValidSettings();

            // Assert
            Assert.That(second["Cloudstrap:Application:SystemName"], Is.EqualTo("contoso"));
        }

        [Test]
        public void CreateBuilder_ConfigurationCarriesTheGivenSettings()
        {
            // Arrange
            Dictionary<string, string?> settings = CloudstrapTestHost.ValidSettings();
            settings["Contoso:Feature"] = "on";

            // Act
            HostApplicationBuilder builder = CloudstrapTestHost.CreateBuilder(settings);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(builder.Configuration["Cloudstrap:Application:SubsystemName"], Is.EqualTo("orders"));
                Assert.That(builder.Configuration["Contoso:Feature"], Is.EqualTo("on"));
            });
        }

        [Test]
        public void CreateBuilder_NullSettings_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => CloudstrapTestHost.CreateBuilder(null!));
        }

        [Test]
        public void WorkloadName_IsContosoOrdersWorker()
        {
            // Act & Assert
            Assert.That(CloudstrapTestHost.WorkloadName, Is.EqualTo("contoso-orders-worker"));
        }
    }
}
