namespace Cloudstrap.Authentication.ClientCredentials
{
    using Duende.AccessTokenManagement;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Carries the bound <see cref="CloudstrapClientCredentialsOptions"/> into the Duende
    /// <see cref="ClientCredentialsClient"/> named <see cref="CloudstrapClientCredentials.TokenClientName"/>.
    /// Other client names — a consumer's own Duende registrations — are never touched.
    /// </summary>
    internal sealed class CloudstrapTokenClientConfigurator : IConfigureNamedOptions<ClientCredentialsClient>
    {
        private readonly IOptions<CloudstrapClientCredentialsOptions> _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudstrapTokenClientConfigurator"/> class.
        /// </summary>
        /// <param name="options">The bound Cloudstrap options.</param>
        public CloudstrapTokenClientConfigurator(IOptions<CloudstrapClientCredentialsOptions> options)
        {
            _options = options;
        }

        /// <inheritdoc/>
        public void Configure(string? name, ClientCredentialsClient options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (!string.Equals(name, CloudstrapClientCredentials.TokenClientName, StringComparison.Ordinal))
            {
                return;
            }

            CloudstrapClientCredentialsOptions source = _options.Value;

            if (source.TokenEndpoint is not null)
            {
                options.TokenEndpoint = source.TokenEndpoint;
            }

            if (!string.IsNullOrEmpty(source.ClientId))
            {
                options.ClientId = ClientId.Parse(source.ClientId);
            }

            if (!string.IsNullOrEmpty(source.ClientSecret))
            {
                options.ClientSecret = ClientSecret.Parse(source.ClientSecret);
            }

            if (!string.IsNullOrEmpty(source.Scope))
            {
                options.Scope = Scope.Parse(source.Scope);
            }

            if (!string.IsNullOrEmpty(source.Resource))
            {
                options.Resource = Resource.Parse(source.Resource);
            }

            options.HttpClientName = source.BackchannelHttpClientName;
        }

        /// <inheritdoc/>
        public void Configure(ClientCredentialsClient options) => Configure(Options.DefaultName, options);
    }
}
