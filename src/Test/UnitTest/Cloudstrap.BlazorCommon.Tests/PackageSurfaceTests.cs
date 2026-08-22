namespace Cloudstrap.BlazorCommon.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.BlazorCommon</c>: the
    /// dependency closure (AC-ASP2 — no Aspire; AC-A3 — no Nihdi; AC-BC2 — no
    /// <c>Microsoft.AspNetCore</c>, this package references no Blazor package at all; AC-BC7 — no
    /// configuration stack; the standalone-leaf fact — no <c>Cloudstrap.*</c>), the De-NIHDI
    /// identifier rule, the exact four-type public surface, and the D-3/D-5 drops made permanent.
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _blazorCommonAssembly = typeof(IViewModel).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|dynatrace|nservicebus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] _approvedNamespaces = ["Cloudstrap.BlazorCommon"];

        private static readonly string[] _approvedPublicTypeNames =
            ["BlazorCommonOptions", "IErrorHandler", "IViewModel", "ServiceCollectionExtensions"];

        [Test]
        public void ReferencedAssemblies_OfBlazorCommonAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _blazorCommonAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act — the closure is framework + the DI abstractions + Scrutor, nothing else
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && name != "netstandard"
                && !name.StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal)
                && name != "Scrutor")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.Extensions.Configuration", StringComparison.Ordinal)
                || name.StartsWith("MudBlazor", StringComparison.Ordinal)
                || name.StartsWith("Cloudstrap", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_OfBlazorCommonAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _blazorCommonAssembly.GetExportedTypes();

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
        public void PublicSurface_IsExactlyTheFourApprovedTypes()
        {
            // Arrange
            Type[] publicTypes = _blazorCommonAssembly.GetExportedTypes();

            // Assert — the spec sketch's surface, one namespace, every public class sealed or static
            Assert.Multiple(() =>
            {
                Assert.That(
                    publicTypes.Select(type => type.Namespace).Distinct(),
                    Is.EquivalentTo(_approvedNamespaces));
                Assert.That(
                    publicTypes.Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal),
                    Is.EqualTo(_approvedPublicTypeNames));
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed),
                    Is.Empty,
                    "Every public class must be sealed or static.");
            });
        }

        [Test]
        public void BlazorCommonAssembly_DeclaresNoDroppedConcepts()
        {
            // Arrange
            Type[] types = _blazorCommonAssembly.GetTypes();

            // Assert — the D-3/D-5 drops made permanent
            Assert.Multiple(() =>
            {
                Assert.That(
                    types.Where(type =>
                        type.Name.Contains("Navigation", StringComparison.OrdinalIgnoreCase)
                        || type.Name.Contains("WasmControls", StringComparison.OrdinalIgnoreCase)),
                    Is.Empty);
                Assert.That(
                    types.SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Static))
                        .Where(member =>
                            (member is PropertyInfo property && property.PropertyType == typeof(Assembly[]))
                            || (member is FieldInfo field && field.FieldType == typeof(Assembly[]))),
                    Is.Empty,
                    "No static mutable assembly-registry pattern may return (D-5).");
            });
        }
    }
}
