namespace Cloudstrap.BlazorServer.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.BlazorServer</c>: AC-BS9/AC-ASP2/
    /// AC-A3 (dependency closure — no Aspire, no security-headers library, no auth package, no
    /// BlazorCommon), the De-NIHDI identifier rule, the exact approved public surface, and the type-level
    /// drops this deliverable made permanent (no distributed-trace service, no automatic tracing handler,
    /// no BlazorServer typed-client API — AC-ASP3/AC-BS4).
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _blazorServerAssembly =
            typeof(CloudstrapBlazorServerOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|dynatrace|nservicebus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void ReferencedAssemblies_OfBlazorServerAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _blazorServerAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act — the closure is one ProjectReference wide; OpenTelemetry.Api* reaches it transitively
            // through Cloudstrap.Extensions for the deferred AddSource contribution, never a direct pin
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !name.StartsWith("OpenTelemetry.Api", StringComparison.Ordinal)
                && name != "Cloudstrap.Core"
                && name != "Cloudstrap.Observability"
                && name != "Cloudstrap.Extensions")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal)
                || name.StartsWith("NWebsec", StringComparison.Ordinal)
                || name.StartsWith("NetEscapades", StringComparison.Ordinal)
                || name.StartsWith("Duende", StringComparison.Ordinal)
                || name.StartsWith("Scrutor", StringComparison.Ordinal)
                || name.StartsWith("MudBlazor", StringComparison.Ordinal)
                || name == "Cloudstrap.BlazorCommon")];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_OfBlazorServerAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _blazorServerAssembly.GetExportedTypes();

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
        public void PublicSurface_IsExactlyTheTenApprovedTypes()
        {
            // Arrange — the eight Gate-1 types plus the two Gate-2 additions, nothing else, ever
            string[] approved =
            [
                nameof(BlazorInteractivity),
                nameof(BlazorServerActivitySources),
                nameof(BlazorServerPipelineOptions),
                nameof(CloudstrapBlazorServerConfigurator),
                nameof(CloudstrapBlazorServerOptions),
                nameof(ExceptionHandlingSettings),
                nameof(HstsSettings),
                nameof(IBlazorInteractionTrace),
                nameof(WebApplicationBuilderExtensions),
                nameof(WebApplicationExtensions),
            ];
            Type[] publicTypes = _blazorServerAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    publicTypes.Select(type => type.Name),
                    Is.EquivalentTo(approved));
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.BlazorServer"),
                    Is.Empty,
                    "Every public type must live in the Cloudstrap.BlazorServer namespace.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static.");
                Assert.That(
                    publicTypes.Where(type => type.IsInterface).Select(type => type.Name),
                    Is.EqualTo(new[] { nameof(IBlazorInteractionTrace) }),
                    "The interaction trace is the package's only public interface.");
            });
        }

        [Test]
        public void BlazorServerAssembly_DeclaresNoDroppedConcepts()
        {
            // Arrange — the Port-Decision drops made permanent: no distributed-trace service or generic
            // overloads, no assembly-registry Controls type, no SecurityHardeningOptions, no automatic
            // tracing handler (D-9), and no BlazorServer typed-client API (AC-ASP3/AC-BS4 — #4's
            // AddCloudstrapHttpServiceClient is the one way)
            Type[] declared = _blazorServerAssembly.GetTypes();

            // Act
            string[] droppedTypes =
            [
                .. declared
                    .Select(type => type.Name)
                    .Where(name => name.Contains("DistributedTrace", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Controls", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("SecurityHardening", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("DelegatingHandler", StringComparison.OrdinalIgnoreCase)),
            ];
            string[] clientMethods =
            [
                .. _blazorServerAssembly
                    .GetExportedTypes()
                    .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    .Select(method => method.Name)
                    .Where(name => name.Contains("HttpServiceClient", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(droppedTypes, Is.Empty, $"Dropped concepts resurfaced as types: {string.Join(", ", droppedTypes)}");
                Assert.That(clientMethods, Is.Empty, $"Typed-client API resurfaced as methods: {string.Join(", ", clientMethods)}");
            });
        }
    }
}
