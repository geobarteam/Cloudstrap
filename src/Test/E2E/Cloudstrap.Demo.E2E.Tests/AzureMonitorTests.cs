namespace Cloudstrap.Demo.E2E.Tests
{
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates Cloudstrap.Observability.AzureMonitor (deliverable #3) through the running app: the
    /// exporter-contribution guard is lifted, a missing connection string aborts startup naming both
    /// sources, and the same binary still runs in another mode on configuration alone.
    /// </summary>
    /// <remarks>
    /// The Bff runs against a syntactically valid but unreachable connection string, so nothing is ever
    /// transmitted to Azure — the exporter retries in the background without disturbing the app, which is
    /// the failure-isolation behavior demonstrated live.
    /// </remarks>
    [TestFixture]
    public sealed class AzureMonitorTests : PageTestBase
    {
        [Test]
        public async Task AzureMonitorMode_SutBoots_AndDiagnosticsShowsAzureMonitorMode()
        {
            // Act — reaching this line already proves the guard was lifted: without the exporter package
            // the shipped AzureMonitorContributionGuard aborts startup and the fixture never comes up
            await Page.GotoAsync(BaseUrl + "/diagnostics");

            // Assert
            await Assertions.Expect(Page.GetByTestId("server-otel-mode"))
                .ToContainTextAsync("AzureMonitor", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
            Assert.That(ConsoleErrors, Is.Empty, "The browser console reported errors while loading the diagnostics page.");
        }

        [Test]
        public void Startup_AzureMonitorWithoutConnectionString_FailsFastNamingBothSources()
        {
            // Arrange & Act — blanking the setting leaves no connection string anywhere, because the
            // standard variable is not set in the child environment either
            using SutProcess process = SutProcess.Start(
                "http://127.0.0.1:5301",
                ["--Cloudstrap:AzureMonitor:ConnectionString="]);
            bool exited = process.WaitForExit(TimeSpan.FromSeconds(60));

            // Assert — the message names both places a connection string can come from
            Assert.Multiple(() =>
            {
                Assert.That(exited, Is.True, "The SUT kept running without an Application Insights connection string.");
                Assert.That(process.CapturedOutput, Does.Contain("Cloudstrap:AzureMonitor:ConnectionString"));
                Assert.That(process.CapturedOutput, Does.Contain("APPLICATIONINSIGHTS_CONNECTION_STRING"));
            });
            Assert.That(process.ExitCode, Is.Not.Zero);
        }

        [Test]
        public async Task Startup_ModeFlippedToConsole_UnchangedCodeBootsAndServes()
        {
            // Arrange — the same binary, with the same unconditional AddAzureMonitor() call in Program.cs
            const string baseUrl = "http://127.0.0.1:5302";
            using SutProcess process = SutProcess.Start(
                baseUrl,
                ["--Cloudstrap:OpenTelemetry:Mode=Console"]);

            // Act
            bool healthy = await WaitForHealthyAsync(process, baseUrl);

            // Assert — per-environment mode flipping on configuration alone
            Assert.That(
                healthy,
                Is.True,
                $"The SUT did not serve /healthz in Console mode. Captured output:{Environment.NewLine}{process.CapturedOutput}");
        }

        private static async Task<bool> WaitForHealthyAsync(SutProcess process, string baseUrl)
        {
            using HttpClient client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    return false;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet — keep polling until the deadline.
                }

                await Task.Delay(250);
            }

            return false;
        }
    }
}
