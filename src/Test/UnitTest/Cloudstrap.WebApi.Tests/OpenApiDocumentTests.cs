namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using NUnit.Framework;

    /// <summary>
    /// AC-W3: an API spanning two versions publishes one document per discovered version, with neutral
    /// metadata derived from <c>Cloudstrap:Application</c> and overridable through configuration and the
    /// per-document hook. No hand-written version filter exists in this package.
    /// </summary>
    [TestFixture]
    public sealed class OpenApiDocumentTests
    {
        [Test]
        public async Task OpenApi_ServesOneDocumentPerDiscoveredVersion()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            JsonElement v1 = await Document(app, "v1");
            JsonElement v2 = await Document(app, "v2");

            // Assert — each document carries only its own version's operations
            Assert.Multiple(() =>
            {
                Assert.That(Paths(v1), Does.Contain("/api/v1/ledger"));
                Assert.That(Paths(v1), Does.Not.Contain("/api/v2/gadgets"));
                Assert.That(Paths(v2), Does.Contain("/api/v2/gadgets"));
                Assert.That(Paths(v2), Does.Not.Contain("/api/v1/ledger"));
            });
        }

        [Test]
        public async Task OpenApi_DocumentTitleAndDescription_DefaultToApplicationOptions()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            JsonElement info = (await Document(app, "v1")).GetProperty("info");
            string title = info.GetProperty("title").GetString() ?? string.Empty;
            string description = info.GetProperty("description").GetString() ?? string.Empty;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(title, Does.Contain("contoso-catalog-api"));
                Assert.That(description, Does.Contain("Contoso"));
                Assert.That(description, Does.Contain("Catalog"));
            });
        }

        [Test]
        public async Task OpenApi_ConfiguredTitleAndDescription_Win()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:OpenApi:Title"] = "Contoso Catalog",
                    ["Cloudstrap:OpenApi:Description"] = "Everything the catalog serves.",
                });

            // Act
            JsonElement info = (await Document(app, "v1")).GetProperty("info");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(info.GetProperty("title").GetString(), Is.EqualTo("Contoso Catalog"));
                Assert.That(
                    info.GetProperty("description").GetString(),
                    Is.EqualTo("Everything the catalog serves."));
            });
        }

        [Test]
        public async Task OpenApi_Disabled_ServesNoDocument()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:OpenApi:Enabled"] = "false" });

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/openapi/v1.json", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task OpenApi_ConfigureHook_AppliesAConsumerTransformer()
        {
            // Arrange — the hook runs after the Cloudstrap defaults, so it has the final say
            await using WebApplication app = await WebApiTestHost.StartAsync(
                configure: configurator => configurator.OpenApi = document =>
                    document.AddDocumentTransformer((generated, context, cancellationToken) =>
                    {
                        generated.Info.Title = "hook-applied";

                        return Task.CompletedTask;
                    }));

            // Act
            JsonElement v1 = await Document(app, "v1");
            JsonElement v2 = await Document(app, "v2");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(v1.GetProperty("info").GetProperty("title").GetString(), Is.EqualTo("hook-applied"));
                Assert.That(v2.GetProperty("info").GetProperty("title").GetString(), Is.EqualTo("hook-applied"));
            });
        }

        [Test]
        public async Task OpenApi_DeprecatedVersion_IsMarkedInTheDocument()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            JsonElement v1 = await Document(app, "v1");
            JsonElement retired = v1.GetProperty("paths").GetProperty("/api/v1/retired").GetProperty("get");

            // Assert
            Assert.That(retired.GetProperty("deprecated").GetBoolean(), Is.True);
        }

        [Test]
        public async Task OpenApi_UnattributedController_AppearsInTheDefaultVersionDocument()
        {
            // Arrange — the ported convention assigned it 1.0, so that is where it must be documented
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            JsonElement v1 = await Document(app, "v1");

            // Assert
            Assert.That(Paths(v1), Does.Contain("/api/legacy"));
        }

        [Test]
        public void CloudstrapOpenApiOptions_Defaults_AreEnabledAndDerived()
        {
            // Arrange
            CloudstrapOpenApiOptions options = new();

            // Act + Assert
            Assert.Multiple(() =>
            {
                Assert.That(CloudstrapOpenApiOptions.SectionName, Is.EqualTo("Cloudstrap:OpenApi"));
                Assert.That(options.Enabled, Is.True);
                Assert.That(options.Title, Is.Null);
                Assert.That(options.Description, Is.Null);
                Assert.That(options.OAuth.TokenUrl, Is.Null);
                Assert.That(options.OAuth.AuthorizationUrl, Is.Null);
                Assert.That(options.OAuth.Scopes, Is.Empty);
            });
        }

        private static async Task<JsonElement> Document(WebApplication app, string version)
        {
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri($"/openapi/{version}.json", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            Assert.That(
                response.StatusCode,
                Is.EqualTo(HttpStatusCode.OK),
                $"The {version} document was not served.");

            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private static string[] Paths(JsonElement document)
        {
            return [.. document.GetProperty("paths").EnumerateObject().Select(path => path.Name)];
        }
    }
}
