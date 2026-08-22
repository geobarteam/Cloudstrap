namespace Cloudstrap.Mvc.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Session;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Mvc</c>: AC-MVC12/AC-ASP2/AC-A3
    /// (dependency closure — no Aspire, no security-headers library, none of the dropped legacy packages,
    /// and none of #5's versioning/OpenAPI stack), the De-NIHDI identifier rule, and the type-level drops
    /// this deliverable made permanent (AC-MVC4, AC-MVC7).
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _mvcAssembly = typeof(MvcPipelineOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|dynatrace|nservicebus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void ReferencedAssemblies_OfMvcAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _mvcAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act — this closure is smaller than #5's: no versioning/OpenAPI stack may leak in
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && name != "Cloudstrap.Core"
                && name != "Cloudstrap.Observability"
                && name != "Cloudstrap.Extensions")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("NWebsec", StringComparison.Ordinal)
                || name.StartsWith("NetEscapades", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal)
                || name.StartsWith("NSwag", StringComparison.Ordinal)
                || name.StartsWith("Duende", StringComparison.Ordinal)
                || name.StartsWith("Asp.Versioning", StringComparison.Ordinal)
                || name.StartsWith("Scalar", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_OfMvcAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _mvcAssembly.GetExportedTypes();

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
        public void PublicTypes_OfMvcAssembly_AreSealedOrStaticAndInTheSingleApprovedNamespace()
        {
            // Arrange
            Type[] publicTypes = _mvcAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes, Is.Not.Empty);
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.Mvc"),
                    Is.Empty,
                    "Every public type must live in the Cloudstrap.Mvc namespace.");
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
        public void MvcAssembly_DeclaresNoSessionOrCorrelationImplementationTypes()
        {
            // Arrange — the D-1 and finding-5 drops, guarded forever: this package owns no session
            // middleware/store/cookie-protection code and no correlation code (SessionSettings is the
            // one allowed options type)
            Type[] declared = _mvcAssembly.GetTypes();

            // Act
            string[] implementors =
            [
                .. declared
                    .Where(type => typeof(ISession).IsAssignableFrom(type)
                        || typeof(ISessionStore).IsAssignableFrom(type))
                    .Select(type => type.Name),
            ];
            string[] offenders =
            [
                .. declared
                    .Select(type => type.Name)
                    .Where(name => name.Contains("SessionMiddleware", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("CookieProtection", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Correlation", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(implementors, Is.Empty, $"ISession/ISessionStore: {string.Join(", ", implementors)}");
                Assert.That(offenders, Is.Empty, $"Dropped concepts resurfaced as types: {string.Join(", ", offenders)}");
            });
        }
    }
}
