namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of
    /// <c>Cloudstrap.Authentication.OpenIdConnect</c>: AC-OIDC11/AC-ASP2/AC-A3 (dependency closure —
    /// no OpenIddict, no EF Core, no Aspire, no legacy enterprise stack, and no dependency on the
    /// packages it merely composes with), the De-NIHDI identifier rule, and the approved public
    /// surface — made permanent.
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _packageAssembly = typeof(CloudstrapOpenIdConnectOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|keycloak",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] _approvedPublicTypeNames =
        [
            "CloudstrapAuthenticationCookieOptions",
            "CloudstrapOpenIdConnect",
            "CloudstrapOpenIdConnectConfigurator",
            "CloudstrapOpenIdConnectOptions",
            "EndpointRouteBuilderExtensions",
            "ServiceCollectionExtensions",
        ];

        [Test]
        public void ReferencedAssemblies_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _packageAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !name.StartsWith("Duende.", StringComparison.Ordinal)
                && name != "Cloudstrap.Core"
                && name != "Cloudstrap.Extensions")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("OpenIddict", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal)
                || name.StartsWith("NSwag", StringComparison.Ordinal)
                || name.StartsWith("LanguageExt", StringComparison.Ordinal)
                || name == "Cloudstrap.WebApi"
                || name == "Cloudstrap.Authentication.ClientCredentials")];

            // Assert — the test infrastructure's dependency set can never leak into the shipped
            // closure, and the packages this one composes with never become dependencies
            // (AC-OIDC11, AC-ASP2, AC-A3 made permanent)
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_AreSealedOrStaticAndInTheSingleApprovedNamespace()
        {
            // Arrange
            Type[] publicTypes = _packageAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes, Is.Not.Empty);
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.Authentication.OpenIdConnect"),
                    Is.Empty,
                    "Every public type must live in the Cloudstrap.Authentication.OpenIdConnect namespace.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static.");
                Assert.That(
                    publicTypes.Where(type => type.IsInterface),
                    Is.Empty,
                    "This package publishes no interfaces — the seam interfaces live in Cloudstrap.Extensions.");
            });
        }

        [Test]
        public void PublicTypes_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _packageAssembly.GetExportedTypes();

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
        public void PublicSurface_MatchesTheSpecSketch()
        {
            // Arrange
            string[] exportedNames = [.. _packageAssembly
                .GetExportedTypes()
                .Select(static type => type.Name)
                .Order(StringComparer.Ordinal)];

            // Assert — exactly the six types the spec's Public API Sketch §1 lists, so an accidental
            // promotion of an internal type is caught forever
            Assert.That(exportedNames, Is.EqualTo(_approvedPublicTypeNames));
        }
    }
}
