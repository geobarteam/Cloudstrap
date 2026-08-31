namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure;
    using Microsoft.AspNetCore.Antiforgery;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    /// <summary>
    /// Pins the DL-2 opt-in BFF user endpoint: the anonymous-safe wire contract with XSRF issuance,
    /// the signed-in principal round trip through the real login, the validated two-sided XSRF
    /// contract (AC-BW7's unit half), the configured path/header overrides, the not-mapped 404 and
    /// the fail-loud missing-antiforgery throw (DL-2, D-7, AC-BW6's server halves).
    /// </summary>
    [TestFixture]
    public sealed class BffUserEndpointTests
    {
        private const string _defaultHeaderName = "X-XSRF-TOKEN";

        [Test]
        public async Task BffUserEndpoint_Anonymous_Returns200AnonymousWireContractWithTheXsrfHeader()
        {
            // Arrange
            await using OidcTestHost host = await StartHostAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act — no sign-in: the AllowAnonymous probe answers, the fallback policy cannot lock it out
            HttpResponseMessage response = await agent.GetNoRedirectAsync(
                new Uri(OidcTestHost.AppBase, "bff/user"));
            string json = await response.Content.ReadAsStringAsync();

            // Assert — camelCase asserted on the raw body; the XSRF request token is already issued
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(json, Does.Contain("\"isAuthenticated\":false"));
                Assert.That(
                    response.Headers.GetValues(_defaultHeaderName).Single(),
                    Is.Not.Empty);
            });
        }

        [Test]
        public async Task BffUserEndpoint_SignedIn_ReturnsNameAndClaimsFromTheCookiePrincipal()
        {
            // Arrange — the full interactive sign-in through #10's real login endpoint
            await using OidcTestHost host = await StartHostAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();
            await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act
            HttpResponseMessage response = await agent.GetNoRedirectAsync(
                new Uri(OidcTestHost.AppBase, "bff/user"));
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            // Assert — the cookie principal crosses the wire 1:1
            JsonElement root = body.RootElement;
            List<(string Type, string Value)> claims =
                [.. root.GetProperty("claims").EnumerateArray()
                    .Select(claim => (claim.GetProperty("type").GetString()!, claim.GetProperty("value").GetString()!))];
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(root.GetProperty("isAuthenticated").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("userName").GetString(), Is.EqualTo(OidcTestHost.UserDisplayName));
                Assert.That(claims, Does.Contain(("role", OidcTestHost.UserRole)));
                Assert.That(claims.Select(claim => claim.Type), Does.Contain("sub"));
            });
        }

        [Test]
        public async Task BffUserEndpoint_MutatingCall_WithTheIssuedToken_PassesValidation_AndWithoutIt_IsRejected()
        {
            // Arrange — signed in, token issued by the user endpoint, antiforgery cookie in the jar
            await using OidcTestHost host = await StartHostAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();
            await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            HttpResponseMessage userResponse = await agent.GetNoRedirectAsync(
                new Uri(OidcTestHost.AppBase, "bff/user"));
            string token = userResponse.Headers.GetValues(_defaultHeaderName).Single();

            // Act — the same POST with and without the issued header token
            using HttpRequestMessage withToken = new(
                HttpMethod.Post,
                new Uri(OidcTestHost.AppBase, "fixture/mutate"));
            withToken.Headers.Add(_defaultHeaderName, token);
            HttpResponseMessage accepted = await agent.SendAsync(withToken, followRedirects: false);

            using HttpRequestMessage withoutToken = new(
                HttpMethod.Post,
                new Uri(OidcTestHost.AppBase, "fixture/mutate"));
            HttpResponseMessage rejected = await agent.SendAsync(withoutToken, followRedirects: false);

            // Assert — validation is real, not theater (D-7)
            Assert.Multiple(() =>
            {
                Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            });
        }

        [Test]
        public async Task BffUserEndpoint_ConfiguredPathAndHeaderName_AreHonored()
        {
            // Arrange — both server halves overridden, the fixture's AddAntiforgery matched
            await using OidcTestHost host = await StartHostAsync(
                configuration: new Dictionary<string, string?>
                {
                    ["Cloudstrap:OpenIdConnect:UserEndpointPath"] = "/session/me",
                    ["Cloudstrap:OpenIdConnect:XsrfHeaderName"] = "X-CUSTOM-XSRF",

                    // Without the fallback policy an unmapped path is an honest 404 rather than a
                    // challenge redirect — the default-path probe below proves "not mapped there".
                    ["Cloudstrap:OpenIdConnect:RequireAuthenticatedEndpoints"] = "false",
                },
                antiforgeryHeaderName: "X-CUSTOM-XSRF");
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            HttpResponseMessage configured = await agent.GetNoRedirectAsync(
                new Uri(OidcTestHost.AppBase, "session/me"));
            HttpResponseMessage defaultPath = await agent.GetNoRedirectAsync(
                new Uri(OidcTestHost.AppBase, "bff/user"));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(configured.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(configured.Headers.GetValues("X-CUSTOM-XSRF").Single(), Is.Not.Empty);
                Assert.That(defaultPath.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public async Task BffUserEndpoint_MapperNotCalled_MapsNothing()
        {
            // Arrange — the #10 posture preserved: the schemes alone map no endpoint. The fallback
            // policy is off so the unmapped path answers an honest 404 instead of a challenge.
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                configuration: new Dictionary<string, string?>
                {
                    ["Cloudstrap:OpenIdConnect:RequireAuthenticatedEndpoints"] = "false",
                });
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            HttpResponseMessage response = await agent.GetNoRedirectAsync(
                new Uri(OidcTestHost.AppBase, "bff/user"));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public void MapCloudstrapBffUserEndpoint_WithoutAntiforgeryServices_ThrowsNamingAddAntiforgery()
        {
            // Act & Assert — fail loud at map time, never security theater
            Assert.That(
                async () => await OidcTestHost.StartAsync(
                    mapEndpoints: app => app.MapCloudstrapBffUserEndpoint()),
                Throws.InvalidOperationException.With.Message.Contains("AddAntiforgery"));
        }

        /// <summary>
        /// Starts the fixture with the DL-2 consumer wiring: antiforgery registered with the matching
        /// header name, the user endpoint mapped, and a mutating fixture endpoint validating through
        /// stock <see cref="IAntiforgery.ValidateRequestAsync"/> — the demo's wiring, mirrored.
        /// </summary>
        private static Task<OidcTestHost> StartHostAsync(
            Dictionary<string, string?>? configuration = null,
            string antiforgeryHeaderName = _defaultHeaderName)
        {
            return OidcTestHost.StartAsync(
                configuration: configuration,
                afterRegistration: (builder, _) =>
                    builder.Services.AddAntiforgery(options => options.HeaderName = antiforgeryHeaderName),
                mapEndpoints: app =>
                {
                    app.MapCloudstrapBffUserEndpoint();
                    app.MapPost("/fixture/mutate", async (HttpContext context, IAntiforgery antiforgery) =>
                    {
                        try
                        {
                            await antiforgery.ValidateRequestAsync(context);
                        }
                        catch (AntiforgeryValidationException)
                        {
                            return Results.BadRequest("antiforgery validation failed");
                        }

                        return Results.Ok("mutated");
                    });
                });
        }
    }
}
