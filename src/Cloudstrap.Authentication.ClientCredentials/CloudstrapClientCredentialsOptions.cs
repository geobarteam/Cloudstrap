namespace Cloudstrap.Authentication.ClientCredentials
{
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// Settings for machine-to-machine (client credentials) token acquisition, bound from the flat
    /// <c>Cloudstrap:ClientCredentials</c> configuration section.
    /// </summary>
    /// <remarks>
    /// The client secret is a configuration value like any other: supply it through Azure Key Vault, an
    /// environment variable or user-secrets — never <c>appsettings.json</c>. It may be omitted entirely
    /// when a consumer registers Duende's <c>IClientAssertionService</c> to sign a client assertion
    /// instead (decision D-1).
    /// </remarks>
    public sealed class CloudstrapClientCredentialsOptions
    {
        /// <summary>
        /// The configuration section the options bind from.
        /// </summary>
        public const string SectionName = "Cloudstrap:ClientCredentials";

        /// <summary>
        /// The default name of the token backchannel's <see cref="HttpClient"/>.
        /// </summary>
        public const string DefaultBackchannelHttpClientName = "cloudstrap-clientcredentials";

        /// <summary>
        /// Gets or sets the absolute URL of the identity provider's token endpoint.
        /// </summary>
        /// <value>
        /// The token endpoint. Read it from the provider's <c>/.well-known/openid-configuration</c>
        /// document — this package deliberately performs no discovery of its own.
        /// </value>
        [Required(ErrorMessage = "'Cloudstrap:ClientCredentials:TokenEndpoint' is required — the absolute"
            + " URL of the token endpoint, found in the identity provider's discovery document.")]
        public Uri? TokenEndpoint
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the client identifier presented to the token endpoint.
        /// </summary>
        /// <value>The client identifier.</value>
        [Required(ErrorMessage = "'Cloudstrap:ClientCredentials:ClientId' is required.")]
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the client secret presented to the token endpoint.
        /// </summary>
        /// <value>
        /// The secret, or <see langword="null"/> for secret-free operation through a consumer-registered
        /// <c>IClientAssertionService</c>.
        /// </value>
        public string? ClientSecret
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the scope requested with every token.
        /// </summary>
        /// <value>
        /// The scope, or <see langword="null"/> to request none. A client's
        /// <c>TokenRequestParameters:Scope</c> overrides it per client.
        /// </value>
        public string? Scope
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the resource indicator (RFC 8707) sent with every token request.
        /// </summary>
        /// <value>
        /// The resource, or <see langword="null"/> to send none. A client's
        /// <c>TokenRequestParameters:Resource</c> overrides it per client.
        /// </value>
        public string? Resource
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets where acquired tokens are cached.
        /// </summary>
        /// <value>
        /// The cache mode. Defaults to <see cref="TokenCacheMode.Isolated"/> — tokens never enter the
        /// application's own caches (decision D-3).
        /// </value>
        public TokenCacheMode TokenCache { get; set; } = TokenCacheMode.Isolated;

        /// <summary>
        /// Gets or sets the name of the <see cref="HttpClient"/> used to call the token endpoint.
        /// </summary>
        /// <value>
        /// The client name. Defaults to <see cref="DefaultBackchannelHttpClientName"/>. When renamed,
        /// configure the renamed client with the standard <c>AddHttpClient(name)</c> registration.
        /// </value>
        public string BackchannelHttpClientName { get; set; } = DefaultBackchannelHttpClientName;
    }
}
