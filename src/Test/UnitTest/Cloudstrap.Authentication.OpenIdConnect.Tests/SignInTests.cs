namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Caching.Distributed;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-OIDC1 and AC-OIDC2: one call, and an unauthenticated request is challenged with
    /// code + PKCE + <c>form_post</c>, the sign-in completes against the real in-process identity
    /// provider, and the caller comes back holding a hardened <c>__Host-Cloudstrap</c> session cookie
    /// whose claims are the token's own.
    /// </summary>
    [TestFixture]
    public sealed class SignInTests
    {
        [Test]
        public async Task UnauthenticatedRequest_IsChallengedWithCodePkceAndFormPost()
        {
            // Arrange
            await using OidcTestHost host = await OidcTestHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage challenge =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "protected/page"));
            Uri location = challenge.Headers.Location!;

            // Assert — a 302 to the provider's authorization endpoint carrying the code+PKCE shape,
            // with the client secret nowhere in the URL (AC-OIDC1)
            Assert.Multiple(() =>
            {
                Assert.That(challenge.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(location.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
                Assert.That(location.AbsolutePath, Is.EqualTo("/connect/authorize"));
                Assert.That(GetQueryParameter(location, "response_type"), Is.EqualTo("code"));
                Assert.That(GetQueryParameter(location, "code_challenge"), Is.Not.Null.And.Not.Empty);
                Assert.That(GetQueryParameter(location, "code_challenge_method"), Is.EqualTo("S256"));
                Assert.That(GetQueryParameter(location, "state"), Is.Not.Null.And.Not.Empty);
                Assert.That(GetQueryParameter(location, "nonce"), Is.Not.Null.And.Not.Empty);
                Assert.That(GetQueryParameter(location, "response_mode"), Is.EqualTo("form_post"));
                Assert.That(GetQueryParameter(location, "client_id"), Is.EqualTo(OidcTestHost.ClientId));
                Assert.That(
                    GetQueryParameter(location, "redirect_uri"),
                    Is.EqualTo(new Uri(OidcTestHost.AppBase, "signin-oidc").AbsoluteUri));
                Assert.That(location.AbsoluteUri, Does.Not.Contain(OidcTestHost.ClientSecret));
            });
        }

        [Test]
        public async Task CompletedSignIn_IssuesTheHardenedHostCookie()
        {
            // Arrange
            await using OidcTestHost host = await OidcTestHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            string sessionCookieHeader = agent.Responses
                .SelectMany(static response =>
                    response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values)
                        ? values
                        : [])
                .Single(static value => value.StartsWith("__Host-Cloudstrap=", StringComparison.Ordinal));

            CookieAuthenticationOptions cookieOptions = host.App.Services
                .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
                .Get(CloudstrapOpenIdConnect.CookieScheme);

            // Assert — the D-1 posture, attribute by attribute, plus the 8 h sliding lifetime
            Assert.Multiple(() =>
            {
                Assert.That(final.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(sessionCookieHeader, Does.Contain("httponly").IgnoreCase);
                Assert.That(sessionCookieHeader, Does.Contain("secure").IgnoreCase);
                Assert.That(sessionCookieHeader, Does.Contain("samesite=lax").IgnoreCase);
                Assert.That(sessionCookieHeader, Does.Contain("path=/").IgnoreCase);
                Assert.That(sessionCookieHeader, Does.Not.Contain("domain=").IgnoreCase);
                Assert.That(cookieOptions.ExpireTimeSpan, Is.EqualTo(TimeSpan.FromHours(8)));
                Assert.That(cookieOptions.SlidingExpiration, Is.True);
            });
        }

        [Test]
        public async Task CompletedSignIn_KeepsClaimTypesAsIssued()
        {
            // Arrange
            await using OidcTestHost host = await OidcTestHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            using JsonDocument page = JsonDocument.Parse(await final.Content.ReadAsStringAsync());

            // Assert — sub stays sub, name stays name, role stays role; no legacy URI mapping; and
            // Identity.Name resolves from the name claim (Deliberate Behavior Change 8)
            Assert.Multiple(() =>
            {
                Assert.That(page.RootElement.GetProperty("sub").GetString(), Is.EqualTo(OidcTestHost.Username));
                Assert.That(
                    page.RootElement.GetProperty("name").GetString(),
                    Is.EqualTo(OidcTestHost.UserDisplayName));
                Assert.That(page.RootElement.GetProperty("role").GetString(), Is.EqualTo(OidcTestHost.UserRole));
                Assert.That(
                    page.RootElement.GetProperty("identityName").GetString(),
                    Is.EqualTo(OidcTestHost.UserDisplayName));
                Assert.That(
                    page.RootElement.GetProperty("legacyNameClaim").ValueKind,
                    Is.EqualTo(JsonValueKind.Null));
            });
        }

        [Test]
        public async Task CompletedSignIn_ReturnsTheUserToTheOriginallyRequestedLocalUrl()
        {
            // Arrange
            await using OidcTestHost host = await OidcTestHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage final = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page?item=42"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Assert — the protected path the challenge came from, query and all, not the root
            Assert.Multiple(() =>
            {
                Assert.That(final.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(final.RequestMessage!.RequestUri!.AbsolutePath, Is.EqualTo("/protected/page"));
                Assert.That(final.RequestMessage.RequestUri.Query, Is.EqualTo("?item=42"));
            });
        }

        [Test]
        public async Task CompletedSignIn_StoresTheTokensInTheAuthenticationSession()
        {
            // Arrange
            await using OidcTestHost host = await OidcTestHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            // Act
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/tokens"));
            string body = await response.Content.ReadAsStringAsync();
            Assert.That(
                response.StatusCode,
                Is.EqualTo(HttpStatusCode.OK),
                () => "The tokens endpoint must answer for the signed-in session: " + body);
            using JsonDocument tokens = JsonDocument.Parse(body);

            // Assert — the tokens live in the ticket (D-2), and no store of any kind was registered:
            // the absence is the feature
            Assert.Multiple(() =>
            {
                Assert.That(
                    tokens.RootElement.GetProperty("accessToken").GetString(),
                    Is.Not.Null.And.Not.Empty);
                Assert.That(
                    tokens.RootElement.GetProperty("refreshToken").GetString(),
                    Is.Not.Null.And.Not.Empty);
                Assert.That(host.App.Services.GetService<IDistributedCache>(), Is.Null);
                Assert.That(host.App.Services.GetService<ITicketStore>(), Is.Null);
            });
        }

        [Test]
        public async Task ConfiguredScope_IsTheScopeRequested()
        {
            // Arrange — a custom scope narrower than the default: no stock default may silently append
            await using OidcTestHost host = await OidcTestHost.StartAsync(new Dictionary<string, string?>
            {
                ["Cloudstrap:OpenIdConnect:Scope"] = "openid catalog.read",
            });
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage challenge =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "protected/page"));

            // Assert
            Assert.That(
                GetQueryParameter(challenge.Headers.Location!, "scope"),
                Is.EqualTo("openid catalog.read"));
        }

        [Test]
        public async Task SecondSignInAfterExpiry_IsChallengedAgain()
        {
            // Arrange — a fresh agent carries no session cookie, exactly like an expired one
            await using OidcTestHost host = await OidcTestHost.StartAsync();
            using BrowserlessUserAgent agent = host.CreateAgent();

            // Act
            using HttpResponseMessage response =
                await agent.GetNoRedirectAsync(new Uri(OidcTestHost.AppBase, "protected/page"));

            // Assert — challenged rather than served: the D-6 fallback policy is live
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(response.Headers.Location!.Authority, Is.EqualTo(OidcTestHost.IdpBase.Authority));
            });
        }

        private static string? GetQueryParameter(Uri uri, string name)
        {
            foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split('=', 2);
                if (string.Equals(parts[0], name, StringComparison.Ordinal))
                {
                    return parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                }
            }

            return null;
        }
    }
}
