namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using Cloudstrap.TestIdentityProvider;
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Boots the test identity provider on loopback and then the Bff host, once for the whole E2E
    /// assembly — or attaches to an already-running Bff when the CLOUDSTRAP_E2E_BASEURL environment
    /// variable is set (the identity provider is booted by the fixture in either mode) — and exposes
    /// the base URL plus the captured SUT console output for telemetry assertions.
    /// </summary>
    [SetUpFixture]
    public sealed class E2eFixture
    {
        /// <summary>Base URL used when the fixture launches the SUT itself.</summary>
        public const string DefaultBaseUrl = "http://127.0.0.1:5300";

        /// <summary>
        /// The loopback port of the test identity provider (D-5) the Bff validates against and
        /// acquires from — 5300 is the Bff, 5301–5303 are second instances, 59999 is the dead port.
        /// </summary>
        public const int IdentityProviderPort = 5310;

        private static SutProcess? _sut;
        private static TestIdentityProviderHost? _identityProvider;

        /// <summary>Base URL of the running SUT for this test run.</summary>
        public static string BaseUrl { get; private set; } = DefaultBaseUrl;

        /// <summary>Everything the SUT wrote to stdout/stderr so far (empty in attach mode).</summary>
        public static string CapturedSutOutput => _sut?.CapturedOutput ?? string.Empty;

        /// <summary>
        /// The number of token requests the fixture-hosted identity provider has served — the hit
        /// counter the caching E2E test asserts against.
        /// </summary>
        public static int IdentityProviderTokenRequestCount => _identityProvider?.TokenRequestCount ?? 0;

        [OneTimeSetUp]
        public async Task StartSutAsync()
        {
            // The identity provider boots first — the Bff acquires machine tokens from it and
            // validates them against its discovery document. Booted in attach mode too. The same
            // provider serves the #9 machine client and, since deliverable #10, the interactive web
            // client and its one neutral test user — no new port.
            _identityProvider = TestIdentityProviderHost.StartLoopback(IdentityProviderPort, options =>
            {
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "wasmtestproject-bff",
                    ClientSecret = "local-e2e-placeholder-secret",
                    Scopes = { "selfapi" },
                    Audiences = { "wasmtestproject-selfapi" },
                });
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = "wasmtestproject-web",
                    ClientSecret = "local-e2e-placeholder-secret-web",
                    Scopes = { "selfapi" },
                    Audiences = { "wasmtestproject-selfapi" },
                    RedirectUris = { new Uri(DefaultBaseUrl + "/signin-oidc") },
                    PostLogoutRedirectUris = { new Uri(DefaultBaseUrl + "/signout-callback-oidc") },
                });
                options.Users.Add(new TestIdentityProviderUser
                {
                    Username = "wasmtestproject.user",
                    Password = "local-e2e-placeholder-password",
                    Claims =
                    {
                        ["name"] = ["Wasm Test User"],
                        ["role"] = ["tester"],
                    },
                });
            });

            string? externalBaseUrl = Environment.GetEnvironmentVariable("CLOUDSTRAP_E2E_BASEURL");
            if (!string.IsNullOrWhiteSpace(externalBaseUrl))
            {
                BaseUrl = externalBaseUrl.TrimEnd('/');
            }
            else
            {
                _sut = SutProcess.Start(DefaultBaseUrl);
            }

            await WaitUntilReadyAsync();
        }

        [OneTimeTearDown]
        public void StopSut()
        {
            _sut?.Dispose();
            _sut = null;
            _identityProvider?.Dispose();
            _identityProvider = null;
        }

        private static async Task WaitUntilReadyAsync()
        {
            using HttpClient client = new HttpClient();
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (_sut is { HasExited: true })
                {
                    break;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(new Uri(BaseUrl + "/"));
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet — keep polling until the deadline.
                }

                await Task.Delay(250);
            }

            throw new InvalidOperationException(
                $"The SUT did not become ready at {BaseUrl} within 60 seconds. " +
                $"Captured output:{Environment.NewLine}{CapturedSutOutput}");
        }
    }
}
