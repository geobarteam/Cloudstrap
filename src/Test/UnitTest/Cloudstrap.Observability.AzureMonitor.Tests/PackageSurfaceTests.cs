namespace Cloudstrap.Observability.AzureMonitor.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Observability.AzureMonitor</c>:
    /// AC-AM11 (no enterprise identifiers, single namespace) and AC-ASP2 (no Aspire dependency).
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _azureMonitorAssembly = typeof(AzureMonitorOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|dynatrace|nservicebus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void ReferencedAssemblies_OfAzureMonitorAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _azureMonitorAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                && !name.StartsWith("Azure.", StringComparison.Ordinal)
                && name != "Cloudstrap.Core"
                && name != "Cloudstrap.Observability")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("LanguageExt", StringComparison.Ordinal)
                || name.StartsWith("NServiceBus", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_OfAzureMonitorAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _azureMonitorAssembly.GetExportedTypes();

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
        public void PublicTypes_OfAzureMonitorAssembly_AreSealedAndInTheSingleApprovedNamespace()
        {
            // Arrange
            Type[] publicTypes = _azureMonitorAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes, Is.Not.Empty);
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.Observability.AzureMonitor"),
                    Is.Empty,
                    "Every public type must live in Cloudstrap.Observability.AzureMonitor.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static unless designed for inheritance.");
            });
        }
    }
}
