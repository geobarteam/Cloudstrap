namespace Cloudstrap.TestIdentityProvider.Tests
{
    using System.Net.Sockets;
    using System.Text.Json;
    using Microsoft.IdentityModel.JsonWebTokens;
    using Microsoft.IdentityModel.Tokens;
    using NUnit.Framework;

    /// <summary>
    /// The identity provider as a controllable test double: claims are configuration data (JSON arrays,
    /// no pipe-separator or <c>schemas.</c> conventions), the token lifetime is short and exact, bad
    /// credentials fail standards-shaped, and the loopback mode serves real HTTP for the E2E fixture.
    /// </summary>
    [TestFixture]
    public sealed class ClaimShapingAndErrorTests
    {
        private static readonly string[] _readerWriterRoles = ["reader", "writer"];

        [Test]
        public async Task TokenClaims_CommonAndClientCredentialsSets_LandInTheAccessToken()
        {
            // Arrange — a client whose configured claim sets include a multi-valued claim declared as a
            // JSON array
            using TestIdentityProviderHost host = TestIdentityProviderHost.StartInProcess(options =>
            {
                TestIdentityProviderClient client = new()
                {
                    ClientId = "contoso-service",
                    ClientSecret = "placeholder-not-a-real-secret",
                    Scopes = { "catalog.read" },
                    Audiences = { "contoso-api" },
                };
                client.TokenClaims.Common["department"] = ["logistics"];
                client.TokenClaims.ClientCredentialsToken["role"] = ["reader", "writer"];
                options.Clients.Add(client);
            });
            using HttpClient client2 = host.CreateClient();

            // Act
            string accessToken = await RequestTokenAsync(client2, host.TokenEndpoint, "contoso-service",
                "placeholder-not-a-real-secret", "catalog.read");
            JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
            using JsonDocument payload = JsonDocument.Parse(
                Base64UrlEncoder.Decode(accessToken.Split('.')[1]));

            // Assert — both sets land in the access token, the multi-valued claim as a proper JSON array
            Assert.Multiple(() =>
            {
                Assert.That(jwt.GetClaim("department").Value, Is.EqualTo("logistics"));
                Assert.That(
                    payload.RootElement.GetProperty("role").ValueKind,
                    Is.EqualTo(JsonValueKind.Array),
                    "The multi-valued claim must be a JSON array — the pipe-separator convention is gone.");
                Assert.That(
                    payload.RootElement.GetProperty("role").EnumerateArray().Select(element => element.GetString()),
                    Is.EqualTo(_readerWriterRoles));
            });
        }

        [Test]
        public async Task AccessTokenLifetime_Configured_DrivesTheTokenExpiry()
        {
            // Arrange
            using TestIdentityProviderHost host = TestIdentityProviderHost.StartInProcess(options =>
            {
                options.AccessTokenLifetime = TimeSpan.FromSeconds(120);
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "contoso-service",
                    ClientSecret = "placeholder-not-a-real-secret",
                    Scopes = { "catalog.read" },
                    Audiences = { "contoso-api" },
                });
            });
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
            JsonWebToken jwt = new JsonWebTokenHandler().ReadJsonWebToken(accessToken);

            // Assert — the configured lifetime is exact in the token itself (the lever the AC-CC3
            // renewal test pulls); expires_in is the remaining lifetime at serialization time, so the
            // standard one-second rounding is allowed
            Assert.Multiple(() =>
            {
                Assert.That(
                    jwt.GetPayloadValue<long>("exp") - jwt.GetPayloadValue<long>("iat"),
                    Is.EqualTo(120));
                Assert.That(
                    tokenDocument.RootElement.GetProperty("expires_in").GetInt64(),
                    Is.InRange(119, 120));
            });
        }

        [Test]
        public async Task TokenEndpoint_WithAWrongSecret_ReturnsInvalidClient()
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
                    ["client_secret"] = "placeholder-wrong-secret",
                    ["scope"] = "catalog.read",
                }));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            // Assert — the standards-shaped error payload, and no token issued
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(document.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_client"));
                Assert.That(document.RootElement.TryGetProperty("access_token", out _), Is.False);
            });
        }

        [Test]
        public async Task TokenEndpoint_WithAnUnknownClient_ReturnsInvalidClient()
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
                    ["client_id"] = "contoso-unknown",
                    ["client_secret"] = "placeholder-not-a-real-secret",
                    ["scope"] = "catalog.read",
                }));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            // Assert — the same contract as a wrong secret
            Assert.Multiple(() =>
            {
                Assert.That(response.IsSuccessStatusCode, Is.False);
                Assert.That(document.RootElement.GetProperty("error").GetString(), Is.EqualTo("invalid_client"));
                Assert.That(document.RootElement.TryGetProperty("access_token", out _), Is.False);
            });
        }

        [Test]
        public async Task Loopback_Host_ServesDiscoveryAndTokensOverRealHttp()
        {
            // Arrange — a real Kestrel host on a free loopback port, talked to by a plain HttpClient
            int port = GetFreeLoopbackPort();
            using TestIdentityProviderHost host = TestIdentityProviderHost.StartLoopback(port, options =>
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "contoso-service",
                    ClientSecret = "placeholder-not-a-real-secret",
                    Scopes = { "catalog.read" },
                    Audiences = { "contoso-api" },
                }));
            using HttpClient client = new();

            // Act
            using HttpResponseMessage discoveryResponse = await client.GetAsync(
                new Uri(host.BaseAddress, ".well-known/openid-configuration"));
            string discoveryJson = await discoveryResponse.Content.ReadAsStringAsync();
            using JsonDocument discovery = JsonDocument.Parse(discoveryJson);
            string accessToken = await RequestTokenAsync(client, host.TokenEndpoint, "contoso-service",
                "placeholder-not-a-real-secret", "catalog.read");

            // Assert — real HTTP end to end: the advertised issuer is the bound loopback address and a
            // token request succeeds (the E2E fixture's hosting mode, proven before the fixture needs it)
            Assert.Multiple(() =>
            {
                Assert.That(discoveryResponse.IsSuccessStatusCode, Is.True);
                Assert.That(
                    new Uri(discovery.RootElement.GetProperty("issuer").GetString()!),
                    Is.EqualTo(host.BaseAddress));
                Assert.That(accessToken, Is.Not.Empty);
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

        private static async Task<string> RequestTokenAsync(
            HttpClient client,
            Uri tokenEndpoint,
            string clientId,
            string clientSecret,
            string scope)
        {
            using HttpResponseMessage response = await client.PostAsync(
                tokenEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = scope,
                }));
            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);

            Assert.That(
                response.IsSuccessStatusCode,
                Is.True,
                () => $"Token request failed with {(int)response.StatusCode}: {json}");

            return document.RootElement.GetProperty("access_token").GetString()!;
        }

        private static int GetFreeLoopbackPort()
        {
            using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
            listener.Start();

            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
