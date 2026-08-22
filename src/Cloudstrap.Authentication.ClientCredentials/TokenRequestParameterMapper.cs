namespace Cloudstrap.Authentication.ClientCredentials
{
    using Cloudstrap.Core;
    using Duende.AccessTokenManagement;

    /// <summary>
    /// Maps a client's <c>Cloudstrap:HttpClients:{name}:TokenRequestParameters</c> section to Duende's
    /// per-request token parameters. <see cref="TokenRequestOptions.SignInScheme"/> and
    /// <see cref="TokenRequestOptions.ChallengeScheme"/> are user-token settings and deliberately do not
    /// map — a client-credentials token request ignores them.
    /// </summary>
    internal static class TokenRequestParameterMapper
    {
        /// <summary>
        /// Maps the configured per-client parameters to Duende's shape.
        /// </summary>
        /// <param name="tokenRequest">The configured parameters, or <see langword="null"/> for none.</param>
        /// <returns>The Duende parameters — empty when the section configures none.</returns>
        public static TokenRequestParameters Map(TokenRequestOptions? tokenRequest)
        {
            if (tokenRequest is null)
            {
                return new TokenRequestParameters();
            }

            return new TokenRequestParameters
            {
                Scope = string.IsNullOrEmpty(tokenRequest.Scope) ? null : Scope.Parse(tokenRequest.Scope),
                Resource = string.IsNullOrEmpty(tokenRequest.Resource) ? null : Resource.Parse(tokenRequest.Resource),
                ForceTokenRenewal = tokenRequest.ForceRenewal,
            };
        }
    }
}
