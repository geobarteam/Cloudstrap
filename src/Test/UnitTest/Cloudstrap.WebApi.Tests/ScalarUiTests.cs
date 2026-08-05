namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using System.Reflection;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using NUnit.Framework;

    /// <summary>
    /// AC-W4: the reference UI is served where a developer needs it, production stays dark unless a human
    /// opts in, a UI over no documents fails startup naming both keys, and no OAuth client secret can reach
    /// the browser because the options type has no such property.
    /// </summary>
    [TestFixture]
    public sealed class ScalarUiTests
    {
        [Test]
        public async Task Scalar_InDevelopmentByDefault_ServesTheReferenceUi()
        {
            // Arrange — Cloudstrap:Scalar:Enabled deliberately left unset
            await using WebApplication app = await WebApiTestHost.StartAsync(environment: "Development");

            // Act
            using HttpResponseMessage response = await ReferenceUi(app, "/scalar");
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));

                // The shell carries its document list as relative URLs in the initializer payload
                Assert.That(body, Does.Contain("\"url\":\"openapi/v1.json\""));
            });
        }

        [Test]
        public async Task Scalar_InProductionByDefault_IsNotMapped()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/scalar", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task Scalar_ExplicitlyEnabledInProduction_ServesTheUi()
        {
            // Arrange — a conscious production choice
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Scalar:Enabled"] = "true" });

            // Act
            using HttpResponseMessage response = await ReferenceUi(app, "/scalar");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task Scalar_WithEnabledFalseInDevelopment_IsNotMapped()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Scalar:Enabled"] = "false" },
                environment: "Development");

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/scalar", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task Scalar_WithConfiguredPath_ServesThere()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Scalar:Path"] = "/docs" },
                environment: "Development");

            // Act
            using HttpResponseMessage configured = await ReferenceUi(app, "/docs");
            using HttpResponseMessage defaultPath = await ReferenceUi(app, "/scalar");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(configured.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(defaultPath.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public void Scalar_EnabledWhileOpenApiDisabled_FailsStartupNamingBothKeys()
        {
            // Arrange — a UI over no documents is a misconfiguration, not a silent 404
            async Task Start()
            {
                await using WebApplication app = await WebApiTestHost.StartAsync(
                    new Dictionary<string, string?>
                    {
                        ["Cloudstrap:Scalar:Enabled"] = "true",
                        ["Cloudstrap:OpenApi:Enabled"] = "false",
                    });
            }

            // Act + Assert
            Assert.That(
                Start,
                Throws.Exception
                    .With.Message.Contains("Cloudstrap:Scalar:Enabled")
                    .And.Message.Contains("Cloudstrap:OpenApi:Enabled"));
        }

        [Test]
        public void ScalarOAuthSettings_ExposesNoClientSecretProperty()
        {
            // Arrange — the browser-secret anti-pattern is made unrepresentable, not merely validated away
            PropertyInfo[] properties = typeof(ScalarOAuthSettings).GetProperties(
                BindingFlags.Public | BindingFlags.Instance);

            // Act
            string[] offenders =
            [
                .. properties
                    .Select(property => property.Name)
                    .Where(name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert
            Assert.That(offenders, Is.Empty, $"Secret-bearing members: {string.Join(", ", offenders)}");
        }

        [Test]
        public async Task Scalar_WithConfiguredOAuthClientIdAndScopes_ReflectsThemInTheShell()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:OpenApi:OAuth:AuthorizationUrl"] = "https://idp.contoso.example/authorize",
                    ["Cloudstrap:OpenApi:OAuth:TokenUrl"] = "https://idp.contoso.example/token",
                    ["Cloudstrap:Scalar:OAuth:ClientId"] = "contoso-catalog-ui",
                    ["Cloudstrap:Scalar:OAuth:SelectedScopes:0"] = "catalog.read",
                },
                environment: "Development");

            // Act
            using HttpResponseMessage response = await ReferenceUi(app, "/scalar");
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain("contoso-catalog-ui"));
                Assert.That(body, Does.Contain("catalog.read"));
            });
        }

        [Test]
        public async Task Scalar_ConfigureHook_RunsAfterCloudstrapDefaults()
        {
            // Arrange — the hook wins over the Cloudstrap title
            await using WebApplication app = await WebApiTestHost.StartAsync(
                configure: configurator => configurator.Scalar = scalar => scalar.Title = "Contoso Reference",
                environment: "Development");

            // Act
            using HttpResponseMessage response = await ReferenceUi(app, "/scalar");
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(body, Does.Contain("Contoso Reference"));
        }

        [Test]
        public async Task Scalar_ListsEveryVersionDocument()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(environment: "Development");

            // Act
            using HttpResponseMessage response = await ReferenceUi(app, "/scalar");
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.Contain("\"url\":\"openapi/v1.json\""));
                Assert.That(body, Does.Contain("\"url\":\"openapi/v2.json\""));
            });
        }

        [Test]
        public void CloudstrapScalarOptions_Defaults_FollowTheEnvironment()
        {
            // Arrange
            CloudstrapScalarOptions options = new();

            // Act + Assert
            Assert.Multiple(() =>
            {
                Assert.That(CloudstrapScalarOptions.SectionName, Is.EqualTo("Cloudstrap:Scalar"));
                Assert.That(options.Enabled, Is.Null);
                Assert.That(options.Path, Is.EqualTo("/scalar"));
                Assert.That(options.OAuth.ClientId, Is.Null);
                Assert.That(options.OAuth.SelectedScopes, Is.Empty);
            });
        }

        /// <summary>
        /// Requests the reference UI, following the single redirect the library issues from the configured
        /// prefix to the page for a specific document — exactly what a browser does.
        /// </summary>
        /// <param name="app">The running application.</param>
        /// <param name="path">The configured reference-UI path.</param>
        /// <returns>The final response.</returns>
        private static async Task<HttpResponseMessage> ReferenceUi(WebApplication app, string path)
        {
            HttpClient client = app.GetTestClient();
            HttpResponseMessage response = await client.GetAsync(
                new Uri(path, UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.MovedPermanently)
                || response.Headers.Location is null)
            {
                return response;
            }

            Uri location = response.Headers.Location;
            response.Dispose();

            return await client.GetAsync(location, TestContext.CurrentContext.CancellationToken);
        }
    }
}
