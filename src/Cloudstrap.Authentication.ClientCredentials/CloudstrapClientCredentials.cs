namespace Cloudstrap.Authentication.ClientCredentials
{
    using Duende.AccessTokenManagement;

    /// <summary>
    /// Well-known names of the client-credentials integration.
    /// </summary>
    public static class CloudstrapClientCredentials
    {
        /// <summary>
        /// The name of the Duende AccessTokenManagement client this package registers. A consumer can
        /// inject Duende's <see cref="IClientCredentialsTokenManager"/> and request a token for this name
        /// directly — there is no Cloudstrap facade over it.
        /// </summary>
        public const string TokenClientName = "cloudstrap";

        /// <summary>
        /// The strongly typed form of <see cref="TokenClientName"/>, parsed once.
        /// </summary>
        internal static readonly ClientCredentialsClientName TokenClient =
            ClientCredentialsClientName.Parse(TokenClientName);
    }
}
