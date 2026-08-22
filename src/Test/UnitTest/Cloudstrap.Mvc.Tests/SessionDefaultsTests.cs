namespace Cloudstrap.Mvc.Tests
{
    using System.Net;
    using System.Reflection;
    using Cloudstrap.Mvc.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Session;
    using Microsoft.AspNetCore.TestHost;
    using NUnit.Framework;

    /// <summary>
    /// AC-MVC2 and AC-MVC4: writing to <c>ISession</c> establishes exactly one hardened cookie that
    /// round-trips, flowing entirely through stock <c>Microsoft.AspNetCore.Session</c> — this package
    /// ships zero session code of its own.
    /// </summary>
    [TestFixture]
    public sealed class SessionDefaultsTests
    {
        [Test]
        public async Task SessionWrite_EstablishesExactlyOneHardenedCookie()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/session/write", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string[] cookies = [.. response.Headers.GetValues("Set-Cookie")];
            string cookie = cookies.Single();

            // Assert — the fork's whole delta, expressed as startup options and pinned on the wire
            Assert.Multiple(() =>
            {
                Assert.That(cookie, Does.StartWith(".Cloudstrap.Session="));
                Assert.That(cookie, Does.Contain("secure").IgnoreCase);
                Assert.That(cookie, Does.Contain("httponly").IgnoreCase);
                Assert.That(cookie, Does.Contain("samesite=lax").IgnoreCase);
                Assert.That(cookie, Does.Contain("path=/").IgnoreCase);
            });
        }

        [Test]
        public async Task SessionCookie_PathFollowsTheConfiguredPathBase()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?> { ["Cloudstrap:Application:PathBase"] = "contoso" });

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/contoso/session/write", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string cookie = response.Headers.GetValues("Set-Cookie").Single();

            // Assert
            Assert.That(cookie, Does.Contain("path=/contoso").IgnoreCase);
        }

        [Test]
        public async Task SessionWrite_ResponseCarriesTheNoStoreHeaders()
        {
            // Arrange — stock behavior, asserted because AC-MVC2 names it: this was never fork-added value
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/session/write", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
                Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
                Assert.That(response.Headers.Pragma.ToString(), Is.EqualTo("no-cache"));
                Assert.That(response.Content.Headers.TryGetValues("Expires", out IEnumerable<string>? expires)
                    ? expires.Single()
                    : null, Is.EqualTo("-1"));
            });
        }

        [Test]
        public async Task SessionRoundTrip_ReadsTheStoredValueBack()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();
            using HttpClient client = app.GetTestClient();

            using HttpResponseMessage write = await client.GetAsync(
                new Uri("/session/write", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string cookiePair = write.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

            using HttpRequestMessage read = new(HttpMethod.Get, new Uri("/session/read", UriKind.Relative));
            read.Headers.Add("Cookie", cookiePair);

            // Act
            using HttpResponseMessage response = await client.SendAsync(
                read,
                TestContext.CurrentContext.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Is.EqualTo(SessionController.Marker));
            });
        }

        [Test]
        public async Task SessionCookieValue_IsOpaque()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/session/write", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string cookie = response.Headers.GetValues("Set-Cookie").Single();

            // Assert — DataProtection-protected, stock: the stored text never appears on the wire
            Assert.That(cookie, Does.Not.Contain(SessionController.Marker));
        }

        [Test]
        public async Task SessionRequest_WithoutAWrite_IssuesNoCookie()
        {
            // Arrange — stock establish-on-write semantics preserved: nothing eager was added
            await using WebApplication app = await MvcTestHost.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert
            Assert.That(response.Headers.Contains("Set-Cookie"), Is.False);
        }

        [Test]
        public void MvcAssembly_ContainsNoSessionCodeOfItsOwn()
        {
            // Arrange
            Assembly mvc = typeof(MvcPipelineOptions).Assembly;

            // Act
            string[] implementors =
            [
                .. mvc.GetTypes()
                    .Where(type => typeof(ISession).IsAssignableFrom(type)
                        || typeof(ISessionStore).IsAssignableFrom(type))
                    .Select(type => type.Name),
            ];
            string[] forbiddenNames =
            [
                .. mvc.GetTypes()
                    .Select(type => type.Name)
                    .Where(name => name.Contains("SessionMiddleware", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("CookieProtection", StringComparison.OrdinalIgnoreCase)),
            ];

            // Assert — D-1 made structural: SessionSettings is the one allowed options type
            Assert.Multiple(() =>
            {
                Assert.That(implementors, Is.Empty, $"ISession/ISessionStore: {string.Join(", ", implementors)}");
                Assert.That(forbiddenNames, Is.Empty, $"Forbidden: {string.Join(", ", forbiddenNames)}");
            });
        }

        [Test]
        public void AddCloudstrapMvc_IdleTimeoutZero_FailsStartupNamingTheKey()
        {
            // Arrange
            Dictionary<string, string?> configuration = new()
            {
                ["Cloudstrap:Mvc:Session:IdleTimeoutMinutes"] = "0",
            };

            // Act + Assert
            Assert.That(
                async () => await MvcTestHost.StartAsync(configuration),
                Throws.Exception.With.Message.Contains("Cloudstrap:Mvc:Session:IdleTimeoutMinutes"));
        }
    }
}
