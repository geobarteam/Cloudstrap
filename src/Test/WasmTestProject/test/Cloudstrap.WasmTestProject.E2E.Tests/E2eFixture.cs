namespace Cloudstrap.WasmTestProject.E2E.Tests
{
    using Cloudstrap.WasmTestProject.E2E.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// Boots the Bff host once for the whole E2E assembly — or attaches to an already-running
    /// instance when the CLOUDSTRAP_E2E_BASEURL environment variable is set — and exposes the
    /// base URL plus the captured SUT console output for telemetry assertions.
    /// </summary>
    [SetUpFixture]
    public sealed class E2eFixture
    {
        /// <summary>Base URL used when the fixture launches the SUT itself.</summary>
        public const string DefaultBaseUrl = "http://127.0.0.1:5300";

        private static SutProcess? _sut;

        /// <summary>Base URL of the running SUT for this test run.</summary>
        public static string BaseUrl { get; private set; } = DefaultBaseUrl;

        /// <summary>Everything the SUT wrote to stdout/stderr so far (empty in attach mode).</summary>
        public static string CapturedSutOutput => _sut?.CapturedOutput ?? string.Empty;

        [OneTimeSetUp]
        public async Task StartSutAsync()
        {
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
