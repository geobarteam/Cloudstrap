namespace Cloudstrap.Messaging.AzureBlob.Tests
{
    using System.Globalization;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures.Contracts;
    using Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using Wolverine;
    using Wolverine.EntityFrameworkCore;

    /// <summary>
    /// The claim check under the transactional outbox on LocalDB (spec D-3; AC-CK13, AC-M2 carried): an
    /// above-threshold command staged through <c>IDbContextOutbox</c> and committed while the process dies
    /// before dispatch is delivered whole by the next node on the store — the offload happened inside the
    /// commit, before it — and a transactional handler that throws commits neither the row nor the message,
    /// while a blob uploaded during the rolled-back transaction may remain as the documented orphan.
    /// </summary>
    [TestFixture]
    public sealed class OutboxInterplayTests
    {
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan _settle = TimeSpan.FromSeconds(2);

        [OneTimeSetUp]
        public async Task ResetDatabase()
        {
            await SqlServerTestDatabase.ResetAsync();
            await SqlServerTestDatabase.ScalarAsync(ExportsDbContext.CreateTableSql);
        }

        [Test]
        public async Task OffloadedCommand_CommittedButNotDispatched_IsRecoveredByANewNode_AndArrivesWhole()
        {
            // Arrange — one blob account shared by the crashing and the recovering node.
            InMemoryBlobContainerClient blobs = new();
            ExportReadyCommand command = new(Guid.NewGuid(), new string('r', 300_000));

            // Act (1) — stage row + large command, commit, "crash" before flushing the outgoing messages.
            using (IHost crashing = TransactionalHost("exports", blobs, retries: false))
            {
                await crashing.StartAsync();
                using (IServiceScope scope = crashing.Services.CreateScope())
                {
                    IDbContextOutbox<ExportsDbContext> outbox = scope.ServiceProvider.GetRequiredService<IDbContextOutbox<ExportsDbContext>>();
                    outbox.DbContext.Exports.Add(new Export { Id = command.ExportId, Label = "recovered" });
                    await outbox.SendAsync(command);
                    await outbox.DbContext.SaveChangesAsync();
                }

                await crashing.StopAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(crashing.Services.GetRequiredService<InvocationRecorder>().Received, Is.Empty, "never dispatched");
                    Assert.That(blobs.Uploads, Is.EqualTo(1), "the offload happened inside the commit, before dispatch");
                });
            }

            int persisted = await CountPersistedEnvelopesAsync("contoso_exports_worker", nameof(ExportReadyCommand));

            // Act (2) — a fresh node on the same store and the same account recovers the envelope.
            using IHost recovering = TransactionalHost("exports", blobs, retries: false);
            await recovering.StartAsync();
            ExportObservation observed = (ExportObservation)await recovering.Services.GetRequiredService<InvocationRecorder>().WaitForNextAsync(_timeout);
            int rows = await CountExportsAsync(command.ExportId);
            await recovering.StopAsync();

            // Assert — no loss, whole message, exactly one row.
            Assert.Multiple(() =>
            {
                Assert.That(persisted, Is.GreaterThanOrEqualTo(1), "the committed envelope waited in the store");
                Assert.That(observed.Message, Is.EqualTo(command), "the recovered message arrived whole");
                Assert.That(rows, Is.EqualTo(1));
                Assert.That(blobs.Uploads, Is.EqualTo(1), "recovery re-read the same blob, it did not re-upload");
            });
        }

        [Test]
        public async Task OffloadedCommand_HandlerThrows_RowAndMessageBothRollBack_BlobMayRemainAsDocumentedOrphan()
        {
            // Arrange — no retries: one failure and the command is dead-lettered.
            InMemoryBlobContainerClient blobs = new();
            using IHost host = TransactionalHost("rollback", blobs, retries: false);
            await host.StartAsync();
            Guid exportId = Guid.NewGuid();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new StageExportCommand(exportId, ContentLength: 300_000, Fail: true));
            }

            object? deadLetter = await SqlServerTestDatabase.WaitForScalarAsync(
                "SELECT TOP 1 id FROM contoso_rollback_worker.wolverine_dead_letters WHERE message_type LIKE '%StageExportCommand%'",
                _timeout);
            await Task.Delay(_settle);
            int rows = await CountExportsAsync(exportId);
            IReadOnlyCollection<object> delivered = host.Services.GetRequiredService<InvocationRecorder>().Received;
            await host.StopAsync();

            // Assert — AC-M2 carried: neither the row nor the cascaded large command survived; a blob uploaded
            // while serializing the doomed message may remain (DL-2's documented orphan, removed by the
            // container's lifecycle policy) — never an error.
            int attempts = host.Services.GetRequiredService<AttemptCounter>().AttemptsFor(exportId);
            TestContext.Out.WriteLine($"Orphan observation: {blobs.Uploads} blob(s) uploaded across {attempts} handler attempt(s) of the rolled-back transaction.");
            Assert.Multiple(() =>
            {
                Assert.That(deadLetter, Is.Not.Null, "the failed command was dead-lettered");
                Assert.That(rows, Is.Zero, "no row");
                Assert.That(delivered, Is.Empty, "no delivery");
                Assert.That(blobs.Uploads, Is.LessThanOrEqualTo(1), "at most the one orphan");
            });
        }

        [Test]
        public async Task DeadLetteredOffloadedMessage_IsReserializedIntoTheDeadLetterRow_WritingASecondBlob()
        {
            // Arrange — a large command whose handler always fails, no retries: Wolverine re-serializes the
            // rehydrated envelope when it writes the dead-letter row, and the claim check offloads it again.
            InMemoryBlobContainerClient blobs = new();
            using IHost host = TransactionalHost("poison", blobs, retries: false);
            await host.StartAsync();
            FlakyExportCommand command = new(Guid.NewGuid(), new string('p', 300_000), FailuresBeforeSuccess: int.MaxValue);

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(command);
            }

            object? deadLetter = await SqlServerTestDatabase.WaitForScalarAsync(
                "SELECT TOP 1 id FROM contoso_poison_worker.wolverine_dead_letters WHERE message_type LIKE '%FlakyExportCommand%'",
                _timeout);
            await Task.Delay(_settle);
            await host.StopAsync();

            // Assert — the documented retention fact (DL-2): a dead-lettered large message holds two blobs, the
            // original and the dead-letter row's; both stay until the container's lifecycle policy removes them.
            Assert.Multiple(() =>
            {
                Assert.That(deadLetter, Is.Not.Null, "dead-lettered after one attempt");
                Assert.That(host.Services.GetRequiredService<AttemptCounter>().AttemptsFor(command.Id), Is.EqualTo(1));
                Assert.That(blobs.Uploads, Is.EqualTo(2), "the original offload plus the dead-letter row's re-serialization");
                Assert.That(blobs.Count, Is.EqualTo(2), "both retained");
            });
        }

        private static async Task<int> CountExportsAsync(Guid exportId)
        {
            object? count = await SqlServerTestDatabase.ScalarAsync(
                "SELECT COUNT(*) FROM dbo.Exports WHERE Id = @id",
                ("@id", exportId));
            return Convert.ToInt32(count, CultureInfo.InvariantCulture);
        }

        private static async Task<int> CountPersistedEnvelopesAsync(string schema, string messageTypeFragment)
        {
            object? count = await SqlServerTestDatabase.ScalarAsync(
                $"SELECT (SELECT COUNT(*) FROM {schema}.wolverine_incoming_envelopes WHERE message_type LIKE '%{messageTypeFragment}%' AND status <> 'Handled') " +
                $"+ (SELECT COUNT(*) FROM {schema}.wolverine_outgoing_envelopes WHERE message_type LIKE '%{messageTypeFragment}%')");
            return Convert.ToInt32(count, CultureInfo.InvariantCulture);
        }

        /// <summary>A durable node with the transactional EF integration and the claim check on the shared LocalDB database.</summary>
        private static IHost TransactionalHost(string subsystem, InMemoryBlobContainerClient blobs, bool retries)
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
            builder.Services.AddSingleton<AttemptCounter>();
            builder.AddCloudstrapMessaging(configurator => configurator.Wolverine = options =>
                {
                    options.Durability.Mode = DurabilityMode.Solo;
                    options.Durability.ScheduledJobFirstExecution = TimeSpan.FromSeconds(1);
                    options.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(1);
                })
                .UseSqlServer()
                .AddCloudstrapTransactionalMessaging<ExportsDbContext>(options => options.UseSqlServer(SqlServerTestDatabase.ConnectionString))
                .UseAzureBlobClaimCheck(claimCheck => claimCheck.ContainerClient = blobs);
            return builder.Build();
        }
    }
}
