namespace Cloudstrap.Demo.E2E.Tests
{
    using System.Net;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using Cloudstrap.Demo.E2E.Tests.Infrastructure;
    using Microsoft.Playwright;
    using NUnit.Framework;

    /// <summary>
    /// Deliverable #16 live (AC-HF21, D-5): the Worker demo host is the Hangfire processing host — it schedules
    /// <c>DemoOrderCountRecurringTask</c> at startup and serves a ready-tagged storage check — and the
    /// BlazorServer demo host is a storage-only host whose authenticated dashboard shows the Worker's job to
    /// the signed-in <c>tester</c> and triggers it, the separate Worker process running it. The Worker is
    /// self-booted here on health port 5353 (5350 belongs to <see cref="WorkerHostTests"/>, 5351 to
    /// <see cref="MessagingTests"/>, 5352 to <see cref="ClaimCheckTests"/>); the BlazorServer on 5340, which
    /// <see cref="BlazorServerTests"/> also uses — the fixtures never overlap (the assembly is non-parallel).
    /// </summary>
    [TestFixture]
    public sealed class HangfireTests : PageTestBase
    {
        private const string _workerBaseUrl = "http://127.0.0.1:5353";
        private const string _workerProjectPath = "src/demo/Worker/Cloudstrap.Demo.Worker.csproj";
        private const string _blazorServerBaseUrl = "http://127.0.0.1:5340";
        private const string _blazorServerProjectPath = "src/demo/BlazorServer/Cloudstrap.Demo.BlazorServer.csproj";
        private const string _demoTaskId = "DemoOrderCountRecurringTask";
        private const string _completedLine = "Recurring task DemoOrderCountRecurringTask completed";
        private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(30);

        private SutProcess? _workerHost;
        private SutProcess? _blazorServerHost;
        private string _sentinelPath = null!;

        [OneTimeSetUp]
        public async Task StartHostsAsync()
        {
            _sentinelPath = Path.Combine(Path.GetTempPath(), $"cloudstrap-demo-hangfire-{Guid.NewGuid():N}.sentinel");

            List<string> arguments =
            [
                "--Cloudstrap:Worker:HealthPort=5353",
                "--Demo:OutageSentinelPath=" + _sentinelPath,
            ];
            DemoDatabase.AddConnectionStringOverride(arguments);

            string? blobOverride = Environment.GetEnvironmentVariable(AzuriteProcess.EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(blobOverride))
            {
                arguments.Add("--Cloudstrap:Storage:ConnectionString=" + blobOverride);
            }

            _workerHost = SutProcess.Start(_workerBaseUrl, arguments, _workerProjectPath);
            using HttpClient probe = new HttpClient { BaseAddress = new Uri(_workerBaseUrl) };
            await WaitUntilHealthyAsync(probe, _workerHost, "/healthz");

            // The storage-only dashboard host, on the same database (and SQL override) as the Worker.
            List<string> blazorArguments = [];
            DemoDatabase.AddConnectionStringOverride(blazorArguments);

            _blazorServerHost = SutProcess.Start(_blazorServerBaseUrl, blazorArguments, _blazorServerProjectPath);
            using HttpClient blazorProbe = new HttpClient { BaseAddress = new Uri(_blazorServerBaseUrl) };
            await WaitUntilHealthyAsync(blazorProbe, _blazorServerHost, "/healthz");
        }

        [OneTimeTearDown]
        public void StopHosts()
        {
            _blazorServerHost?.Dispose();
            _workerHost?.Dispose();
            if (File.Exists(_sentinelPath))
            {
                File.Delete(_sentinelPath);
            }
        }

        [Test]
        public async Task Hangfire_WorkerStartup_SchedulesTheDemoTaskOnTheProcessingHost_AndLogsTheSummary()
        {
            // Act — the scheduling service writes the posture line, then one line per job.
            string output = await WaitForOutputAsync(
                () => _workerHost!.CapturedOutput,
                text => text.Contains($"Recurring job '{_demoTaskId}'", StringComparison.Ordinal));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(output, Does.Contain("run server True").And.Contain("1 recurring task(s)"));
                Assert.That(output, Does.Contain($"Recurring job '{_demoTaskId}': cron '* * * * *', time zone 'UTC'"));
                Assert.That(output, Does.Not.Contain("Server=("), "The connection string never reaches a log line.");
            });
        }

        [Test]
        public async Task Hangfire_WorkerReady_IncludesTheStorageCheck_AndIsHealthy()
        {
            // Arrange
            using HttpClient probe = new HttpClient { BaseAddress = new Uri(_workerBaseUrl) };

            // Act
            using HttpResponseMessage ready = await probe.GetAsync(new Uri("/ready", UriKind.Relative));

            // Assert — the ready-tagged hangfire check answers alongside the Worker's own checks.
            Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK), await ready.Content.ReadAsStringAsync());
        }

        [Test]
        public async Task Hangfire_AnonymousDashboardRequest_IsChallengedToTheIdentityProvider_NeverServesTheDashboard()
        {
            // Arrange — no sign-in, no redirect following.
            using HttpClientHandler handler = new() { AllowAutoRedirect = false };
            using HttpClient client = new(handler) { BaseAddress = new Uri(_blazorServerBaseUrl) };

            // Act
            using HttpResponseMessage response = await client.GetAsync(new Uri("/hangfire", UriKind.Relative));
            string body = await response.Content.ReadAsStringAsync();

            // Assert — AC-HF10 live: the OIDC challenge, never the dashboard.
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
                Assert.That(response.Headers.Location?.ToString(), Does.StartWith($"http://127.0.0.1:{E2eFixture.IdentityProviderPort}/"));
                Assert.That(body, Does.Not.Contain("Hangfire Dashboard"));
            });
        }

        [Test]
        public async Task Hangfire_SignedInTester_SeesTheWorkersRecurringJob_OnAHostThatDoesNotReferenceTheWorker()
        {
            // Act — the anonymous navigation is challenged; the seeded user (role tester) signs in.
            await SignInToRecurringJobsAsync();

            // Assert — the Worker's job, rendered by a host that has no copy of the Worker assembly (AC-HF9 live).
            await Assertions.Expect(Page.Locator($"input.js-jobs-list-checkbox[value='{_demoTaskId}']")).ToBeVisibleAsync();
            Assert.That(
                File.Exists(Path.Combine(BlazorServerOutputDirectory(), "Cloudstrap.Demo.Worker.dll")),
                Is.False,
                "The dashboard host must not reference the Worker.");
        }

        [Test]
        public async Task Hangfire_TesterTriggersTheJobFromTheDashboard_TheWorkerRunsItWithinSeconds()
        {
            // Arrange
            await SignInToRecurringJobsAsync();
            int completedBefore = CountOccurrences(_workerHost!.CapturedOutput, _completedLine);

            // Act — tick the job's row and press "Trigger now" (a dashboard POST, antiforgery-protected).
            await Page.Locator($"input.js-jobs-list-checkbox[value='{_demoTaskId}']").CheckAsync();
            await Page.Locator("button.js-jobs-list-command", new PageLocatorOptions { HasTextString = "Trigger now" }).ClickAsync();
            string output = await WaitForOutputAsync(
                () => _workerHost!.CapturedOutput,
                text => CountOccurrences(text, _completedLine) > completedBefore);

            // Assert — the separate Worker process ran the job.
            Assert.Multiple(() =>
            {
                Assert.That(CountOccurrences(output, _completedLine), Is.GreaterThan(completedBefore));
                Assert.That(output, Does.Contain("Demo order count:"));
                Assert.That(ConsoleErrors, Is.Empty, string.Join(Environment.NewLine, ConsoleErrors));
            });
        }

        private static int CountOccurrences(string text, string fragment)
        {
            return Regex.Count(text, Regex.Escape(fragment));
        }

        private static string BlazorServerOutputDirectory()
        {
            string configuration = typeof(HangfireTests).Assembly
                .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
            return Path.Combine(SutProcess.FindRepoRoot(), "src", "demo", "BlazorServer", "bin", configuration, "net10.0");
        }

        private static async Task<string> WaitForOutputAsync(Func<string> captured, Func<string, bool> ready)
        {
            DateTime deadline = DateTime.UtcNow + _deadline;
            string output = captured();
            while (!ready(output) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                output = captured();
            }

            return output;
        }

        private static async Task WaitUntilHealthyAsync(HttpClient client, SutProcess process, string path)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                try
                {
                    using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));
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
                $"The host behind {client.BaseAddress} did not answer {path} in time.{Environment.NewLine}{process.CapturedOutput}");
        }

        private async Task SignInToRecurringJobsAsync()
        {
            await Page.GotoAsync(_blazorServerBaseUrl + "/hangfire/recurring");
            await Page.WaitForURLAsync($"http://127.0.0.1:{E2eFixture.IdentityProviderPort}/**");
            await BrowserSignIn.FillLoginFormAsync(Page);
            await Page.WaitForURLAsync(_blazorServerBaseUrl + "/hangfire/recurring**");
        }
    }
}
