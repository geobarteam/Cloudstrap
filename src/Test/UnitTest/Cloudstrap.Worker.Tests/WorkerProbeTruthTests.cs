namespace Cloudstrap.Worker.Tests
{
    using System.Globalization;
    using System.Net;
    using System.Net.Sockets;
    using System.Reflection;
    using Cloudstrap.Observability;
    using Cloudstrap.Worker.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// The truth-and-loud-failure contract (AC-WK4, AC-WK5, AC-WK6, AC-WK9): probes reflect the
    /// registered checks in both directions under the tag contract, disabled means never bound,
    /// the bind address is an explicit option with zero environment sniffing, and an occupied
    /// port fails host startup naming it.
    /// </summary>
    [TestFixture]
    public sealed class WorkerProbeTruthTests
    {
        [Test]
        public async Task ReadyProbe_FlipsTo503WithAFailingReadyCheckWhileHealthzStays200_AndRecovers()
        {
            // Arrange — a healthy live-tagged check plus a toggleable ready-tagged check
            ToggleHealthCheck toggle = new ToggleHealthCheck();
            int port = WorkerTestHost.GetFreePort();
            using IHost host = BuildWorkerHost(port, services =>
            {
                services.AddSingleton(toggle);
                services.AddHealthChecks()
                    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [CloudstrapHealthCheckTags.Liveness])
                    .AddCheck<ToggleHealthCheck>("dependency", tags: [CloudstrapHealthCheckTags.Readiness]);
            });
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act + Assert — healthy first, flip to Unhealthy, recover: both directions
                Assert.That((await client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode, Is.EqualTo(HttpStatusCode.OK));

                toggle.Status = HealthStatus.Unhealthy;
                Assert.Multiple(async () =>
                {
                    Assert.That(
                        (await client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode,
                        Is.EqualTo(HttpStatusCode.ServiceUnavailable));
                    Assert.That(
                        (await client.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode,
                        Is.EqualTo(HttpStatusCode.OK));
                });

                toggle.Status = HealthStatus.Healthy;
                Assert.That((await client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task Probes_UntaggedCheck_IsServedByNeitherProbe()
        {
            // Arrange — an untagged Unhealthy check: the tag predicate is the contract
            int port = WorkerTestHost.GetFreePort();
            using IHost host = BuildWorkerHost(port, services => services.AddHealthChecks()
                .AddCheck("untagged", () => HealthCheckResult.Unhealthy()));
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act + Assert — neither probe serves it, so both stay 200
                Assert.Multiple(async () =>
                {
                    Assert.That(
                        (await client.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode,
                        Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(
                        (await client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode,
                        Is.EqualTo(HttpStatusCode.OK));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task ReadyProbe_DegradedCheck_Answers200WithDegradedBody()
        {
            // Arrange — Degraded maps to 200 (orchestrators treat non-503 as passing)
            int port = WorkerTestHost.GetFreePort();
            using IHost host = BuildWorkerHost(port, services => services.AddHealthChecks()
                .AddCheck("degraded", () => HealthCheckResult.Degraded(), tags: [CloudstrapHealthCheckTags.Readiness]));
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act
                using HttpResponseMessage response = await client.GetAsync(new Uri("/ready", UriKind.Relative));

                // Assert
                Assert.Multiple(async () =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("Degraded"));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task Listener_WithHealthChecksDisabled_NeverBindsThePort()
        {
            // Arrange — the kill switch with a configured port THIS TEST owns
            int port = WorkerTestHost.GetFreePort();
            Dictionary<string, string?> settings = WorkerTestHost.ValidSettings();
            settings["Cloudstrap:Worker:HealthPort"] = port.ToString(CultureInfo.InvariantCulture);
            settings["Cloudstrap:Worker:HealthListenAddress"] = "localhost";
            settings["Cloudstrap:HealthChecks:Enabled"] = "false";
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(settings);
            builder.AddCloudstrapWorker();
            using IHost host = builder.Build();
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act + Assert — the host runs, the port was never bound
                Assert.That(
                    async () => await client.GetAsync(new Uri("/healthz", UriKind.Relative)),
                    Throws.TypeOf<HttpRequestException>());
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task Listener_LocalhostOverride_AnswersOnLoopback()
        {
            // Arrange — the explicit dev-time override (AC-WK6's override half)
            int port = WorkerTestHost.GetFreePort();
            using IHost host = BuildWorkerHost(port);
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act
                using HttpResponseMessage response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
                WorkerHealthListener listener = host.Services.GetServices<IHostedService>()
                    .OfType<WorkerHealthListener>()
                    .Single();

                // Assert — answers on 127.0.0.1 and the bound address set is loopback-only
                Assert.Multiple(() =>
                {
                    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(listener.BoundAddresses, Has.Count.EqualTo(1));
                    Assert.That(listener.BoundAddresses[0], Does.StartWith("http://localhost:"));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public void Listener_DefaultAddress_ComposesAllInterfacesBinding()
        {
            // Act — the composed-URL seam (mechanic (f)): the default is asserted without a live
            // all-interfaces bind, which a unit suite must not perform (firewall prompts, LAN flake)
            string defaultUrl = WorkerHealthListener.ComposeUrl(new WorkerOptions());
            string overrideUrl = WorkerHealthListener.ComposeUrl(
                new WorkerOptions { HealthListenAddress = "localhost", HealthPort = 5432 });

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(defaultUrl, Is.EqualTo("http://*:9000"));
                Assert.That(overrideUrl, Is.EqualTo("http://localhost:5432"));
            });
        }

        [Test]
        public void WorkerAssembly_ContainsNoEnvironmentSniffing()
        {
            // Assert — the bind decision is WorkerOptions and nothing else: no environment-derived
            // branching anywhere in the shipped assembly (the source's EnvironmentIsLocal() is dead)
            IEnumerable<MethodInfo> methods = typeof(WorkerOptions).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance |
                    BindingFlags.DeclaredOnly));
            Assert.That(
                methods.Where(method =>
                    method.Name.Contains("EnvironmentIsLocal", StringComparison.OrdinalIgnoreCase)
                    || method.Name.Contains("IsRunningIn", StringComparison.OrdinalIgnoreCase)),
                Is.Empty);
        }

        [Test]
        public async Task Listener_PortAlreadyOccupied_FailsHostStartupNamingThePort()
        {
            // Arrange — a test-held socket occupies the configured port
            int port = WorkerTestHost.GetFreePort();
            TcpListener occupant = new TcpListener(IPAddress.Loopback, port);
            occupant.Start();
            using IHost host = BuildWorkerHost(port);

            try
            {
                // Act
                Exception? thrown = null;
                try
                {
                    await host.StartAsync();
                }
                catch (Exception exception)
                {
                    thrown = exception;
                }

                // Assert — startup faults (a worker never runs silently unprobed) and the failure
                // names the port so the operator knows what to free
                Assert.That(thrown, Is.Not.Null, "Host startup must fault when the health port is occupied.");
                string messages = Flatten(thrown!);
                Assert.That(messages, Does.Contain(port.ToString(CultureInfo.InvariantCulture)));
            }
            finally
            {
                occupant.Stop();
            }
        }

        private static string Flatten(Exception exception)
        {
            List<string> messages = [];
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                messages.Add(current.Message);
            }

            return string.Join(" | ", messages);
        }

        private static IHost BuildWorkerHost(int port, Action<IServiceCollection>? configureServices = null)
        {
            Dictionary<string, string?> settings = WorkerTestHost.ValidSettings();
            settings["Cloudstrap:Worker:HealthPort"] = port.ToString(CultureInfo.InvariantCulture);
            settings["Cloudstrap:Worker:HealthListenAddress"] = "localhost";
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(settings);
            builder.AddCloudstrapWorker();
            configureServices?.Invoke(builder.Services);
            return builder.Build();
        }
    }
}
