namespace Cloudstrap.Messaging.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Messaging</c> (AC-MSG15): the
    /// approved dependency closure (AC-ASP2 — no Aspire; AC-A3 — no Nihdi; no NServiceBus, Particular, Duende
    /// or MudBlazor), the exact public surface approved at Gates 1 and 3 with the mechanic-(i) posture made
    /// permanent (no <c>MessageCorrelationOptions</c>), the De-NIHDI identifier rule, and the spec's dropped
    /// concepts kept dead forever.
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _messagingAssembly = typeof(CloudstrapMessagingOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|cfe|nservicebus|particular|dynatrace",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] _approvedPublicTypeNames =
        [
            "AzureServiceBusOptions",
            "CloudstrapMessagingBuilder",
            "CloudstrapMessagingConfigurator",
            "CloudstrapMessagingOptions",
            "CorrelationEnforcementRegistry",
            "CorrelationMiddleware",
            "CorrelationRequiredException",
            "DeadLetterOptions",
            "DurabilityOptions",
            "HostApplicationBuilderExtensions",
            "MessageConventions",
            "MessageKind",
            "MessagingTransport",
            "RetryOptions",
            "SqlTransportOptions",
        ];

        private static readonly string[] _allowedReferencePrefixes =
        [
            "System",
            "netstandard",
            "Microsoft.Extensions.",
            "Microsoft.EntityFrameworkCore",
            "Wolverine",
            "JasperFx",
            "Azure.",
            "OpenTelemetry",
            "Cloudstrap.Core",
            "Cloudstrap.Observability",
        ];

        private static readonly string[] _droppedConcepts =
        [
            "SendOnly", "TransactionMode", "PersistenceType", "Bridge", "Encryption", "DataBus", "Databus",
            "TypeLoader", "CommandExecutor", "TransactionalSession", "UniformSession", "Audit", "ServiceControl",
            "ServicePlatform", "TenantId", "ClientSecret", "MessageCorrelationOptions",
        ];

        [Test]
        public void ReferencedAssemblies_OfMessagingAssembly_MatchTheApprovedClosure()
        {
            // Arrange
            List<string> referenced = [.. _messagingAssembly
                .GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)];

            // Act — the closure is the five WolverineFx packages, their disclosed transitives, OpenTelemetry's
            // API for additive registration and the two Cloudstrap project references; nothing else, ever.
            string[] unexpected = [.. referenced.Where(name =>
                !_allowedReferencePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))];
            string[] forbidden = [.. referenced.Where(name =>
                name.StartsWith("NServiceBus", StringComparison.Ordinal)
                || name.StartsWith("Particular", StringComparison.Ordinal)
                || name.StartsWith("Aspire", StringComparison.Ordinal)
                || name.StartsWith("Nihdi", StringComparison.Ordinal)
                || name.StartsWith("Duende", StringComparison.Ordinal)
                || name.StartsWith("MudBlazor", StringComparison.Ordinal))];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(unexpected, Is.Empty, $"Unexpected references: {string.Join(", ", unexpected)}");
                Assert.That(forbidden, Is.Empty, $"Forbidden references: {string.Join(", ", forbidden)}");
            });
        }

        [Test]
        public void PublicSurface_IsExactlyTheApprovedTypes()
        {
            // Arrange
            Type[] publicTypes = _messagingAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    publicTypes.Select(type => type.Name),
                    Is.EquivalentTo(_approvedPublicTypeNames));
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.Messaging"),
                    Is.Empty,
                    "Every public type must live in the Cloudstrap.Messaging namespace.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static.");
                Assert.That(
                    publicTypes.Where(type => type.IsInterface),
                    Is.Empty,
                    "No public interfaces: Wolverine's own IMessageBus / IDbContextOutbox are the consumer surface.");
            });
        }

        [Test]
        public void PublicTypes_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _messagingAssembly.GetExportedTypes();

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
        public void MessagingAssembly_DeclaresNoDroppedConcepts()
        {
            // Arrange — the spec's Drop rows stay dead: no settings, no encryption, no bridge, no mediator,
            // no session wrappers, no audit/platform tooling, no credential settings, no duplicate options type.
            Type[] declared = [.. _messagingAssembly.GetTypes().Where(type => !IsCompilerGenerated(type))];

            // Act
            string[] droppedTypes =
            [
                .. declared
                    .Select(type => type.Name)
                    .Where(name => _droppedConcepts.Any(concept => name.Contains(concept, StringComparison.Ordinal))),
            ];
            string[] droppedMembers =
            [
                .. declared
                    .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    .Select(member => member.Name)
                    .Where(name => _droppedConcepts.Any(concept => name.Contains(concept, StringComparison.Ordinal))),
            ];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(droppedTypes, Is.Empty, $"Dropped concepts resurfaced as types: {string.Join(", ", droppedTypes)}");
                Assert.That(droppedMembers, Is.Empty, $"Dropped concepts resurfaced as members: {string.Join(", ", droppedMembers)}");
            });
        }

        private static bool IsCompilerGenerated(Type type)
        {
            return type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;
        }
    }
}
