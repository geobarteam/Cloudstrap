namespace Cloudstrap.TestIdentityProvider.Tests
{
    using System.Net;
    using System.Text.Json;
    using Microsoft.IdentityModel.JsonWebTokens;
    using Microsoft.IdentityModel.Tokens;
    using NUnit.Framework;

    /// <summary>
    /// D-5: the repo can boot a real, standards-shaped OpenID Connect server in-process and obtain a
    /// verifiable client-credentials JWT from its real discovery document — the capability every #9
    /// acceptance test builds on.
    /// </summary>
    [TestFixture]
    public sealed class DiscoveryAndTokenTests
    {
        private static readonly string[] _clientCredentialsGrantOnly = ["client_credentials"];

        [Test]
        public async Task Discovery_Get_ServesTheDocumentWithClientCredentialsGrantOnly()
        {
            // Arrange
            using TestIdentityProviderHost host = StartHostWithContosoClient();
            using HttpClient client = host.CreateClient();

            // Act
            using HttpResponseMessage response =
                await client.GetAsync(new Uri(".well-known/openid-configuration", UriKind.Relative));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            // Assert — the document advertises exactly the client-credentials grant (no implicit, no
            // hybrid, no code) and the endpoints the acceptance tests rely on
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    document.RootElement.GetProperty("grant_types_supported").EnumerateArray()
                        .Select(element => element.GetString()),
                    Is.EqualTo(_clientCredentialsGrantOnly));
                Assert.That(document.RootElement.TryGetProperty("token_endpoint", out _), Is.True);
                Assert.That(document.RootElement.TryGetProperty("jwks_uri", out _), Is.True);
            });
        }

        [Test]
        public async Task Jwks_Get_ServesASigningKey()
        {
            // Arrange
            using TestIdentityProviderHost host = StartHostWithContosoClient();
            using HttpClient client = host.CreateClient();

            // Act
            string jwksJson = await FetchJwksJsonAsync(client);
            JsonWebKeySet keySet = new(jwksJson);

            // Assert — at least one key, usable for signature verification
            Assert.Multiple(() =>
            {
                Assert.That(keySet.Keys, Is.Not.Empty);
                Assert.That(keySet.GetSigningKeys(), Is.Not.Empty);
            });
        }

        [Test]
        public async Task TokenEndpoint_WithAConfiguredClient_IssuesASignedJwtWithTheConfiguredAudienceAndScope()
        {
            // Arrange
            using TestIdentityProviderHost host = StartHostWithContosoClient();
            using HttpClient client = host.CreateClient();

            // Act
            using HttpResponseMessage response = await client.PostAsync(
                host.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = "contoso-service",
                    ["client_secret"] = "placeholder-not-a-real-secret",
                    ["scope"] = "catalog.read",
                }));
            string tokenJson = await response.Content.ReadAsStringAsync();
            using JsonDocument tokenDocument = JsonDocument.Parse(tokenJson);
            string accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString()!;

            string jwksJson = await FetchJwksJsonAsync(client);
            JsonWebKeySet keySet = new(jwksJson);
            JsonWebTokenHandler handler = new();
            TokenValidationResult validation = await handler.ValidateTokenAsync(
                accessToken,
                new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidAudience = "contoso-api",
                    IssuerSigningKeys = keySet.GetSigningKeys(),
                });
            JsonWebToken jwt = handler.ReadJsonWebToken(accessToken);

            // Assert — a signed, unencrypted JWT whose signature verifies against the fetched JWKS,
            // carrying the host-derived issuer, the configured audience and the granted scope
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(validation.IsValid, Is.True, () => validation.Exception?.ToString() ?? "invalid");
                Assert.That(new Uri(jwt.Issuer), Is.EqualTo(host.BaseAddress));
                Assert.That(jwt.Audiences, Does.Contain("contoso-api"));
                Assert.That(jwt.GetClaim("client_id").Value, Is.EqualTo("contoso-service"));
                Assert.That(jwt.GetClaim("scope").Value, Is.EqualTo("catalog.read"));
            });
        }

        [Test]
        public async Task TwoInstances_AreFullyIsolated()
        {
            // Arrange — two independent in-process providers, each with its own client
            using TestIdentityProviderHost hostAlpha = TestIdentityProviderHost.StartInProcess(options =>
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "contoso-alpha",
                    ClientSecret = "placeholder-alpha-secret",
                    Scopes = { "alpha.read" },
                    Audiences = { "alpha-api" },
                }));
            using TestIdentityProviderHost hostBeta = TestIdentityProviderHost.StartInProcess(options =>
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "contoso-beta",
                    ClientSecret = "placeholder-beta-secret",
                    Scopes = { "beta.read" },
                    Audiences = { "beta-api" },
                }));
            using HttpClient alphaClient = hostAlpha.CreateClient();
            using HttpClient betaClient = hostBeta.CreateClient();

            // Act — alpha's credentials against beta's endpoint (scope-less, which is legal for the
            // client-credentials grant, so the request reaches client authentication rather than being
            // rejected for the foreign scope), plus both JWKS documents
            using HttpResponseMessage crossResponse = await betaClient.PostAsync(
                hostBeta.TokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = "contoso-alpha",
                    ["client_secret"] = "placeholder-alpha-secret",
                }));
            string crossJson = await crossResponse.Content.ReadAsStringAsync();
            using JsonDocument crossDocument = JsonDocument.Parse(crossJson);

            JsonWebKeySet alphaKeys = new(await FetchJwksJsonAsync(alphaClient));
            JsonWebKeySet betaKeys = new(await FetchJwksJsonAsync(betaClient));

            // Assert — a client configured on one instance is unknown to the other, and the ephemeral
            // signing keys are per instance (the spec's isolation edge case)
            Assert.Multiple(() =>
            {
                Assert.That(crossResponse.IsSuccessStatusCode, Is.False);
                Assert.That(
                    crossDocument.RootElement.GetProperty("error").GetString(),
                    Is.EqualTo("invalid_client"));
                Assert.That(
                    alphaKeys.Keys.Select(key => key.Kid),
                    Is.Not.EquivalentTo(betaKeys.Keys.Select(key => key.Kid)),
                    "Each instance must sign with its own ephemeral key.");
            });
        }

        private static TestIdentityProviderHost StartHostWithContosoClient() =>
            TestIdentityProviderHost.StartInProcess(options =>
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "contoso-service",
                    ClientSecret = "placeholder-not-a-real-secret",
                    Scopes = { "catalog.read" },
                    Audiences = { "contoso-api" },
                }));

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
