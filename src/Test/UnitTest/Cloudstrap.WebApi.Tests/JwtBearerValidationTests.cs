namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-W8 and AC-A3: inbound tokens are validated on stock ASP.NET Core with the four hardened defaults of
    /// D-2, each of them overridable, and misconfiguration fails startup naming the key. Every token is
    /// issued locally — no identity provider is contacted.
    /// </summary>
    [TestFixture]
    public sealed class JwtBearerValidationTests
    {
        [Test]
        public async Task ValidToken_OnSecuredEndpoint_Returns200()
        {
            // Arrange
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Authenticate(app, TestTokens.Issue());

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task WrongAudienceToken_Returns401()
        {
            // Arrange — audience validation stays on
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Authenticate(
                app,
                TestTokens.Issue(audience: "fabrikam-other-api"));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task TokenExpired90SecondsAgo_Returns401()
        {
            // Arrange — beyond the reduced 60-second skew
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Authenticate(
                app,
                TestTokens.Issue(expiresIn: TimeSpan.FromSeconds(-90)));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task TokenExpired30SecondsAgo_Returns200()
        {
            // Arrange — inside the skew; paired with the test above this pins the value 60, not "some skew"
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Authenticate(
                app,
                TestTokens.Issue(expiresIn: TimeSpan.FromSeconds(-30)));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task WithConfiguredClockSkew_TheOverrideWins()
        {
            // Arrange
            await using WebApplication app = await StartWithBearer(
                new Dictionary<string, string?> { ["Cloudstrap:JwtBearer:ClockSkewSeconds"] = "300" });

            // Act
            using HttpResponseMessage response = await Authenticate(
                app,
                TestTokens.Issue(expiresIn: TimeSpan.FromSeconds(-90)));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task InboundClaims_AreNotRemapped()
        {
            // Arrange
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Authenticate(app, TestTokens.Issue());
            string body = await response.Content.ReadAsStringAsync(TestContext.CurrentContext.CancellationToken);
            string[] claimTypes =
            [
                .. JsonDocument.Parse(body).RootElement
                    .GetProperty("claimTypes")
                    .EnumerateArray()
                    .Select(claim => claim.GetString() ?? string.Empty),
            ];

            // Assert — raw JWT names, not the SOAP-era URIs the framework maps to by default
            Assert.Multiple(() =>
            {
                Assert.That(claimTypes, Does.Contain("sub"));
                Assert.That(
                    claimTypes,
                    Does.Not.Contain("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"));
            });
        }

        [Test]
        public async Task RequireHttpsMetadata_DefaultsToTrueOutsideDevelopmentAndFalseInDevelopment()
        {
            // Arrange
            await using WebApplication production = await StartWithBearer();
            await using WebApplication development = await StartWithBearer(environment: "Development");

            // Act
            bool inProduction = ResolvedBearerOptions(production).RequireHttpsMetadata;
            bool inDevelopment = ResolvedBearerOptions(development).RequireHttpsMetadata;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(inProduction, Is.True);
                Assert.That(inDevelopment, Is.False);
            });
        }

        [Test]
        public async Task RequireHttpsMetadata_ExplicitValueWins_InBothDirections()
        {
            // Arrange
            await using WebApplication relaxed = await StartWithBearer(
                new Dictionary<string, string?> { ["Cloudstrap:JwtBearer:RequireHttpsMetadata"] = "false" });
            await using WebApplication strict = await StartWithBearer(
                new Dictionary<string, string?> { ["Cloudstrap:JwtBearer:RequireHttpsMetadata"] = "true" },
                environment: "Development");

            // Act + Assert
            Assert.Multiple(() =>
            {
                Assert.That(ResolvedBearerOptions(relaxed).RequireHttpsMetadata, Is.False);
                Assert.That(ResolvedBearerOptions(strict).RequireHttpsMetadata, Is.True);
            });
        }

        [Test]
        public void MissingAuthority_FailsStartupNamingTheKey()
        {
            // Arrange
            async Task Start()
            {
                await using WebApplication app = await StartWithBearer(
                    new Dictionary<string, string?> { ["Cloudstrap:JwtBearer:Authority"] = string.Empty });
            }

            // Act + Assert
            Assert.That(Start, Throws.Exception.With.Message.Contains("Cloudstrap:JwtBearer:Authority"));
        }

        [Test]
        public void MissingAudience_FailsStartupNamingTheKey()
        {
            // Arrange
            async Task Start()
            {
                await using WebApplication app = await StartWithBearer(
                    new Dictionary<string, string?> { ["Cloudstrap:JwtBearer:Audience"] = string.Empty });
            }

            // Act + Assert
            Assert.That(Start, Throws.Exception.With.Message.Contains("Cloudstrap:JwtBearer:Audience"));
        }

        [Test]
        public async Task ConfigureHook_RunsLastAndCanOverrideValidationParameters()
        {
            // Arrange — the documented replacement for the dropped legacy-issuer flag
            await using WebApplication app = await StartWithBearer(
                configureBearer: TestTokens.Validation(TestTokens.LegacyIssuer));

            // Act
            using HttpResponseMessage response = await Authenticate(
                app,
                TestTokens.Issue(issuer: TestTokens.LegacyIssuer));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task TokenFromAnUntrustedIssuer_Returns401()
        {
            // Arrange — the counterpart proving the hook above widened something real
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Authenticate(
                app,
                TestTokens.Issue(issuer: TestTokens.LegacyIssuer));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public void AddCloudstrapJwtBearer_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            WebApplicationBuilder builder = null!;

            // Act + Assert
            Assert.That(() => builder.AddCloudstrapJwtBearer(), Throws.ArgumentNullException);
        }

        [Test]
        public void CloudstrapJwtBearerOptions_Defaults_AreTheFourHardenedValues()
        {
            // Arrange
            CloudstrapJwtBearerOptions options = new();

            // Act + Assert
            Assert.Multiple(() =>
            {
                Assert.That(CloudstrapJwtBearerOptions.SectionName, Is.EqualTo("Cloudstrap:JwtBearer"));
                Assert.That(options.ClockSkewSeconds, Is.EqualTo(60));
                Assert.That(options.MapInboundClaims, Is.False);
                Assert.That(options.RequireHttpsMetadata, Is.Null);
                Assert.That(options.RequireAuthenticatedEndpoints, Is.True);
            });
        }

        private static async Task<WebApplication> StartWithBearer(
            IDictionary<string, string?>? configuration = null,
            string environment = "Production",
            Action<JwtBearerOptions>? configureBearer = null)
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
                environment: environment,
                beforeBuild: builder => builder.AddCloudstrapJwtBearer(
                    configureBearer ?? TestTokens.Validation()));
        }

        private static async Task<HttpResponseMessage> Authenticate(WebApplication app, string token)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/api/token", UriKind.Relative));
            request.Headers.Add("Authorization", TestTokens.BearerHeader(token));

            return await app.GetTestClient().SendAsync(request, TestContext.CurrentContext.CancellationToken);
        }

        private static JwtBearerOptions ResolvedBearerOptions(WebApplication app)
        {
            return app.Services
                .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
                .Get(JwtBearerDefaults.AuthenticationScheme);
        }
    }
}
