namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using NUnit.Framework;

    /// <summary>
    /// AC-W5: the published documents describe exactly the authentication the middleware enforces — the
    /// scheme with explicitly configured URLs, requirements on secured operations, none on anonymous ones,
    /// and no security metadata at all when the API takes no tokens.
    /// </summary>
    [TestFixture]
    public sealed class OpenApiSecurityTests
    {
        [Test]
        public async Task Document_WithBearerRegistered_ContainsTheSecurityScheme()
        {
            // Arrange
            await using WebApplication app = await StartWithBearer(WithOAuthUrls());

            // Act
            JsonElement scheme = (await Document(app)).GetProperty("components")
                .GetProperty("securitySchemes")
                .GetProperty("Bearer");

            // Assert — the token URL is exactly what was configured; nothing is composed from the authority
            Assert.Multiple(() =>
            {
                Assert.That(scheme.GetProperty("type").GetString(), Is.EqualTo("oauth2"));
                Assert.That(
                    scheme.GetProperty("flows")
                        .GetProperty("authorizationCode")
                        .GetProperty("tokenUrl")
                        .GetString(),
                    Is.EqualTo("https://idp.contoso.example/token"));
            });
        }

        [Test]
        public async Task Document_WithConfiguredAuthorizationUrlAndScopes_ReflectsThem()
        {
            // Arrange
            Dictionary<string, string?> configuration = WithOAuthUrls();
            configuration["Cloudstrap:OpenApi:OAuth:Scopes:catalog.read"] = "Read the catalog.";

            await using WebApplication app = await StartWithBearer(configuration);

            // Act
            JsonElement flow = (await Document(app)).GetProperty("components")
                .GetProperty("securitySchemes")
                .GetProperty("Bearer")
                .GetProperty("flows")
                .GetProperty("authorizationCode");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    flow.GetProperty("authorizationUrl").GetString(),
                    Is.EqualTo("https://idp.contoso.example/authorize"));
                Assert.That(
                    flow.GetProperty("scopes").GetProperty("catalog.read").GetString(),
                    Is.EqualTo("Read the catalog."));
            });
        }

        [Test]
        public async Task Document_SecuredOperation_CarriesASecurityRequirement()
        {
            // Arrange
            await using WebApplication app = await StartWithBearer(WithOAuthUrls());

            // Act
            JsonElement operation = Operation(await Document(app), "/api/guarded");

            // Assert
            Assert.That(operation.TryGetProperty("security", out JsonElement security), Is.True);
            Assert.That(security.GetArrayLength(), Is.GreaterThan(0));
        }

        [Test]
        public async Task Document_AllowAnonymousOperation_CarriesNoSecurityRequirement()
        {
            // Arrange — the capability the dropped NSwag operation processor existed for
            await using WebApplication app = await StartWithBearer(WithOAuthUrls());

            // Act
            JsonElement operation = Operation(await Document(app), "/api/guarded/open");

            // Assert
            Assert.That(operation.TryGetProperty("security", out JsonElement _), Is.False);
        }

        [Test]
        public async Task Document_WithoutBearerRegistered_ContainsNoSecuritySchemeAndNoRequirements()
        {
            // Arrange — an anonymous API publishes no security metadata at all
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            JsonElement document = await Document(app);
            JsonElement operation = Operation(document, "/api/guarded");

            // Assert
            Assert.Multiple(() =>
            {
                bool hasSchemes = document.TryGetProperty("components", out JsonElement components)
                    && components.TryGetProperty("securitySchemes", out JsonElement _);

                Assert.That(hasSchemes, Is.False);
                Assert.That(operation.TryGetProperty("security", out JsonElement _), Is.False);
            });
        }

        [Test]
        public async Task Document_WithBearerRegisteredButNoTokenUrlConfigured_StillDocumentsTheBearerScheme()
        {
            // Arrange — no URL is ever invented; the scheme is simply described without a flow
            await using WebApplication app = await StartWithBearer();

            // Act
            JsonElement scheme = (await Document(app)).GetProperty("components")
                .GetProperty("securitySchemes")
                .GetProperty("Bearer");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(scheme.GetProperty("type").GetString(), Is.EqualTo("http"));
                Assert.That(scheme.GetProperty("scheme").GetString(), Is.EqualTo("bearer"));
                Assert.That(scheme.GetProperty("bearerFormat").GetString(), Is.EqualTo("JWT"));
            });
        }

        private static Dictionary<string, string?> WithOAuthUrls()
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cloudstrap:OpenApi:OAuth:AuthorizationUrl"] = "https://idp.contoso.example/authorize",
                ["Cloudstrap:OpenApi:OAuth:TokenUrl"] = "https://idp.contoso.example/token",
            };
        }

        private static async Task<WebApplication> StartWithBearer(
            IDictionary<string, string?>? configuration = null)
        {
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Cloudstrap:JwtBearer:Authority"] = TestTokens.Issuer,
                ["Cloudstrap:JwtBearer:Audience"] = TestTokens.Audience,
            };

            if (configuration is not null)
            {
                foreach (KeyValuePair<string, string?> entry in configuration)
                {
                    values[entry.Key] = entry.Value;
                }
            }

            return await WebApiTestHost.StartAsync(
                values,
                beforeBuild: builder => builder.AddCloudstrapJwtBearer(TestTokens.Validation()));
        }

        private static async Task<JsonElement> Document(WebApplication app)
        {
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/openapi/v1.json", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);

            return JsonDocument.Parse(body).RootElement.Clone();
        }

        private static JsonElement Operation(JsonElement document, string path)
        {
            return document.GetProperty("paths").GetProperty(path).GetProperty("get");
        }
    }
}
