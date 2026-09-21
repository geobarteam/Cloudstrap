namespace Cloudstrap.Messaging.Tests
{
    using System.Xml.Linq;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwire on the Wolverine engine family's central package pins (deliverable #15, DL-7): every
    /// <c>WolverineFx.*</c> package moves in lockstep, the family sits at or above the release that carries
    /// the consolidated Azure Blob Storage claim-check store, that store's package is pinned, and the
    /// superseded <c>WolverineFx.ClaimCheck.*</c> packages never return.
    /// </summary>
    [TestFixture]
    public sealed class DependencyPinTests
    {
        /// <summary>
        /// The first release that ships <c>WolverineFx.AzureBlobStorage</c> as the claim-check store
        /// (6.32.0 consolidated the package; 6.33.0 is the floor the spec recorded).
        /// </summary>
        private static readonly Version _claimCheckConsolidationRelease = new(6, 33, 0);

        private static readonly Lazy<Dictionary<string, string>> _wolverinePins = new(ReadWolverinePins);

        [Test]
        public void WolverineFamily_AllPackageVersionsAreInLockstep()
        {
            // Arrange
            Dictionary<string, string> pins = _wolverinePins.Value;

            // Act
            string[] distinctVersions = [.. pins.Values.Distinct(StringComparer.Ordinal)];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(pins, Is.Not.Empty, "No WolverineFx.* PackageVersion entries were found.");
                Assert.That(
                    distinctVersions,
                    Has.Length.EqualTo(1),
                    $"The WolverineFx family must share one version; found: {Describe(pins)}");
            });
        }

        [Test]
        public void WolverineFamily_VersionIsAtLeastTheClaimCheckConsolidationRelease()
        {
            // Arrange
            string pinned = _wolverinePins.Value["WolverineFx"];

            // Act
            bool parsed = Version.TryParse(pinned, out Version? version);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.True, $"'{pinned}' is not a stable System.Version.");
                Assert.That(
                    version,
                    Is.GreaterThanOrEqualTo(_claimCheckConsolidationRelease),
                    $"WolverineFx is pinned at {pinned}; the Azure Blob claim-check store needs {_claimCheckConsolidationRelease} or later.");
            });
        }

        [Test]
        public void WolverineFamily_IncludesAzureBlobStorage_AndNeverTheSupersededClaimCheckPackage()
        {
            // Arrange
            Dictionary<string, string> pins = _wolverinePins.Value;

            // Act
            string[] superseded = [.. pins.Keys.Where(id => id.StartsWith("WolverineFx.ClaimCheck", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(pins, Does.ContainKey("WolverineFx.AzureBlobStorage"));
                Assert.That(superseded, Is.Empty, $"Superseded packages pinned: {string.Join(", ", superseded)}");
            });
        }

        private static Dictionary<string, string> ReadWolverinePins()
        {
            string propsPath = Path.Combine(FindRepoRoot(), "src", "Directory.Packages.props");
            XDocument props = XDocument.Load(propsPath);

            return props
                .Descendants("PackageVersion")
                .Select(element => (
                    Id: (string?)element.Attribute("Include") ?? string.Empty,
                    Version: (string?)element.Attribute("Version") ?? string.Empty))
                .Where(pin => pin.Id.StartsWith("WolverineFx", StringComparison.Ordinal))
                .ToDictionary(pin => pin.Id, pin => pin.Version, StringComparer.Ordinal);
        }

        private static string Describe(Dictionary<string, string> pins)
        {
            return string.Join(", ", pins.Select(pin => $"{pin.Key}={pin.Value}"));
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
