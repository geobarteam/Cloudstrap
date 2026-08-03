namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using NUnit.Framework;

    /// <summary>
    /// AC-W1's remaining half: the ported JSON and routing opinions, each with its documented override, and
    /// the guard clauses on the two entry points.
    /// </summary>
    [TestFixture]
    public sealed class WebApiDefaultsTests
    {
        [Test]
        public async Task Response_OmitsNullProperties_ByDefault()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            string body = await app.GetTestClient().GetStringAsync(
                new Uri("/api/payload", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(body, Does.Not.Contain("description"));
        }

        [Test]
        public async Task Response_WithIgnoreNullValuesFalse_KeepsNullProperties()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:WebApi:Json:IgnoreNullValues"] = "false" });

            // Act
            string body = await app.GetTestClient().GetStringAsync(
                new Uri("/api/payload", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(body, Does.Contain("\"description\":null"));
        }

        [Test]
        public async Task GeneratedLinks_AreLowercase_ByDefault()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            string body = await app.GetTestClient().GetStringAsync(
                new Uri("/api/link", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string path = JsonDocument.Parse(body).RootElement.GetProperty("path").GetString() ?? string.Empty;

            // Assert
            Assert.That(path, Is.EqualTo("/api/link/target"));
        }

        [Test]
        public async Task GeneratedLinks_WithLowercaseUrlsFalse_KeepTheirCasing()
        {
            // Arrange
            await using WebApplication app = await WebApiTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:WebApi:LowercaseUrls"] = "false" });

            // Act
            string body = await app.GetTestClient().GetStringAsync(
                new Uri("/api/Link", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string path = JsonDocument.Parse(body).RootElement.GetProperty("path").GetString() ?? string.Empty;

            // Assert
            Assert.That(path, Is.EqualTo("/api/Link/Target"));
        }

        [Test]
        public async Task AddCloudstrapWebApi_JsonHook_WinsOverCloudstrapDefaults()
        {
            // Arrange — the hook drops the camel-case web default
            await using WebApplication app = await WebApiTestHost.StartAsync(
                configure: configurator => configurator.Json = options =>
                    options.JsonSerializerOptions.PropertyNamingPolicy = null);

            // Act
            string body = await app.GetTestClient().GetStringAsync(
                new Uri("/api/payload", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(body, Does.Contain("\"Name\""));
        }

        [Test]
        public async Task AddCloudstrapWebApi_MvcHook_RunsAndCanAddApplicationParts()
        {
            // Arrange — the fixture controllers live in this assembly and reach MVC only through the hook
            await using WebApplication withPart = await WebApiTestHost.StartAsync();
            await using WebApplication withoutPart = await WebApiTestHost.StartAsync(includeTestControllers: false);

            // Act
            using HttpResponseMessage found = await withPart.GetTestClient()
                .GetAsync(new Uri("/api/legacy", UriKind.Relative), TestContext.CurrentContext.CancellationToken);
            using HttpResponseMessage missing = await withoutPart.GetTestClient()
                .GetAsync(new Uri("/api/legacy", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(found.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public void AddCloudstrapWebApi_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            WebApplicationBuilder builder = null!;

            // Act + Assert
            Assert.That(() => builder.AddCloudstrapWebApi(), Throws.ArgumentNullException);
        }

        [Test]
        public void UseCloudstrapWebApi_OnNullApp_ThrowsArgumentNullException()
        {
            // Arrange
            WebApplication app = null!;

            // Act + Assert
            Assert.That(() => app.UseCloudstrapWebApi(), Throws.ArgumentNullException);
        }

        [Test]
        public void CloudstrapWebApiConfigurator_ExposesTheDocumentedHooks()
        {
            // Arrange
            CloudstrapWebApiConfigurator configurator = new();

            // Act
            configurator.ApiVersioning = _ => { };
            configurator.Json = _ => { };
            configurator.Mvc = _ => { };

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(configurator.ApiVersioning, Is.Not.Null);
                Assert.That(configurator.Json, Is.Not.Null);
                Assert.That(configurator.Mvc, Is.Not.Null);
            });
        }

        [Test]
        public void WebApiPipelineOptions_DefaultsToMappingControllers()
        {
            // Arrange
            WebApiPipelineOptions options = new();

            // Act + Assert
            Assert.Multiple(() =>
            {
                Assert.That(options.MapControllers, Is.True);
                Assert.That(options.BeforeRouting, Is.Null);
                Assert.That(options.BeforeAuthorization, Is.Null);
                Assert.That(options.BeforeEndpoints, Is.Null);
                Assert.That(options.ConfigureEndpoints, Is.Null);
            });
        }

        [Test]
        public void WebApiOptions_Defaults_MatchTheDocumentedOpinions()
        {
            // Arrange
            WebApiOptions options = new();

            // Act + Assert
            Assert.Multiple(() =>
            {
                Assert.That(WebApiOptions.SectionName, Is.EqualTo("Cloudstrap:WebApi"));
                Assert.That(options.LowercaseUrls, Is.True);
                Assert.That(options.Json.IgnoreNullValues, Is.True);
                Assert.That(options.ApiVersioning.DefaultVersion, Is.EqualTo("1.0"));
                Assert.That(options.ApiVersioning.AssumeDefaultVersionWhenUnspecified, Is.True);
                Assert.That(options.ApiVersioning.ReportApiVersions, Is.True);
            });
        }

        [Test]
        public async Task AddAndUse_ReturnTheirOwnInstance_SoCallsChain()
        {
            // Arrange
            await using WebApplication app = WebApiTestHost.Build();

            // Act
            WebApplication returned = app.UseCloudstrapWebApi();
            await app.StartAsync(TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(returned, Is.SameAs(app));
        }
    }
}
