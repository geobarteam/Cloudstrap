namespace Cloudstrap.Demo.E2E.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// The deliverable #17 demonstration (AC-PX14): the Bff forwards <c>/proxy/api/**</c> to the Api demo
    /// host through one <c>AddCloudstrapProxy()</c> call in <c>Client</c> mode — a signed-in browser
    /// reaches the Api as the Bff's own machine identity, an anonymous caller is stopped at the Bff, and
    /// the Bff's session cookie never crosses the hop.
    /// </summary>
    [TestFixture]
    public sealed class TrustedSubsystemProxyTests : PageTestBase
    {
        private const string _proxiedWhoAmI = "/proxy/api/api/v1/downstream/whoami";
        private const string _proxiedInbound = "/proxy/api/api/v1/downstream/inbound";

        /// <summary>AC-PX14 (a): the Api sees the Bff's machine token, not the signed-in user's.</summary>
        [Test]
        public async Task SignedInBrowser_ProxiedWhoAmI_ReturnsTheMachineIdentity_NotTheUser()
        {
            // Arrange
            await BrowserSignIn.SignInAsync(Page, BaseUrl);

            // Act — the browser calls the Bff with its cookie only; the Bff forwards with its own token
            JsonElement json = await FetchJsonInPageAsync(_proxiedWhoAmI);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(json.GetProperty("clientId").GetString(), Is.EqualTo("demo-bff"));
                Assert.That(json.GetProperty("host").GetString(), Is.EqualTo("demo-api"));
                Assert.That(json.GetProperty("subject").GetString(), Is.Not.EqualTo(BrowserSignIn.Username));
            });
        }

        /// <summary>AC-PX14 (b): an anonymous caller never rides on the Bff's token.</summary>
        [Test]
        public async Task AnonymousCall_ToTheProxiedRoute_IsStoppedAtTheBff()
        {
            // Arrange
            using HttpClientHandler handler = new() { AllowAutoRedirect = false };
            using HttpClient client = new(handler);

            // Act
            using HttpResponseMessage response = await client.GetAsync(new Uri(BaseUrl + _proxiedWhoAmI));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — the Bff's challenge for a non-browser caller is a bare 401; nothing reached the Api
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), body);
                Assert.That(body, Does.Not.Contain("demo-api"));
            });
        }

        /// <summary>
        /// AC-PX14 (b), "Api never hit": an anonymous probe path never shows up in the Api's console telemetry,
        /// while a signed-in probe sent after it does — the control that proves the Api's output would show a hit.
        /// </summary>
        [Test]
        public async Task AnonymousCall_ToTheProxiedRoute_NeverReachesTheApi()
        {
            // Arrange
            string anonymousProbe = "/proxy/api/anonymous-probe-" + Guid.NewGuid().ToString("N");
            string signedInProbe = "/proxy/api/signed-in-probe-" + Guid.NewGuid().ToString("N");
            using HttpClientHandler handler = new() { AllowAutoRedirect = false };
            using HttpClient client = new(handler);

            // Act — the anonymous probe first, then a signed-in probe the Api does receive (it answers 404)
            using HttpResponseMessage anonymous = await client.GetAsync(new Uri(BaseUrl + anonymousProbe));
            await BrowserSignIn.SignInAsync(Page, BaseUrl);
            int signedInStatus = await Page.EvaluateAsync<int>(
                "async (url) => (await fetch(url)).status",
                signedInProbe);
            bool controlSeen = await WaitForApiOutputAsync(ExpectedApiPath(signedInProbe));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(signedInStatus, Is.EqualTo((int)HttpStatusCode.NotFound), "the Api's own 404 for the control probe");
                Assert.That(controlSeen, Is.True, "the Api's console telemetry must show the signed-in control probe");
                Assert.That(E2eFixture.CapturedApiOutput, Does.Not.Contain(ExpectedApiPath(anonymousProbe)));
            });
        }

        /// <summary>AC-PX14 (c): the caller's cookie stays at the Bff; the Api gets a bearer token.</summary>
        [Test]
        public async Task SignedInBrowser_ProxiedInbound_ShowsNoCookieAndTheBearerScheme()
        {
            // Arrange
            await BrowserSignIn.SignInAsync(Page, BaseUrl);

            // Act
            JsonElement json = await FetchJsonInPageAsync(_proxiedInbound);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(json.GetProperty("cookiePresent").GetBoolean(), Is.False);
                Assert.That(json.GetProperty("authorizationScheme").GetString(), Is.EqualTo("Bearer"));
                Assert.That(json.GetProperty("host").GetString(), Is.EqualTo("demo-api"));
            });
        }

        /// <summary>The path the Api receives for a Bff path: the route removes the <c>/proxy/api</c> prefix.</summary>
        private static string ExpectedApiPath(string bffPath) => "url.path: " + bffPath["/proxy/api".Length..];

        /// <summary>Polls the Api's captured output for a text: its console exporter batches spans.</summary>
        private static async Task<bool> WaitForApiOutputAsync(string text)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (E2eFixture.CapturedApiOutput.Contains(text, StringComparison.Ordinal))
                {
                    return true;
                }

                await Task.Delay(250);
            }

            return false;
        }

        /// <summary>
        /// Fetches a Bff URL from inside the signed-in page, so the browser's own session cookie is what
        /// the Bff sees, and returns the JSON body after asserting a 200 JSON answer.
        /// </summary>
        private async Task<JsonElement> FetchJsonInPageAsync(string relativeUrl)
        {
            JsonElement result = await Page.EvaluateAsync<JsonElement>(
                "async (url) => { const r = await fetch(url); return { status: r.status, type: r.headers.get('content-type') ?? '', body: await r.text() }; }",
                relativeUrl);
            int status = result.GetProperty("status").GetInt32();
            string contentType = result.GetProperty("type").GetString() ?? string.Empty;
            string body = result.GetProperty("body").GetString() ?? string.Empty;

            Assert.That(status, Is.EqualTo(200), () => $"GET {relativeUrl} answered {status}: {body}");
            Assert.That(
                contentType,
                Does.Contain("application/json"),
                () => $"GET {relativeUrl} answered {contentType}, not the Api's JSON: {body}");

            using JsonDocument document = JsonDocument.Parse(body);

            return document.RootElement.Clone();
        }
    }
}
