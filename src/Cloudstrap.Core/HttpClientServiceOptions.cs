namespace Cloudstrap.Core
{
    /// <summary>
    /// Settings for a single named outbound HTTP client, bound from the
    /// <c>Cloudstrap:HttpClients:{name}</c> configuration section.
    /// </summary>
    public sealed class HttpClientServiceOptions
    {
        /// <summary>
        /// Gets or sets the base address every request of this client is resolved against.
        /// Required, and must be an absolute URI.
        /// </summary>
        /// <value>The base address, or <see langword="null"/> when not configured — which fails validation.</value>
        public Uri? BaseAddress
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets how long a request may run before it is cancelled.
        /// </summary>
        /// <value>The request timeout. Defaults to 30 seconds.</value>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Gets or sets a value indicating whether the signed-in user's access token is attached to requests.
        /// </summary>
        /// <value><see langword="true"/> to attach the user access token. Defaults to <see langword="false"/>.</value>
        public bool AddUserAccessToken
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether an application (client credentials) access token is attached
        /// to requests.
        /// </summary>
        /// <value><see langword="true"/> to attach the client access token. Defaults to <see langword="false"/>.</value>
        public bool AddClientAccessToken
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether a health check probing this client is registered.
        /// </summary>
        /// <value><see langword="true"/> to register a health check. Defaults to <see langword="false"/>.</value>
        public bool EnableHealthCheck
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the prefix used to name this client's health check.
        /// </summary>
        /// <value>The health check name prefix, or <see langword="null"/> to use the client name.</value>
        public string? HealthCheckPrefix
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the parameters used when requesting access tokens for this client.
        /// </summary>
        /// <value>The token request parameters, or <see langword="null"/> when the defaults apply.</value>
        public TokenRequestOptions? TokenRequestParameters
        {
            get; set;
        }
    }
}
