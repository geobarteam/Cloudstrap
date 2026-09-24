namespace Cloudstrap.Messaging.AzureBlob.Tests
{
    using System.Globalization;
    using Azure;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures.Contracts;
    using Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure;
    using NUnit.Framework;
    using Wolverine.ErrorHandling;

    /// <summary>
    /// A payload that cannot be loaded is a deterministic failure (AC-CK4, DL-1): the envelope is
    /// dead-lettered in one attempt, never retried, logged with the message type, the message id and the
    /// container name — never the payload or a connection string. On the pinned Wolverine, a failure while
    /// the body is being restored happens inside deserialization, which Wolverine dead-letters before any
    /// failure rule (the leaf's or a consumer's) is consulted; transient storage faults are therefore the
    /// Azure SDK's retry policy's job, and the dead-letter row stays replayable.
    /// </summary>
    [TestFixture]
    public sealed class MissingPayloadTests
    {
        private const string _deadLetters = "contoso_orders_worker.wolverine_dead_letters";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan _settle = TimeSpan.FromSeconds(2);

        [OneTimeSetUp]
        public Task ResetDatabase()
        {
            return SqlServerTestDatabase.ResetAsync();
        }

        [SetUp]
        public Task ClearDeadLetters()
        {
            // Every test dead-letters the same message type into one shared table: start each from an empty one.
            return SqlServerTestDatabase.ScalarAsync(
                $"IF OBJECT_ID('{_deadLetters}') IS NOT NULL DELETE FROM {_deadLetters}");
        }

        [Test]
        public async Task MissingBlob_DeadLettersImmediately_WithoutRunningTheRetryLadder()
        {
            // Arrange — a generous ladder that must never run; every download answers 404.
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(blobs, listenerSettings: settings =>
            {
                settings["Cloudstrap:Messaging:Retries:NumberOfImmediate"] = "3";
                settings["Cloudstrap:Messaging:Retries:NumberOfDelayed"] = "2";
            });
            blobs.DownloadFailure = new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null);
            ExportReadyCommand command = new(Guid.NewGuid(), new string('m', 300_000));

            // Act
            await harness.SendAsync(command);
            object? deadLetterId = await WaitForDeadLetterAsync(nameof(ExportReadyCommand));
            await Task.Delay(_settle);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(deadLetterId, Is.Not.Null, "a dead-letter row for the message");
                Assert.That(harness.ListenerRecorder.Received, Is.Empty, "the handler never ran");
                Assert.That(blobs.Uploads, Is.EqualTo(1));
                Assert.That(blobs.Downloads, Is.EqualTo(1), "one load attempt, no retries");
            });
        }

        [Test]
        public async Task MissingBlob_IsLoggedWithTypeIdAndContainer_NeverThePayloadOrConnectionString()
        {
            // Arrange — a sentinel inside the body that must appear in no log line.
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(blobs);
            blobs.DownloadFailure = new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null);
            string content = string.Concat(Enumerable.Repeat("sentinel-payload-never-logged;", 12_000));
            ExportReadyCommand command = new(Guid.NewGuid(), content);

            // Act
            await harness.SendAsync(command);
            object? deadLetterId = await WaitForDeadLetterAsync(nameof(ExportReadyCommand));
            await Task.Delay(_settle);

            // Assert
            Assert.That(deadLetterId, Is.Not.Null);
            string id = Convert.ToString(deadLetterId, CultureInfo.InvariantCulture)!;
            object? rowType = await SqlServerTestDatabase.ScalarAsync(
                $"SELECT message_type FROM {_deadLetters} WHERE id = @id", ("@id", deadLetterId!));
            string[] messages = [.. harness.ListenerLogs.Entries.Select(entry => entry.Message + " " + entry.Exception)];
            string[] leafLines = [.. harness.ListenerLogs.Entries
                .Where(entry => entry.Category == "Cloudstrap.Messaging.AzureBlob")
                .Select(entry => entry.Message)];
            Assert.Multiple(() =>
            {
                // The triage triple as the pinned Wolverine exposes it: the dead-letter row and Wolverine's own
                // moved-to-error line carry the message type and id (Wolverine's line omits the type when
                // deserialization itself failed); the leaf's line, written where the load fails, carries the
                // container and the HTTP status. Nothing anywhere carries the payload or a connection string.
                Assert.That(Convert.ToString(rowType, CultureInfo.InvariantCulture), Does.Contain("ExportReadyCommand"), "the row names the type");
                Assert.That(messages, Has.Some.Contains(id), "Wolverine's line names the id");
                Assert.That(leafLines, Has.Some.Contains("contoso-claimcheck").And.Contains("HTTP 404"), "the leaf names the container and status");
                Assert.That(messages, Has.None.Contains("sentinel-payload-never-logged"));
                Assert.That(messages, Has.None.Contains("devstoreaccount1"));
                Assert.That(messages, Has.None.Contains("UseDevelopmentStorage"));
            });
        }

        [Test]
        public async Task TransientStorageFailure_DuringRestore_IsDeadLetteredNotRetriedByTheLadder()
        {
            // Arrange — a 503 while the body is restored. The pinned Wolverine dead-letters deserialization
            // failures before its failure rules run, so the retry ladder is never consulted: transient faults
            // are absorbed by the Azure SDK's own retry policy on the real client, and the row is replayable.
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(blobs, listenerSettings: settings =>
                settings["Cloudstrap:Messaging:Retries:NumberOfImmediate"] = "3");
            blobs.DownloadFailure = new RequestFailedException(503, "The server is busy.", "ServerBusy", null);
            ExportReadyCommand command = new(Guid.NewGuid(), new string('t', 300_000));

            // Act
            await harness.SendAsync(command);
            object? deadLetterId = await WaitForDeadLetterAsync(nameof(ExportReadyCommand));
            await Task.Delay(_settle);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(deadLetterId, Is.Not.Null, "dead-lettered, replayable");
                Assert.That(harness.ListenerRecorder.Received, Is.Empty);
                Assert.That(blobs.Downloads, Is.EqualTo(1), "the ladder did not re-read the payload");
                Assert.That(blobs.Count, Is.EqualTo(1), "the blob is retained for replay");
            });
        }

        [Test]
        public async Task ConsumerFailureRule_CannotInterceptAMissingPayload_DeserializationDeadLettersFirst()
        {
            // Arrange — the consumer's final-say delegate asks to discard 404s; deserialization dead-letters first.
            InMemoryBlobContainerClient blobs = new();
            await using TwoNodeHarness harness = await TwoNodeHarness.StartAsync(
                blobs,
                listenerWolverine: options => options.Policies
                    .OnException<RequestFailedException>(ex => ex.Status == 404, "consumer discards missing payloads")
                    .Discard());
            blobs.DownloadFailure = new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null);
            ExportReadyCommand command = new(Guid.NewGuid(), new string('d', 300_000));

            // Act
            await harness.SendAsync(command);
            object? deadLetterId = await WaitForDeadLetterAsync(nameof(ExportReadyCommand));

            // Assert — the documented posture: the leaf's and the consumer's rules alike sit behind
            // Wolverine's deserialization dead-lettering; a missing payload is always a replayable row.
            Assert.That(deadLetterId, Is.Not.Null);
        }

        private static Task<object?> WaitForDeadLetterAsync(string messageTypeFragment)
        {
            return SqlServerTestDatabase.WaitForScalarAsync(
                $"SELECT TOP 1 id FROM {_deadLetters} WHERE message_type LIKE '%{messageTypeFragment}%'",
                _timeout);
        }
    }
}
