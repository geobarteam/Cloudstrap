namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates Cloudstrap.Core (deliverable #1) through the running app: configuration
    /// binding on the server and in the WASM client, and fail-fast validation at startup.
    /// </summary>
    [TestFixture]
    public sealed class DiagnosticsTests : PageTestBase
    {
        [Test]
        public async Task DiagnosticsPage_Loads_ShowsServerBoundCloudstrapOptions()
        {
            // Act
            await Page.GotoAsync(BaseUrl + "/diagnostics");

            // Assert — values must match the Bff host's appsettings.json 'Cloudstrap' section
            await Assertions.Expect(Page.GetByTestId("server-workload"))
                .ToContainTextAsync("wasmtestproject-application-bff", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
            await Assertions.Expect(Page.GetByTestId("server-otel-mode")).ToContainTextAsync("Console");
            await Assertions.Expect(Page.GetByTestId("server-correlation-header")).ToContainTextAsync("X-Correlation-ID");
            Assert.That(ConsoleErrors, Is.Empty, "The browser console reported errors while loading the diagnostics page.");
        }

        [Test]
        public async Task Header_ShowsClientSideBoundApplicationOptions()
        {
            // Act
            await Page.GotoAsync(BaseUrl + "/");

            // Assert — the badge renders from options bound INSIDE the WASM client
            // (wwwroot/appsettings.json), proving Cloudstrap.Core is WASM-loadable.
            await Assertions.Expect(Page.GetByTestId("client-workload"))
                .ToContainTextAsync("wasmtestproject-application-wasm", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
        }

        [Test]
        public void Startup_MissingSystemName_FailsFastWithValidationError()
        {
            // Arrange & Act — separate port; the process must abort before binding it anyway
            using SutProcess process = SutProcess.Start(
                "http://127.0.0.1:5301",
                ["--Cloudstrap:Application:SystemName="]);
            bool exited = process.WaitForExit(TimeSpan.FromSeconds(30));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(exited, Is.True, "The SUT kept running despite an invalid 'Cloudstrap' section.");
                Assert.That(process.CapturedOutput, Does.Contain("SystemName"));
            });
            Assert.That(process.ExitCode, Is.Not.Zero);
        }
    }
}
