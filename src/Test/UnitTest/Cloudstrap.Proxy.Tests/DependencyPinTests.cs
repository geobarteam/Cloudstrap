namespace Cloudstrap.Proxy.Tests
{
    using System.Xml.Linq;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwire on deliverable #17's dependency posture: <c>Yarp.ReverseProxy</c> is pinned at or above the
    /// 2.3.0 floor (the destination-resolver hook and the forwarder APIs the package uses), and neither the package
    /// nor its test project references an Aspire, service-discovery or Duende package, nor either Cloudstrap
    /// authentication package (AC-ASP2, D-8; spec finding 1). The enterprise-package rule (AC-A3) is checked over the
    /// whole resolved closure by <see cref="RepositoryHygieneTests"/>.
    /// </summary>
    [TestFixture]
    public sealed class DependencyPinTests
    {
        private static readonly string[] _forbiddenPackagePrefixes =
            ["Aspire.", "Microsoft.Extensions.ServiceDiscovery", "Duende."];

        private static readonly string[] _forbiddenProjectPrefixes = ["Cloudstrap.Authentication."];

        [Test]
        public void YarpReverseProxy_IsPinned_AtOrAbove_2_3_0()
        {
            // Arrange
            string propsPath = Path.Combine(RepositoryPaths.Root, "src", "Directory.Packages.props");
            XElement? pin = XDocument.Load(propsPath)
                .Descendants("PackageVersion")
                .SingleOrDefault(element => (string?)element.Attribute("Include") == "Yarp.ReverseProxy");

            // Act
            string? version = (string?)pin?.Attribute("Version");

            // Assert
            Assert.That(version, Is.Not.Null, "Yarp.ReverseProxy is not pinned in src/Directory.Packages.props.");
            Assert.That(Version.TryParse(version!.Split('-')[0], out Version? parsed), Is.True, $"Unparseable version '{version}'.");
            Assert.That(parsed, Is.GreaterThanOrEqualTo(new Version(2, 3, 0)));
        }

        [TestCase("src/Cloudstrap.Proxy/Cloudstrap.Proxy.csproj")]
        [TestCase("src/Test/UnitTest/Cloudstrap.Proxy.Tests/Cloudstrap.Proxy.Tests.csproj")]
        public void ProxyPackage_ReferencesNoAspireServiceDiscoveryDuendeOrAuthenticationPackage(string projectPath)
        {
            // Arrange
            XDocument project = XDocument.Load(Path.Combine(RepositoryPaths.Root, projectPath));

            // Act
            string[] forbiddenPackages = [.. project.Descendants("PackageReference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
                .Where(id => _forbiddenPackagePrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))];
            string[] forbiddenProjects = [.. project.Descendants("ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension((string?)element.Attribute("Include") ?? string.Empty))
                .Where(name => _forbiddenProjectPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(forbiddenPackages, Is.Empty);
                Assert.That(forbiddenProjects, Is.Empty);
            });
        }
    }
}
