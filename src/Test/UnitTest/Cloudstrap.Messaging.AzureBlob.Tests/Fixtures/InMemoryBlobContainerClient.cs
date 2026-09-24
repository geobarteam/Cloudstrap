namespace Cloudstrap.Messaging.AzureBlob.Tests.Fixtures
{
    using System.Collections.Concurrent;
    using Azure;
    using Azure.Core;
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;

    /// <summary>
    /// A no-network blob "account": a <see cref="BlobContainerClient"/> built on the SDK's mocking
    /// constructor whose blob clients read and write a shared in-memory dictionary. Wolverine's Azure
    /// claim-check store is exercised for real; only the wire to storage is replaced. Counters expose what
    /// the store did, and <see cref="DownloadFailure"/> lets a test make every download fail the way the SDK
    /// would (a 404 for a missing blob, a 503 for a throttled account).
    /// </summary>
    internal sealed class InMemoryBlobContainerClient : BlobContainerClient
    {
        private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);
        private readonly string _name;
        private int _uploads;
        private int _downloads;

        /// <summary>Initializes a new instance of the <see cref="InMemoryBlobContainerClient"/> class.</summary>
        /// <param name="name">The container name the double reports.</param>
        public InMemoryBlobContainerClient(string name = "contoso-claimcheck")
        {
            _name = name;
        }

        /// <inheritdoc />
        public override string Name => _name;

        /// <inheritdoc />
        public override string AccountName => "inmemory";

        /// <summary>Gets how many blobs were uploaded.</summary>
        public int Uploads => _uploads;

        /// <summary>Gets how many blob downloads were attempted (failed ones included).</summary>
        public int Downloads => _downloads;

        /// <summary>Gets how many blobs the container currently holds.</summary>
        public int Count => _blobs.Count;

        /// <summary>Gets a snapshot of the stored payloads.</summary>
        public IReadOnlyCollection<byte[]> Payloads => [.. _blobs.Values];

        /// <summary>Gets or sets the exception every download throws, or <see langword="null"/> to serve the stored bytes.</summary>
        public Exception? DownloadFailure
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the container must be created before an upload succeeds:
        /// until <see cref="Created"/> is set by a create call, uploads answer 404 <c>ContainerNotFound</c> as
        /// the real service does.
        /// </summary>
        public bool RequireCreate
        {
            get; set;
        }

        /// <summary>Gets a value indicating whether a create call was made.</summary>
        public bool Created
        {
            get; private set;
        }

        /// <summary>Gets how many create calls were made.</summary>
        public int CreateCalls
        {
            get; private set;
        }

        /// <inheritdoc />
        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Create());
        }

        /// <inheritdoc />
        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType,
            IDictionary<string, string> metadata,
            BlobContainerEncryptionScopeOptions encryptionScopeOptions,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Create());
        }

        private Response<BlobContainerInfo> Create()
        {
            CreateCalls++;
            Created = true;
            BlobContainerInfo info = BlobsModelFactory.BlobContainerInfo(new ETag("\"c\""), DateTimeOffset.UtcNow);
            return Response.FromValue(info, new InMemoryResponse(201));
        }

        /// <inheritdoc />
        public override BlobClient GetBlobClient(string blobName)
        {
            return new InMemoryBlobClient(this, blobName);
        }

        internal void Store(string blobName, byte[] content)
        {
            if (RequireCreate && !Created)
            {
                throw new RequestFailedException(404, "The specified container does not exist.", "ContainerNotFound", null);
            }

            _blobs[blobName] = content;
            Interlocked.Increment(ref _uploads);
        }

        internal byte[] Load(string blobName)
        {
            Interlocked.Increment(ref _downloads);
            if (DownloadFailure is not null)
            {
                throw DownloadFailure;
            }

            return _blobs.TryGetValue(blobName, out byte[]? content)
                ? content
                : throw new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null);
        }

        internal bool Delete(string blobName)
        {
            return _blobs.TryRemove(blobName, out _);
        }

        internal bool Exists(string blobName)
        {
            return _blobs.ContainsKey(blobName);
        }

        /// <summary>The blob client of the double: the overloads Wolverine's Azure store calls, in memory.</summary>
        private sealed class InMemoryBlobClient : BlobClient
        {
            private readonly InMemoryBlobContainerClient _container;
            private readonly string _blobName;

            public InMemoryBlobClient(InMemoryBlobContainerClient container, string blobName)
            {
                _container = container;
                _blobName = blobName;
            }

            public override string Name => _blobName;

            public override Task<Response<BlobContentInfo>> UploadAsync(BinaryData content, BlobUploadOptions options, CancellationToken cancellationToken = default)
            {
                _container.Store(_blobName, content.ToArray());
                BlobContentInfo info = BlobsModelFactory.BlobContentInfo(new ETag("\"1\""), DateTimeOffset.UtcNow, null!, null!, null!, 0);
                return Task.FromResult(Response.FromValue(info, new InMemoryResponse(201)));
            }

            public override Task<Response<BlobDownloadResult>> DownloadContentAsync(CancellationToken cancellationToken)
            {
                byte[] bytes = _container.Load(_blobName);
                BlobDownloadResult result = BlobsModelFactory.BlobDownloadResult(BinaryData.FromBytes(bytes), null!);
                return Task.FromResult(Response.FromValue(result, new InMemoryResponse(200)));
            }

            public override Task<Response<bool>> DeleteIfExistsAsync(
                DeleteSnapshotsOption snapshotsOption = DeleteSnapshotsOption.None,
                BlobRequestConditions? conditions = null,
                CancellationToken cancellationToken = default)
            {
                bool deleted = _container.Delete(_blobName);
                return Task.FromResult(Response.FromValue(deleted, new InMemoryResponse(deleted ? 202 : 404)));
            }

            public override Task<Response<bool>> ExistsAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Response.FromValue(_container.Exists(_blobName), new InMemoryResponse(200)));
            }
        }

        /// <summary>A header-less raw response carrying only a status code.</summary>
        private sealed class InMemoryResponse(int status) : Response
        {
            public override int Status => status;

            public override string ReasonPhrase => status.ToString(System.Globalization.CultureInfo.InvariantCulture);

            public override Stream? ContentStream
            {
                get; set;
            }

            public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");

            public override void Dispose()
            {
            }

            protected override bool ContainsHeader(string name)
            {
                return false;
            }

            protected override IEnumerable<HttpHeader> EnumerateHeaders()
            {
                return [];
            }

            protected override bool TryGetHeader(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
            {
                value = null;
                return false;
            }

            protected override bool TryGetHeaderValues(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumerable<string>? values)
            {
                values = null;
                return false;
            }
        }
    }
}
