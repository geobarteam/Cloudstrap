namespace Cloudstrap.Mvc.Tests.Infrastructure
{
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.Caching.Memory;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// A test-owned <see cref="IDistributedCache"/> wrapping the framework's in-memory implementation and
    /// recording reads and writes — so "the consumer's cache is the one session state uses" is observable.
    /// </summary>
    internal sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly MemoryDistributedCache _inner =
            new(Options.Create(new MemoryDistributedCacheOptions()));

        private int _reads;
        private int _writes;

        /// <summary>Gets the number of read calls observed.</summary>
        public int Reads => _reads;

        /// <summary>Gets the number of write calls observed.</summary>
        public int Writes => _writes;

        /// <inheritdoc/>
        public byte[]? Get(string key)
        {
            Interlocked.Increment(ref _reads);

            return _inner.Get(key);
        }

        /// <inheritdoc/>
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            Interlocked.Increment(ref _reads);

            return _inner.GetAsync(key, token);
        }

        /// <inheritdoc/>
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Interlocked.Increment(ref _writes);
            _inner.Set(key, value, options);
        }

        /// <inheritdoc/>
        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Interlocked.Increment(ref _writes);

            return _inner.SetAsync(key, value, options, token);
        }

        /// <inheritdoc/>
        public void Refresh(string key)
        {
            _inner.Refresh(key);
        }

        /// <inheritdoc/>
        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            return _inner.RefreshAsync(key, token);
        }

        /// <inheritdoc/>
        public void Remove(string key)
        {
            _inner.Remove(key);
        }

        /// <inheritdoc/>
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            return _inner.RemoveAsync(key, token);
        }
    }
}
