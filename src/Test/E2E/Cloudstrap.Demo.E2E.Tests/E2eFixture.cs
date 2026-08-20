namespace Cloudstrap.Demo.E2E.Tests
{
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using Cloudstrap.Demo.IdentityProvider;
    using Cloudstrap.TestIdentityProvider;
    using NUnit.Framework;

    /// <summary>
    /// Boots the test identity provider on loopback, then the Api demo host, then the Bff host,
    /// once for the whole E2E assembly — or attaches to an already-running Bff when the
    /// CLOUDSTRAP_E2E_BASEURL environment variable is set (the identity provider and the Api host
    /// are booted by the fixture in either mode) — and exposes the base URLs plus the captured SUT
    /// console output for telemetry assertions.
    /// </summary>
    [SetUpFixture]
    public sealed class E2eFixture
    {
        /// <summary>Base URL used when the fixture launches the SUT itself.</summary>
        public const string DefaultBaseUrl = "http://127.0.0.1:5300";

        /// <summary>
        /// Base URL of the fixture-owned Api demo host — the pure JWT downstream peer the Bff's
        /// UserApi client targets (deliverable #27).
        /// </summary>
        public const string ApiBaseUrl = "http://127.0.0.1:5330";

        /// <summary>
        /// The loopback port of the test identity provider (D-5) the Bff validates against and
        /// acquires from — 5300 is the Bff, 5301–5303 are second instances, 5330 is the Api demo
        /// host, 59999 is the dead port.
        /// </summary>
        public const int IdentityProviderPort = 5310;

        private const string _apiProjectPath = "src/demo/Api/Cloudstrap.Demo.Api.csproj";

        private static SutProcess? _sut;
        private static SutProcess? _api;
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
            // client and its one neutral test user — no new port. The seed is the demo IdP host's
            // helper, so the two hosts can never drift apart; the fixture keeps its own loopback
            // host for the token-request counter.
            _identityProvider = TestIdentityProviderHost.StartLoopback(
                IdentityProviderPort,
                options => TestIdentityProviderSeed.Configure(options, [new Uri(DefaultBaseUrl)]));

            // The Api demo host boots after the IdP (it validates tokens against 5310) and before
            // the Bff (whose UserApi readiness check probes the Api's /healthz). Fixture-owned in
            // attach mode too, like the IdP.
            _api = SutProcess.Start(ApiBaseUrl, projectRelativePath: _apiProjectPath);
            await WaitUntilReadyAsync(ApiBaseUrl + "/healthz", () => _api, "Api demo host");

            string? externalBaseUrl = Environment.GetEnvironmentVariable("CLOUDSTRAP_E2E_BASEURL");
            if (!string.IsNullOrWhiteSpace(externalBaseUrl))
            {
                BaseUrl = externalBaseUrl.TrimEnd('/');
            }
            else
            {
                _sut = SutProcess.Start(DefaultBaseUrl);
            }

            await WaitUntilReadyAsync(BaseUrl + "/", () => _sut, "SUT");
        }

        [OneTimeTearDown]
        public void StopSut()
        {
            _sut?.Dispose();
            _sut = null;
            _api?.Dispose();
            _api = null;
            _identityProvider?.Dispose();
            _identityProvider = null;
        }

        private static async Task WaitUntilReadyAsync(string url, Func<SutProcess?> process, string what)
        {
            using HttpClient client = new HttpClient();
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (process() is { HasExited: true })
                {
                    break;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(new Uri(url));
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
                $"The {what} did not become ready at {url} within 60 seconds. " +
                $"Captured output:{Environment.NewLine}{process()?.CapturedOutput ?? CapturedSutOutput}");
        }
    }
}
