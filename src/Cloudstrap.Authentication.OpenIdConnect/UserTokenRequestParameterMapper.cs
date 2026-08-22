namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Cloudstrap.Core;
    using Duende.AccessTokenManagement;
    using Duende.AccessTokenManagement.OpenIdConnect;

    /// <summary>
    /// Maps a client's <c>Cloudstrap:HttpClients:{name}:TokenRequestParameters</c> section to Duende's
    /// per-request user-token parameters. All five members map — including
    /// <see cref="TokenRequestOptions.SignInScheme"/> and
    /// <see cref="TokenRequestOptions.ChallengeScheme"/>, the two settings the client-credentials
    /// package ignores with a warning (spec finding 10).
    /// </summary>
    internal static class UserTokenRequestParameterMapper
    {
        /// <summary>
        /// Maps the configured per-client parameters to Duende's shape.
        /// </summary>
        /// <param name="tokenRequest">The configured parameters, or <see langword="null"/> for none.</param>
        /// <returns>The Duende parameters — empty when the section configures none.</returns>
        public static UserTokenRequestParameters Map(TokenRequestOptions? tokenRequest)
        {
            if (tokenRequest is null)
            {
                return new UserTokenRequestParameters();
            }

            return new UserTokenRequestParameters
            {
                Scope = string.IsNullOrEmpty(tokenRequest.Scope) ? null : Scope.Parse(tokenRequest.Scope),
                Resource = string.IsNullOrEmpty(tokenRequest.Resource) ? null : Resource.Parse(tokenRequest.Resource),
                ForceTokenRenewal = tokenRequest.ForceRenewal,
                SignInScheme = string.IsNullOrEmpty(tokenRequest.SignInScheme)
                    ? null
                    : Scheme.Parse(tokenRequest.SignInScheme),
                ChallengeScheme = string.IsNullOrEmpty(tokenRequest.ChallengeScheme)
                    ? null
                    : Scheme.Parse(tokenRequest.ChallengeScheme),
            };
        }
    }
}
