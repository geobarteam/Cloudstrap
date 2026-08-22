namespace Cloudstrap.Mvc.Tests
{
    using System.Net;
    using Cloudstrap.Mvc.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    /// <summary>
    /// Routing and static-file behavior of the two-call composite: the conventional default route,
    /// attribute routes, the web root, and both pipeline switches (AC-MVC1, AC-MVC10 switch clause).
    /// </summary>
    [TestFixture]
    public sealed class MvcRoutingTests
    {
        [Test]
        public async Task AddAndUse_HomeControllerIndex_AnswersAtTheRoot()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Is.EqualTo(HomeController.Marker));
            });
        }

        [Test]
        public async Task AddAndUse_ConventionalRoute_BindsControllerActionAndId()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/widgets/details/5", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Is.EqualTo("widget-5"));
            });
        }

        [Test]
        public async Task AddAndUse_AttributeRoutedController_AlsoAnswers()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/api/catalog", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Is.EqualTo(CatalogController.Marker));
            });
        }

        [Test]
        public async Task Use_WithMapDefaultControllerRouteFalse_MapsNoControllerEndpoints()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(pipeline: options =>
            {
                options.MapDefaultControllerRoute = false;
                options.ConfigureEndpoints = endpoints =>
                    endpoints.MapGet("/ping", () => Results.Text("pong"));
            });
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage root = await client.GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage conventional = await client.GetAsync(
                new Uri("/widgets/details/5", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage mapped = await client.GetAsync(
                new Uri("/ping", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(root.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(conventional.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(mapped.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public async Task Use_ServesStaticFilesFromTheWebRootByDefault()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/site.css", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/css"));
            });
        }

        [Test]
        public async Task Use_WithUseStaticFilesFalse_ServesNoStaticFiles()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(
                pipeline: options => options.UseStaticFiles = false);
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/site.css", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task AddCloudstrapMvc_MvcHook_RunsAndCanAddApplicationParts()
        {
            // Arrange — the host's application name hides the test assembly from default part discovery,
            // so the fixture controllers are reachable only when the documented hook adds their part.
            await using WebApplication bare = await MvcTestHost.StartAsync(includeTestControllers: false);
            await using WebApplication hooked = await MvcTestHost.StartAsync(
                configure: configurator => configurator.Mvc = mvc =>
                    mvc.AddApplicationPart(typeof(MvcRoutingTests).Assembly),
                includeTestControllers: false);
            using HttpClient bareClient = bare.GetTestClient();
            using HttpClient hookedClient = hooked.GetTestClient();

            // Act
            using HttpResponseMessage withoutPart = await bareClient.GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage withPart = await hookedClient.GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(withoutPart.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(withPart.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public void AddCloudstrapMvc_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            WebApplicationBuilder builder = null!;

            // Act + Assert
            Assert.That(
                () => builder.AddCloudstrapMvc(),
                Throws.ArgumentNullException);
        }

        [Test]
        public void UseCloudstrapMvc_OnNullApp_ThrowsArgumentNullException()
        {
            // Arrange
            WebApplication app = null!;

            // Act + Assert
            Assert.That(
                () => app.UseCloudstrapMvc(),
                Throws.ArgumentNullException);
        }
    }
}
