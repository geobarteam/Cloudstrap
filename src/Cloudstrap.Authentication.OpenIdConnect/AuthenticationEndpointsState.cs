namespace Cloudstrap.Authentication.OpenIdConnect
{
    /// <summary>
    /// Per-application state of the opt-in authentication endpoints: whether the mapper was called and
    /// at which paths — reported by the startup logger — and the once-only latch for the
    /// missing-end-session warning. A singleton, so parallel test hosts never share state.
    /// </summary>
    internal sealed class AuthenticationEndpointsState
    {
        private int _endSessionWarningLogged;

        /// <summary>
        /// Gets a value indicating whether <c>MapCloudstrapAuthenticationEndpoints</c> was called.
        /// </summary>
        public bool EndpointsMapped
        {
            get; private set;
        }

        /// <summary>
        /// Gets the login path in force when the endpoints are mapped.
        /// </summary>
        public string? LoginPath
        {
            get; private set;
        }

        /// <summary>
        /// Gets the logout path in force when the endpoints are mapped.
        /// </summary>
        public string? LogoutPath
        {
            get; private set;
        }

        /// <summary>
        /// Records that the endpoints were mapped and where.
        /// </summary>
        /// <param name="loginPath">The login path.</param>
        /// <param name="logoutPath">The logout path.</param>
        public void MarkMapped(string loginPath, string logoutPath)
        {
            EndpointsMapped = true;
            LoginPath = loginPath;
            LogoutPath = logoutPath;
        }

        /// <summary>
        /// Latches the missing-end-session warning, so it is written exactly once per application.
        /// </summary>
        /// <returns><see langword="true"/> on the first call only.</returns>
        public bool TryMarkEndSessionWarningLogged() =>
            Interlocked.Exchange(ref _endSessionWarningLogged, 1) == 0;
    }
}
