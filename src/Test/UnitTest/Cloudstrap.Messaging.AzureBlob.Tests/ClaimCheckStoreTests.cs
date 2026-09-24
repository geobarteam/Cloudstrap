namespace Cloudstrap.Messaging.AzureBlob.Tests
{
    using System.Text;
    using Azure;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures;
    using Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using Wolverine.ClaimCheck.AzureBlobStorage;
    using Wolverine.Persistence;

    /// <summary>
    /// The leaf's store over Wolverine's Azure store (offline, on the in-memory account): the dedicated
    /// container is created on first use — Wolverine's store uploads straight into it and creates nothing —
    /// and a payload that cannot be loaded is logged with container and status, never the payload, before
    /// the SDK's exception continues unchanged.
    /// </summary>
    [TestFixture]
    public sealed class ClaimCheckStoreTests
    {
        private static readonly byte[] _payload = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");

        [Test]
        public async Task StoreAsync_ContainerMissing_CreatesTheContainerOnFirstUse_ThenStores()
        {
            // Arrange — an account where the container does not exist yet.
            InMemoryBlobContainerClient blobs = new() { RequireCreate = true };
            ClaimCheckStore store = CreateStore(blobs, new CapturingLoggerProvider());

            // Act
            ClaimCheckToken token = await store.StoreAsync(_payload, "application/json");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(blobs.Created, Is.True, "the container was created");
                Assert.That(blobs.CreateCalls, Is.EqualTo(1));
                Assert.That(blobs.Uploads, Is.EqualTo(1));
                Assert.That(token.Length, Is.EqualTo(_payload.Length));
            });
        }

        [Test]
        public async Task StoreAsync_ContainerExists_StoresWithoutAnyCreationCall()
        {
            // Arrange
            InMemoryBlobContainerClient blobs = new();
            ClaimCheckStore store = CreateStore(blobs, new CapturingLoggerProvider());

            // Act
            await store.StoreAsync(_payload, "application/json");
            await store.StoreAsync(_payload, "application/json");

            // Assert — no probing, no creation on the happy path: uploads only.
            Assert.Multiple(() =>
            {
                Assert.That(blobs.CreateCalls, Is.Zero);
                Assert.That(blobs.Uploads, Is.EqualTo(2));
            });
        }

        [Test]
        public async Task LoadAsync_StorageFailure_LogsContainerAndStatus_NeverThePayload_AndRethrowsUnchanged()
        {
            // Arrange — the payload is stored, then every download answers 404.
            InMemoryBlobContainerClient blobs = new();
            CapturingLoggerProvider logs = new();
            ClaimCheckStore store = CreateStore(blobs, logs);
            ClaimCheckToken token = await store.StoreAsync(Encoding.UTF8.GetBytes("payload-sentinel-never-logged"), "text/plain");
            blobs.DownloadFailure = new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null);

            // Act
            RequestFailedException? failure = Assert.ThrowsAsync<RequestFailedException>(() => store.LoadAsync(token));

            // Assert
            string[] lines = [.. logs.Entries.Select(entry => entry.Message)];
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Status, Is.EqualTo(404), "the SDK's exception continues unchanged");
                Assert.That(lines, Has.Some.Contains("contoso-claimcheck").And.Contains("HTTP 404").And.Contains(token.Id));
                Assert.That(lines, Has.None.Contains("payload-sentinel-never-logged"));
            });
        }

        private static ClaimCheckStore CreateStore(InMemoryBlobContainerClient blobs, CapturingLoggerProvider logs)
        {
            ILogger logger = logs.CreateLogger("Cloudstrap.Messaging.AzureBlob");
            return new ClaimCheckStore(new AzureBlobClaimCheckStore(blobs), blobs, logger);
        }
    }
}
