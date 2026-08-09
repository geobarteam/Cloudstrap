namespace Cloudstrap.TestIdentityProvider.Tests
{
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Microsoft.IdentityModel.JsonWebTokens;
    using Microsoft.IdentityModel.Tokens;
    using NUnit.Framework;

    /// <summary>
    /// D-4: the in-repo identity provider performs a complete, PKCE-enforced authorization-code
    /// sign-in — a real authorization endpoint, a minimal login form, code issuance and the code
    /// exchange for id, access and refresh tokens — with users as declarative configuration. This is
    /// the AC-A1 verification vehicle every remaining #10 step verifies against.
    /// </summary>
    [TestFixture]
    public sealed class AuthorizationCodeFlowTests
    {
        private const string _clientId = "contoso-web";
        private const string _clientSecret = "placeholder-not-a-real-secret";
        private const string _redirectUri = "https://app.example.com/signin-oidc";
        private const string _username = "contoso.user";
        private const string _password = "placeholder-not-a-real-password";
        private const string _requestedScope = "openid profile offline_access catalog.read";

        private static readonly string[] _s256Only = ["S256"];
        private static readonly string[] _clientCredentialsGrantOnly = ["client_credentials"];

        [Test]
        public async Task Discovery_Get_AdvertisesTheAuthorizationCodeAndRefreshGrantsAndRequiresPkce()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();

            // Act
            using HttpResponseMessage response =
                await client.GetAsync(new Uri(".well-known/openid-configuration", UriKind.Relative));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            string?[] grantTypes = [.. document.RootElement.GetProperty("grant_types_supported")
                .EnumerateArray().Select(element => element.GetString())];
            string?[] responseTypes = [.. document.RootElement.GetProperty("response_types_supported")
                .EnumerateArray().Select(element => element.GetString())];

            // Assert — code + refresh arrive next to the existing client-credentials grant; PKCE is
            // S256-only; no implicit or hybrid response type is representable
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(grantTypes, Does.Contain("authorization_code"));
                Assert.That(grantTypes, Does.Contain("refresh_token"));
                Assert.That(grantTypes, Does.Contain("client_credentials"));
                Assert.That(responseTypes, Does.Contain("code"));
                Assert.That(responseTypes, Does.Not.Contain("token"));
                Assert.That(responseTypes, Does.Not.Contain("id_token token"));
                Assert.That(
                    document.RootElement.GetProperty("code_challenge_methods_supported").EnumerateArray()
                        .Select(element => element.GetString()),
                    Is.EqualTo(_s256Only));
                Assert.That(document.RootElement.TryGetProperty("authorization_endpoint", out _), Is.True);
            });
        }

        [Test]
        public async Task Authorize_WithoutASession_ServesTheLoginForm()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            (_, string challenge) = CreatePkcePair();

            // Act
            using HttpResponseMessage response = await client.GetAsync(BuildAuthorizeUri(challenge));
            string html = await response.Content.ReadAsStringAsync();

            // Assert — a 200 HTML login form: no redirect loop, no consent page
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
                Assert.That(html, Does.Contain("name=\"username\""));
                Assert.That(html, Does.Contain("name=\"password\""));
                Assert.That(html, Does.Contain("type=\"submit\""));
                Assert.That(html, Does.Not.Contain("consent"));
            });
        }

        [Test]
        public async Task Authorize_WithValidCredentials_RedirectsToTheClientRedirectUriWithACode()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            (_, string challenge) = CreatePkcePair();

            // Act
            (Uri location, _) = await SignInAndGetCodeRedirectAsync(client, BuildAuthorizeUri(challenge));

            // Assert — the browser lands on the client's registered redirect URI carrying a code and
            // the caller's state
            Assert.Multiple(() =>
            {
                Assert.That(location.GetLeftPart(UriPartial.Path), Is.EqualTo(_redirectUri));
                Assert.That(GetQueryParameter(location, "code"), Is.Not.Null.And.Not.Empty);
                Assert.That(GetQueryParameter(location, "state"), Is.EqualTo("state-123"));
            });
        }

        [Test]
        public async Task TokenEndpoint_WithTheCodeAndVerifier_IssuesIdAccessAndRefreshTokens()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            (string verifier, string challenge) = CreatePkcePair();
            (Uri location, _) = await SignInAndGetCodeRedirectAsync(client, BuildAuthorizeUri(challenge));
            string code = GetQueryParameter(location, "code")!;

            // Act
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
            string tokenJson = await response.Content.ReadAsStringAsync();
            using JsonDocument tokenDocument = JsonDocument.Parse(tokenJson);

            string idToken = tokenDocument.RootElement.GetProperty("id_token").GetString()!;
            string accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString()!;
            JsonWebTokenHandler handler = new();
            JsonWebToken idJwt = handler.ReadJsonWebToken(idToken);

            JsonWebKeySet keySet = new(await FetchJwksJsonAsync(client));
            TokenValidationResult accessValidation = await handler.ValidateTokenAsync(
                accessToken,
                new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidAudience = "contoso-api",
                    IssuerSigningKeys = keySet.GetSigningKeys(),
                });
            JsonWebToken accessJwt = handler.ReadJsonWebToken(accessToken);

            // Assert — id, access and refresh tokens; the id token carries the configured user, the
            // IdToken claim set and the nonce; the access token is an unencrypted, signature-verifiable
            // JWT carrying the client's audiences and granted scopes
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    tokenDocument.RootElement.TryGetProperty("refresh_token", out JsonElement refreshToken)
                        && !string.IsNullOrEmpty(refreshToken.GetString()),
                    Is.True,
                    "A refresh token must be issued for the offline_access scope.");
                Assert.That(idJwt.Subject, Is.EqualTo(_username));
                Assert.That(idJwt.GetClaim("locale").Value, Is.EqualTo("en-BE"));
                Assert.That(idJwt.GetClaim("nonce").Value, Is.EqualTo("nonce-123"));
                Assert.That(
                    accessValidation.IsValid,
                    Is.True,
                    () => accessValidation.Exception?.ToString() ?? "invalid");
                Assert.That(accessJwt.Subject, Is.EqualTo(_username));
                Assert.That(accessJwt.Audiences, Does.Contain("contoso-api"));
                Assert.That(accessJwt.GetClaim("scope").Value, Does.Contain("catalog.read"));
            });
        }

        [Test]
        public async Task TokenEndpoint_WithAMismatchedCodeVerifier_IsRejected()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            (_, string challenge) = CreatePkcePair();
            (string mismatchedVerifier, _) = CreatePkcePair();
            (Uri location, _) = await SignInAndGetCodeRedirectAsync(client, BuildAuthorizeUri(challenge));
            string code = GetQueryParameter(location, "code")!;

            // Act
            using HttpResponseMessage response = await client.PostAsync(
                host.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["code"] = code,
                    ["redirect_uri"] = _redirectUri,
                    ["code_verifier"] = mismatchedVerifier,
                }));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            // Assert — standards-shaped error; no token issued
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(document.RootElement.TryGetProperty("error", out _), Is.True);
                Assert.That(document.RootElement.TryGetProperty("access_token", out _), Is.False);
            });
        }

        [Test]
        public async Task Authorize_WithoutACodeChallenge_IsRejected()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();

            // Act — a code request with no code_challenge at all
            using HttpResponseMessage response = await client.GetAsync(
                BuildAuthorizeUri(codeChallenge: null));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — PKCE is required for every code-flow client, always: the provider rejects the
            // request outright with a standards-shaped invalid_request error and issues no code
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(response.Headers.Location, Is.Null);
                Assert.That(body, Does.Contain("invalid_request"));
                Assert.That(body, Does.Not.Contain("code="));
            });
        }

        [Test]
        public async Task Authorize_WithAnUnregisteredRedirectUri_IsRejected()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            (_, string challenge) = CreatePkcePair();

            // Act
            using HttpResponseMessage response = await client.GetAsync(
                BuildAuthorizeUri(challenge, redirectUri: "https://evil.example.com/callback"));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — the client's RedirectUris list is the whitelist: no redirect anywhere, no code
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(response.Headers.Location, Is.Null);
                Assert.That(body, Does.Not.Contain("code="));
            });
        }

        [Test]
        public async Task WrongPassword_ReRendersTheFormAndIssuesNoCode()
        {
            // Arrange
            using TestIdentityProviderHost host = StartInteractiveHost();
            using HttpClient client = host.CreateClient();
            (_, string challenge) = CreatePkcePair();
            using HttpResponseMessage formResponse = await client.GetAsync(BuildAuthorizeUri(challenge));
            string formHtml = await formResponse.Content.ReadAsStringAsync();

            // Act
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("connect/login", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = _username,
                    ["password"] = "wrong-placeholder-password",
                    ["returnUrl"] = ExtractHiddenReturnUrl(formHtml),
                }));
            string html = await response.Content.ReadAsStringAsync();

            // Assert — the form is re-rendered; no session cookie is issued, so no code can follow
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(html, Does.Contain("name=\"username\""));
                Assert.That(response.Headers.Contains("Set-Cookie"), Is.False);
            });
        }

        [Test]
        public async Task ClientCredentialsClient_WithNoRedirectUris_StillBehavesExactlyAsBefore()
        {
            // Arrange — the #9 regression guard: a client-credentials-only configuration
            using TestIdentityProviderHost host = TestIdentityProviderHost.StartInProcess(options =>
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "contoso-service",
                    ClientSecret = _clientSecret,
                    Scopes = { "catalog.read" },
                    Audiences = { "contoso-api" },
                }));
            using HttpClient client = host.CreateClient();

            // Act — the discovery-driven token request #9 makes
            using HttpResponseMessage discoveryResponse =
                await client.GetAsync(new Uri(".well-known/openid-configuration", UriKind.Relative));
            string discoveryJson = await discoveryResponse.Content.ReadAsStringAsync();
            using JsonDocument discoveryDocument = JsonDocument.Parse(discoveryJson);
            Uri tokenEndpoint = new(discoveryDocument.RootElement.GetProperty("token_endpoint").GetString()!);

            using HttpResponseMessage tokenResponse = await client.PostAsync(
                tokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = "contoso-service",
                    ["client_secret"] = _clientSecret,
                    ["scope"] = "catalog.read",
                }));
            string tokenJson = await tokenResponse.Content.ReadAsStringAsync();
            using JsonDocument tokenDocument = JsonDocument.Parse(tokenJson);

            // Assert — the token request succeeds and the discovery document still advertises exactly
            // the client-credentials grant: the new grants only appear when interactive clients or
            // users are configured
            Assert.Multiple(() =>
            {
                Assert.That(tokenResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    tokenDocument.RootElement.TryGetProperty("access_token", out JsonElement accessToken)
                        && !string.IsNullOrEmpty(accessToken.GetString()),
                    Is.True);
                Assert.That(
                    discoveryDocument.RootElement.GetProperty("grant_types_supported").EnumerateArray()
                        .Select(element => element.GetString()),
                    Is.EqualTo(_clientCredentialsGrantOnly));
            });
        }

        private static TestIdentityProviderHost StartInteractiveHost() =>
            TestIdentityProviderHost.StartInProcess(options =>
            {
                TestIdentityProviderClient client = new()
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret,
                    Scopes = { "catalog.read" },
                    Audiences = { "contoso-api" },
                    RedirectUris = { new Uri(_redirectUri) },
                };
                client.TokenClaims.IdToken["locale"] = ["en-BE"];
                client.TokenClaims.AccessToken["department"] = ["logistics"];
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
            });

        /// <summary>
        /// Drives the interactive round trip up to the code redirect: GET the authorization endpoint
        /// (login form), POST the credentials, follow the post-login redirect with the session cookie.
        /// </summary>
        private static async Task<(Uri Location, string CookieHeader)> SignInAndGetCodeRedirectAsync(
            HttpClient client,
            Uri authorizeUri)
        {
            using HttpResponseMessage formResponse = await client.GetAsync(authorizeUri);
            string formHtml = await formResponse.Content.ReadAsStringAsync();

            using HttpResponseMessage loginResponse = await client.PostAsync(
                new Uri("connect/login", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = _username,
                    ["password"] = _password,
                    ["returnUrl"] = ExtractHiddenReturnUrl(formHtml),
                }));
            Assert.That(
                loginResponse.StatusCode,
                Is.EqualTo(HttpStatusCode.Found),
                "Posting valid credentials must redirect back to the authorization endpoint.");

            string cookieHeader = string.Join(
                "; ",
                loginResponse.Headers.GetValues("Set-Cookie").Select(static value => value.Split(';')[0]));

            using HttpRequestMessage authorizeAgain = new(HttpMethod.Get, loginResponse.Headers.Location);
            authorizeAgain.Headers.Add("Cookie", cookieHeader);
            using HttpResponseMessage codeResponse = await client.SendAsync(authorizeAgain);
            Assert.That(
                codeResponse.StatusCode,
                Is.EqualTo(HttpStatusCode.Found),
                "The authenticated authorization request must redirect to the client.");

            return (codeResponse.Headers.Location!, cookieHeader);
        }

        private static Uri BuildAuthorizeUri(
            string? codeChallenge,
            string redirectUri = _redirectUri,
            string state = "state-123",
            string nonce = "nonce-123")
        {
            StringBuilder query = new();
            query.Append("connect/authorize");
            query.Append("?response_type=code");
            query.Append("&client_id=").Append(Uri.EscapeDataString(_clientId));
            query.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
            query.Append("&scope=").Append(Uri.EscapeDataString(_requestedScope));
            query.Append("&state=").Append(Uri.EscapeDataString(state));
            query.Append("&nonce=").Append(Uri.EscapeDataString(nonce));

            if (codeChallenge is not null)
            {
                query.Append("&code_challenge=").Append(Uri.EscapeDataString(codeChallenge));
                query.Append("&code_challenge_method=S256");
            }

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

        private static async Task<string> FetchJwksJsonAsync(HttpClient client)
        {
            using HttpResponseMessage discoveryResponse =
                await client.GetAsync(new Uri(".well-known/openid-configuration", UriKind.Relative));
            string discoveryJson = await discoveryResponse.Content.ReadAsStringAsync();
            using JsonDocument discoveryDocument = JsonDocument.Parse(discoveryJson);
            Uri jwksUri = new(discoveryDocument.RootElement.GetProperty("jwks_uri").GetString()!);

            using HttpResponseMessage jwksResponse = await client.GetAsync(jwksUri);

            return await jwksResponse.Content.ReadAsStringAsync();
        }
    }
}
