namespace Cloudstrap.Demo.E2E.Tests
{
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using Cloudstrap.Demo.IdentityProvider;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// The BlazorServer demo app (deliverable #27, D-B): a stock Blazor Server app on shipped
    /// packages only — OIDC login at the shared demo IdP, a user-flagged typed client into the Api
    /// demo host, Otlp-mode observability. Deliverable #12 extends this app with the real
    /// BlazorServer helpers later.
    /// </summary>
    [TestFixture]
    public sealed class BlazorServerTests : PageTestBase
    {
        private const string _blazorServerBaseUrl = "http://127.0.0.1:5340";

        private const string _blazorServerProjectPath =
            "src/demo/BlazorServer/Cloudstrap.Demo.BlazorServer.csproj";

        private SutProcess? _blazorServerHost;

        [OneTimeSetUp]
        public async Task StartBlazorServerHostAsync()
        {
            // The fixture-owned IdP (5310) and Api host (5330) are already running; this fixture
            // boots only its own host, the MvcHostTests precedent.
            _blazorServerHost = SutProcess.Start(_blazorServerBaseUrl, null, _blazorServerProjectPath);
            using HttpClient client = new HttpClient { BaseAddress = new Uri(_blazorServerBaseUrl) };
            await WaitUntilReadyAsync(client, _blazorServerHost);
        }

        [OneTimeTearDown]
        public void StopBlazorServerHost()
        {
            _blazorServerHost?.Dispose();
        }

        [Test]
        public async Task BlazorServer_SignInAndWhoAmI_RendersUserAndApiEcho_NoConsoleErrors()
        {
            // Act — an anonymous navigation to the protected page auto-triggers the OIDC challenge
            // to the shared IdP; signing in lands the browser back on /whoami
            await Page.GotoAsync(_blazorServerBaseUrl + "/whoami");
            await Page.WaitForURLAsync($"http://127.0.0.1:{E2eFixture.IdentityProviderPort}/**");
            await BrowserSignIn.FillLoginFormAsync(Page);
            await Page.WaitForURLAsync(_blazorServerBaseUrl + "/**");

            // Assert — the page renders the seeded user's display name AND the Api demo host's
            // marker: the user's token crossed to the separate JWT host (spec matrix row, AC-DR5)
            await Assertions.Expect(Page.GetByTestId("user-name"))
                .ToHaveTextAsync(TestIdentityProviderSeed.DisplayName);
            await Assertions.Expect(Page.GetByTestId("api-host")).ToHaveTextAsync("demo-api");
            Assert.That(ConsoleErrors, Is.Empty, string.Join(Environment.NewLine, ConsoleErrors));
        }

        private static async Task WaitUntilReadyAsync(HttpClient client, SutProcess process)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    break;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(new Uri("/", UriKind.Relative));
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet — keep polling until the deadline.
                }

                await Task.Delay(250);
            }

            throw new InvalidOperationException(
                $"The BlazorServer host did not become ready at {client.BaseAddress}.{Environment.NewLine}{process.CapturedOutput}");
        }
    }
}
