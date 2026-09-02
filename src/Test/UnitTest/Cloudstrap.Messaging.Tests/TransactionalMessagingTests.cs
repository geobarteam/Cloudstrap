namespace Cloudstrap.Messaging.Tests
{
    using Cloudstrap.Messaging.Tests.Fixtures;
    using Cloudstrap.Messaging.Tests.Fixtures.Contracts;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using Wolverine;
    using Wolverine.EntityFrameworkCore;

    /// <summary>
    /// <c>AddCloudstrapTransactionalMessaging&lt;TDbContext&gt;()</c> on LocalDB (spec D-3): a handler that throws
    /// after staging an entity and a message commits neither (AC-M2), a succeeding handler commits both with
    /// dispatch after commit (AC-MSG7), non-handler code gets the same guarantee through
    /// <c>IDbContextOutbox&lt;TDbContext&gt;</c> including crash recovery (AC-MSG8), and the call without a
    /// durability provider fails fast naming <c>UseSqlServer</c>.
    /// </summary>
    [TestFixture]
    public sealed class TransactionalMessagingTests
    {
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _settle = TimeSpan.FromSeconds(2);

        [OneTimeSetUp]
        public async Task ResetDatabase()
        {
            await SqlServerTestDatabase.ResetAsync();
            await SqlServerTestDatabase.ScalarAsync(OrdersDbContext.CreateTableSql);
        }

        [Test]
        public async Task TransactionalHandler_ThrowsAfterStagingEntityAndMessage_CommitsNeither()
        {
            // Arrange — no retries: one failure and the message is dead-lettered.
            using IHost host = TransactionalHost("failing", retries: false);
            await host.StartAsync();
            Guid orderId = Guid.NewGuid();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new StageOrderCommand(orderId, "doomed", Fail: true));
            }

            object? deadLetter = await SqlServerTestDatabase.WaitForScalarAsync(
                "SELECT TOP 1 id FROM contoso_failing_worker.wolverine_dead_letters WHERE message_type LIKE '%StageOrderCommand%'",
                _timeout);
            await Task.Delay(_settle);
            int rows = await CountOrdersAsync(orderId);
            IReadOnlyCollection<object> events = host.Services.GetRequiredService<InvocationRecorder>().Received;
            await host.StopAsync();

            // Assert — AC-M2 verbatim: neither the row nor the message survived the failed handler.
            Assert.Multiple(() =>
            {
                Assert.That(deadLetter, Is.Not.Null, "the failed command was dead-lettered");
                Assert.That(rows, Is.Zero);
                Assert.That(events, Is.Empty);
            });
        }

        [Test]
        public async Task TransactionalHandler_Succeeds_EntityAndMessageCommitAtomically_DispatchAfterCommit()
        {
            // Arrange
            using IHost host = TransactionalHost("succeeding", retries: false);
            await host.StartAsync();
            Guid orderId = Guid.NewGuid();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new StageOrderCommand(orderId, "kept", Fail: false));
            }

            object received = await host.Services.GetRequiredService<InvocationRecorder>().WaitForNextAsync(_timeout);
            int rows = await CountOrdersAsync(orderId);
            await host.StopAsync();

            // Assert — the row is committed and the cascaded event was dispatched after the commit.
            Assert.Multiple(() =>
            {
                Assert.That(received, Is.EqualTo(new OrderPlacedEvent(orderId)));
                Assert.That(rows, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DbContextOutbox_HttpPathPattern_StagesAndDeliversExactlyOnce()
        {
            // Arrange — the documented non-handler pattern: stage the entity, send through the outbox, save+flush.
            using IHost host = TransactionalHost("http", retries: false);
            await host.StartAsync();
            Guid orderId = Guid.NewGuid();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                IDbContextOutbox<OrdersDbContext> outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<OrdersDbContext>>();
                outbox.DbContext.Orders.Add(new Order { Id = orderId, Description = "via outbox" });
                await outbox.PublishAsync(new OrderPlacedEvent(orderId));
                await outbox.SaveChangesAndFlushMessagesAsync();
            }

            InvocationRecorder recorder = host.Services.GetRequiredService<InvocationRecorder>();
            object received = await recorder.WaitForNextAsync(_timeout);
            await Task.Delay(_settle);
            int rows = await CountOrdersAsync(orderId);
            await host.StopAsync();

            // Assert — row and delivery both observed, exactly once.
            Assert.Multiple(() =>
            {
                Assert.That(received, Is.EqualTo(new OrderPlacedEvent(orderId)));
                Assert.That(recorder.Received, Is.Empty, "no duplicate delivery");
                Assert.That(rows, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task DbContextOutbox_CommittedButNotDispatched_IsRecoveredByANewNode()
        {
            // Arrange — commit entity + outbox envelope, but "crash" before flushing the outgoing messages.
            Guid orderId = Guid.NewGuid();
            using (IHost crashing = TransactionalHost("recovery", retries: false))
            {
                await crashing.StartAsync();
                using (IServiceScope scope = crashing.Services.CreateScope())
                {
                    IDbContextOutbox<OrdersDbContext> outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<OrdersDbContext>>();
                    outbox.DbContext.Orders.Add(new Order { Id = orderId, Description = "recovered" });
                    await outbox.PublishAsync(new OrderPlacedEvent(orderId));
                    await outbox.DbContext.SaveChangesAsync();
                }

                await crashing.StopAsync();
                Assert.That(crashing.Services.GetRequiredService<InvocationRecorder>().Received, Is.Empty, "never dispatched");
            }

            // Act — a fresh node on the same store recovers the committed-but-undispatched envelope.
            using IHost recovering = TransactionalHost("recovery", retries: false);
            await recovering.StartAsync();
            object received = await recovering.Services.GetRequiredService<InvocationRecorder>().WaitForNextAsync(_timeout);
            int rows = await CountOrdersAsync(orderId);
            await recovering.StopAsync();

            // Assert — no loss: effective exactly-once delivery across the crash.
            Assert.Multiple(() =>
            {
                Assert.That(received, Is.EqualTo(new OrderPlacedEvent(orderId)));
                Assert.That(rows, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task TransactionalMessaging_WithoutDurabilityProvider_FailsFastNamingUseSqlServer()
        {
            // Arrange — the transactional integration without UseSqlServer(); no SQL is touched.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.AddCloudstrapMessaging()
                .AddCloudstrapTransactionalMessaging<OrdersDbContext>(options => options.UseSqlServer(SqlServerTestDatabase.ConnectionString));
            using IHost host = builder.Build();

            // Act
            Exception? failure = Assert.CatchAsync(async () => await host.StartAsync());

            // Assert
            Assert.That(failure!.ToString(), Does.Contain("UseSqlServer"));
        }

        private static async Task<int> CountOrdersAsync(Guid orderId)
        {
            object? count = await SqlServerTestDatabase.ScalarAsync(
                "SELECT COUNT(*) FROM dbo.Orders WHERE Id = @id",
                ("@id", orderId));
            return Convert.ToInt32(count, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A durable node with the transactional EF integration on the shared LocalDB database.</summary>
        private static IHost TransactionalHost(string subsystem, bool retries)
        {
            Dictionary<string, string?> settings = new()
            {
                ["Cloudstrap:Application:SystemName"] = "contoso",
                ["Cloudstrap:Application:SubsystemName"] = subsystem,
                ["Cloudstrap:Application:SubsystemType"] = "worker",
                ["Cloudstrap:Messaging:AutoProvision"] = "true",
                ["ConnectionStrings:DefaultConnection"] = SqlServerTestDatabase.ConnectionString,
            };
            if (!retries)
            {
                settings["Cloudstrap:Messaging:Retries:NumberOfImmediate"] = "0";
                settings["Cloudstrap:Messaging:Retries:NumberOfDelayed"] = "0";
            }

            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging(configurator => configurator.Wolverine = options =>
                {
                    options.Durability.Mode = DurabilityMode.Solo;
                    options.Durability.ScheduledJobFirstExecution = TimeSpan.FromSeconds(1);
                    options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
                })
                .UseSqlServer()
                .AddCloudstrapTransactionalMessaging<OrdersDbContext>(options => options.UseSqlServer(SqlServerTestDatabase.ConnectionString));
            return builder.Build();
        }
    }
}
