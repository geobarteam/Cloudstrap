namespace Cloudstrap.Messaging.Tests.Fixtures
{
    using System.Collections.Concurrent;

    /// <summary>
    /// A per-host singleton the fixture handlers report into, so a test can await the arrival of a
    /// message at its handler without polling.
    /// </summary>
    public sealed class InvocationRecorder : IDisposable
    {
        private readonly ConcurrentQueue<object> _received = new();
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>Gets every message a handler recorded, in arrival order.</summary>
        public IReadOnlyCollection<object> Received => _received;

        /// <summary>Records one handled message.</summary>
        public void Record(object message)
        {
            _received.Enqueue(message);
            _signal.Release();
        }

        /// <summary>Waits until at least one more recorded message is available and dequeues it.</summary>
        public async Task<object> WaitForNextAsync(TimeSpan timeout)
        {
            if (!await _signal.WaitAsync(timeout).ConfigureAwait(false))
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
