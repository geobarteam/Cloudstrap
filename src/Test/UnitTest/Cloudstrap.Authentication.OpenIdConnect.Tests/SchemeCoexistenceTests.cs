namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Net;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// AC-OIDC9: bearer and browser callers coexist in one host — a bearer request is validated by the
    /// JWT scheme and fails 401 without ever seeing a login page, a browser request is challenged to
    /// the identity provider — whichever order the two packages were registered in.
    /// </summary>
    [TestFixture]
    public sealed class SchemeCoexistenceTests
    {
        [Test]
        public async Task BearerRequestWithAnInvalidToken_Gets401AndNoLoginRedirect()
        {
            // Arrange
            await using CoexistenceHost host = await CoexistenceHost.StartAsync(jwtBearerFirst: false);
            using HttpClient client = host.CreateClient();

            // Act
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri("coexist/default", UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
            using HttpResponseMessage response = await client.SendAsync(request);

            // Assert — 401 via the JWT scheme, never a login page (AC-OIDC9's headline)
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(response.Headers.Location, Is.Null);
                Assert.That(
                    response.Headers.WwwAuthenticate.Any(static header =>
                        string.Equals(header.Scheme, "Bearer", StringComparison.Ordinal)),
                    Is.True,
                    "The 401 must carry WWW-Authenticate: Bearer.");
            });
        }

        [Test]
        public async Task BearerRequestWithAValidMachineToken_Succeeds()
        {
            // Arrange — a real client-credentials token from the same in-process provider
            await using CoexistenceHost host = await CoexistenceHost.StartAsync(jwtBearerFirst: false);
            using HttpClient client = host.CreateClient();
            string machineToken = await AcquireMachineTokenAsync(host);

            // Act
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri("coexist/default", UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", machineToken);
            using HttpResponseMessage response = await client.SendAsync(request);

            // Assert — coexistence composes with the machine-token stack rather than shadowing it
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task BrowserRequestWithoutAToken_IsChallengedToTheIdentityProvider()
        {
            // Arrange
            await using CoexistenceHost host = await CoexistenceHost.StartAsync(jwtBearerFirst: false);
            using HttpClient client = host.CreateClient();

            // Act
            using HttpResponseMessage response =
                await client.GetAsync(new Uri("coexist/default", UriKind.Relative));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(
                    response.Headers.Location!.Authority,
                    Is.EqualTo(OidcTestHost.IdpBase.Authority));
                Assert.That(response.Headers.Location.AbsolutePath, Is.EqualTo("/connect/authorize"));
            });
        }

        [Test]
        public async Task EndpointPinnedToBearer_WithNoHeaderAtAll_Gets401NotARedirect()
        {
            // Arrange — the documented per-endpoint override, exactly how the SUT keeps the #9 E2E
            // machine endpoint's 401 in Step 10
            await using CoexistenceHost host = await CoexistenceHost.StartAsync(jwtBearerFirst: false);
            using HttpClient client = host.CreateClient();

            // Act
            using HttpResponseMessage response =
                await client.GetAsync(new Uri("coexist/bearer", UriKind.Relative));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(response.Headers.Location, Is.Null);
            });
        }

        [Test]
        public async Task RegistrationOrder_DoesNotChangeAnyOfTheAbove()
        {
            // Arrange — the same assertions against the host built in the other order (plan pick 4)
            await using CoexistenceHost host = await CoexistenceHost.StartAsync(jwtBearerFirst: true);
            using HttpClient client = host.CreateClient();
            string machineToken = await AcquireMachineTokenAsync(host);

            // Act
            using HttpRequestMessage invalidBearerRequest = new(
                HttpMethod.Get,
                new Uri("coexist/default", UriKind.Relative));
            invalidBearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
            using HttpResponseMessage invalidBearer = await client.SendAsync(invalidBearerRequest);

            using HttpRequestMessage validBearerRequest = new(
                HttpMethod.Get,
                new Uri("coexist/default", UriKind.Relative));
            validBearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", machineToken);
            using HttpResponseMessage validBearer = await client.SendAsync(validBearerRequest);

            using HttpResponseMessage browser =
                await client.GetAsync(new Uri("coexist/default", UriKind.Relative));
            using HttpResponseMessage pinned =
                await client.GetAsync(new Uri("coexist/bearer", UriKind.Relative));
            using HttpResponseMessage anonymous =
                await client.GetAsync(new Uri("coexist/anonymous", UriKind.Relative));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(invalidBearer.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(invalidBearer.Headers.Location, Is.Null);
                Assert.That(validBearer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(browser.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(browser.Headers.Location!.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
                Assert.That(pinned.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public async Task WithoutTheJwtBearerPackage_TheForwardingIsInert()
        {
            // Arrange — OIDC alone: no scheme named Bearer is registered
            await using OidcTestHost host = await OidcTestHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act — a request carrying a Bearer header is challenged like any other browser request
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri(OidcTestHost.AppBase, "protected/page"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
            using HttpResponseMessage response = await agent.SendAsync(request, followRedirects: false);

            // Assert — nothing throws looking for an unregistered scheme
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(response.Headers.Location!.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
            });
        }

        [Test]
        public async Task AnonymousEndpoint_StaysAnonymousUnderBothPackages()
        {
            // Arrange — both fallback policies in play (D-6's documented carve-out)
            await using CoexistenceHost host = await CoexistenceHost.StartAsync(jwtBearerFirst: false);
            using HttpClient client = host.CreateClient();

            // Act
            using HttpResponseMessage anonymous =
                await client.GetAsync(new Uri("coexist/anonymous", UriKind.Relative));
            using HttpResponseMessage health =
                await client.GetAsync(new Uri("healthz", UriKind.Relative));

            // Assert — the explicit opt-out and the Cloudstrap-mapped health endpoint stay reachable
            Assert.Multiple(() =>
            {
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        /// <summary>
        /// Acquires a real machine token from the in-process provider's token endpoint.
        /// </summary>
        private static async Task<string> AcquireMachineTokenAsync(CoexistenceHost host)
        {
            using HttpClient idpClient = host.IdentityProvider.CreateClient();
            using HttpResponseMessage response = await idpClient.PostAsync(
                host.IdentityProvider.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = "contoso-service",
                    ["client_secret"] = "placeholder-not-a-real-secret",
                    ["scope"] = "catalog.read",
                }));
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            return document.RootElement.GetProperty("access_token").GetString()!;
        }
    }
}
