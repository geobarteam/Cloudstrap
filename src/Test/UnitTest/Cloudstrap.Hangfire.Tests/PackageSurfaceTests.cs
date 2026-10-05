namespace Cloudstrap.Hangfire.Tests
{
    using System.ComponentModel;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using global::Hangfire;
    using NUnit.Framework;

    /// <summary>
    /// Permanent tripwires guarding the shipped surface of <c>Cloudstrap.Hangfire</c> (AC-HF20, AC-HF18, AC-ASP2,
    /// AC-A3): the approved dependency closure, the exact public surface approved at Gates 1–3, the persisted
    /// dispatcher wire contract, the De-NIHDI identifier rule, the spec's dropped concepts kept dead, and the
    /// LGPL-3.0 notice in the package README.
    /// </summary>
    [TestFixture]
    public sealed class PackageSurfaceTests
    {
        private static readonly Assembly _hangfireAssembly = typeof(HangfireOptions).Assembly;

        private static readonly Regex _forbiddenIdentifiers = new(
            "nihdi|riziv|cfe|bff|wfe|nservicebus|dynatrace",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly string[] _approvedPublicTypeNames =
        [
            "CloudstrapHangfireActivitySources",
            "CloudstrapHangfireConfigurator",
            "EndpointRouteBuilderExtensions",
            "HangfireDashboardOptions",
            "HangfireJobOptions",
            "HangfireOptions",
            "HangfireServerOptions",
            "HangfireStorageOptions",
            "HealthChecksBuilderExtensions",
            "HostApplicationBuilderExtensions",
            "IBackgroundRecurringTask",
            "RecurringTaskRunner",
        ];

        private static readonly string[] _allowedReferencePrefixes =
        [
            "System",
            "netstandard",
            "Microsoft.Extensions.",
            "Microsoft.AspNetCore",
            "Microsoft.Data.SqlClient",
            "Hangfire",
            "Scrutor",
            "OpenTelemetry",
            "Cloudstrap.Core",
            "Cloudstrap.Observability",
        ];

        private static readonly string[] _forbiddenReferencePrefixes =
        [
            "Aspire", "Nihdi", "NServiceBus", "Particular", "Duende", "MudBlazor", "Wolverine", "Hangfire.InMemory", "Hangfire.Pro",
        ];

        private static readonly string[] _droppedConcepts =
        [
            "BackgroundJobScheduler", "ForwardedPrefix", "AccessRole", "LocalRequestsOnly", "TrustedSubsystem",
            "ConfigurationReader", "AccessEvaluator", "ServerName", "QueuePollInterval", "WithoutDashboard", "ForNihdi",
            "EnvironmentTier", "InMemory",
        ];

        private static readonly Type[] _runAsyncParameters = [typeof(string), typeof(CancellationToken)];

        [Test]
        public void ReferencedAssemblies_MatchTheApprovedClosure()
        {
            // Arrange
            string[] referenced = [.. _hangfireAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name ?? string.Empty)];

            // Act
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
            Type[] publicTypes = _hangfireAssembly.GetExportedTypes();

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(publicTypes.Select(type => type.Name), Is.EquivalentTo(_approvedPublicTypeNames));
                Assert.That(publicTypes.Where(type => type.Namespace != "Cloudstrap.Hangfire"), Is.Empty);
                Assert.That(
                    publicTypes.Where(type => type.IsClass && !type.IsSealed && !type.IsAbstract),
                    Is.Empty,
                    "Public classes must be sealed or static.");
                Assert.That(
                    publicTypes.Where(type => type.IsInterface).Select(type => type.Name),
                    Is.EqualTo(new[] { nameof(IBackgroundRecurringTask) }));
            });
        }

        [Test]
        public void RecurringTaskRunner_WireContract_IsFrozen()
        {
            // Arrange — Hangfire persists this type, its assembly and the method by name for every recurring job.
            Type runner = typeof(RecurringTaskRunner);
            MethodInfo[] methods = runner.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            // Act
            MethodInfo runAsync = methods.Single();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(runner.FullName, Is.EqualTo("Cloudstrap.Hangfire.RecurringTaskRunner"));
                Assert.That(runner.Assembly.GetName().Name, Is.EqualTo("Cloudstrap.Hangfire"));
                Assert.That(runAsync.Name, Is.EqualTo("RunAsync"));
                Assert.That(runAsync.GetParameters().Select(parameter => parameter.ParameterType), Is.EqualTo(_runAsyncParameters));
                Assert.That(runAsync.ReturnType, Is.EqualTo(typeof(Task)));
                Assert.That(runner.GetCustomAttribute<EditorBrowsableAttribute>()?.State, Is.EqualTo(EditorBrowsableState.Never));
                Assert.That(runAsync.GetCustomAttribute<JobDisplayNameAttribute>()?.DisplayName, Is.EqualTo("{0}"));
            });
        }

        [Test]
        public void IBackgroundRecurringTask_ReferencesNoHangfireType()
        {
            // Arrange
            IEnumerable<Type> referenced = typeof(IBackgroundRecurringTask).GetMembers().SelectMany(member => member switch
            {
                PropertyInfo property => [property.PropertyType],
                MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
                _ => Array.Empty<Type>(),
            });

            // Act
            string[] hangfireTypes = [.. referenced
                .Where(type => type.Namespace?.StartsWith("Hangfire", StringComparison.Ordinal) == true)
                .Select(type => type.FullName ?? type.Name)];

            // Assert
            Assert.That(hangfireTypes, Is.Empty);
        }

        [Test]
        public void PublicTypes_ContainNoForbiddenIdentifiers()
        {
            // Arrange
            IEnumerable<string> names = _hangfireAssembly.GetExportedTypes().SelectMany(type =>
                new[] { type.Name }.Concat(type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(member => member.Name)));

            // Act
            string[] hits = [.. names.Where(name => _forbiddenIdentifiers.IsMatch(name)).Distinct(StringComparer.Ordinal)];

            // Assert
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void Assembly_DeclaresNoDroppedConcepts()
        {
            // Arrange — every declared type and member, public or not.
            const BindingFlags everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            IEnumerable<string> names = _hangfireAssembly.GetTypes()
                .Where(type => type.Namespace?.StartsWith("Cloudstrap.Hangfire", StringComparison.Ordinal) == true)
                .SelectMany(type => new[] { type.Name }.Concat(type.GetMembers(everything).Select(member => member.Name)));

            // Act
            string[] hits = [.. names
                .Where(name => _droppedConcepts.Any(concept => name.Contains(concept, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.Ordinal)];

            // Assert
            Assert.That(hits, Is.Empty);
        }

        [Test]
        public void PackageReadme_CarriesTheLgplNotice_AndTheGoldenRules()
        {
            // Arrange
            string readmePath = Path.Combine(FindRepoRoot(), "src", "Cloudstrap.Hangfire", "README.md");

            // Act
            string readme = File.Exists(readmePath) ? File.ReadAllText(readmePath) : string.Empty;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(readme, Does.Contain("LGPL-3.0"));
                Assert.That(readme, Does.Contain("Newtonsoft.Json"));
                Assert.That(readme, Does.Contain("RunServer = false"));
                Assert.That(readme, Does.Contain("Storage:SchemaName"));
            });
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Cloudstrap.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root (a directory containing src/Cloudstrap.sln) above '{AppContext.BaseDirectory}'.");
        }
    }
}
