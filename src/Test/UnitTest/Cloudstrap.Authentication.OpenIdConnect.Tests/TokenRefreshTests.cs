namespace Cloudstrap.Authentication.OpenIdConnect.Tests
{
    using System.Net;
    using System.Text.Json;
    using Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure;
    using Cloudstrap.Extensions;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.JsonWebTokens;
    using NUnit.Framework;

    /// <summary>
    /// AC-OIDC4 and AC-A2's interactive half: an expired access token renews itself behind the user's
    /// back — exactly one refresh grant, the request proceeds, the user is never re-challenged — and
    /// when refresh genuinely cannot succeed it fails loudly instead of sending an unauthenticated
    /// request.
    /// </summary>
    /// <remarks>
    /// Clock strategy (plan-level pick 2): short real lifetimes at the provider and a zeroed
    /// refresh-before-expiry buffer — never a fake clock, which Duende, the cookie ticket and
    /// OpenIddict would each have to honor coherently.
    /// </remarks>
    [TestFixture]
    public sealed class TokenRefreshTests
    {
        [Test]
        public async Task ExpiredAccessToken_RenewsTransparentlyWithExactlyOneRefreshGrant()
        {
            // Arrange
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartShortLifetimeHostAsync(capturing);
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);

            using HttpResponseMessage firstCall =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            string firstToken = await firstCall.Content.ReadAsStringAsync();
            int refreshCountAfterFirstCall = host.IdentityProvider.RefreshTokenRequestCount;

            // Act — wait until the short-lived token is really expired, then call again
            await WaitUntilExpiredAsync(firstToken);
            int responsesBeforeSecondCall = agent.Responses.Count;
            using HttpResponseMessage secondCall =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            string secondToken = await secondCall.Content.ReadAsStringAsync();

            bool anyChallengeIssued = agent.Responses.Skip(responsesBeforeSecondCall).Any(static response =>
                response.StatusCode == HttpStatusCode.Found
                && response.Headers.Location?.AbsolutePath == "/connect/authorize");

            // Assert — one refresh grant, a different token, both calls succeeded, no re-challenge
            Assert.Multiple(() =>
            {
                Assert.That(firstCall.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(secondCall.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    host.IdentityProvider.RefreshTokenRequestCount,
                    Is.EqualTo(refreshCountAfterFirstCall + 1));
                Assert.That(secondToken, Is.Not.EqualTo(firstToken));
                Assert.That(anyChallengeIssued, Is.False, "The user must never be re-challenged.");
            });
        }

        [Test]
        public async Task SeveralRequestsWithinOneLifetime_TriggerNoRefresh()
        {
            // Arrange — a comfortable lifetime; every call rides the same stored token
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartShortLifetimeHostAsync(
                capturing,
                accessTokenLifetimeSeconds: 120);
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            int refreshCountAfterSignIn = host.IdentityProvider.RefreshTokenRequestCount;

            // Act
            for (int call = 0; call < 3; call++)
            {
                using HttpResponseMessage response =
                    await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            }

            // Assert — no refresh, and the very same token value attached each time
            Assert.Multiple(() =>
            {
                Assert.That(host.IdentityProvider.RefreshTokenRequestCount, Is.EqualTo(refreshCountAfterSignIn));
                Assert.That(capturing.SeenBearerTokens.Distinct().Count(), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task RefreshedTokens_AreWrittenBackIntoTheSession()
        {
            // Arrange
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartShortLifetimeHostAsync(capturing);
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            string originalStoredToken = await ReadStoredAccessTokenAsync(agent);

            // Act — expire, renew through a call, then read the session again
            await WaitUntilExpiredAsync(originalStoredToken);
            using HttpResponseMessage renewingCall =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            string renewedToken = await renewingCall.Content.ReadAsStringAsync();
            string storedTokenAfterRenewal = await ReadStoredAccessTokenAsync(agent);

            int refreshCountAfterRenewal = host.IdentityProvider.RefreshTokenRequestCount;
            using HttpResponseMessage subsequentCall =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));

            // Assert — the ticket now carries the renewed token, so the next request rides it without
            // refreshing again (D-2's storage model working end to end)
            Assert.Multiple(() =>
            {
                Assert.That(storedTokenAfterRenewal, Is.Not.EqualTo(originalStoredToken));
                Assert.That(storedTokenAfterRenewal, Is.EqualTo(renewedToken));
                Assert.That(host.IdentityProvider.RefreshTokenRequestCount, Is.EqualTo(refreshCountAfterRenewal));
            });
        }

        [Test]
        public async Task ExpiredRefreshToken_FailsLoudlyAndSendsNothing()
        {
            // Arrange — both lifetimes elapse; nothing renewable remains
            CapturingLoggerProvider logs = new();
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartShortLifetimeHostAsync(
                capturing,
                accessTokenLifetimeSeconds: 1,
                refreshTokenLifetimeSeconds: 1,
                afterRegistration: (builder, _) => builder.Logging.AddProvider(logs),
                logLevel: "Information");
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            string refreshTokenValue = await ReadStoredTokenAsync(agent, "refreshToken");
            string accessTokenValue = await ReadStoredTokenAsync(agent, "accessToken");
            int requestsBefore = capturing.RequestCount;

            // Act
            await WaitUntilExpiredAsync(accessTokenValue, extraSeconds: 2);
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog-caught"));
            string body = await response.Content.ReadAsStringAsync();

            List<string> failureLogs = [.. logs.Entries
                .Where(static entry => entry.Level == LogLevel.Error
                    && entry.Message.Contains("No user access token could be produced", StringComparison.Ordinal))
                .Select(static entry => entry.Message)];

            // Assert — loud, request-free, logged once with no token value in it
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.StartWith("failed:"));
                Assert.That(capturing.RequestCount, Is.EqualTo(requestsBefore));
                Assert.That(failureLogs, Has.Count.EqualTo(1));
                Assert.That(failureLogs[0], Does.Not.Contain(refreshTokenValue));
                Assert.That(failureLogs[0], Does.Not.Contain(accessTokenValue));
            });
        }

        [Test]
        public async Task ProviderUnreachableDuringRefresh_SameContract()
        {
            // Arrange — sign in normally, then kill the backchannel before the refresh
            CapturingLoggerProvider logs = new();
            CapturingPrimaryHandler capturing = new();
            BreakableHandler? breakable = null;
            await using OidcTestHost host = await StartShortLifetimeHostAsync(
                capturing,
                configure: configurator =>
                {
                    Action<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>? wiring =
                        configurator.OpenIdConnect;
                    configurator.OpenIdConnect = oidc =>
                    {
                        wiring?.Invoke(oidc);
                        breakable = new BreakableHandler(oidc.BackchannelHttpHandler!);
                        oidc.BackchannelHttpHandler = breakable;
                    };
                },
                afterRegistration: (builder, _) => builder.Logging.AddProvider(logs),
                logLevel: "Information");
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            string accessTokenValue = await ReadStoredTokenAsync(agent, "accessToken");
            int requestsBefore = capturing.RequestCount;

            // Act
            breakable!.Broken = true;
            await WaitUntilExpiredAsync(accessTokenValue);
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog-caught"));
            string body = await response.Content.ReadAsStringAsync();

            int failureLogCount = logs.Entries.Count(static entry => entry.Level == LogLevel.Error
                && entry.Message.Contains("No user access token could be produced", StringComparison.Ordinal));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(body, Does.StartWith("failed:"));
                Assert.That(capturing.RequestCount, Is.EqualTo(requestsBefore));
                Assert.That(failureLogCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ConcurrentRequestsAcrossExpiry_StillProduceExactlyOneRefreshGrant()
        {
            // Arrange — the stampede control that is exactly why ATM was chosen over a hand-rolled
            // OnValidatePrincipal refresh
            CapturingPrimaryHandler capturing = new();
            await using OidcTestHost host = await StartShortLifetimeHostAsync(capturing);
            using BrowserlessUserAgent agent = host.CreateAgent();
            using HttpResponseMessage signedIn = await agent.SignInAsync(
                new Uri(OidcTestHost.AppBase, "protected/page"),
                OidcTestHost.Username,
                OidcTestHost.Password);
            string accessTokenValue = await ReadStoredTokenAsync(agent, "accessToken");
            int refreshCountBefore = host.IdentityProvider.RefreshTokenRequestCount;

            // Act — two parallel calls at the expiry boundary
            await WaitUntilExpiredAsync(accessTokenValue);
            Task<HttpResponseMessage> firstCall =
                agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            Task<HttpResponseMessage> secondCall =
                agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/call-catalog"));
            await Task.WhenAll(firstCall, secondCall);
            using HttpResponseMessage firstResponse = firstCall.Result;
            using HttpResponseMessage secondResponse = secondCall.Result;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(host.IdentityProvider.RefreshTokenRequestCount, Is.EqualTo(refreshCountBefore + 1));
            });
        }

        /// <summary>
        /// Starts the fixture with a short-lived access token at the provider, the ATM
        /// refresh-before-expiry buffer zeroed (plan-level pick 2), and the flagged Catalog client.
        /// </summary>
        private static Task<OidcTestHost> StartShortLifetimeHostAsync(
            CapturingPrimaryHandler capturing,
            int accessTokenLifetimeSeconds = 2,
            int? refreshTokenLifetimeSeconds = null,
            Action<CloudstrapOpenIdConnectConfigurator>? configure = null,
            Action<WebApplicationBuilder, Cloudstrap.TestIdentityProvider.TestIdentityProviderHost>? afterRegistration = null,
            string logLevel = "Warning")
        {
            Dictionary<string, string?> configuration = OidcTestHost.DefaultConfig();
            configuration["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "https://catalog.contoso.example/";
            configuration["Cloudstrap:HttpClients:Catalog:AddUserAccessToken"] = "true";
            configuration["Logging:LogLevel:Default"] = logLevel;

            return OidcTestHost.StartAsync(
                configuration: configuration,
                configure: configurator =>
                {
                    configurator.TokenManagement = static tokenManagement =>
                        tokenManagement.RefreshBeforeExpiration = TimeSpan.Zero;
                    configure?.Invoke(configurator);
                },
                configureIdentityProvider: options =>
                {
                    options.AccessTokenLifetime = TimeSpan.FromSeconds(accessTokenLifetimeSeconds);
                    if (refreshTokenLifetimeSeconds is not null)
                    {
                        options.RefreshTokenLifetime = TimeSpan.FromSeconds(refreshTokenLifetimeSeconds.Value);
                    }
                },
                afterRegistration: (builder, identityProvider) =>
                {
                    builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                        .ConfigurePrimaryHttpMessageHandler(() => capturing);
                    afterRegistration?.Invoke(builder, identityProvider);
                },
                mapEndpoints: static app =>
                {
                    app.MapGet("/protected/call-catalog", async (ICatalogClient catalog) =>
                    {
                        using HttpResponseMessage response =
                            await catalog.Client.GetAsync(new Uri("data", UriKind.Relative));

                        return Results.Text(await response.Content.ReadAsStringAsync());
                    });
                    app.MapGet("/protected/call-catalog-caught", async (ICatalogClient catalog) =>
                    {
                        try
                        {
                            using HttpResponseMessage response =
                                await catalog.Client.GetAsync(new Uri("data", UriKind.Relative));

                            return Results.Text("sent:" + response.StatusCode);
                        }
                        catch (InvalidOperationException exception)
                        {
                            return Results.Text("failed:" + exception.Message, statusCode: 500);
                        }
                    });
                });
        }

        /// <summary>
        /// Waits — bounded, not sleep-flaky — until the given JWT is really expired <em>as the token
        /// manager sees it</em>: the ticket's stored <c>expires_at</c> is stamped when the handler
        /// processes the token response, up to a second after the JWT's own <c>exp</c>, so the margin
        /// covers that skew.
        /// </summary>
        private static async Task WaitUntilExpiredAsync(string jwtValue, double extraSeconds = 1.5)
        {
            DateTime validTo = new JsonWebTokenHandler().ReadJsonWebToken(jwtValue).ValidTo;
            DateTime deadline = validTo.AddSeconds(extraSeconds);

            while (DateTime.UtcNow < deadline)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                Assert.That(
                    remaining,
                    Is.LessThanOrEqualTo(TimeSpan.FromSeconds(6)),
                    "The wait for expiry must stay bounded — shorten the configured lifetime.");
                await Task.Delay(remaining < TimeSpan.FromMilliseconds(200)
                    ? remaining
                    : TimeSpan.FromMilliseconds(200));
            }
        }

        private static Task<string> ReadStoredAccessTokenAsync(BrowserlessUserAgent agent) =>
            ReadStoredTokenAsync(agent, "accessToken");

        private static async Task<string> ReadStoredTokenAsync(BrowserlessUserAgent agent, string property)
        {
            using HttpResponseMessage response =
                await agent.GetAsync(new Uri(OidcTestHost.AppBase, "protected/tokens"));
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            return document.RootElement.GetProperty(property).GetString()!;
        }
    }
}
