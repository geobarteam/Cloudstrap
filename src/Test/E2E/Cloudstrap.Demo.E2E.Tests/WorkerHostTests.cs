namespace Cloudstrap.Demo.E2E.Tests
{
    using System.Net;
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using NUnit.Framework;

    /// <summary>
    /// The Worker demo app (deliverable #7, D-4): a headless generic host whose five-line
    /// bootstrap serves truthful container probes on port 5350 while a plain
    /// <c>BackgroundService</c> runs — proven over real HTTP with no IdP and no peer host.
    /// </summary>
    [TestFixture]
    public sealed class WorkerHostTests
    {
        private const string _workerBaseUrl = "http://127.0.0.1:5350";

        private const string _workerProjectPath = "src/demo/Worker/Cloudstrap.Demo.Worker.csproj";

        private SutProcess? _workerHost;
        private HttpClient _client = null!;
        private string _sentinelPath = null!;

        [OneTimeSetUp]
        public async Task StartWorkerHostAsync()
        {
            // The outage sentinel is a per-run temp file — the process-external toggle the drill
            // test flips; the path is handed to the host as configuration.
            _sentinelPath = Path.Combine(
                Path.GetTempPath(),
                $"cloudstrap-demo-outage-{Guid.NewGuid():N}.sentinel");

            // A generic host ignores ASPNETCORE_URLS — the port must arrive as configuration; the
            // baseUrl parameter only feeds the fixture's own readiness poller. Since deliverable #14
            // the Worker is a SQL Server messaging node: the SQL override (spec D-3) is forwarded when
            // set, exactly as the fixture forwards it to the Api.
            List<string> arguments =
            [
                "--Cloudstrap:Worker:HealthPort=5350",
                "--Demo:OutageSentinelPath=" + _sentinelPath,
            ];
            string? sqlOverride = Environment.GetEnvironmentVariable("CLOUDSTRAP_TEST_SQL");
            if (!string.IsNullOrWhiteSpace(sqlOverride))
            {
                arguments.Add("--ConnectionStrings:DefaultConnection=" + sqlOverride);
            }

            _workerHost = SutProcess.Start(_workerBaseUrl, arguments, _workerProjectPath);
            _client = new HttpClient { BaseAddress = new Uri(_workerBaseUrl) };
            await WaitUntilReadyAsync(_client, _workerHost);
        }

        [OneTimeTearDown]
        public void StopWorkerHost()
        {
            _client.Dispose();
            _workerHost?.Dispose();
            if (File.Exists(_sentinelPath))
            {
                File.Delete(_sentinelPath);
            }
        }

        [Test]
        public async Task WorkerHost_ReadyFlipsTo503WhileTheOutageSentinelExists_AndRecovers()
        {
            // Arrange — healthy first
            Assert.That(
                (await _client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode,
                Is.EqualTo(HttpStatusCode.OK));

            try
            {
                // Act — the outage drill: create the sentinel, poll the flip (AC-WK4 live through a
                // real orchestrator-style HTTP surface)
                await File.WriteAllTextAsync(_sentinelPath, "outage drill");
                await WaitForStatusAsync("/ready", HttpStatusCode.ServiceUnavailable);

                // Assert — readiness is out while liveness stays in (the tag contract)
                Assert.That(
                    (await _client.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode,
                    Is.EqualTo(HttpStatusCode.OK));
            }
            finally
            {
                // Recovery — and a failed run cannot poison later fixtures
                File.Delete(_sentinelPath);
            }

            await WaitForStatusAsync("/ready", HttpStatusCode.OK);
        }

        [Test]
        public async Task WorkerHost_ProbePolling_ProducesNoTraceSpans()
        {
            // Arrange — capture is proven live first (heartbeat + startup lines present), so the
            // negative assertion below is meaningful.
            string output = await WaitForOutputAsync(
                () => _workerHost!.CapturedOutput,
                text => text.Contains("Demo worker heartbeat", StringComparison.Ordinal));
            Assert.That(output, Does.Contain("Demo worker heartbeat"));

            // Assert — after all the suite's accumulated probe polling, no console-exporter HTTP
            // server span for the probe paths appears: #2's config-driven noise filter covers the
            // worker listener for free because it uses the shared Cloudstrap:HealthChecks paths
            // (the console span shape prints the route/path as 'GET <path>' in Activity.DisplayName
            // and the path in url.path attributes).
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.Not.Contain("GET /healthz"));
                Assert.That(output, Does.Not.Contain("GET /ready"));
                Assert.That(output, Does.Not.Contain("url.path: /healthz"));
                Assert.That(output, Does.Not.Contain("url.path: /ready"));
            });
        }

        private async Task WaitForStatusAsync(string path, HttpStatusCode expected)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            HttpStatusCode last = 0;
            while (DateTime.UtcNow < deadline)
            {
                using HttpResponseMessage response = await _client.GetAsync(new Uri(path, UriKind.Relative));
                last = response.StatusCode;
                if (last == expected)
                {
                    return;
                }

                await Task.Delay(250);
            }

            Assert.Fail($"GET {path} never reached {expected} (last: {last}).");
        }

        [Test]
        public async Task WorkerHost_Probes_AnswerAnonymouslyWithFrameworkBodies()
        {
            // Act — no auth of any kind on the probe port (AC-WK2 live), unknown paths 404 (AC-WK7)
            using HttpResponseMessage healthz = await _client.GetAsync(new Uri("/healthz", UriKind.Relative));
            using HttpResponseMessage ready = await _client.GetAsync(new Uri("/ready", UriKind.Relative));
            using HttpResponseMessage unknown = await _client.GetAsync(new Uri("/not-a-probe", UriKind.Relative));

            // Assert
            Assert.Multiple(async () =>
            {
                Assert.That(healthz.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(await healthz.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
                Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            });
        }

        [Test]
        public async Task WorkerHost_Heartbeat_AndStartupLog_AreCapturedFromStdout()
        {
            // Act — poll the captured console with a deadline (never a bare sleep): the generic
            // host runs a real BackgroundService while probing, and Console-mode observability is
            // live and capturable.
            string output = await WaitForOutputAsync(
                () => _workerHost!.CapturedOutput,
                text => text.Contains("Configuration loaded for", StringComparison.Ordinal)
                    && text.Contains("Demo worker heartbeat", StringComparison.Ordinal));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.Contain("Configuration loaded for"));
                Assert.That(output, Does.Contain("Demo worker heartbeat"));
            });
        }

        private static async Task<string> WaitForOutputAsync(Func<string> captured, Func<string, bool> ready)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            string output = captured();
            while (!ready(output) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                output = captured();
            }

            return output;
        }

        private static async Task WaitUntilReadyAsync(HttpClient client, SutProcess process)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    break;
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
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
                $"The Worker demo host did not become ready at {client.BaseAddress}.{Environment.NewLine}{process.CapturedOutput}");
        }
    }
}
