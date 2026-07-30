namespace Cloudstrap.Observability.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Observability</c>:
    /// AC-O2/AC-ASP2 (dependency closure) and AC-B11 (no enterprise identifiers).
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _observabilityAssembly = typeof(CloudstrapBootstrapLogger).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|dynatrace|nservicebus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void ReferencedAssemblies_OfObservabilityAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _observabilityAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !name.StartsWith("Serilog", StringComparison.Ordinal)
                && !name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                && name != "Cloudstrap.Core")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Azure", StringComparison.Ordinal)
                || name.StartsWith("Aspire", StringComparison.Ordinal)
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
        public void PublicTypes_OfObservabilityAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _observabilityAssembly.GetExportedTypes();

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
        public void PublicTypes_OfObservabilityAssembly_AreSealedAndInTheTwoApprovedNamespaces()
        {
            // Arrange
            Type[] publicTypes = _observabilityAssembly.GetExportedTypes();
            string[] approvedNamespaces = ["Cloudstrap.Observability", "Cloudstrap.Observability.Correlation"];

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes, Is.Not.Empty);
                Assert.That(
                    publicTypes.Where(type => !approvedNamespaces.Contains(type.Namespace)),
                    Is.Empty,
                    "Every public type must live in Cloudstrap.Observability or Cloudstrap.Observability.Correlation.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed unless designed for inheritance.");
            });
        }
    }
}
