namespace Cloudstrap.Proxy.Tests
{
    using System.Xml.Linq;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwire on deliverable #17's dependency posture: <c>Yarp.ReverseProxy</c> is pinned at or above the
    /// 2.3.0 floor (the destination-resolver hook and the forwarder APIs the package uses), and neither the package
    /// nor its test project references an Aspire, service-discovery, Duende or enterprise-auth package, nor either
    /// Cloudstrap authentication package (AC-ASP2, AC-A3, D-8; spec finding 1).
    /// </summary>
    [TestFixture]
    public sealed class DependencyPinTests
    {
        private static readonly string[] _forbiddenPackagePrefixes =
            ["Aspire.", "Microsoft.Extensions.ServiceDiscovery", "Duende.", "Nihdi."];

        private static readonly string[] _forbiddenProjectPrefixes = ["Cloudstrap.Authentication."];

        [Test]
        public void YarpReverseProxy_IsPinned_AtOrAbove_2_3_0()
        {
            // Arrange
            string propsPath = Path.Combine(FindRepoRoot(), "src", "Directory.Packages.props");
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
        public void ProxyPackage_ReferencesNoAspireServiceDiscoveryDuendeOrNihdiAuthPackage(string projectPath)
        {
            // Arrange
            XDocument project = XDocument.Load(Path.Combine(FindRepoRoot(), projectPath));

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

        private static string FindRepoRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Cloudstrap.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root (a directory containing src/Cloudstrap.sln) above '{AppContext.BaseDirectory}'.");
        }
    }
}
