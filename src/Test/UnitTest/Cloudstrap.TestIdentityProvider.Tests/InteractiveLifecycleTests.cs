namespace Cloudstrap.TestIdentityProvider.Tests
{
    using System.Net;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Microsoft.IdentityModel.JsonWebTokens;
    using Microsoft.IdentityModel.Tokens;
    using NUnit.Framework;

    /// <summary>
    /// D-4: the interactive session has a whole lifecycle — a refresh grant renews the access token on
    /// a short, configurable lifetime, <c>/connect/userinfo</c> returns the configured claim set, and
    /// <c>/connect/logout</c> ends the session and returns only to a registered address. These are the
    /// levers AC-OIDC4 and AC-OIDC5 pull.
    /// </summary>
    [TestFixture]
    public sealed class InteractiveLifecycleTests
    {
        private const string _clientId = "contoso-web";
        private const string _clientSecret = "placeholder-not-a-real-secret";
        private const string _redirectUri = "https://app.example.com/signin-oidc";
        private const string _postLogoutRedirectUri = "https://app.example.com/signed-out";
        private const string _username = "contoso.user";
        private const string _password = "placeholder-not-a-real-password";
        private const string _requestedScope = "openid profile offline_access catalog.read";

        [Test]
        public async Task RefreshGrant_ExchangesTheRefreshTokenForANewAccessToken()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            using JsonDocument tokens = await SignInAndExchangeCodeAsync(host, client);
            string originalAccessToken = tokens.RootElement.GetProperty("access_token").GetString()!;
            string refreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!;
            JsonWebTokenHandler handler = new();
            JsonWebToken originalJwt = handler.ReadJsonWebToken(originalAccessToken);
            int refreshCountBefore = host.RefreshTokenRequestCount;

            // Act — wait one JWT-exp granule so the renewed expiry is observably later
            await Task.Delay(TimeSpan.FromSeconds(1.1));
            using HttpResponseMessage response = await client.PostAsync(
                host.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["refresh_token"] = refreshToken,
                }));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            string renewedAccessToken = document.RootElement.GetProperty("access_token").GetString()!;
            JsonWebToken renewedJwt = handler.ReadJsonWebToken(renewedAccessToken);

            // Assert — a different access token with a later exp, and exactly one refresh grant counted
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(renewedAccessToken, Is.Not.EqualTo(originalAccessToken));
                Assert.That(renewedJwt.ValidTo, Is.GreaterThan(originalJwt.ValidTo));
                Assert.That(host.RefreshTokenRequestCount, Is.EqualTo(refreshCountBefore + 1));
            });
        }

        [Test]
        public async Task AccessTokenLifetime_Configured_DrivesTheInteractiveTokenExpiry()
        {
            // Arrange — the lever plan-level pick 2 pulls, proven at the provider before the package
            // depends on it
            using TestIdentityProviderHost host = StartInteractiveHost(static options =>
                options.AccessTokenLifetime = TimeSpan.FromSeconds(2));
            using HttpClient client = host.CreateClient();

            // Act
            using JsonDocument tokens = await SignInAndExchangeCodeAsync(host, client);
            JsonWebToken jwt = new JsonWebTokenHandler()
                .ReadJsonWebToken(tokens.RootElement.GetProperty("access_token").GetString());

            // Assert — the token's own exp − iat is exactly the configured lifetime; expires_in is the
            // remaining time at response-writing granularity
            Assert.Multiple(() =>
            {
                Assert.That(jwt.ValidTo - jwt.IssuedAt, Is.EqualTo(TimeSpan.FromSeconds(2)));
                Assert.That(
                    tokens.RootElement.GetProperty("expires_in").GetInt32(),
                    Is.GreaterThanOrEqualTo(1).And.LessThanOrEqualTo(2));
            });
        }

        [Test]
        public async Task RefreshTokenLifetime_Configured_ExpiresTheRefreshToken()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost(static options =>
                options.RefreshTokenLifetime = TimeSpan.FromSeconds(1));
            using HttpClient client = host.CreateClient();
            using JsonDocument tokens = await SignInAndExchangeCodeAsync(host, client);
            string refreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!;

            // Act — wait past the refresh token's lifetime
            await Task.Delay(TimeSpan.FromSeconds(2));
            using HttpResponseMessage response = await client.PostAsync(
                host.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["refresh_token"] = refreshToken,
                }));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            // Assert — standards-shaped failure, nothing issued
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(document.RootElement.TryGetProperty("error", out _), Is.True);
                Assert.That(document.RootElement.TryGetProperty("access_token", out _), Is.False);
            });
        }

        [Test]
        public async Task UserInfo_WithAValidAccessToken_ReturnsTheConfiguredClaimSet()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            using JsonDocument tokens = await SignInAndExchangeCodeAsync(host, client);
            string accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;

            // Act
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                new Uri("connect/userinfo", UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using HttpResponseMessage response = await client.SendAsync(request);
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            // Assert — sub plus exactly the client's UserInfo claims; the IdToken-only claim does not
            // leak here (the destination split is real)
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(document.RootElement.GetProperty("sub").GetString(), Is.EqualTo(_username));
                Assert.That(document.RootElement.GetProperty("favorite_color").GetString(), Is.EqualTo("green"));
                Assert.That(document.RootElement.TryGetProperty("locale", out _), Is.False);
                Assert.That(document.RootElement.TryGetProperty("name", out _), Is.False);
            });
        }

        [Test]
        public async Task UserInfo_WithoutAToken_IsRejected()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();

            // Act
            using HttpResponseMessage response =
                await client.GetAsync(new Uri("connect/userinfo", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — 401, nothing echoed
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(body, Is.Empty);
            });
        }

        [Test]
        public async Task EndSession_ClearsTheSessionAndRedirectsToARegisteredPostLogoutUri()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            (string code, string cookieHeader, string verifier) = await SignInAndGetCodeAsync(client);
            using JsonDocument tokens = await ExchangeCodeAsync(host, client, code, verifier);
            string idToken = tokens.RootElement.GetProperty("id_token").GetString()!;

            // Act — RP-initiated logout carrying the id_token_hint and a registered post-logout URI
            using HttpRequestMessage logoutRequest = new(
                HttpMethod.Get,
                new Uri(
                    "connect/logout"
                    + "?id_token_hint=" + Uri.EscapeDataString(idToken)
                    + "&post_logout_redirect_uri=" + Uri.EscapeDataString(_postLogoutRedirectUri)
                    + "&client_id=" + Uri.EscapeDataString(_clientId),
                    UriKind.Relative));
            logoutRequest.Headers.Add("Cookie", cookieHeader);
            using HttpResponseMessage logoutResponse = await client.SendAsync(logoutRequest);

            string sessionCookieDeletion = logoutResponse.Headers.TryGetValues("Set-Cookie", out var setCookies)
                ? string.Join("\n", setCookies)
                : string.Empty;

            // The browser deleted the session cookie, so a fresh authorization request carries none
            (_, string challenge) = CreatePkcePair();
            using HttpResponseMessage freshAuthorize = await client.GetAsync(BuildAuthorizeUri(challenge));
            string freshHtml = await freshAuthorize.Content.ReadAsStringAsync();

            // Assert — the session really ended and the browser was sent to the registered address
            Assert.Multiple(() =>
            {
                Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.Found));
                Assert.That(
                    logoutResponse.Headers.Location?.GetLeftPart(UriPartial.Path),
                    Is.EqualTo(_postLogoutRedirectUri));
                Assert.That(sessionCookieDeletion, Does.Contain(".TestIdentityProvider.Session=;"));
                Assert.That(freshAuthorize.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(freshHtml, Does.Contain("name=\"username\""));
            });
        }

        [Test]
        public async Task EndSession_WithAnUnregisteredPostLogoutUri_DoesNotRedirectThere()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            (string code, string cookieHeader, string verifier) = await SignInAndGetCodeAsync(client);
            using JsonDocument tokens = await ExchangeCodeAsync(host, client, code, verifier);
            string idToken = tokens.RootElement.GetProperty("id_token").GetString()!;

            // Act
            using HttpRequestMessage logoutRequest = new(
                HttpMethod.Get,
                new Uri(
                    "connect/logout"
                    + "?id_token_hint=" + Uri.EscapeDataString(idToken)
                    + "&post_logout_redirect_uri=" + Uri.EscapeDataString("https://evil.example.com/out")
                    + "&client_id=" + Uri.EscapeDataString(_clientId),
                    UriKind.Relative));
            logoutRequest.Headers.Add("Cookie", cookieHeader);
            using HttpResponseMessage response = await client.SendAsync(logoutRequest);

            // Assert — the whitelist holds: the browser is never sent to the unregistered address
            Assert.That(
                response.Headers.Location?.AbsoluteUri,
                Is.Null.Or.Not.StartsWith("https://evil.example.com"));
        }

        [Test]
        public async Task Discovery_AdvertisesTheUserInfoAndEndSessionEndpoints()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();

            // Act
            using HttpResponseMessage response =
                await client.GetAsync(new Uri(".well-known/openid-configuration", UriKind.Relative));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            // Assert — a relying party discovers both endpoints exactly as it would at any conformant
            // provider (AC-OIDC5's precondition)
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(document.RootElement.TryGetProperty("userinfo_endpoint", out _), Is.True);
                Assert.That(document.RootElement.TryGetProperty("end_session_endpoint", out _), Is.True);
            });
        }

        private static TestIdentityProviderHost StartInteractiveHost(
            Action<TestIdentityProviderOptions>? mutate = null) =>
            TestIdentityProviderHost.StartInProcess(options =>
            {
                TestIdentityProviderClient client = new()
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret,
                    Scopes = { "catalog.read" },
                    Audiences = { "contoso-api" },
                    RedirectUris = { new Uri(_redirectUri) },
                    PostLogoutRedirectUris = { new Uri(_postLogoutRedirectUri) },
                };
                client.TokenClaims.IdToken["locale"] = ["en-BE"];
                client.TokenClaims.UserInfo["favorite_color"] = ["green"];
                options.Clients.Add(client);
                options.Users.Add(new TestIdentityProviderUser
                {
                    Username = _username,
                    Password = _password,
                    Claims =
                    {
                        ["name"] = ["Contoso User"],
                        ["role"] = ["tester"],
                    },
                });
                mutate?.Invoke(options);
            });

        /// <summary>
        /// Drives the interactive round trip through the login form to a code, returning the code, the
        /// session cookie header a browser would keep, and the PKCE verifier matching the code.
        /// </summary>
        private static async Task<(string Code, string CookieHeader, string Verifier)> SignInAndGetCodeAsync(
            HttpClient client)
        {
            (string verifier, string challenge) = CreatePkcePair();

            using HttpResponseMessage formResponse = await client.GetAsync(BuildAuthorizeUri(challenge));
            string formHtml = await formResponse.Content.ReadAsStringAsync();

            using HttpResponseMessage loginResponse = await client.PostAsync(
                new Uri("connect/login", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = _username,
                    ["password"] = _password,
                    ["returnUrl"] = ExtractHiddenReturnUrl(formHtml),
                }));
            string cookieHeader = string.Join(
                "; ",
                loginResponse.Headers.GetValues("Set-Cookie").Select(static value => value.Split(';')[0]));

            using HttpRequestMessage authorizeAgain = new(HttpMethod.Get, loginResponse.Headers.Location);
            authorizeAgain.Headers.Add("Cookie", cookieHeader);
            using HttpResponseMessage codeResponse = await client.SendAsync(authorizeAgain);
            string code = GetQueryParameter(codeResponse.Headers.Location!, "code")!;

            return (code, cookieHeader, verifier);
        }

        /// <summary>
        /// Exchanges the authorization code for tokens with its matching PKCE verifier.
        /// </summary>
        private static async Task<JsonDocument> ExchangeCodeAsync(
            TestIdentityProviderHost host,
            HttpClient client,
            string code,
            string verifier)
        {
            using HttpResponseMessage response = await client.PostAsync(
                host.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["code"] = code,
                    ["redirect_uri"] = _redirectUri,
                    ["code_verifier"] = verifier,
                }));
            string json = await response.Content.ReadAsStringAsync();
            Assert.That(
                response.StatusCode,
                Is.EqualTo(HttpStatusCode.OK),
                () => "The code exchange must succeed before the lifecycle can be exercised: " + json);

            return JsonDocument.Parse(json);
        }

        private static async Task<JsonDocument> SignInAndExchangeCodeAsync(
            TestIdentityProviderHost host,
            HttpClient client)
        {
            (string code, _, string verifier) = await SignInAndGetCodeAsync(client);

            return await ExchangeCodeAsync(host, client, code, verifier);
        }

        private static Uri BuildAuthorizeUri(string codeChallenge)
        {
            StringBuilder query = new();
            query.Append("connect/authorize");
            query.Append("?response_type=code");
            query.Append("&client_id=").Append(Uri.EscapeDataString(_clientId));
            query.Append("&redirect_uri=").Append(Uri.EscapeDataString(_redirectUri));
            query.Append("&scope=").Append(Uri.EscapeDataString(_requestedScope));
            query.Append("&state=state-123");
            query.Append("&nonce=nonce-123");
            query.Append("&code_challenge=").Append(Uri.EscapeDataString(codeChallenge));
            query.Append("&code_challenge_method=S256");

            return new Uri(query.ToString(), UriKind.Relative);
        }

        private static (string Verifier, string Challenge) CreatePkcePair()
        {
            string verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            string challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            return (verifier, challenge);
        }

        private static string ExtractHiddenReturnUrl(string html)
        {
            const string marker = "name=\"returnUrl\"";
            int markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(markerIndex, Is.GreaterThanOrEqualTo(0), "The login form must carry the returnUrl field.");

            const string valueMarker = "value=\"";
            int valueIndex = html.IndexOf(valueMarker, markerIndex, StringComparison.Ordinal) + valueMarker.Length;
            int endIndex = html.IndexOf('"', valueIndex);

            return WebUtility.HtmlDecode(html[valueIndex..endIndex]);
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
