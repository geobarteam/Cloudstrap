namespace Cloudstrap.WebApi.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.WebApi</c>: AC-W14/AC-ASP2/AC-A3
    /// (dependency closure — no Aspire, no NSwag or Swashbuckle, none of the dropped legacy packages), the
    /// De-NIHDI identifier rule, and the two type-level drops this deliverable made permanent.
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _webApiAssembly = typeof(WebApiOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|dynatrace|nservicebus|swagger",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void ReferencedAssemblies_OfWebApiAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _webApiAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !name.StartsWith("Asp.Versioning", StringComparison.Ordinal)
                && !name.StartsWith("Scalar.", StringComparison.Ordinal)
                && name != "Cloudstrap.Core"
                && name != "Cloudstrap.Observability"
                && name != "Cloudstrap.Extensions")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("NSwag", StringComparison.Ordinal)
                || name.StartsWith("Swashbuckle", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal)
                || name.StartsWith("NWebsec", StringComparison.Ordinal)
                || name.StartsWith("Duende", StringComparison.Ordinal)
                || name.StartsWith("LanguageExt", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_OfWebApiAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _webApiAssembly.GetExportedTypes();

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
        public void PublicTypes_OfWebApiAssembly_AreSealedOrStaticAndInTheSingleApprovedNamespace()
        {
            // Arrange
            Type[] publicTypes = _webApiAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes, Is.Not.Empty);
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.WebApi"),
                    Is.Empty,
                    "Every public type must live in the Cloudstrap.WebApi namespace.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static.");
                Assert.That(
                    publicTypes.Where(type => type.IsInterface),
                    Is.Empty,
                    "This package publishes no interfaces.");
            });
        }

        [Test]
        public void WebApiAssembly_DeclaresNoTypeNamedForSwaggerOrCorrelation()
        {
            // Arrange — the NSwag stack and the duplicate correlation middleware are dropped for good
            string[] declared = [.. _webApiAssembly.GetTypes().Select(type => type.Name)];

            // Act
            string[] offenders =
            [
                .. declared.Where(name =>
                    name.Contains("Swagger", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Correlation", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert
            Assert.That(offenders, Is.Empty, $"Dropped concepts resurfaced as types: {string.Join(", ", offenders)}");
        }
    }
}
