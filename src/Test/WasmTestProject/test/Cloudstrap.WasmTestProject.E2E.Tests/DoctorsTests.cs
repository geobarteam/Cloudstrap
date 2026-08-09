namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using System.Net;
    using System.Net.Http.Json;
    using System.Text.Json;
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates the full client → API round-trip with an <c>AddDoctor</c> business span
    /// (Cloudstrap.Observability, deliverable #2) visible in the SUT's console telemetry — behind
    /// sign-in: the whole doctors feature requires an authenticated cookie session, and anonymous
    /// API callers get a bare 401, never a login redirect.
    /// </summary>
    [TestFixture]
    public sealed class DoctorsTests : PageTestBase
    {
        [Test]
        public async Task GetDoctors_AnonymousApiGet_Returns401()
        {
            // Arrange — a plain API caller: no cookie, no bearer, no text/html Accept
            using HttpClient client = new HttpClient { BaseAddress = new Uri(E2eFixture.BaseUrl) };

            // Act
            using HttpResponseMessage response = await client.GetAsync(new Uri("api/doctor", UriKind.Relative));

            // Assert — a bare 401, not a redirect-followed 200 login page
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task AddDoctor_AnonymousApiPost_Returns401()
        {
            // Arrange
            using HttpClient client = new HttpClient { BaseAddress = new Uri(E2eFixture.BaseUrl) };

            // Act
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("api/doctor", UriKind.Relative),
                new
                {
                    name = "Dr. Anonymous Probe",
                    specialty = "Should Not Exist"
                });

            // Assert — the write path is closed to anonymous callers too
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task UserState_AnonymousApiGet_Returns200SignedOut()
        {
            // Arrange — a plain anonymous API caller
            using HttpClient client = new HttpClient { BaseAddress = new Uri(E2eFixture.BaseUrl) };

            // Act
            using HttpResponseMessage response = await client.GetAsync(new Uri("api/v1/user/state", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — always 200 JSON reporting signed-out: the page can probe it without any
            // console-visible 401 noise
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            using JsonDocument json = JsonDocument.Parse(body);
            Assert.That(json.RootElement.GetProperty("signedIn").GetBoolean(), Is.False);
        }

        [Test]
        public async Task DoctorsPage_AnonymousNavigation_AutoTriggersLoginAndShowsDoctors()
        {
            // Act — an anonymous navigation must land on the provider's login form with no click
            await Page.GotoAsync(BaseUrl + "/doctors");
            await Page.WaitForURLAsync(
                $"http://127.0.0.1:{E2eFixture.IdentityProviderPort}/**",
                new PageWaitForURLOptions { Timeout = 60_000 });
            await Assertions.Expect(Page.Locator("[data-testid='username']")).ToBeVisibleAsync();

            await BrowserSignIn.FillLoginFormAsync(Page);
            await Page.WaitForURLAsync(BaseUrl + "/doctors**");

            // Assert — the signed-in page: identity line, seeded grid, add form, no console noise
            // (the state probe ran before any [Authorize]'d fetch)
            await Assertions.Expect(Page.GetByTestId("doctors-user"))
                .ToContainTextAsync("Wasm Test User", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
            await Assertions.Expect(Page.GetByTestId("doctors-grid")).ToContainTextAsync("Dr. Alice Carter");
            await Assertions.Expect(Page.GetByTestId("add-doctor-submit")).ToBeVisibleAsync();
            Assert.That(
                ConsoleErrors,
                Is.Empty,
                "The browser console reported errors during the auto-triggered login flow.");
        }

        [Test]
        public async Task DoctorsPage_Loads_ShowsSeededDoctors()
        {
            // Arrange — the doctors feature requires sign-in; land straight on the page
            await BrowserSignIn.SignInAsync(Page, BaseUrl, "/doctors");

            // Assert — all three seeded doctors are listed (other tests may have added more,
            // the store lives for the whole run, so no exact row count)
            await Assertions.Expect(Page.GetByTestId("doctors-grid"))
                .ToContainTextAsync("Dr. Alice Carter", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
            await Assertions.Expect(Page.GetByTestId("doctors-grid")).ToContainTextAsync("Dr. Ben Okafor");
            await Assertions.Expect(Page.GetByTestId("doctors-grid")).ToContainTextAsync("Dr. Chloe Martin");
            Assert.That(ConsoleErrors, Is.Empty, "The browser console reported errors while loading the doctors page.");
        }

        [Test]
        public async Task DoctorsPage_AddDoctor_NewDoctorAppearsInGrid()
        {
            // Arrange
            await BrowserSignIn.SignInAsync(Page, BaseUrl, "/doctors");
            await Assertions.Expect(Page.GetByTestId("doctors-grid"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

            // Act
            await Page.GetByTestId("doctor-name-input").FillAsync("Dr. Test Doctor");
            await Page.GetByTestId("doctor-specialty-input").FillAsync("Testing");
            await Page.GetByTestId("add-doctor-submit").ClickAsync();

            // Assert
            await Assertions.Expect(Page.GetByTestId("doctors-grid"))
                .ToContainTextAsync("Dr. Test Doctor", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        }

        [Test]
        public async Task AddDoctor_EmitsBusinessTraceInConsoleTelemetry()
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLOUDSTRAP_E2E_BASEURL")))
            {
                Assert.Inconclusive(
                    "Console-telemetry assertions need the fixture-launched SUT; they cannot run in attach mode.");
            }

            // Arrange — the API requires the cookie session, so the browser is the honest vehicle:
            // sign in and drive the add through the real form
            await BrowserSignIn.SignInAsync(Page, BaseUrl, "/doctors");
            await Assertions.Expect(Page.GetByTestId("doctors-grid"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

            // Act — the server wraps the operation in an 'AddDoctor' span
            await Page.GetByTestId("doctor-name-input").FillAsync("Dr. Telemetry Probe");
            await Page.GetByTestId("doctor-specialty-input").FillAsync("Observability");
            await Page.GetByTestId("add-doctor-submit").ClickAsync();
            await Assertions.Expect(Page.GetByTestId("doctors-grid"))
                .ToContainTextAsync("Dr. Telemetry Probe", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

            // Assert — the Console exporter prints the span; poll the captured stdout until it shows up
            bool found = false;
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (E2eFixture.CapturedSutOutput.Contains("AddDoctor", StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }

                await Task.Delay(250);
            }

            Assert.That(found, Is.True, "The 'AddDoctor' business span never appeared in the SUT's console telemetry.");
        }
    }
}
