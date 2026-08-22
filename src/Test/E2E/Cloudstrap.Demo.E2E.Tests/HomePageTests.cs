namespace Cloudstrap.Demo.E2E.Tests
{
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    [TestFixture]
    public sealed class HomePageTests : PageTestBase
    {
        [Test]
        public async Task HomePage_Loads_ShowsWelcomeHeadingAndNoConsoleErrors()
        {
            // Arrange
            ILocator heading = Page.GetByRole(
                AriaRole.Heading,
                new PageGetByRoleOptions { Name = "Welcome to the Cloudstrap WASM Test Project" });

            // Act
            await Page.GotoAsync(BaseUrl + "/");

            // Assert — generous timeout: the WASM runtime downloads on first load
            await Assertions.Expect(heading).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
            Assert.That(ConsoleErrors, Is.Empty, "The browser console reported errors while loading the home page.");
        }
    }
}
