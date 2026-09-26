namespace Cloudstrap.Hangfire.Tests
{
    using System.Xml.Linq;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwire on the central package pins of deliverable #16 (DD-7, D-4): the three LGPL-3.0
    /// <c>Hangfire.*</c> packages move in lockstep at the reviewed version, the SQL client is pinned, and no
    /// in-memory, commercial or Aspire Hangfire package is ever pinned.
    /// </summary>
    [TestFixture]
    public sealed class DependencyPinTests
    {
        private static readonly string[] _hangfireFamily = ["Hangfire.Core", "Hangfire.SqlServer", "Hangfire.AspNetCore"];
        private static readonly Lazy<Dictionary<string, string>> _pins = new(ReadPins);

        [Test]
        public void HangfireFamily_AllThreePackagesPinnedInLockstep_At1_8_25()
        {
            // Arrange
            Dictionary<string, string> pins = _pins.Value;

            // Act
            string?[] versions = [.. _hangfireFamily
                .Select(id => pins.TryGetValue(id, out string? version) ? version : null)];

            // Assert
            Assert.That(versions, Is.All.EqualTo("1.8.25"), $"Pinned: {Describe(pins, "Hangfire")}");
        }

        [Test]
        public void MicrosoftDataSqlClient_IsPinned()
        {
            // Act & Assert
            Assert.That(_pins.Value, Does.ContainKey("Microsoft.Data.SqlClient"));
        }

        [Test]
        public void NoInMemoryProCommercialOrAspireHangfirePackages_ArePinned()
        {
            // Arrange
            string[] forbiddenPrefixes = ["Hangfire.InMemory", "Hangfire.Pro", "Aspire."];

            // Act
            string[] forbidden = [.. _pins.Value.Keys
                .Where(id => forbiddenPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))];

            // Assert
            Assert.That(forbidden, Is.Empty);
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

        private static string Describe(Dictionary<string, string> pins, string prefix)
        {
            return string.Join(", ", pins.Where(pin => pin.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(pin => $"{pin.Key}={pin.Value}"));
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
