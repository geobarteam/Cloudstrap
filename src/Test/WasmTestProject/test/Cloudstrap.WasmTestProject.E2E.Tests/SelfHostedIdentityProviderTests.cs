namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// The demonstration slice of SecureDoctorsAndDemoIdp: the seeded test identity provider runs as
    /// its own host process (the demo launch story), a second Bff instance is pointed at it through
    /// configuration alone, and the whole doctors flow — auto-triggered login included — works end to
    /// end in a real browser. The fixture-owned 5310 provider keeps serving the main instance
    /// undisturbed.
    /// </summary>
    [TestFixture]
    public sealed class SelfHostedIdentityProviderTests : PageTestBase
    {
        private const string _idpBaseUrl = "http://127.0.0.1:5311";
        private const string _bffBaseUrl = "http://127.0.0.1:5304";

        private const string _idpHostProjectPath =
            "src/Test/WasmTestProject/src/Host/IdentityProvider/Cloudstrap.WasmTestProject.Host.IdentityProvider.csproj";

        [Test]
        public async Task SeparateIdpHost_FullBrowserLogin_AddsDoctorThroughTheUi()
        {
            // Arrange — the IdP host as its own process on 5311, its redirect URIs re-seeded for the
            // 5304 Bff through configuration alone
            using SutProcess idpHost = SutProcess.Start(
                _idpBaseUrl,
                ["--WasmTestProject:ApplicationBaseAddresses:0=" + _bffBaseUrl + "/"],
                _idpHostProjectPath);
            using HttpClient idpClient = new HttpClient { BaseAddress = new Uri(_idpBaseUrl) };
            await WaitUntilReadyAsync(idpClient, idpHost, ".well-known/openid-configuration");

            // …and a second Bff instance validating against — and signing in at — that host
            using SutProcess bff = SutProcess.Start(
                _bffBaseUrl,
                [
                    "--Cloudstrap:OpenIdConnect:Authority=" + _idpBaseUrl,
                    "--Cloudstrap:JwtBearer:Authority=" + _idpBaseUrl,
                    "--Cloudstrap:ClientCredentials:TokenEndpoint=" + _idpBaseUrl + "/connect/token",
                    "--Cloudstrap:HttpClients:SelfApi:BaseAddress=" + _bffBaseUrl + "/",
                    "--Cloudstrap:HttpClients:UserApi:BaseAddress=" + _bffBaseUrl + "/",
                ]);
            using HttpClient bffClient = new HttpClient { BaseAddress = new Uri(_bffBaseUrl) };
            await WaitUntilReadyAsync(bffClient, bff, "/");

            // Act — anonymous /doctors auto-triggers login at the separately hosted provider, with
            // no click; the form is the provider's seeded login page
            await Page.GotoAsync(_bffBaseUrl + "/doctors");
            await Page.WaitForURLAsync(_idpBaseUrl + "/**", new PageWaitForURLOptions { Timeout = 60_000 });
            await Assertions.Expect(Page.Locator("[data-testid='username']")).ToBeVisibleAsync();
            await BrowserSignIn.FillLoginFormAsync(Page);
            await Page.WaitForURLAsync(_bffBaseUrl + "/doctors**");

            await Assertions.Expect(Page.GetByTestId("doctors-grid"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            await Page.GetByTestId("doctor-name-input").FillAsync("Dr. Demo Host");
            await Page.GetByTestId("doctor-specialty-input").FillAsync("Separate IdP");
            await Page.GetByTestId("add-doctor-submit").ClickAsync();

            // Assert — the full round-trip worked against the separately hosted provider
            await Assertions.Expect(Page.GetByTestId("doctors-grid"))
                .ToContainTextAsync("Dr. Demo Host", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        }

        private static async Task WaitUntilReadyAsync(HttpClient client, SutProcess process, string probePath)
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
                    using HttpResponseMessage response = await client.GetAsync(
                        new Uri(probePath, UriKind.Relative));
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
                $"The process did not become ready at {client.BaseAddress}.{Environment.NewLine}{process.CapturedOutput}");
        }
    }
}
