namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using System.Net;
    using System.Text.Json;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates Cloudstrap.Authentication.ClientCredentials (deliverable #9) through the running
    /// app (AC-CC16, AC-CC14 live): the Bff's flagged <c>SelfApi</c> client calls a JWT-protected
    /// endpoint on itself with a bearer token issued by the test identity provider on loopback —
    /// acquisition by #9 and validation by #5, live over real HTTP, while everything else stays
    /// anonymous (`RequireAuthenticatedEndpoints=false`, the #5 documented whole-application opt-out).
    /// </summary>
    [TestFixture]
    public sealed class ClientCredentialsTests
    {
        private HttpClient _client = null!;

        [SetUp]
        public void SetUp() => _client = new HttpClient { BaseAddress = new Uri(E2eFixture.BaseUrl) };

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public async Task ProtectedEndpoint_CalledDirectlyWithoutAToken_Returns401()
        {
            // Act — a plain client, no Authorization header: #5's validation is live against the
            // loopback IdP's real discovery document and JWKS
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/v1/machine/status", UriKind.Relative));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task MachineCall_ThroughTheFlaggedClient_Returns200WithATokenIssuedByTheTestIdp()
        {
            // Act — the anonymous relay endpoint drives the flagged SelfApi client into the protected
            // endpoint: acquisition and validation, live through the running app
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/v1/machine/call", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            using JsonDocument document = JsonDocument.Parse(body);
            Assert.Multiple(() =>
            {
                Assert.That(
                    document.RootElement.GetProperty("clientId").GetString(),
                    Is.EqualTo("wasmtestproject-bff"));
                Assert.That(
                    new Uri(document.RootElement.GetProperty("issuer").GetString()!),
                    Is.EqualTo(new Uri($"http://127.0.0.1:{E2eFixture.IdentityProviderPort}")));
            });
        }

        [Test]
        public async Task MachineCall_Twice_ReusesTheCachedToken()
        {
            // Act — two round trips through the flagged client
            using HttpResponseMessage firstResponse = await _client.GetAsync(
                new Uri("api/v1/machine/call", UriKind.Relative));
            int countAfterFirst = E2eFixture.IdentityProviderTokenRequestCount;
            using HttpResponseMessage secondResponse = await _client.GetAsync(
                new Uri("api/v1/machine/call", UriKind.Relative));
            int countAfterSecond = E2eFixture.IdentityProviderTokenRequestCount;

            // Assert — the second call reused the first call's token (AC-CC2 live): no new token
            // request between the two calls, at least one acquisition in the run overall
            Assert.Multiple(() =>
            {
                Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(countAfterSecond, Is.EqualTo(countAfterFirst));
                Assert.That(countAfterSecond, Is.GreaterThanOrEqualTo(1));
            });
        }
    }
}
