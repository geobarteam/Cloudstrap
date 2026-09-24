namespace Cloudstrap.Messaging.AzureBlob.Tests
{
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Messaging.AzureBlob</c> (AC-CK11):
    /// the approved dependency closure (AC-ASP2 — no Aspire; AC-A3 — no Nihdi; no Azure.Identity, no
    /// Cloudstrap.Extensions, no NServiceBus/Particular/Duende/MudBlazor/AspNetCore), the exact public
    /// surface approved at Gate 2, the De-NIHDI identifier rule, the spec's dropped and deliberately-not-shipped
    /// concepts kept dead forever, and the options type carrying no account or secret setting (DL-9).
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _leafAssembly = typeof(AzureBlobClaimCheckOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|cfe|nservicebus|particular|dynatrace",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] _approvedPublicTypeNames =
        [
            "AzureBlobClaimCheckOptions",
            "AzureBlobClaimCheckSettings",
            "CloudstrapMessagingBuilderExtensions",
        ];

        private static readonly string[] _allowedReferencePrefixes =
        [
            "System",
            "netstandard",
            "Microsoft.Extensions.",
            "Wolverine",
            "JasperFx",
            "Azure.Core",
            "Azure.Storage",
            "Cloudstrap.Messaging",
            "Cloudstrap.Core",
        ];

        private static readonly string[] _forbiddenReferencePrefixes =
        [
            "Azure.Identity",
            "Cloudstrap.Extensions",
            "NServiceBus",
            "Particular",
            "Aspire",
            "Nihdi",
            "Duende",
            "MudBlazor",
            "Microsoft.AspNetCore",
        ];

        private static readonly string[] _droppedConcepts =
        [
            "DataBus", "Databus", "Encrypt", "Certificate", "ClientSecret", "TenantId", "BlobServiceUri",
            "ConnectionString", "Enable", "Sweep", "TimeToLive", "Prefix",
        ];

        private static readonly string[] _approvedOptionProperties = ["OffloadThresholdBytes", "ContainerName"];

        [Test]
        public void ReferencedAssemblies_OfTheLeaf_MatchTheApprovedClosure()
        {
            // Arrange
            string[] referenced = [.. _leafAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name ?? string.Empty)];

            // Act — the closure is Cloudstrap.Messaging + WolverineFx.AzureBlobStorage and their disclosed transitives.
            string[] unexpected = [.. referenced.Where(name =>
                !_allowedReferencePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))];
            string[] forbidden = [.. referenced.Where(name =>
                _forbiddenReferencePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))];

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
            Type[] publicTypes = _leafAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes.Select(type => type.Name), Is.EquivalentTo(_approvedPublicTypeNames));
                Assert.That(
                    publicTypes.Where(type => type.Namespace != "Cloudstrap.Messaging.AzureBlob"),
                    Is.Empty,
                    "Every public type must live in the Cloudstrap.Messaging.AzureBlob namespace.");
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static.");
                Assert.That(publicTypes.Where(type => type.IsInterface), Is.Empty, "No public interfaces.");
            });
        }

        [Test]
        public void PublicTypes_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            Type[] publicTypes = _leafAssembly.GetExportedTypes();

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
        public void LeafAssembly_DeclaresNoDroppedConcepts()
        {
            // Arrange — the spec's Drop rows and "deliberately not shipped" list stay dead: no property-name
            // convention, no encryption, no credentials, no account settings, no Enable flag, no sweeping/TTL.
            Type[] declared = [.. _leafAssembly.GetTypes().Where(type => !IsCompilerGenerated(type))];

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
                    .Where(member => member is not MethodInfo { IsSpecialName: true })
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

        [Test]
        public void OptionsType_DeclaresNoSecretBearingOrAccountSetting()
        {
            // Arrange
            PropertyInfo[] properties = typeof(AzureBlobClaimCheckOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Act & Assert — exactly the two approved settings (DL-9): when and where on the host's account, nothing else.
            Assert.That(properties.Select(property => property.Name), Is.EquivalentTo(_approvedOptionProperties));
        }

        private static bool IsCompilerGenerated(Type type)
        {
            return type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;
        }
    }
}
