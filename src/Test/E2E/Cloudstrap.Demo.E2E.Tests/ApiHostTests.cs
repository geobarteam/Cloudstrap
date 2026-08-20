namespace Cloudstrap.Demo.E2E.Tests
{
    using System.Net;
    using NUnit.Framework;

    /// <summary>
    /// Demonstrates the Api demo app (deliverable #27): a pure JWT API host built from two shipped
    /// calls (<c>AddCloudstrapWebApi</c> + <c>AddCloudstrapJwtBearer</c>) with
    /// <c>RequireAuthenticatedEndpoints</c> left at its hardened <c>true</c> default — anonymous
    /// callers get 401 from the fallback policy with no <c>[Authorize]</c> attribute anywhere,
    /// while the health probes stay anonymously reachable (the #5 probe carve-out).
    /// </summary>
    [TestFixture]
    public sealed class ApiHostTests
    {
        private HttpClient _client = null!;

        [SetUp]
        public void SetUp() => _client = new HttpClient { BaseAddress = new Uri(E2eFixture.ApiBaseUrl) };

        [TearDown]
        public void TearDown() => _client.Dispose();

        [Test]
        public async Task ApiHost_AnonymousWhoAmI_Returns401()
        {
            // Act — no Authorization header: the whole-app fallback policy is the gate, the
            // controller carries no [Authorize] attribute (AC-DR6 / carried AC-D7).
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("api/v1/downstream/whoami", UriKind.Relative));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task ApiHost_AnonymousHealthz_Returns200()
        {
            // Act — the probe carve-out must coexist with the hardened default
            // (AC-DR6 / carried AC-D8).
            using HttpResponseMessage response = await _client.GetAsync(
                new Uri("healthz", UriKind.Relative));

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
