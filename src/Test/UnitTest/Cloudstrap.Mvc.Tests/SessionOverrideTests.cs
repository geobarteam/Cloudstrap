namespace Cloudstrap.Mvc.Tests
{
    using System.Net;
    using Cloudstrap.Mvc.Tests.Infrastructure;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-MVC3 and AC-MVC11: every session convention is overridable — through configuration and through
    /// the hook that runs last — off means off with stock failure semantics, and a consumer-registered
    /// distributed cache is the one session state actually uses.
    /// </summary>
    [TestFixture]
    public sealed class SessionOverrideTests
    {
        [Test]
        public async Task SessionCookieName_ConfiguredOverride_Wins()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:Session:CookieName"] = ".Contoso.Session",
                });

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/session/write", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string cookie = response.Headers.GetValues("Set-Cookie").Single();

            // Assert
            Assert.That(cookie, Does.StartWith(".Contoso.Session="));
        }

        [Test]
        public async Task SessionSecurePolicy_SameAsRequestOverride_DropsSecureOverHttp()
        {
            // Arrange — the documented local-dev override, proven behaviorally on the wire
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:Session:CookieSecurePolicy"] = "SameAsRequest",
                });

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/session/write", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string cookie = response.Headers.GetValues("Set-Cookie").Single();

            // Assert
            Assert.That(cookie, Does.Not.Contain("secure").IgnoreCase);
        }

        [Test]
        public async Task SessionIdleTimeoutAndIsEssential_ConfiguredOverrides_LandOnTheResolvedOptions()
        {
            // Arrange — the resolved-options idiom for settings whose behavior needs a 20-minute wait
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:Session:IdleTimeoutMinutes"] = "5",
                    ["Cloudstrap:Mvc:Session:IsEssential"] = "true",
                });

            // Act
            SessionOptions resolved = app.Services
                .GetRequiredService<IOptions<SessionOptions>>()
                .Value;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(resolved.IdleTimeout, Is.EqualTo(TimeSpan.FromMinutes(5)));
                Assert.That(resolved.Cookie.IsEssential, Is.True);
            });
        }

        [Test]
        public async Task SessionHook_RunsAfterTheCloudstrapDefaultsAndWins()
        {
            // Arrange — a third name, distinct from both the default and the configured value
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:Session:CookieName"] = ".Contoso.Session",
                },
                configure: configurator =>
                    configurator.Session = session => session.Cookie.Name = ".Hook.Session");

            // Act
            using HttpResponseMessage response = await app.GetTestClient().GetAsync(
                new Uri("/session/write", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);
            string cookie = response.Headers.GetValues("Set-Cookie").Single();

            // Assert — the hook has the final say
            Assert.That(cookie, Does.StartWith(".Hook.Session="));
        }

        [Test]
        public async Task SessionDisabled_WiresNoSessionServicesAndIssuesNoCookie()
        {
            // Arrange
            await using WebApplication app = await MvcTestHost.StartAsync(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:Mvc:Session:Enabled"] = "false",
                });
            using HttpClient client = app.GetTestClient();

            // Act
            using HttpResponseMessage page = await client.GetAsync(
                new Uri("/", UriKind.Relative),
                TestContext.CurrentContext.CancellationToken);

            // Assert — no session cookie anywhere, and touching HttpContext.Session surfaces the stock
            // InvalidOperationException, unmasked (confirmed through TestServer in RED)
            Assert.Multiple(() =>
            {
                Assert.That(page.Headers.Contains("Set-Cookie"), Is.False);
                Assert.That(
                    async () => await client.GetAsync(
                        new Uri("/session/write", UriKind.Relative),
                        TestContext.CurrentContext.CancellationToken),
                    Throws.InstanceOf<InvalidOperationException>());
            });
        }

        [Test]
        public async Task ConsumerRegisteredDistributedCache_IsTheOneSessionUses()
        {
            // Arrange — registered before AddCloudstrapMvc: the TryAdd fallback never displaces it
            RecordingDistributedCache recorder = new();
            await using WebApplication app = await MvcTestHost.StartAsync(
                beforeBuild: builder =>
                    builder.Services.AddSingleton<IDistributedCache>(recorder));
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

            // Assert — the round-trip flowed through the consumer's cache instance
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(recorder.Writes, Is.GreaterThan(0));
                Assert.That(recorder.Reads, Is.GreaterThan(0));
            });
        }
    }
}
