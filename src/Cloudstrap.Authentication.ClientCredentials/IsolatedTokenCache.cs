namespace Cloudstrap.Authentication.ClientCredentials
{
    using Microsoft.Extensions.Caching.Hybrid;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Owns the memory-only <see cref="HybridCache"/> behind <see cref="TokenCacheMode.Isolated"/>: a
    /// private container with no distributed tier, so a token cached here can never reach the
    /// application's own <c>IDistributedCache</c> (decision D-3).
    /// </summary>
    internal sealed class IsolatedTokenCache : IDisposable
    {
        private readonly ServiceProvider _provider;

        /// <summary>
        /// Initializes a new instance of the <see cref="IsolatedTokenCache"/> class.
        /// </summary>
        public IsolatedTokenCache()
        {
            ServiceCollection services = new();
            services.AddHybridCache();
            _provider = services.BuildServiceProvider();
            Cache = _provider.GetRequiredService<HybridCache>();
        }

        /// <summary>
        /// Gets the isolated, memory-only cache instance.
        /// </summary>
        /// <value>The cache.</value>
        public HybridCache Cache
        {
            get;
        }

        /// <inheritdoc/>
        public void Dispose() => _provider.Dispose();
    }
}
