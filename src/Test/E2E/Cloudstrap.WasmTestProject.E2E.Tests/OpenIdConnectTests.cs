namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using Cloudstrap.WasmTestProject.Host.IdentityProvider;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// The deliverable #10 demonstration (AC-OIDC12): a real headless Chromium signs in at the
    /// loopback test identity provider through the Bff's one <c>AddCloudstrapOpenIdConnect()</c> call,
    /// the user-flagged typed client calls the protected API <em>as that user</em>, and signing out
    /// ends both sessions — while the machine endpoint's 401 contract stays intact.
    /// </summary>
    [TestFixture]
    public sealed class OpenIdConnectTests : PageTestBase
    {
        private const string _username = BrowserSignIn.Username;

        [Test]
        public async Task Login_ThroughTheBrowser_SignsTheUserInAndIssuesTheHardenedCookie()
        {
            // Act — navigate to the opt-in login endpoint, authenticate at the provider, come back
            await SignInThroughBrowserAsync();

            BrowserContextCookiesResult? sessionCookie = (await Page.Context.CookiesAsync())
                .SingleOrDefault(static cookie => cookie.Name == "__Host-Cloudstrap");

            (int status, JsonElement whoAmIJson) = await FetchJsonInPageAsync("/api/v1/user/whoami");

            // Assert — D-1 proven in a real browser on loopback, and the cookie principal is the
            // signed-in user (AC-A1, AC-OIDC2, AC-OIDC12)
            Assert.Multiple(() =>
            {
                Assert.That(sessionCookie, Is.Not.Null, "The __Host-Cloudstrap session cookie must exist.");
                Assert.That(sessionCookie!.HttpOnly, Is.True);
                Assert.That(sessionCookie.Secure, Is.True);
                Assert.That(sessionCookie.SameSite, Is.EqualTo(SameSiteAttribute.Lax));
                Assert.That(sessionCookie.Path, Is.EqualTo("/"));
                Assert.That(status, Is.EqualTo((int)HttpStatusCode.OK));
                Assert.That(whoAmIJson.GetProperty("sub").GetString(), Is.EqualTo(_username));
                Assert.That(
                    whoAmIJson.GetProperty("name").GetString(),
                    Is.EqualTo(TestIdentityProviderSeed.DisplayName));
            });
        }

        [Test]
        public async Task SignedInUser_CallsTheProtectedApiAsThemselves()
        {
            // Arrange
            await SignInThroughBrowserAsync();

            // Act — the in-app round trip: the user-flagged UserApi client drives the protected
            // machine endpoint, which echoes who the caller was
            (int status, JsonElement json) = await FetchJsonInPageAsync("/api/v1/user/call");

            // Assert — the user's token, acquired through the interactive flow and validated by #5,
            // reached the peer through a client flagged for both token kinds (AC-OIDC3 + AC-CC13 live)
            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo((int)HttpStatusCode.OK));
                Assert.That(json.GetProperty("subject").GetString(), Is.EqualTo(_username));
                Assert.That(json.GetProperty("clientId").GetString(), Is.EqualTo("wasmtestproject-web"));
            });
        }

        [Test]
        public async Task Logout_EndsBothTheLocalAndTheProviderSession()
        {
            // Arrange
            await SignInThroughBrowserAsync();

            // Act — RP-initiated logout, then try to reach a protected endpoint again
            await Page.GotoAsync(BaseUrl + "/account/logout");
            await Page.WaitForURLAsync(BaseUrl + "/**");

            bool sessionCookieGone = (await Page.Context.CookiesAsync())
                .All(static cookie => cookie.Name != "__Host-Cloudstrap");

            await Page.GotoAsync(BaseUrl + "/api/v1/user/whoami");
            bool loginFormVisible = await Page.Locator("[data-testid='username']").IsVisibleAsync();

            // Assert — the browser ends up on the provider's login form rather than being silently
            // re-authenticated: both sessions really ended (AC-OIDC5)
            Assert.Multiple(() =>
            {
                Assert.That(sessionCookieGone, Is.True, "The __Host-Cloudstrap cookie must be gone.");
                Assert.That(
                    Page.Url,
                    Does.StartWith($"http://127.0.0.1:{E2eFixture.IdentityProviderPort}"),
                    "The anonymous request must be challenged back to the provider.");
                Assert.That(
                    loginFormVisible,
                    Is.True,
                    "The provider must serve its login form — not silently re-authenticate.");
            });
        }

        [Test]
        public async Task AnonymousBrowser_IsChallengedWhileTheMachineEndpointStill401s()
        {
            // Act — an anonymous browser hits a protected user endpoint…
            await Page.GotoAsync(BaseUrl + "/api/v1/user/whoami");

            // …while a plain API caller hits the bearer-pinned machine endpoint
            using HttpClient plainClient = new();
            using HttpResponseMessage machineStatus =
                await plainClient.GetAsync(new Uri(BaseUrl + "/api/v1/machine/status"));

            // Assert — coexistence live: browsers are challenged, machine callers get 401, and the #9
            // E2E contract is intact (AC-OIDC9)
            Assert.Multiple(() =>
            {
                Assert.That(
                    Page.Url,
                    Does.StartWith($"http://127.0.0.1:{E2eFixture.IdentityProviderPort}"),
                    "The anonymous browser must be redirected to the provider.");
                Assert.That(machineStatus.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            });
        }

        /// <summary>
        /// Fetches an application URL from inside the page — the real browser's cookie handling, which
        /// is exactly what D-1's loopback claim is about (Chromium applies <c>Secure</c> cookies to the
        /// trustworthy loopback origin; Playwright's out-of-browser request context does not).
        /// </summary>
        private async Task<(int Status, JsonElement Body)> FetchJsonInPageAsync(string relativeUrl)
        {
            JsonElement result = await Page.EvaluateAsync<JsonElement>(
                "async (url) => { const r = await fetch(url); return { status: r.status, body: await r.text() }; }",
                relativeUrl);
            int status = result.GetProperty("status").GetInt32();
            string body = result.GetProperty("body").GetString() ?? string.Empty;

            Assert.That(status, Is.EqualTo(200), () => $"GET {relativeUrl} answered {status}: {body}");

            using JsonDocument document = JsonDocument.Parse(body);

            return (status, document.RootElement.Clone());
        }

        /// <summary>
        /// Navigates to the opt-in login endpoint, fills the provider's login form, and waits for the
        /// <c>form_post</c> callback to land the browser back on the application.
        /// </summary>
        private Task SignInThroughBrowserAsync() => BrowserSignIn.SignInAsync(Page, BaseUrl);
    }
}
