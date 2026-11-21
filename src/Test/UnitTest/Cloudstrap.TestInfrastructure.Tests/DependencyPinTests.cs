namespace Cloudstrap.TestInfrastructure.Tests
{
    using System.Xml.Linq;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwire on deliverable #8's central package pin (D-3): Microsoft's fake logger replaces every
    /// hand-written log-capture copy at the reviewed version, and no Aspire package is ever pinned (AC-ASP2).
    /// </summary>
    [TestFixture]
    public sealed class DependencyPinTests
    {
        private static readonly Lazy<Dictionary<string, string>> _pins = new(ReadPins);

        [Test]
        public void DiagnosticsTesting_IsPinnedAt10_8_0()
        {
            // Act
            bool pinned = _pins.Value.TryGetValue("Microsoft.Extensions.Diagnostics.Testing", out string? version);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(pinned, Is.True, "Microsoft.Extensions.Diagnostics.Testing is not pinned");
                Assert.That(version, Is.EqualTo("10.8.0"));
            });
        }

        [Test]
        public void NoAspirePackage_IsPinned()
        {
            // Act
            string[] aspire = [.. _pins.Value.Keys.Where(id => id.StartsWith("Aspire.", StringComparison.OrdinalIgnoreCase))];

            // Assert
            Assert.That(aspire, Is.Empty);
        }

        private static Dictionary<string, string> ReadPins()
        {
            string propsPath = Path.Combine(FindRepoRoot(), "src", "Directory.Packages.props");
            XDocument props = XDocument.Load(propsPath);

            return props
                .Descendants("PackageVersion")
                .Select(element => (
                    Id: (string?)element.Attribute("Include") ?? string.Empty,
                    Version: (string?)element.Attribute("Version") ?? string.Empty))
                .ToDictionary(pin => pin.Id, pin => pin.Version, StringComparer.Ordinal);
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

            throw new InvalidOperationException("Repository root (containing src/Cloudstrap.sln) not found.");
        }
    }
}
