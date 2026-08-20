namespace Cloudstrap.Demo.IdentityProvider
{
    using Cloudstrap.TestIdentityProvider;

    /// <summary>
    /// The one seed both identity-provider hosts share — this demo host and the E2E fixture's
    /// in-process 5310 instance — so the clients, user and credentials can never drift apart.
    /// Demo-only test infrastructure: the credentials are obvious placeholders, never real secrets.
    /// </summary>
    public static class TestIdentityProviderSeed
    {
        /// <summary>The seeded demo user's username — the one value sign-in helpers must submit.</summary>
        public const string Username = "geobarteam";

        /// <summary>The seeded demo user's password (a local-only placeholder, never a real secret).</summary>
        public const string Password = "password";

        /// <summary>The seeded demo user's <c>name</c> claim, as pages and tokens display it.</summary>
        public const string DisplayName = "Geo Bar Team";

        /// <summary>
        /// Seeds the provider with the demo clients (<c>demo-bff</c> for
        /// machine tokens, <c>demo-web</c> for interactive login) and the one neutral test
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
                ClientId = "demo-bff",
                ClientSecret = "local-e2e-placeholder-secret",
                Scopes = { "selfapi" },
                Audiences = { "demo-selfapi" },
            });

            TestIdentityProviderClient webClient = new TestIdentityProviderClient
            {
                ClientId = "demo-web",
                ClientSecret = "local-e2e-placeholder-secret-web",
                Scopes = { "selfapi" },
                Audiences = { "demo-selfapi" },
            };
            foreach (Uri baseAddress in applicationBaseAddresses)
            {
                webClient.RedirectUris.Add(new Uri(baseAddress, "signin-oidc"));
                webClient.PostLogoutRedirectUris.Add(new Uri(baseAddress, "signout-callback-oidc"));
            }

            options.Clients.Add(webClient);

            options.Users.Add(new TestIdentityProviderUser
            {
                Username = Username,
                Password = Password,
                Claims =
                {
                    ["name"] = [DisplayName],
                    ["role"] = ["tester"],
                },
            });
        }
    }
}
