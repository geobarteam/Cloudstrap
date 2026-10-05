namespace Cloudstrap.TestInfrastructure.Tests
{
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    using Microsoft.Data.SqlClient;
    using NUnit.Framework;

    /// <summary>
    /// Permanent repository tripwires of deliverable #8: the shared test infrastructure stays a test-only, never-packed
    /// library that nothing shipped depends on (AC-TS11, AC-ASP2), and the consolidation it carries out — per-suite
    /// catalogs, one declaration per hoisted type, one forwarding site for the SQL override — cannot silently regress.
    /// The scans read the working tree (bin/obj excluded) and skip this suite's own directory, which necessarily
    /// contains the literals it forbids.
    /// </summary>
    [TestFixture]
    public sealed partial class RepositoryHygieneTests
    {
        private const string _libraryName = "Cloudstrap.TestInfrastructure";

        private static readonly string[] _suiteCatalogs = ["CloudstrapMessagingTests", "CloudstrapClaimCheckTests", "CloudstrapHangfireTests"];
        private static readonly string[] _demoDatabaseFile = ["DemoDatabase.cs"];

        private static readonly Lazy<string> _repoRoot = new(FindRepoRoot);

        private static string Src => Path.Combine(_repoRoot.Value, "src");

        private static string LibraryProject =>
            Path.Combine(Src, "Test", "TestInfrastructure", _libraryName, _libraryName + ".csproj");

        [Test]
        public void TestInfrastructureLibrary_LivesUnderSrcTest_AndIsNotATestExecutable()
        {
            // Arrange
            Assert.That(File.Exists(LibraryProject), Is.True, LibraryProject);
            XDocument project = XDocument.Load(LibraryProject);

            // Act
            string[] packingTrue = [.. project
                .Descendants()
                .Where(element => element.Name.LocalName is "IsPackable" or "GeneratePackageOnBuild")
                .Where(element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                .Select(element => element.Name.LocalName)];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(Path.GetFileNameWithoutExtension(LibraryProject), Does.Not.EndWith(".Tests"));
                Assert.That(packingTrue, Is.Empty);
            });
        }

        [Test]
        public void TestInfrastructureLibrary_ReferencesNoTestFrameworkMockingOrAspirePackage()
        {
            // Arrange
            string[] forbiddenPrefixes = ["NUnit", "Moq", "Aspire.", "Microsoft.AspNetCore.Mvc.Testing", "Microsoft.EntityFrameworkCore"];

            // Act
            string[] forbidden = [.. PackageReferences(LibraryProject)
                .Where(id => forbiddenPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))];

            // Assert
            Assert.That(forbidden, Is.Empty);
        }

        [Test]
        public void NoShippedPackageOrDemoApp_ReferencesTheTestInfrastructure()
        {
            // Arrange
            IEnumerable<string> shipped = Directory
                .EnumerateDirectories(Src, "Cloudstrap.*")
                .SelectMany(directory => SourceFiles(directory, "*.csproj"));
            IEnumerable<string> demo = SourceFiles(Path.Combine(Src, "demo"), "*.csproj");

            // Act
            string[] offenders = [.. shipped.Concat(demo)
                .Where(path => File.ReadAllText(path).Contains(_libraryName, StringComparison.Ordinal))
                .Select(Relative)];

            // Assert
            Assert.That(offenders, Is.Empty);
        }

        [TestCase("Cloudstrap.Hangfire.Tests", "CloudstrapHangfireTests")]
        [TestCase("Cloudstrap.Messaging.Tests", "CloudstrapMessagingTests")]
        [TestCase("Cloudstrap.Messaging.AzureBlob.Tests", "CloudstrapClaimCheckTests")]
        public void SqlSuite_DeclaresItsOwnCatalog_ThroughTheSharedHelper(string suite, string catalog)
        {
            // Arrange
            string suiteDirectory = Path.Combine(Src, "Test", "UnitTest", suite);
            string[] sources = [.. SourceFiles(suiteDirectory, "*.cs")];

            // Act
            int declarations = sources.Sum(path => CountCatalogDeclarations(File.ReadAllText(path), catalog));
            string[] localCopies = [.. sources.Where(path => Path.GetFileName(path) == "SqlServerTestDatabase.cs").Select(Relative)];
            string[] linkedCopies = [.. SourceFiles(suiteDirectory, "*.csproj")
                .SelectMany(project => XDocument.Load(project).Descendants())
                .Where(element => element.Name.LocalName == "Compile")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
                .Where(include => include.Contains("SqlServerTestDatabase", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(declarations, Is.EqualTo(1), $"Declarations of catalog {catalog} in {suite}");
                Assert.That(localCopies, Is.Empty, "Local SqlServerTestDatabase copies");
                Assert.That(linkedCopies, Is.Empty, "Linked SqlServerTestDatabase copies");
            });
        }

        [Test]
        public void HangfireSuite_HasNoTypeNamedInvocationRecorder()
        {
            // Act
            string[] offenders = [.. SourceFiles(Path.Combine(Src, "Test", "UnitTest", "Cloudstrap.Hangfire.Tests"), "*.cs")
                .Where(path => File.ReadAllText(path).Contains("class InvocationRecorder", StringComparison.Ordinal))
                .Select(Relative)];

            // Assert
            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void SqlSuites_NeverShareACatalog()
        {
            // Arrange
            Regex declaration = CatalogDeclaration();

            // Act
            string[] catalogs = [.. SourceFiles(Path.Combine(Src, "Test", "UnitTest"), "*.cs")
                .SelectMany(path => declaration.Matches(File.ReadAllText(path)))
                .Select(match => match.Groups["catalog"].Value)];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(catalogs, Is.Unique);
                Assert.That(catalogs, Is.EquivalentTo(_suiteCatalogs));
            });
        }

        [TestCase("class SqlServerTestDatabase")]
        [TestCase("class InvocationRecorder")]
        [TestCase("class CloudstrapTestHost")]
        public void SharedHelper_IsDeclaredExactlyOnce_InTheTestInfrastructure(string declaration)
        {
            // Act
            string[] declaringFiles = [.. SourceFiles(Src, "*.cs")
                .Where(path => Regex.IsMatch(File.ReadAllText(path), $@"\b{Regex.Escape(declaration)}\b"))
                .Select(Relative)];

            // Assert
            Assert.That(declaringFiles, Has.Length.EqualTo(1).And.All.StartsWith(Path.Combine("src", "Test", "TestInfrastructure", _libraryName)));
        }

        [Test]
        public void MessagingTestHost_IsDeclaredNowhere()
        {
            // Act
            string[] offenders = [.. SourceFiles(Src, "*.cs")
                .Where(path => File.ReadAllText(path).Contains("MessagingTestHost", StringComparison.Ordinal))
                .Select(Relative)];

            // Assert
            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void E2eSuite_ReadsTheSqlOverrideOnlyThroughTheSharedHelper()
        {
            // Arrange
            string[] sources = [.. SourceFiles(Path.Combine(Src, "Test", "E2E"), "*.cs")];

            // Act
            string[] inlineReads = [.. sources
                .Where(path => File.ReadAllText(path).Contains(SqlServerTestDatabase.EnvironmentVariable, StringComparison.Ordinal))
                .Select(Relative)];
            string[] forwardingSites = [.. sources
                .Where(path => File.ReadAllText(path).Contains(nameof(SqlServerTestDatabase.OverrideConnectionStringFor), StringComparison.Ordinal))
                .Select(path => Path.GetFileName(path))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(inlineReads, Is.Empty, "Inline CLOUDSTRAP_TEST_SQL reads");
                Assert.That(forwardingSites, Is.EqualTo(_demoDatabaseFile), "Forwarding sites");
            });
        }

        [TestCase("ci.yml")]
        [TestCase("release.yml")]
        public void Workflow_SqlOverride_CarriesNoCatalog(string workflow)
        {
            // Arrange
            string[] lines = WorkflowLines(workflow);

            // Act
            string value = OverrideValue(lines[SqlOverrideLine(lines)]);

            // Assert
            Assert.That(new SqlConnectionStringBuilder(value).InitialCatalog, Is.Empty, value);
        }

        [Test]
        public void CiWorkflow_SqlOverrideComment_NamesEveryHonoringSuite()
        {
            // Arrange
            string[] lines = WorkflowLines("ci.yml");

            // Act
            string comment = lines[SqlOverrideLine(lines) - 1].Trim();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(comment, Does.StartWith("#"));
                Assert.That(comment, Does.Contain("Messaging").And.Contain("AzureBlob").And.Contain("Hangfire").And.Contain("E2E"));
            });
        }

        private static string[] WorkflowLines(string workflow)
        {
            return File.ReadAllLines(Path.Combine(_repoRoot.Value, ".github", "workflows", workflow));
        }

        private static int SqlOverrideLine(string[] lines)
        {
            int index = Array.FindIndex(lines, line => line.TrimStart().StartsWith(SqlServerTestDatabase.EnvironmentVariable + ":", StringComparison.Ordinal));
            Assert.That(index, Is.Positive, "No CLOUDSTRAP_TEST_SQL entry in the workflow");
            return index;
        }

        private static string OverrideValue(string line)
        {
            return line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim().Trim('"');
        }

        private static int CountCatalogDeclarations(string source, string catalog)
        {
            string literal = $"(\"{catalog}\")";
            return CountOccurrences(source, "new" + literal) + CountOccurrences(source, "new SqlServerTestDatabase" + literal);
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        private static IEnumerable<string> PackageReferences(string projectPath)
        {
            return XDocument.Load(projectPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => (string?)element.Attribute("Include") ?? string.Empty);
        }

        /// <summary>Enumerates files under <paramref name="directory"/>, skipping bin/obj and this suite's own directory.</summary>
        private static IEnumerable<string> SourceFiles(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            string self = Path.Combine(Src, "Test", "UnitTest", "Cloudstrap.TestInfrastructure.Tests") + Path.DirectorySeparatorChar;
            return Directory
                .EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .Where(path => !path.StartsWith(self, StringComparison.OrdinalIgnoreCase))
                .Where(path => !IsBuildOutput(path));
        }

        private static bool IsBuildOutput(string path)
        {
            string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return segments.Any(segment => segment is "bin" or "obj");
        }

        private static string Relative(string path)
        {
            return Path.GetRelativePath(_repoRoot.Value, path);
        }

        /// <summary>A suite's catalog declaration: <c>new("X")</c> on a <c>SqlServerTestDatabase</c> holder, or <c>new SqlServerTestDatabase("X")</c>.</summary>
        [GeneratedRegex(@"(?:SqlServerTestDatabase\s+\w+\s*\{[^}]*\}\s*=\s*new|new\s+SqlServerTestDatabase)\(""(?<catalog>[A-Za-z0-9_]+)""\)")]
        private static partial Regex CatalogDeclaration();

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
