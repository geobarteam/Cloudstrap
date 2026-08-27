namespace Cloudstrap.Demo.E2E.Tests
{
    using System.Net;
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using Cloudstrap.Demo.IdentityProvider;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// The BlazorServer demo app on the #12 composite (`AddCloudstrapBlazorServer` +
    /// `UseCloudstrapBlazorServer&lt;App&gt;`): OIDC login at the shared demo IdP, a user-flagged
    /// typed client into the Api demo host, Otlp-mode observability with the console exporter on,
    /// anonymous probes and hardened headers from the composite, and the D-9 interaction trace
    /// around the WhoAmI API call.
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
        public async Task BlazorServer_CompositePipeline_ServesAnonymousProbesAndHardenedHeaders()
        {
            // Arrange — a plain HttpClient, no sign-in: the composite's probes are anonymous
            using HttpClient client = new HttpClient { BaseAddress = new Uri(_blazorServerBaseUrl) };

            // Act
            using HttpResponseMessage healthz = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
            using HttpResponseMessage ready = await client.GetAsync(new Uri("/ready", UriKind.Relative));
            using HttpResponseMessage home = await client.GetAsync(new Uri("/", UriKind.Relative));

            // Assert — AC-BS1 live (anonymous probes) + AC-BS2/AC-BS10 live (the hardened headers and
            // the correlation echo now come from UseCloudstrapBlazorServer, not hand-rolled code)
            Assert.Multiple(() =>
            {
                Assert.That(healthz.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    home.Headers.GetValues("X-Content-Type-Options").Single(),
                    Is.EqualTo("nosniff"));
                Assert.That(
                    home.Headers.GetValues("Referrer-Policy").Single(),
                    Is.EqualTo("no-referrer"));
                Assert.That(
                    home.Headers.GetValues("X-Frame-Options").Single(),
                    Is.EqualTo("SAMEORIGIN"));
                Assert.That(
                    home.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.Not.Empty);
            });
        }

        [Test]
        public async Task BlazorServer_WhoAmI_EmitsAnInteractionRootSpanThroughTheCompositePipeline()
        {
            // Act — the existing sign-in flow: the OIDC challenge, the seeded login, back on /whoami
            await Page.GotoAsync(_blazorServerBaseUrl + "/whoami");
            await Page.WaitForURLAsync($"http://127.0.0.1:{E2eFixture.IdentityProviderPort}/**");
            await BrowserSignIn.FillLoginFormAsync(Page);
            await Page.WaitForURLAsync(_blazorServerBaseUrl + "/**");

            // Assert — the ViewModel wiring carries the page through the composite pipeline (AC-BS4 live)
            await Assertions.Expect(Page.GetByTestId("user-name"))
                .ToHaveTextAsync(TestIdentityProviderSeed.DisplayName);
            await Assertions.Expect(Page.GetByTestId("api-host")).ToHaveTextAsync("demo-api");

            // Assert — the D-9 interaction root span reached the app's console exporter: the ViewModel
            // wrapped its API call in StartInteraction, and the composite contributed the source to the
            // host-owned pipeline (AC-BS5 live; the WorkerHostTests CapturedOutput polling precedent)
            string output = await WaitForOutputAsync(
                () => _blazorServerHost!.CapturedOutput,
                text => text.Contains("Cloudstrap.BlazorServer.Interaction", StringComparison.Ordinal));
            Assert.That(output, Does.Contain("Cloudstrap.BlazorServer.Interaction"));
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

        private static async Task<string> WaitForOutputAsync(Func<string> captured, Func<string, bool> ready)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            string output = captured();
            while (!ready(output) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                output = captured();
            }

            return output;
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
