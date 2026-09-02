namespace Cloudstrap.Messaging.Tests
{
    using System.Reflection;
    using Cloudstrap.Messaging.Tests.Fixtures.Contracts;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using Wolverine;
    using Wolverine.Runtime;
    using Wolverine.Runtime.Routing;
    using Wolverine.SqlServer;

    /// <summary>
    /// The message conventions (AC-MSG4, D-1): suffix classification of dependency-free contract types, the
    /// <c>Destinations</c> map routing commands to destination workload queues, the consumer's override
    /// hooks in their documented order, and the one-line startup summary stating the routing in force.
    /// </summary>
    [TestFixture]
    public sealed class ConventionTests
    {
        private const string _contractsNamespace = "Cloudstrap.Messaging.Tests.Fixtures.Contracts";
        private static readonly string[] _expectedHookOrder = ["conventions", "wolverine"];

        [Test]
        public void Conventions_SuffixTypes_AreClassifiedAsCommandEventAndMessage()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();

            // Act
            MessageConventions conventions = host.Services.GetRequiredService<MessageConventions>();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(conventions.Classify(typeof(PlaceOrderCommand)), Is.EqualTo(MessageKind.Command));
                Assert.That(conventions.Classify(typeof(OrderPlacedEvent)), Is.EqualTo(MessageKind.Event));
                Assert.That(conventions.Classify(typeof(OrderNoteMessage)), Is.EqualTo(MessageKind.Message));
                Assert.That(conventions.Classify(typeof(OrderSnapshot)), Is.EqualTo(MessageKind.None));
            });
        }

        [Test]
        public void Conventions_ContractAssemblyTypes_CarryNoPackageDependency()
        {
            // Arrange — every type in the contracts fixture namespace.
            Type[] contracts = [.. typeof(PlaceOrderCommand).Assembly.GetTypes()
                .Where(type => type.Namespace == _contractsNamespace && type.IsPublic)];
            Assert.That(contracts, Has.Length.GreaterThanOrEqualTo(4));

            // Act + Assert — no attribute, interface or base class from a messaging package anywhere.
            Assert.Multiple(() =>
            {
                foreach (Type contract in contracts)
                {
                    Assert.That(contract.BaseType, Is.EqualTo(typeof(object)), contract.Name);
                    Assert.That(
                        contract.GetInterfaces().Select(i => i.Assembly.GetName().Name!),
                        Has.None.Matches<string>(IsMessagingPackage),
                        contract.Name);
                    Assert.That(
                        contract.GetCustomAttributesData().Select(a => a.AttributeType.Assembly.GetName().Name!),
                        Has.None.Matches<string>(IsMessagingPackage),
                        contract.Name);
                }
            });
        }

        [Test]
        public void Conventions_DestinationsMap_RoutesCommandsToTheConfiguredWorkloadQueue()
        {
            // Arrange — SQL transport, one Destinations entry; the host is never started (no SQL touched:
            // the connection string points at an unreachable host on purpose).
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(SqlTransportSettings());
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();

            // Act
            IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            string[] routes = RoutesFor(runtime, typeof(PlaceOrderCommand));

            // Assert — the destination workload's queue on the SQL transport (whose identifiers sanitize '-' to '_').
            Assert.That(routes, Is.EqualTo(new[] { SqlQueueUri("contoso-billing-worker") }));
        }

        [Test]
        public void Conventions_ConfiguratorConventions_ReplacesTheDefaultRules()
        {
            // Arrange — a custom destination rule and a custom classification replace the defaults.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(SqlTransportSettings());
            builder.AddCloudstrapMessaging(configurator => configurator.Conventions = conventions =>
            {
                conventions.DestinationFor = _ => "contoso-override-worker";
                conventions.Classify = type => type == typeof(OrderSnapshot) ? MessageKind.Command : MessageKind.None;
            });
            using IHost host = builder.Build();

            // Act
            IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            MessageConventions conventions = host.Services.GetRequiredService<MessageConventions>();
            string[] snapshotRoutes = RoutesFor(runtime, typeof(OrderSnapshot));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(conventions.Classify(typeof(PlaceOrderCommand)), Is.EqualTo(MessageKind.None));
                Assert.That(snapshotRoutes, Is.EqualTo(new[] { SqlQueueUri("contoso-override-worker") }));
            });
        }

        [Test]
        public void Conventions_ConfiguratorWolverine_RunsLastOverConventions()
        {
            // Arrange — both hooks record when they run; the Wolverine delegate also routes explicitly.
            List<string> order = [];
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(SqlTransportSettings());
            builder.AddCloudstrapMessaging(configurator =>
            {
                configurator.Conventions = conventions =>
                {
                    order.Add("conventions");
                    conventions.DestinationFor = _ => "contoso-convention-worker";
                };
                configurator.Wolverine = options =>
                {
                    order.Add("wolverine");
                    options.PublishMessage<PlaceOrderCommand>().ToSqlServerQueue("contoso-explicit-worker");
                };
            });
            using IHost host = builder.Build();

            // Act
            IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            string[] routes = RoutesFor(runtime, typeof(PlaceOrderCommand));

            // Assert — the Wolverine delegate ran after Conventions, and its explicit route is the one in force.
            Assert.Multiple(() =>
            {
                Assert.That(order, Is.EqualTo(_expectedHookOrder));
                Assert.That(routes, Is.EqualTo(new[] { SqlQueueUri("contoso-explicit-worker") }));
            });
        }

        [Test]
        public async Task StartupSummary_LogsTheRoutingInForceInOneLine()
        {
            // Arrange — local transport (the host starts), one Destinations entry to be named.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings[$"Cloudstrap:Messaging:Destinations:{_contractsNamespace}"] = "contoso-billing-worker";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            await host.StopAsync();

            // Assert — exactly one summary line naming transport, endpoint and each destination.
            CapturedLogEntry[] summaries = [.. logs.Entries
                .Where(entry => entry.Category == typeof(MessagingStartupSummaryLogger).FullName)];
            Assert.That(summaries, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(summaries[0].Level, Is.EqualTo(LogLevel.Information));
                Assert.That(summaries[0].Message, Does.Contain("Local"));
                Assert.That(summaries[0].Message, Does.Contain(MessagingTestHost.WorkloadName));
                Assert.That(summaries[0].Message, Does.Contain(_contractsNamespace));
                Assert.That(summaries[0].Message, Does.Contain("contoso-billing-worker"));
            });
        }

        private static bool IsMessagingPackage(string assemblyName)
        {
            return assemblyName.StartsWith("Wolverine", StringComparison.Ordinal)
                || assemblyName.StartsWith("JasperFx", StringComparison.Ordinal)
                || assemblyName.StartsWith("Cloudstrap", StringComparison.Ordinal);
        }

        private static Dictionary<string, string?> SqlTransportSettings()
        {
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Transport"] = "SqlServer";
            settings[$"Cloudstrap:Messaging:Destinations:{_contractsNamespace}"] = "contoso-billing-worker";
            settings["ConnectionStrings:DefaultConnection"] =
                "Server=unreachable.invalid;Database=Never;Integrated Security=true;Connect Timeout=1";
            return settings;
        }

        /// <summary>The SQL transport's queue URI for a workload: Wolverine sanitizes '-' to '_' in SQL identifiers.</summary>
        private static string SqlQueueUri(string workloadName)
        {
            return $"sqlserver://{workloadName.Replace('-', '_')}/";
        }

        private static string[] RoutesFor(IWolverineRuntime runtime, Type messageType)
        {
            return [.. runtime.RoutingFor(messageType).Routes.OfType<MessageRoute>().Select(route => route.Uri.ToString())];
        }
    }
}
