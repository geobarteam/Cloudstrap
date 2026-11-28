namespace Cloudstrap.Proxy.Tests
{
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Cloudstrap.Proxy.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires on the vocabulary and the dependency closure of <c>Cloudstrap.Proxy</c> (AC-PX13, AC-ASP2,
    /// AC-A3, D-8): no enterprise identifier in the package or its tests, no secret or tenant value in its docs, and no
    /// Aspire, service-discovery or enterprise-auth package anywhere in its resolved closure. The scans read the working
    /// tree (bin/obj excluded) and skip this file, which necessarily contains the words it forbids.
    /// </summary>
    [TestFixture]
    public sealed partial class RepositoryHygieneTests
    {
        private static readonly string[] _forbiddenClosurePrefixes =
            ["Aspire.", "Microsoft.Extensions.ServiceDiscovery", "Nihdi."];

        private static string PackageDirectory => Path.Combine(RepositoryPaths.Root, "src", "Cloudstrap.Proxy");

        private static string TestDirectory => Path.Combine(RepositoryPaths.Root, "src", "Test", "UnitTest", "Cloudstrap.Proxy.Tests");

        [Test]
        public void ProxyFiles_CarryNoEnterpriseIdentifiers()
        {
            // Arrange
            string self = Path.Combine(TestDirectory, nameof(RepositoryHygieneTests) + ".cs");
            string[] files = [.. SourceFiles(PackageDirectory).Concat(SourceFiles(TestDirectory))
                .Where(path => !string.Equals(path, self, StringComparison.OrdinalIgnoreCase))];

            // Act
            string[] offenders = [.. files
                .Select(path => (Path: path, Match: EnterpriseIdentifier().Match(File.ReadAllText(path))))
                .Where(hit => hit.Match.Success)
                .Select(hit => $"{Path.GetRelativePath(RepositoryPaths.Root, hit.Path)}: '{hit.Match.Value}'")];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(files.Count(path => path.StartsWith(PackageDirectory, StringComparison.OrdinalIgnoreCase)), Is.Positive, "the package directory was not scanned");
                Assert.That(offenders, Is.Empty);
            });
        }

        [Test]
        public void ProxyDocs_CarryNoSecretOrTenantValue()
        {
            // Arrange
            string[] docs = [.. SourceFiles(PackageDirectory).Where(path => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))];

            // Act
            string[] offenders = [.. docs
                .Select(path => (Path: path, Match: SecretOrTenant().Match(File.ReadAllText(path))))
                .Where(hit => hit.Match.Success)
                .Select(hit => $"{Path.GetRelativePath(RepositoryPaths.Root, hit.Path)}: '{hit.Match.Value}'")];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(docs, Is.Not.Empty, "no package doc was scanned");
                Assert.That(offenders, Is.Empty);
            });
        }

        [Test]
        public void ProxyPackageClosure_HasNoAspireOrNihdiAuthPackage()
        {
            // Arrange: the restore output lists every package in the resolved closure under "libraries"
            string assetsPath = Path.Combine(PackageDirectory, "obj", "project.assets.json");
            Assert.That(File.Exists(assetsPath), Is.True, $"{assetsPath} is missing; restore the solution first.");
            using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));

            // Act
            string[] libraries = [.. assets.RootElement.GetProperty("libraries").EnumerateObject().Select(library => library.Name)];
            string[] forbidden = [.. libraries
                .Where(name => _forbiddenClosurePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(libraries, Has.Some.StartsWith("Yarp.ReverseProxy/"), "the closure was not read");
                Assert.That(forbidden, Is.Empty);
            });
        }

        private static IEnumerable<string> SourceFiles(string directory)
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment is "bin" or "obj"));
        }

        /// <summary>The enterprise vocabulary the De-NIHDI-fication checklist removes, in any case.</summary>
        [GeneratedRegex("nihdi|riziv|inami|wfe|cfe", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex EnterpriseIdentifier();

        /// <summary>
        /// A GUID (a tenant or client id), an Entra tenant domain, or a non-placeholder secret value in a JSON sample.
        /// </summary>
        [GeneratedRegex(
            @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|onmicrosoft\.com|""(ClientSecret|Secret|Password)""\s*:\s*""[^""<]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex SecretOrTenant();
    }
}
