namespace Cloudstrap.Core.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Core</c>:
    /// AC-C8/AC-ASP2 (dependency closure) and AC-C7 (no enterprise identifiers).
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _coreAssembly = typeof(CloudstrapOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers =
            new("nihdi|riziv|dynatrace", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void ReferencedAssemblies_OfCoreAssembly_AreSystemOrMicrosoftExtensionsOnly()
        {
            // Arrange
            IEnumerable<string> referenced = _coreAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty);

            // Act
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))];

            // Assert — no Aspire.*, no LanguageExt.*, no ASP.NET Core assemblies may creep in.
            Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
        }

        [Test]
        public void PublicTypes_OfCoreAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _coreAssembly.GetExportedTypes();

            // Act
            string[] offenders =
            [
                .. publicTypes
                    .SelectMany(type => type
                        .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(member => $"{type.FullName}.{member.Name}")
                        .Prepend(type.FullName ?? string.Empty))
                    .Where(name => _forbiddenIdentifiers.IsMatch(name)),
            ];

            // Assert
            Assert.That(offenders, Is.Empty, $"Forbidden identifiers: {string.Join(", ", offenders)}");
        }

        [Test]
        public void PublicTypes_OfCoreAssembly_AreSealedAndInTheSingleCoreNamespace()
        {
            // Arrange
            Type[] publicTypes = _coreAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes, Is.Not.Empty);
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.Core"),
                    Is.Empty,
                    "Every public type must live in the single Cloudstrap.Core namespace.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed unless designed for inheritance.");
            });
        }
    }
}
