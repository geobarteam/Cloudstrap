namespace Cloudstrap.Extensions.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Extensions</c>: AC-E14/AC-ASP2
    /// (dependency closure — no Aspire, no authentication stack, none of the dropped legacy packages) and the
    /// De-NIHDI identifier rule.
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _extensionsAssembly = typeof(KeyVaultOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|dynatrace|nservicebus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void ReferencedAssemblies_OfExtensionsAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _extensionsAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !name.StartsWith("Azure.", StringComparison.Ordinal)
                && !name.StartsWith("HealthChecks.", StringComparison.Ordinal)
                && name != "Cloudstrap.Core"
                && name != "Cloudstrap.Observability")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("Duende", StringComparison.Ordinal)
                || name.StartsWith("NWebsec", StringComparison.Ordinal)
                || name.StartsWith("LanguageExt", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal))];

            // Assert — the token seam is an interface this package declares, never an auth dependency
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_OfExtensionsAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _extensionsAssembly.GetExportedTypes();

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
        public void PublicTypes_OfExtensionsAssembly_AreSealedOrStaticAndInTheSingleApprovedNamespace()
        {
            // Arrange
            Type[] publicTypes = _extensionsAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes, Is.Not.Empty);
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.Extensions"),
                    Is.Empty,
                    "Every public type must live in the Cloudstrap.Extensions namespace.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static.");
                Assert.That(
                    publicTypes.Where(type => type.IsInterface).Select(type => type.Name).Order(StringComparer.Ordinal),
                    Is.EqualTo(new[]
                    {
                        nameof(IClientAccessTokenHandlerProvider),
                        nameof(IUserAccessTokenHandlerProvider),
                    }),
                    "The two access token handler seams are the only interfaces this package publishes.");
            });
        }
    }
}
