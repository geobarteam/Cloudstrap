namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using System.Net.Http.Json;
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates the full client → API round-trip with an <c>AddDoctor</c> business span
    /// (Cloudstrap.Observability, deliverable #2) visible in the SUT's console telemetry.
    /// </summary>
    [TestFixture]
    public sealed class DoctorsTests : PageTestBase
    {
        [Test]
        public async Task DoctorsPage_Loads_ShowsSeededDoctors()
        {
            // Act
            await Page.GotoAsync(BaseUrl + "/doctors");

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
            await Page.GotoAsync(BaseUrl + "/doctors");
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

            // Arrange
            using HttpClient client = new HttpClient { BaseAddress = new Uri(E2eFixture.BaseUrl) };

            // Act — POST straight to the API; the server wraps the operation in an 'AddDoctor' span
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("api/doctor", UriKind.Relative),
                new
                {
                    name = "Dr. Telemetry Probe",
                    specialty = "Observability"
                });
            response.EnsureSuccessStatusCode();

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
