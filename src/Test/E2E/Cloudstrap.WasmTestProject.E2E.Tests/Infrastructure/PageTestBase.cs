namespace Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure
{
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// Base class for browser-driven E2E tests: one headless Chromium per fixture,
    /// a fresh browser context and page per test, and console-error collection.
    /// </summary>
    public abstract class PageTestBase
    {
        private IPlaywright? _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;

        /// <summary>The page under test, recreated for every test.</summary>
        protected IPage Page { get; private set; } = null!;

        /// <summary>Browser console messages of type <c>error</c> collected during the current test.</summary>
        protected IList<string> ConsoleErrors { get; } = new List<string>();

        /// <summary>Base URL of the running SUT.</summary>
        protected static string BaseUrl => E2eFixture.BaseUrl;

        [OneTimeSetUp]
        public async Task LaunchBrowserAsync()
        {
            _playwright = await Playwright.CreateAsync();
            try
            {
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            }
            catch (PlaywrightException exception)
            {
                throw new InvalidOperationException(
                    "Chromium is not installed for Playwright. Run once: " +
                    "pwsh src/Test/E2E/Cloudstrap.WasmTestProject.E2E.Tests/bin/<Configuration>/net10.0/playwright.ps1 install chromium",
                    exception);
            }
        }

        [SetUp]
        public async Task OpenPageAsync()
        {
            ConsoleErrors.Clear();
            _context = await _browser!.NewContextAsync();
            Page = await _context.NewPageAsync();
            Page.Console += OnConsoleMessage;
        }

        [TearDown]
        public async Task ClosePageAsync()
        {
            if (_context is not null)
            {
                await _context.DisposeAsync();
                _context = null;
            }
        }

        [OneTimeTearDown]
        public async Task CloseBrowserAsync()
        {
            if (_browser is not null)
            {
                await _browser.DisposeAsync();
                _browser = null;
            }

            _playwright?.Dispose();
            _playwright = null;
        }

        private void OnConsoleMessage(object? sender, IConsoleMessage message)
        {
            if (message.Type == "error")
            {
                ConsoleErrors.Add(message.Text);
            }
        }
    }
}
