namespace Cloudstrap.Worker.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Worker</c>: AC-WK10 /
    /// AC-ASP2 / AC-A3 (dependency closure — no Aspire, no Nihdi, and no direct telemetry or
    /// messaging references: this package composes), the De-NIHDI identifier rule, and the drops
    /// this deliverable made permanent (no probe-evaluation logic, no environment sniffing).
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _workerAssembly = typeof(WorkerOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|dynatrace|nservicebus",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex _environmentSniffing = new(
            "environmentislocal|isrunningin",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] _approvedNamespaces = ["Cloudstrap.Worker"];

        private static readonly string[] _approvedPublicTypeNames =
            ["HostApplicationBuilderExtensions", "WorkerOptions"];

        [Test]
        public void ReferencedAssemblies_OfWorkerAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _workerAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act — the closure is composition-only: framework + the three sibling packages
            string[] unexpected = [.. referenced.Where(name =>
                !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && name != "Cloudstrap.Core"
                && name != "Cloudstrap.Observability"
                && name != "Cloudstrap.Extensions")];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal)
                || name.StartsWith("Serilog", StringComparison.Ordinal)
                || name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                || name.StartsWith("Duende", StringComparison.Ordinal)
                || name.StartsWith("Hangfire", StringComparison.Ordinal)
                || name.StartsWith("Wolverine", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicTypes_OfWorkerAssembly_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _workerAssembly.GetExportedTypes();

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
        public void PublicTypes_OfWorkerAssembly_AreSealedOrStaticAndInTheSingleApprovedNamespace()
        {
            // Arrange
            Type[] publicTypes = _workerAssembly.GetExportedTypes();

            // Assert — exactly the two public types of the spec sketch, sealed or static, one namespace
            Assert.Multiple(() =>
            {
                Assert.That(
                    publicTypes.Select(type => type.Namespace).Distinct(),
                    Is.EquivalentTo(_approvedNamespaces));
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !(type.IsAbstract && type.IsSealed)),
                    Is.Empty,
                    "Every public class must be sealed or static.");
                Assert.That(publicTypes.Where(type => type.IsInterface), Is.Empty);
                Assert.That(
                    publicTypes.Select(type => type.Name).OrderBy(name => name, StringComparer.Ordinal),
                    Is.EqualTo(_approvedPublicTypeNames));
            });
        }

        [Test]
        public void WorkerAssembly_DeclaresNoProbeEvaluationOrEnvironmentSniffingTypes()
        {
            // Arrange
            Type[] types = _workerAssembly.GetTypes();

            // Assert — the Step 2/3 sweeps made permanent (findings 1 and 4 guarded forever)
            Assert.Multiple(() =>
            {
                Assert.That(
                    types.Where(type => typeof(IHealthCheck).IsAssignableFrom(type)
                        || typeof(IHealthCheckPublisher).IsAssignableFrom(type)),
                    Is.Empty);
                Assert.That(
                    types.Where(type => type.Name.Contains("Probe", StringComparison.OrdinalIgnoreCase)
                        || type.Name.Contains("HealthCheckService", StringComparison.OrdinalIgnoreCase)),
                    Is.Empty);
                Assert.That(
                    types.SelectMany(type => type.GetMethods(
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                            BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        .Where(method => _environmentSniffing.IsMatch(method.Name)),
                    Is.Empty);
            });
        }
    }
}
