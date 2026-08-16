namespace Cloudstrap.WasmTestProject.Host.IdentityProvider
{
    using Cloudstrap.TestIdentityProvider;

    /// <summary>
    /// The one seed both identity-provider hosts share — this demo host and the E2E fixture's
    /// in-process 5310 instance — so the clients, user and credentials can never drift apart.
    /// Demo-only test infrastructure: the credentials are obvious placeholders, never real secrets.
    /// </summary>
    public static class TestIdentityProviderSeed
    {
        /// <summary>
        /// Seeds the provider with the WasmTestProject clients (<c>wasmtestproject-bff</c> for
        /// machine tokens, <c>wasmtestproject-web</c> for interactive login) and the one neutral test
        /// user, deriving the web client's redirect URIs from the application base address(es) so
        /// they follow whichever Bff instance is being served.
        /// </summary>
        /// <param name="options">The provider options to seed.</param>
        /// <param name="applicationBaseAddresses">
        /// The base address(es) of the application(s) allowed to sign in — each contributes a
        /// <c>signin-oidc</c> redirect URI and a <c>signout-callback-oidc</c> post-logout URI.
        /// </param>
        public static void Configure(
            TestIdentityProviderOptions options,
            IReadOnlyCollection<Uri> applicationBaseAddresses)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(applicationBaseAddresses);

            options.Clients.Add(new TestIdentityProviderClient
            {
                ClientId = "wasmtestproject-bff",
                ClientSecret = "local-e2e-placeholder-secret",
                Scopes = { "selfapi" },
                Audiences = { "wasmtestproject-selfapi" },
            });

            TestIdentityProviderClient webClient = new TestIdentityProviderClient
            {
                ClientId = "wasmtestproject-web",
                ClientSecret = "local-e2e-placeholder-secret-web",
                Scopes = { "selfapi" },
                Audiences = { "wasmtestproject-selfapi" },
            };
            foreach (Uri baseAddress in applicationBaseAddresses)
            {
                webClient.RedirectUris.Add(new Uri(baseAddress, "signin-oidc"));
                webClient.PostLogoutRedirectUris.Add(new Uri(baseAddress, "signout-callback-oidc"));
            }

            options.Clients.Add(webClient);

            options.Users.Add(new TestIdentityProviderUser
            {
                Username = "geobarteam",
                Password = "password",
                Claims =
                {
                    ["name"] = ["Geo Bar Team"],
                    ["role"] = ["tester"],
                },
            });
        }
    }
}
