namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// Drives the Scalar reference UI in a real browser (deliverable #5 demo).
    /// </summary>
    /// <remarks>
    /// Deliberately no console-error assertion, unlike <c>HomePageTests</c>: the reference UI pulls its
    /// JavaScript bundle from a CDN, and a CI agent may have no route to it. The load-bearing assertions
    /// about the page's content are the <c>HttpClient</c> ones in <c>WebApiTests</c>; this test proves the
    /// route is reachable and renders as a document in a browser.
    /// </remarks>
    [TestFixture]
    public sealed class ScalarPageTests : PageTestBase
    {
        [Test]
        public async Task ScalarPage_Loads_InTheBrowser()
        {
            // Act
            IResponse? response = await Page.GotoAsync($"{BaseUrl}/scalar");

            // Assert
            Assert.That(response, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(response!.Ok, Is.True, $"Navigation returned {response.Status}.");
                Assert.That(Page.Url, Does.Contain("/scalar"));
            });

            string title = await Page.TitleAsync();
            Assert.That(title, Is.Not.Empty);
        }
    }
}
