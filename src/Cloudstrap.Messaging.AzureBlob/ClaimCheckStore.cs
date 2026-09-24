namespace Cloudstrap.Messaging.AzureBlob
{
    using Azure;
    using Azure.Storage.Blobs;
    using Azure.Storage.Blobs.Models;
    using Microsoft.Extensions.Logging;
    using Wolverine.Persistence;

    /// <summary>
    /// The leaf's store: Wolverine's Azure claim-check store plus the two things it leaves to its host —
    /// creating the dedicated container on first use (Wolverine's store uploads straight into it and creates
    /// nothing), and the one failure log line for a payload that could not be loaded (AC-CK4): the payload id,
    /// the container name and the HTTP status — never the payload, never a URI or a connection string. The
    /// SDK's exception is rethrown unchanged, so Wolverine's own dead-letter line (message id) and the
    /// dead-letter row's exception type stay exactly what the SDK raised.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Creation happens lazily, on the first upload that answers <c>404 ContainerNotFound</c>, so a node that
    /// never offloads never touches storage at startup and a pre-created container (infrastructure-as-code)
    /// costs nothing extra. The identity therefore needs container-create rights on the account, or the
    /// container must exist — independent of <c>Cloudstrap:Messaging:AutoProvision</c>.
    /// </para>
    /// <para>
    /// A payload is restored while the envelope is deserialized, and Wolverine dead-letters a deserialization
    /// failure before any failure rule or dead-letter interceptor on a durable listener runs — so the store,
    /// not a failure rule, is where the leaf observes the failure.
    /// </para>
    /// </remarks>
    internal sealed partial class ClaimCheckStore : IClaimCheckStore
    {
        private const string _containerNotFound = "ContainerNotFound";

        private readonly IClaimCheckStore _inner;
        private readonly BlobContainerClient _container;
        private readonly ILogger _logger;

        public ClaimCheckStore(IClaimCheckStore inner, BlobContainerClient container, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(container);
            ArgumentNullException.ThrowIfNull(logger);

            _inner = inner;
            _container = container;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<ClaimCheckToken> StoreAsync(ReadOnlyMemory<byte> payload, string contentType, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.StoreAsync(payload, contentType, cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException failure) when (failure.Status == 404 && failure.ErrorCode == _containerNotFound)
            {
                LogCreatingContainer(_container.Name);
                await _container.CreateIfNotExistsAsync(PublicAccessType.None, metadata: null, cancellationToken).ConfigureAwait(false);
                return await _inner.StoreAsync(payload, contentType, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<ReadOnlyMemory<byte>> LoadAsync(ClaimCheckToken token, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(token);

            try
            {
                return await _inner.LoadAsync(token, cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException failure)
            {
                LogPayloadUnavailable(token.Id, _container.Name, failure.Status);
                throw;
            }
        }

        /// <inheritdoc />
        public Task DeleteAsync(ClaimCheckToken token, CancellationToken cancellationToken = default)
        {
            return _inner.DeleteAsync(token, cancellationToken);
        }

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Error,
            Message = "Cloudstrap claim check: payload '{PayloadId}' could not be loaded from container '{Container}' (HTTP {Status}); " +
                      "the message is dead-lettered and can be replayed once the payload is available")]
        private partial void LogPayloadUnavailable(string payloadId, string container, int status);

        [LoggerMessage(
            EventId = 3,
            Level = LogLevel.Information,
            Message = "Cloudstrap claim check: container '{Container}' does not exist yet; creating it on first use")]
        private partial void LogCreatingContainer(string container);
    }
}
