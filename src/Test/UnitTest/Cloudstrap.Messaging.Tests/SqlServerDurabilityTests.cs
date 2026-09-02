namespace Cloudstrap.Messaging.Tests
{
    using System.Globalization;
    using Cloudstrap.Messaging.Tests.Fixtures;
    using Cloudstrap.Messaging.Tests.Fixtures.Contracts;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using Wolverine;

    /// <summary>
    /// <c>UseSqlServer()</c> on a real SQL Server (spec D-3: LocalDB by default, <c>CLOUDSTRAP_TEST_SQL</c>
    /// overrides): workload-derived, sanitized durability schemas (AC-MSG13), the store's queryable
    /// dead-letter table with type-and-id-only logging (AC-MSG6, AC-MSG5's tail), the naming-the-key
    /// connection-string failure (AC-MSG2), and the SQL Server transport moving a command between two nodes
    /// on one database.
    /// </summary>
    [TestFixture]
    public sealed class SqlServerDurabilityTests
    {
        private const string _contractsNamespace = "Cloudstrap.Messaging.Tests.Fixtures.Contracts";
        private const string _transportSchema = "test_transport";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

        [OneTimeSetUp]
        public Task ResetDatabase()
        {
            return SqlServerTestDatabase.ResetAsync();
        }

        [Test]
        public async Task UseSqlServer_DefaultSchema_IsTheSanitizedWorkloadName()
        {
            // Arrange — two workloads, one database.
            using IHost orders = DurableHost(DurableSettings("contoso", "orders", "worker"), out _);
            using IHost billing = DurableHost(DurableSettings("contoso", "billing", "worker"), out _);

            // Act
            await orders.StartAsync();
            await billing.StartAsync();
            bool ordersSchema = await SqlServerTestDatabase.TableExistsAsync("contoso_orders_worker", "wolverine_incoming_envelopes");
            bool billingSchema = await SqlServerTestDatabase.TableExistsAsync("contoso_billing_worker", "wolverine_incoming_envelopes");
            await orders.StopAsync();
            await billing.StopAsync();

            // Assert — each workload owns its own sanitized schema; no collision.
            Assert.Multiple(() =>
            {
                Assert.That(ordersSchema, Is.True, "contoso_orders_worker");
                Assert.That(billingSchema, Is.True, "contoso_billing_worker");
            });
        }

        [Test]
        public async Task UseSqlServer_DurabilitySchemaNameOverride_Wins()
        {
            // Arrange
            Dictionary<string, string?> settings = DurableSettings("contoso", "shipping", "worker");
            settings["Cloudstrap:Messaging:Durability:SchemaName"] = "shipping_store";
            using IHost host = DurableHost(settings, out _);

            // Act
            await host.StartAsync();
            bool overridden = await SqlServerTestDatabase.TableExistsAsync("shipping_store", "wolverine_incoming_envelopes");
            bool derived = await SqlServerTestDatabase.TableExistsAsync("contoso_shipping_worker", "wolverine_incoming_envelopes");
            await host.StopAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(overridden, Is.True);
                Assert.That(derived, Is.False);
            });
        }

        [Test]
        public void UseSqlServer_UnresolvableConnectionStringName_FailsFastNamingTheKey_NeverTheValue()
        {
            // Arrange — the durability connection string name has no entry; a sentinel elsewhere must not leak.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Durability:ConnectionStringName"] = "Store";
            settings["ConnectionStrings:DefaultConnection"] = "Server=sentinel-secret-host;Password=sentinel-secret";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CloudstrapMessagingBuilder messaging = builder.AddCloudstrapMessaging();

            // Act
            Exception? failure = Assert.Catch(() => messaging.UseSqlServer());

            // Assert
            string text = failure!.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("'Cloudstrap:Messaging:Durability:ConnectionStringName'"));
                Assert.That(text, Does.Not.Contain("sentinel-secret"));
            });
        }

        [Test]
        public async Task UseSqlServer_PoisonMessage_LandsInTheDeadLetterTableWithTypeAndIdLogged_NeverThePayload()
        {
            // Arrange — no retries at all: the ladder is exhausted on the first failure.
            Dictionary<string, string?> settings = DurableSettings("contoso", "poison", "worker");
            settings["Cloudstrap:Messaging:Retries:NumberOfImmediate"] = "0";
            settings["Cloudstrap:Messaging:Retries:NumberOfDelayed"] = "0";
            using IHost host = DurableHost(settings, out CapturingLoggerProvider logs);
            await host.StartAsync();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new PoisonCommand("sentinel-payload-never-logged"));
            }

            object? deadLetterId = await SqlServerTestDatabase.WaitForScalarAsync(
                "SELECT TOP 1 id FROM contoso_poison_worker.wolverine_dead_letters WHERE message_type LIKE '%PoisonCommand%'",
                _timeout);
            await host.StopAsync();

            // Assert — a queryable dead-letter row; the log names the type and the id, never the payload.
            Assert.That(deadLetterId, Is.Not.Null, "a dead-letter row for the poison message");
            string id = Convert.ToString(deadLetterId, CultureInfo.InvariantCulture)!;
            string[] messages = [.. logs.Entries.Select(entry => entry.Message + " " + entry.Exception)];
            Assert.Multiple(() =>
            {
                Assert.That(messages, Has.Some.Contains("PoisonCommand"));
                Assert.That(messages, Has.Some.Contains(id));
                Assert.That(messages, Has.None.Contains("sentinel-payload-never-logged"));
            });
        }

        [Test]
        public async Task SqlTransport_TwoHostsOnOneDatabase_CommandCrossesFromSenderToListener()
        {
            // Arrange — two nodes, distinct workloads, one shared transport schema; the sender routes the
            // contracts namespace to the listener's workload queue.
            Dictionary<string, string?> senderSettings = DurableSettings("contoso", "orders", "api");
            senderSettings["Cloudstrap:Messaging:Transport"] = "SqlServer";
            senderSettings["Cloudstrap:Messaging:SqlTransport:SchemaName"] = _transportSchema;
            senderSettings[$"Cloudstrap:Messaging:Destinations:{_contractsNamespace}"] = "contoso-orders-worker";
            Dictionary<string, string?> listenerSettings = DurableSettings("contoso", "orders", "worker");
            listenerSettings["Cloudstrap:Messaging:Transport"] = "SqlServer";
            listenerSettings["Cloudstrap:Messaging:SqlTransport:SchemaName"] = _transportSchema;
            // Both hosts share this test assembly, so the sender also discovers PlaceOrderCommandHandler and
            // Wolverine would handle the command locally ahead of any convention. A real producer has no
            // handler for the commands it sends; here the escape hatch switches local routing off.
            using IHost listener = DurableHost(listenerSettings, out _);
            using IHost sender = DurableHost(senderSettings, out _, options => options.Policies.DisableConventionalLocalRouting());
            await listener.StartAsync();
            await sender.StartAsync();
            Guid orderId = Guid.NewGuid();

            // Act
            using (IServiceScope scope = sender.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().SendAsync(new PlaceOrderCommand(orderId));
            }

            object received = await listener.Services.GetRequiredService<InvocationRecorder>().WaitForNextAsync(_timeout);
            await sender.StopAsync();
            await listener.StopAsync();

            // Assert — the command crossed nodes over the SQL Server queue; the sender never handled it itself.
            Assert.Multiple(() =>
            {
                Assert.That(received, Is.EqualTo(new PlaceOrderCommand(orderId)));
                Assert.That(sender.Services.GetRequiredService<InvocationRecorder>().Received, Is.Empty);
            });
        }

        private static Dictionary<string, string?> DurableSettings(string system, string subsystem, string type)
        {
            return new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = system,
                ["Cloudstrap:Application:SubsystemName"] = subsystem,
                ["Cloudstrap:Application:SubsystemType"] = type,
                ["Cloudstrap:Messaging:AutoProvision"] = "true",
                ["ConnectionStrings:DefaultConnection"] = SqlServerTestDatabase.ConnectionString,
            };
        }

        /// <summary>Builds a durable host: UseSqlServer(), solo durability mode (single node, no leader election).</summary>
        private static IHost DurableHost(
            Dictionary<string, string?> settings,
            out CapturingLoggerProvider logs,
            Action<WolverineOptions>? wolverine = null)
        {
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CapturingLoggerProvider provider = new();
            builder.Logging.AddProvider(provider);
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging(configurator => configurator.Wolverine = options =>
                {
                    options.Durability.Mode = DurabilityMode.Solo;
                    wolverine?.Invoke(options);
                })
                .UseSqlServer();
            logs = provider;
            return builder.Build();
        }
    }
}
