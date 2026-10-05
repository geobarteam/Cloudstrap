namespace Cloudstrap.TestInfrastructure
{
    using System.Collections.Concurrent;

    /// <summary>
    /// A per-host singleton the fixture handlers report into, so a test can await the arrival of a message at its
    /// handler without polling.
    /// </summary>
    public sealed class InvocationRecorder : IDisposable
    {
        private readonly ConcurrentQueue<object> _received = new();
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>Gets every message a handler recorded, in arrival order.</summary>
        public IReadOnlyCollection<object> Received => _received;

        /// <summary>Records one handled message.</summary>
        /// <param name="message">The handled message.</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
        public void Record(object message)
        {
            ArgumentNullException.ThrowIfNull(message);
            _received.Enqueue(message);
            _signal.Release();
        }

        /// <summary>Waits until at least one more recorded message is available and dequeues it.</summary>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="cancellationToken">A token to cancel the wait.</param>
        /// <returns>The next recorded message.</returns>
        /// <exception cref="TimeoutException">No message was recorded within <paramref name="timeout"/>.</exception>
        public async Task<object> WaitForNextAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (!await _signal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            {
                throw new TimeoutException($"No message reached a fixture handler within {timeout}.");
            }

            return _received.TryDequeue(out object? message)
                ? message
                : throw new InvalidOperationException("The recorder was signalled but held no message.");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _signal.Dispose();
        }
    }
}
