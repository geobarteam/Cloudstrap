namespace Cloudstrap.TestIdentityProvider
{
    using Microsoft.Extensions.Options;
    using OpenIddict.Abstractions;
    using OpenIddict.Server;

    /// <summary>
    /// Carries the data-driven parts of <see cref="TestIdentityProviderOptions"/> — token lifetimes, an
    /// explicit issuer, and the union of all configured client scopes — into OpenIddict's server
    /// options, and enables the interactive grants (authorization code with a server-wide PKCE
    /// requirement, plus refresh tokens) only when interactive users or redirect URIs are configured,
    /// so a client-credentials-only provider keeps exactly its original discovery document.
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
            options.RefreshTokenLifetime = providerOptions.RefreshTokenLifetime;

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

            if (providerOptions.Users.Count > 0
                || providerOptions.Clients.Any(static client => client.RedirectUris.Count > 0))
            {
                // The equivalent of AllowAuthorizationCodeFlow() + AllowRefreshTokenFlow() +
                // RequireProofKeyForCodeExchange() + SetAuthorizationEndpointUris(...), applied here so
                // interactivity is driven by the configured options. S256 is the only code-challenge
                // method; implicit and hybrid flows are neither enabled nor representable.
                options.GrantTypes.Add(OpenIddictConstants.GrantTypes.AuthorizationCode);
                options.GrantTypes.Add(OpenIddictConstants.GrantTypes.RefreshToken);
                options.ResponseTypes.Add(OpenIddictConstants.ResponseTypes.Code);
                options.CodeChallengeMethods.Clear();
                options.CodeChallengeMethods.Add(OpenIddictConstants.CodeChallengeMethods.Sha256);
                options.RequireProofKeyForCodeExchange = true;
                options.AuthorizationEndpointUris.Add(new Uri("connect/authorize", UriKind.Relative));
                options.UserInfoEndpointUris.Add(new Uri("connect/userinfo", UriKind.Relative));
                options.EndSessionEndpointUris.Add(new Uri("connect/logout", UriKind.Relative));
                options.Scopes.Add(OpenIddictConstants.Scopes.OpenId);
                options.Scopes.Add(OpenIddictConstants.Scopes.Profile);
                options.Scopes.Add(OpenIddictConstants.Scopes.OfflineAccess);
            }
        }
    }
}
