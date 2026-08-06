namespace Cloudstrap.TestIdentityProvider
{
    using Microsoft.Extensions.Options;
    using OpenIddict.Server;

    /// <summary>
    /// Carries the data-driven parts of <see cref="TestIdentityProviderOptions"/> — token lifetime, an
    /// explicit issuer, and the union of all configured client scopes — into OpenIddict's server options.
    /// </summary>
    internal sealed class TestIdentityProviderServerOptionsConfigurator : IConfigureOptions<OpenIddictServerOptions>
    {
        private readonly IOptions<TestIdentityProviderOptions> _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestIdentityProviderServerOptionsConfigurator"/> class.
        /// </summary>
        /// <param name="options">The bound test identity provider options.</param>
        public TestIdentityProviderServerOptionsConfigurator(IOptions<TestIdentityProviderOptions> options)
        {
            _options = options;
        }

        /// <inheritdoc/>
        public void Configure(OpenIddictServerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            TestIdentityProviderOptions providerOptions = _options.Value;

            options.AccessTokenLifetime = providerOptions.AccessTokenLifetime;

            if (providerOptions.Issuer is not null)
            {
                options.Issuer = providerOptions.Issuer;
            }

            foreach (string scope in providerOptions.Clients
                .SelectMany(static client => client.Scopes)
                .Distinct(StringComparer.Ordinal))
            {
                options.Scopes.Add(scope);
            }
        }
    }
}
