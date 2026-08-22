namespace Cloudstrap.Authentication.ClientCredentials
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The log messages this package writes. Source-generated, so logging costs nothing when the level is
    /// disabled and the message template is checked at compile time. No message ever carries a credential
    /// or token value (AC-CC5).
    /// </summary>
    internal static partial class ClientCredentialsLog
    {
        /// <summary>
        /// Warns that a user-token setting is configured on a client-credentials token request, where it
        /// has no effect. Written once per client when its pipeline is built.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="key">The full configuration key of the ignored setting.</param>
        [LoggerMessage(
            EventId = 2000,
            Level = LogLevel.Warning,
            Message = "'{Key}' is a user-token setting and is ignored for client-credentials token"
                + " requests — remove it, or use Cloudstrap.Authentication.OpenIdConnect for user tokens.")]
        public static partial void UserTokenSettingIgnored(ILogger logger, string key);

        /// <summary>
        /// Warns that static force-renewal is enabled, so every request on the client fetches a fresh
        /// token. Written once per client when its pipeline is built.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="key">The full configuration key of the setting.</param>
        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Warning,
            Message = "'{Key}' is enabled: every request on this client fetches a fresh token, bypassing"
                + " the token cache. Intended for diagnostics, not for production traffic.")]
        public static partial void ForceRenewalConfigured(ILogger logger, string key);

        /// <summary>
        /// Records a failed token acquisition — the single log entry for the failure (AC-CC8). The
        /// outbound request it would have authenticated is never sent.
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="tokenEndpoint">The configured token endpoint.</param>
        /// <param name="error">The standard error identifier — never a credential or token value.</param>
        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Error,
            Message = "Client credentials token acquisition against '{TokenEndpoint}' failed: {Error}."
                + " The outbound request was not sent.")]
        public static partial void TokenAcquisitionFailed(ILogger logger, Uri? tokenEndpoint, string error);

        /// <summary>
        /// States the credential type in force — a name, never a value — so secret-free operation is
        /// visible, not magic (D-1).
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="credentialType">The credential type description.</param>
        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Information,
            Message = "Cloudstrap client credentials: token requests authenticate with {CredentialType}.")]
        public static partial void CredentialTypeInForce(ILogger logger, string credentialType);

        /// <summary>
        /// States the token cache mode in force — "visible, not magic" (D-3).
        /// </summary>
        /// <param name="logger">The logger to write to.</param>
        /// <param name="tokenCacheMode">The mode in force.</param>
        [LoggerMessage(
            EventId = 2004,
            Level = LogLevel.Information,
            Message = "Cloudstrap client credentials: the token cache mode in force is {TokenCacheMode}.")]
        public static partial void TokenCacheModeInForce(ILogger logger, TokenCacheMode tokenCacheMode);
    }
}
