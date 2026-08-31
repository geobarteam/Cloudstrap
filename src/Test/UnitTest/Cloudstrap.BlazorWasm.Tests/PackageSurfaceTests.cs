namespace Cloudstrap.BlazorWasm.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.BlazorWasm</c>: the
    /// WASM-linker-safe closure (AC-BW8 — no FrameworkReference, no server-side ASP.NET Core
    /// assembly; AC-ASP2 — no Aspire; AC-A3 — no Nihdi; DL-1 — no <c>Cloudstrap.*</c>), the
    /// De-NIHDI identifier rule including <c>Cfe</c>, the exact six-type public surface, and the
    /// dropped concepts made permanent (D-1, DL-4, the skill-drift ghosts).
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _blazorWasmAssembly = typeof(CookieHandler).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|cfe|dynatrace|nservicebus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] _approvedPublicTypeNames =
        [
            "CloudstrapBlazorWasmOptions",
            "CookieHandler",
            "IAntiforgeryTokenStore",
            "IBffAuthenticationStateProvider",
            "ServiceCollectionExtensions",
            "WebAssemblyHostBuilderExtensions",
        ];

        private static readonly string[] _approvedInterfaceNames =
            ["IAntiforgeryTokenStore", "IBffAuthenticationStateProvider"];

        [Test]
        public void ReferencedAssemblies_OfBlazorWasmAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _blazorWasmAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act — the closure is the four NuGet packages plus their observed Components/Authorization
            // transitives; anything server-side, Aspire-side or Cloudstrap-side is forbidden forever
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && name != "netstandard"
                && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal)
                && name != "Microsoft.AspNetCore.Components.WebAssembly"
                && name != "Microsoft.AspNetCore.Components.Authorization"
                && name != "Microsoft.AspNetCore.Components"
                && name != "Microsoft.AspNetCore.Authorization"
                && name != "Microsoft.AspNetCore.Metadata"
                && name != "Refit")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal)
                || name.StartsWith("Duende", StringComparison.Ordinal)
                || name.StartsWith("MudBlazor", StringComparison.Ordinal)
                || name.StartsWith("Scrutor", StringComparison.Ordinal)
                || name.StartsWith("Cloudstrap", StringComparison.Ordinal)
                || name == "Microsoft.AspNetCore.App"
                || name.StartsWith("Microsoft.AspNetCore.Antiforgery", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal)
                || name.StartsWith("Microsoft.JSInterop", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_OfBlazorWasmAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _blazorWasmAssembly.GetExportedTypes();

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
        public void PublicSurface_IsExactlyTheSixApprovedTypes()
        {
            // Arrange
            Type[] publicTypes = _blazorWasmAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    publicTypes.Select(type => type.Name),
                    Is.EquivalentTo(_approvedPublicTypeNames));
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.BlazorWasm"),
                    Is.Empty,
                    "Every public type must live in the Cloudstrap.BlazorWasm namespace.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static.");
                Assert.That(
                    publicTypes.Where(type => type.IsInterface).Select(type => type.Name),
                    Is.EquivalentTo(_approvedInterfaceNames),
                    "Exactly the two approved interfaces (the D-8 internals made permanent).");
            });
        }

        [Test]
        public void BlazorWasmAssembly_DeclaresNoDroppedConcepts()
        {
            // Arrange — D-1/DL-4 (no localization or culture machinery), the dropped BlazorWasmOptions
            // wrapper, and the skill-drift ghosts (AddRefitClientWithCookies, PathBase*) stay dead
            Type[] declared = _blazorWasmAssembly.GetTypes();

            // Act
            string[] droppedTypes =
            [
                .. declared
                    .Select(type => type.Name)
                    .Where(name => name.Contains("Localization", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Culture", StringComparison.OrdinalIgnoreCase)
                        || (name.Contains("BlazorWasmOptions", StringComparison.Ordinal)
                            && name != "CloudstrapBlazorWasmOptions")),
            ];
            string[] droppedMethods =
            [
                .. declared
                    .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    .Select(method => method.Name)
                    .Where(name => name.Contains("Localization", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Culture", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("AddRefitClientWithCookies", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("PathBase", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(droppedTypes, Is.Empty, $"Dropped concepts resurfaced as types: {string.Join(", ", droppedTypes)}");
                Assert.That(droppedMethods, Is.Empty, $"Dropped concepts resurfaced as methods: {string.Join(", ", droppedMethods)}");
            });
        }
    }
}
