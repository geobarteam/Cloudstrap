namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Net;
    using Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// AC-OIDC5 and D-5: two opt-in endpoints give a consumer working login and logout — logout ends
    /// the local session <em>and</em> the identity provider session — and a caller-supplied return URL
    /// can only ever be local, closing the source's open redirect.
    /// </summary>
    [TestFixture]
    public sealed class AuthenticationEndpointTests
    {
        [Test]
        public async Task Logout_EndsTheLocalSessionAndSendsTheBrowserToTheEndSessionEndpoint()
        {
            // Arrange
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                mapEndpoints: static app => app.MapCloudstrapAuthenticationEndpoints());
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "account/login?returnUrl=/protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act — RP-initiated logout, following the whole chain back to the application
            using HttpResponseMessage afterLogout =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "account/logout"));

            HttpResponseMessage endSessionHop = agent.Responses.Single(static response =>
                response.RequestMessage?.RequestUri?.AbsolutePath == "/connect/logout");
            string endSessionQuery = endSessionHop.RequestMessage!.RequestUri!.Query;
            bool sessionCookieDeleted = agent.Responses
                .SelectMany(static response =>
                    response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
                        ? values
                        : [])
                .Any(static value => value.StartsWith("__Host-Cloudstrap=;", StringComparison.Ordinal));

            // The next request is anonymous again, and the provider serves its login form rather than
            // silently re-authenticating
            using HttpResponseMessage challenge =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "protected/page"));
            using HttpResponseMessage providerPage = await agent.GetAsync(challenge.Headers.Location!);
            string providerHtml = await providerPage.Content.ReadAsStringAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(endSessionHop.RequestMessage.RequestUri.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
                Assert.That(endSessionQuery, Does.Contain("id_token_hint="));
                Assert.That(endSessionQuery, Does.Contain("post_logout_redirect_uri="));
                Assert.That(sessionCookieDeleted, Is.True, "The __Host-Cloudstrap cookie must be expired.");
                Assert.That(challenge.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(challenge.Headers.Location!.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
                Assert.That(providerHtml, Does.Contain("name=\"username\""));
            });
        }

        [Test]
        public async Task Login_WithALocalReturnUrl_ComesBackToIt()
        {
            // Arrange
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                mapEndpoints: static app => app.MapCloudstrapAuthenticationEndpoints());
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "account/login?returnUrl=/protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(final.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(final.RequestMessage!.RequestUri!.AbsolutePath, Is.EqualTo("/protected/page"));
            });
        }

        [TestCase("https://evil.example.com/phish")]
        [TestCase("//evil.example.com")]
        [TestCase("/\\evil.example.com")]
        [TestCase("%2F%2Fevil.example.com")]
        public async Task Login_WithAnAbsoluteReturnUrl_IgnoresItAndUsesTheDefault(string returnUrl)
        {
            // Arrange
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                mapEndpoints: static app => app.MapCloudstrapAuthenticationEndpoints());
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act — the agent knows only the two in-process hosts, so any redirect toward the foreign
            // authority would throw rather than pass silently
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "account/login?returnUrl=" + Uri.EscapeDataString(returnUrl)),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Assert — the open redirect of source finding 5 is closed: the user lands on the default
            // local page (Deliberate Behavior Change 10)
            Assert.Multiple(() =>
            {
                Assert.That(final.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(final.RequestMessage!.RequestUri!.Authority, Is.EqualTo(OidcTestHost.AppBase.Authority));
                Assert.That(final.RequestMessage.RequestUri.AbsolutePath, Is.EqualTo("/"));
            });
        }

        [Test]
        public async Task MapperNotCalled_MapsNothing()
        {
            // Arrange — the schemes alone map no endpoint: without the opt-in call both paths are 404.
            // The D-6 fallback policy is opted out here because it challenges anonymous requests on
            // any unmatched path, which would mask the 404 this test is about.
            await using OidcTestHost host = await OidcTestHost.StartAsync(new Dictionary<string, string?>
            {
                ["Cloudstrap:OpenIdConnect:RequireAuthenticatedEndpoints"] = "false",
            });
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage login =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "account/login"));
            using HttpResponseMessage logout =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "account/logout"));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    login.StatusCode,
                    Is.EqualTo(HttpStatusCode.NotFound),
                    () => "Location: " + login.Headers.Location);
                Assert.That(
                    logout.StatusCode,
                    Is.EqualTo(HttpStatusCode.NotFound),
                    () => "Location: " + logout.Headers.Location);
            });
        }

        [Test]
        public async Task ConfiguredPaths_AreHonored()
        {
            // Arrange
            CapturingLoggerProvider logs = new();
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                configuration: new Dictionary<string, string?>
                {
                    ["Cloudstrap:OpenIdConnect:LoginPath"] = "/auth/enter",
                    ["Cloudstrap:OpenIdConnect:LogoutPath"] = "/auth/exit",
                    ["Cloudstrap:OpenIdConnect:RequireAuthenticatedEndpoints"] = "false",
                    ["Logging:LogLevel:Default"] = "Information",
                },
                mapEndpoints: static app => app.MapCloudstrapAuthenticationEndpoints(),
                afterRegistration: (builder, _) => builder.Logging.AddProvider(logs));
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage movedLogin =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "auth/enter"));
            using HttpResponseMessage defaultLogin =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "account/login"));

            // Assert — the endpoints move, and the startup log states the paths in force
            Assert.Multiple(() =>
            {
                Assert.That(movedLogin.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(movedLogin.Headers.Location!.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
                Assert.That(defaultLogin.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(
                    logs.Entries.Any(static entry =>
                        entry.Message.Contains("/auth/enter", StringComparison.Ordinal)
                        && entry.Message.Contains("/auth/exit", StringComparison.Ordinal)),
                    Is.True,
                    "The startup log must state the endpoint paths in force.");
            });
        }

        [Test]
        public async Task ConsumerOwnSignOut_WorksWithoutTheMapper()
        {
            // Arrange — an application endpoint calling SignOut() with no scheme arguments
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                mapEndpoints: static app => app.MapGet(
                    "/app/signout",
                    static () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/" })));
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act
            using HttpResponseMessage response =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "app/signout"));
            bool sessionCookieDeleted = response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
                && values.Any(static value => value.StartsWith("__Host-Cloudstrap=;", StringComparison.Ordinal));

            // Assert — the stock default scheme names were kept, so bare SignOut() reaches the OIDC
            // scheme and ends both sessions: the end-session redirect and the cookie deletion together
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(response.Headers.Location!.AbsolutePath, Is.EqualTo("/connect/logout"));
                Assert.That(response.Headers.Location.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
                Assert.That(sessionCookieDeleted, Is.True, "Bare SignOut() must also end the local session.");
            });
        }

        [Test]
        public async Task IdpWithoutAnEndSessionEndpoint_StillCompletesLocalSignOutAndLogsOnce()
        {
            // Arrange — the provider's metadata advertises no end_session_endpoint
            CapturingLoggerProvider logs = new();
            await using OidcTestHost host = await OidcTestHost.StartAsync(
                configuration: new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:Default"] = "Information",
                },
                mapEndpoints: static app => app.MapCloudstrapAuthenticationEndpoints(),
                afterRegistration: (builder, _) => builder.Logging.AddProvider(logs),
                stripEndSessionEndpoint: true);
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "account/login?returnUrl=/protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act — log out twice: the sign-out still completes locally, and the message appears once
            using HttpResponseMessage firstLogout =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "account/logout"));
            bool sessionCookieDeleted =
                firstLogout.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
                && values.Any(static value => value.StartsWith("__Host-Cloudstrap=;", StringComparison.Ordinal));
            using HttpResponseMessage secondLogout =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "account/logout"));

            int endSessionWarnings = logs.Entries.Count(static entry =>
                entry.Message.Contains("end_session_endpoint", StringComparison.Ordinal));

            // Assert — the cookie is cleared, the browser stays local, no exception, one log entry
            Assert.Multiple(() =>
            {
                Assert.That(firstLogout.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(firstLogout.Headers.Location!.IsAbsoluteUri, Is.False);
                Assert.That(sessionCookieDeleted, Is.True, "Local sign-out must still complete.");
                Assert.That(secondLogout.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(endSessionWarnings, Is.EqualTo(1));
            });
        }
    }
}
