namespace Cloudstrap.Authentication.ClientCredentials
{
    /// <summary>
    /// Where acquired access tokens are cached (decision D-3 of the founding specification).
    /// </summary>
    public enum TokenCacheMode
    {
        /// <summary>
        /// Tokens live in a memory-only cache private to this package — the default. Bearer tokens never
        /// reach the application's own caches, so a shared <c>IDistributedCache</c> can never leak them;
        /// the trade-off is one token acquisition per application instance instead of one per cluster.
        /// </summary>
        Isolated = 0,

        /// <summary>
        /// Tokens use the application's <c>HybridCache</c>, including its distributed second tier when one
        /// is registered. Fewer token requests across instances; the trade-off is bearer tokens at rest in
        /// a shared store — see the README for Duende's cache-encryption guidance before opting in.
        /// </summary>
        Shared = 1,
    }
}
