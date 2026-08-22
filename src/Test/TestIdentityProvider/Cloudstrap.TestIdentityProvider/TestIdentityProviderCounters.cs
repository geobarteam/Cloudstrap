namespace Cloudstrap.TestIdentityProvider
{
    /// <summary>
    /// Endpoint hit counters for the interactive flows — the observable levers renewal and challenge
    /// tests assert against. The pre-existing token-endpoint counter lives on
    /// <see cref="TestIdentityProviderHost"/> and is deliberately untouched by this type.
    /// </summary>
    public sealed class TestIdentityProviderCounters
    {
        private int _authorizeRequestCount;
        private int _refreshTokenRequestCount;

        /// <summary>
        /// Gets the number of requests the authorization endpoint has received.
        /// </summary>
        /// <value>The authorization request count.</value>
        public int AuthorizeRequestCount => Volatile.Read(ref _authorizeRequestCount);

        /// <summary>
        /// Gets the number of refresh-token grant requests the token endpoint has received.
        /// </summary>
        /// <value>The refresh-token grant request count.</value>
        public int RefreshTokenRequestCount => Volatile.Read(ref _refreshTokenRequestCount);

        /// <summary>
        /// Records one authorization-endpoint request.
        /// </summary>
        internal void IncrementAuthorizeRequestCount() => Interlocked.Increment(ref _authorizeRequestCount);

        /// <summary>
        /// Records one refresh-token grant request.
        /// </summary>
        internal void IncrementRefreshTokenRequestCount() => Interlocked.Increment(ref _refreshTokenRequestCount);
    }
}
