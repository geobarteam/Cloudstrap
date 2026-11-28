namespace Cloudstrap.Proxy
{
    /// <summary>
    /// The access token a proxied cluster's requests carry to the downstream API. Set per cluster with the
    /// <see cref="ProxyMetadataKeys.TokenMode"/> cluster metadata key; clusters without it use
    /// <see cref="CloudstrapProxyOptions.DefaultTokenMode"/>.
    /// </summary>
    public enum ProxyTokenMode
    {
        /// <summary>
        /// No token is attached. The downstream API receives the request without an <c>Authorization</c> header
        /// (the caller's own one is still removed unless the route opts out). Must be chosen explicitly.
        /// </summary>
        None = 0,

        /// <summary>
        /// The application's own (client-credentials) token, from the registered
        /// <c>IClientAccessTokenHandlerProvider</c> (install <c>Cloudstrap.Authentication.ClientCredentials</c>).
        /// The trusted-subsystem default.
        /// </summary>
        Client = 1,

        /// <summary>
        /// The signed-in user's token, from the registered <c>IUserAccessTokenHandlerProvider</c> (install
        /// <c>Cloudstrap.Authentication.OpenIdConnect</c>).
        /// </summary>
        User = 2,
    }
}
