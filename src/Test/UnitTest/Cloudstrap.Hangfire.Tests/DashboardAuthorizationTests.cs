namespace Cloudstrap.Hangfire.Tests
{
    using System.Net;
    using Cloudstrap.Hangfire.Tests.Infrastructure;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// The dashboard's authorization posture (D-1, AC-HF10–AC-HF13) ⚠️ auth risk area: no request below
    /// "authenticated" is ever served, a role or a consumer policy narrows it further, the Hangfire-level floor
    /// holds even when endpoint authorization is bypassed, and the path follows configuration and the path base.
    /// </summary>
    [TestFixture]
    public sealed class DashboardAuthorizationTests
    {
        private const string _dashboardMarker = "Hangfire Dashboard";

        [Test]
        public async Task Anonymous_CookieScheme_IsRedirectedToLogin_DashboardHtmlNeverServed()
        {
            // Arrange
            await using DashboardTestApp app = await DashboardTestApp.StartAsync(DashboardTestScheme.Cookie);

            // Act
            using HttpResponseMessage response = await app.GetAsync("/hangfire");
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                Assert.That(response.Headers.Location?.ToString(), Does.Contain("/Account/Login"));
                Assert.That(body, Does.Not.Contain(_dashboardMarker));
            });
        }

        [Test]
        public async Task Anonymous_BearerScheme_Returns401()
        {
            // Arrange
            await using DashboardTestApp app = await DashboardTestApp.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetAsync("/hangfire");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task Authenticated_NotInRequiredRole_Returns403()
        {
            // Arrange
            await using DashboardTestApp app = await DashboardTestApp.StartAsync(settings: RequiredRole("ops"));

            // Act
            using HttpResponseMessage response = await app.GetAsync("/hangfire", user: "alice", roles: "reader");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }

        [Test]
        public async Task Authenticated_InRequiredRole_Returns200()
        {
            // Arrange
            await using DashboardTestApp app = await DashboardTestApp.StartAsync(settings: RequiredRole("ops"));

            // Act
            using HttpResponseMessage response = await app.GetAsync("/hangfire", user: "alice", roles: "reader,ops");
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Does.Contain(_dashboardMarker));
            });
        }

        [Test]
        public async Task NoRequiredRole_AnyAuthenticatedPrincipal_Returns200()
        {
            // Arrange — the trusted-subsystem posture: any authenticated caller (for example a proxy host's token).
            await using DashboardTestApp app = await DashboardTestApp.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetAsync("/hangfire", user: "proxy-host");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task AuthorizationPolicySet_ConsumerPolicyReplacesTheInlineOne()
        {
            // Arrange — the consumer policy requires scope=ops; RequiredRole is then ignored.
            Dictionary<string, string?> settings = RequiredRole("ops");
            settings["Cloudstrap:Hangfire:Dashboard:AuthorizationPolicy"] = DashboardTestApp.OpsScopePolicy;
            await using DashboardTestApp app = await DashboardTestApp.StartAsync(settings: settings);

            // Act
            using HttpResponseMessage roleOnly = await app.GetAsync("/hangfire", user: "alice", roles: "ops");
            using HttpResponseMessage withScope = await app.GetAsync("/hangfire", user: "alice", scope: "ops");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(roleOnly.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(withScope.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public void NoAuthenticationSchemeRegistered_MapThrowsNamingTheRequirement()
        {
            // Act & Assert
            Assert.That(
                async () => await DashboardTestApp.StartAsync(DashboardTestScheme.None),
                Throws.InvalidOperationException.With.Message.Contains("authentication scheme"));
        }

        [Test]
        public async Task EndpointAuthorizationBypassedWithAllowAnonymous_HangfireFloorStillDeniesTheAnonymousRequest()
        {
            // Arrange
            await using DashboardTestApp app = await DashboardTestApp.StartAsync(allowAnonymous: true);

            // Act
            using HttpResponseMessage anonymous = await app.GetAsync("/hangfire");
            string body = await anonymous.Content.ReadAsStringAsync();
            using HttpResponseMessage authenticated = await app.GetAsync("/hangfire", user: "alice");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(body, Does.Not.Contain(_dashboardMarker));
                Assert.That(authenticated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public async Task DefaultPath_IsHangfire()
        {
            // Arrange
            await using DashboardTestApp app = await DashboardTestApp.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetAsync("/hangfire", user: "alice");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task ConfiguredPath_IsServed_DefaultPathIs404()
        {
            // Arrange
            await using DashboardTestApp app = await DashboardTestApp.StartAsync(settings: new Dictionary<string, string?>
            {
                ["Cloudstrap:Hangfire:Dashboard:Path"] = "/ops/jobs",
            });

            // Act
            using HttpResponseMessage configured = await app.GetAsync("/ops/jobs", user: "alice");
            using HttpResponseMessage defaultPath = await app.GetAsync("/hangfire", user: "alice");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(configured.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(defaultPath.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public async Task UnderPathBase_IsServedAtPathBasePlusPath()
        {
            // Arrange
            await using DashboardTestApp app = await DashboardTestApp.StartAsync(pathBase: "/portal");

            // Act
            using HttpResponseMessage response = await app.GetAsync("/portal/hangfire", user: "alice");
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Does.Contain("/portal/hangfire"));
            });
        }

        [Test]
        public async Task XForwardedPrefixHeader_HasNoEffect()
        {
            // Arrange — proxy prefixes are deliverable #18's concern, never handled here.
            await using DashboardTestApp app = await DashboardTestApp.StartAsync();

            // Act
            using HttpResponseMessage response = await app.GetAsync(
                "/hangfire",
                user: "alice",
                headers: new Dictionary<string, string> { ["X-Forwarded-Prefix"] = "/edge-proxy" });
            string body = await response.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(body, Does.Not.Contain("/edge-proxy"));
            });
        }

        [Test]
        public async Task StorageOnlyHost_Mapping_LogsTheSummaryOnce_WithTheDashboardPath()
        {
            // Arrange & Act
            await using DashboardTestApp app = await DashboardTestApp.StartAsync();

            // Assert
            string[] summaries = [.. app.Logs.Entries
                .Where(entry => entry.Category == "Cloudstrap.Hangfire" && entry.Level == LogLevel.Information)
                .Select(entry => entry.Message)
                .Where(message => message.StartsWith("Cloudstrap Hangfire:", StringComparison.Ordinal))];
            Assert.Multiple(() =>
            {
                Assert.That(summaries, Has.Length.EqualTo(1));
                Assert.That(summaries[0], Does.Contain("run server False").And.Contain("dashboard /hangfire"));
            });
        }

        private static Dictionary<string, string?> RequiredRole(string role)
        {
            return new Dictionary<string, string?> { ["Cloudstrap:Hangfire:Dashboard:RequiredRole"] = role };
        }
    }
}
