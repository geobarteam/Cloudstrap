namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The log messages this package writes. Source-generated, so logging costs nothing when the level
    /// is disabled and the message template is checked at compile time. No message ever carries a
    /// secret, authorization code or token value (AC-OIDC7).
    /// </summary>
    internal static partial class OpenIdConnectLog
    {
        /// <summary>
        /// States the sign-in posture in force at host startup — scheme and cookie names only, never a
        /// value ("visible, not magic").
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="cookieScheme">The default (cookie) scheme name.</param>
        /// <param name="challengeScheme">The challenge scheme name.</param>
        /// <param name="cookieName">The session cookie's name.</param>
        [LoggerMessage(
            EventId = 3000,
            Level = LogLevel.Information,
            Message = "Cloudstrap OpenID Connect: interactive sign-in holds the session in the"
                + " '{CookieScheme}' scheme (cookie '{CookieName}') and challenges through"
                + " '{ChallengeScheme}'; tokens are stored in the authentication session — no"
                + " server-side token store is registered.")]
        public static partial void SignInPostureInForce(
            ILogger logger,
            string cookieScheme,
            string challengeScheme,
            string cookieName);

        /// <summary>
        /// States that the opt-in authentication endpoints are mapped and where.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="loginPath">The login path in force.</param>
        /// <param name="logoutPath">The logout path in force.</param>
        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Information,
            Message = "Cloudstrap OpenID Connect: the opt-in authentication endpoints are mapped —"
                + " login '{LoginPath}', logout '{LogoutPath}'.")]
        public static partial void AuthenticationEndpointsMapped(
            ILogger logger,
            string loginPath,
            string logoutPath);

        /// <summary>
        /// States that the opt-in authentication endpoints are not mapped.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = 3002,
            Level = LogLevel.Information,
            Message = "Cloudstrap OpenID Connect: the opt-in authentication endpoints are not mapped —"
                + " call MapCloudstrapAuthenticationEndpoints() to add login and logout, or map your"
                + " own Challenge/SignOut endpoints.")]
        public static partial void AuthenticationEndpointsNotMapped(ILogger logger);

        /// <summary>
        /// Warns — once per application — that the identity provider advertises no
        /// <c>end_session_endpoint</c>, so sign-out ends the local session only.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = 3003,
            Level = LogLevel.Warning,
            Message = "The identity provider's discovery document advertises no end_session_endpoint:"
                + " sign-out ends the local session only, and the identity provider session stays"
                + " alive until it expires there.")]
        public static partial void EndSessionEndpointUnavailable(ILogger logger);

        /// <summary>
        /// Records that no user token could be produced for a flagged client — the single log entry
        /// for the failure. Names the flag and the reason category; never a token or credential value.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="flag">The full configuration key of the client's flag.</param>
        /// <param name="reason">The reason — a standard error identifier or category, never a value.</param>
        [LoggerMessage(
            EventId = 3004,
            Level = LogLevel.Error,
            Message = "No user access token could be produced for the client flagged '{Flag}':"
                + " {Reason}. The outbound request was not sent.")]
        public static partial void UserTokenUnavailable(ILogger logger, string flag, string reason);

        /// <summary>
        /// States that bearer coexistence is active: a JWT bearer scheme is registered, so requests
        /// carrying a bearer header authenticate and fail through it.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = 3005,
            Level = LogLevel.Information,
            Message = "Cloudstrap OpenID Connect: bearer coexistence is active — requests carrying an"
                + " 'Authorization: Bearer' header authenticate through the 'Bearer' scheme and fail"
                + " with 401, never a login redirect.")]
        public static partial void BearerCoexistenceActive(ILogger logger);

        /// <summary>
        /// States that bearer coexistence is inert: no JWT bearer scheme is registered.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        [LoggerMessage(
            EventId = 3006,
            Level = LogLevel.Information,
            Message = "Cloudstrap OpenID Connect: bearer coexistence is inert — no 'Bearer' scheme is"
                + " registered; every request uses the cookie/OpenID Connect path.")]
        public static partial void BearerCoexistenceInert(ILogger logger);
    }
}
