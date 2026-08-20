namespace Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure
{
    using Cloudstrap.WasmTestProject.Host.IdentityProvider;
    using Microsoft.Playwright;

    /// <summary>
    /// Signs a browser page in at the fixture-hosted test identity provider through the Bff's opt-in
    /// login endpoint — the one sign-in flow shared by every fixture that needs an authenticated
    /// cookie session. The credentials alias <see cref="TestIdentityProviderSeed"/>, the single
    /// source of truth, so a seed change can never strand the sign-in helpers.
    /// </summary>
    internal static class BrowserSignIn
    {
        /// <summary>The seeded test user's username.</summary>
        public const string Username = TestIdentityProviderSeed.Username;

        /// <summary>The seeded test user's password (a local-only placeholder, not a real secret).</summary>
        public const string Password = TestIdentityProviderSeed.Password;

        /// <summary>
        /// Navigates to the opt-in login endpoint, fills the provider's login form, and waits for the
        /// <c>form_post</c> callback to land the browser back on the application.
        /// </summary>
        /// <param name="page">The Playwright page driving the browser.</param>
        /// <param name="baseUrl">The application's base URL, without a trailing slash.</param>
        /// <param name="returnUrl">The application-relative URL to land on after sign-in.</param>
        public static async Task SignInAsync(IPage page, string baseUrl, string returnUrl = "/")
        {
            await page.GotoAsync(baseUrl + "/account/login?returnUrl=" + returnUrl);
            await page.WaitForURLAsync($"http://127.0.0.1:{E2eFixture.IdentityProviderPort}/**");
            await FillLoginFormAsync(page);
            await page.WaitForURLAsync(baseUrl + "/**");
        }

        /// <summary>
        /// Fills the identity provider's login form the browser is currently on and submits it —
        /// for flows that reach the provider by some other route than the opt-in login endpoint.
        /// </summary>
        /// <param name="page">The Playwright page currently showing the provider's login form.</param>
        public static async Task FillLoginFormAsync(IPage page)
        {
            await page.FillAsync("[data-testid='username']", Username);
            await page.FillAsync("[data-testid='password']", Password);
            await page.ClickAsync("[data-testid='submit']");
        }
    }
}
