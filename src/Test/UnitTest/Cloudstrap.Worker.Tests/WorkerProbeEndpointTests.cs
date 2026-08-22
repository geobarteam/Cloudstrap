namespace Cloudstrap.Worker.Tests
{
    using System.Net;
    using Cloudstrap.Worker.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// The listener's live HTTP behavior (AC-WK2, AC-WK3, AC-WK7): #4's probe implementation on the
    /// configured port and paths with the framework's semantics, exactly two endpoints, and zero
    /// probe-evaluation code of this package's own.
    /// </summary>
    [TestFixture]
    public sealed class WorkerProbeEndpointTests
    {
        [Test]
        public async Task Probes_WithNoChecksRegistered_BothAnswer200Healthy()
        {
            // Arrange
            int port = WorkerTestHost.GetFreePort();
            using IHost host = BuildWorkerHost(port);
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act
                using HttpResponseMessage healthz = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
                using HttpResponseMessage ready = await client.GetAsync(new Uri("/ready", UriKind.Relative));

                // Assert — framework semantics: zero matching checks = Healthy (parity with the
                // web-host probes)
                Assert.Multiple(async () =>
                {
                    Assert.That(healthz.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(await healthz.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
                    Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(await ready.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task Probes_WithHealthyTaggedChecks_Answer200WithTheFrameworkBody()
        {
            // Arrange — the checks are registered on the PARENT host's stock builder; the listener
            // must evaluate them through the bridged HealthCheckService (the D-1 "one probe
            // implementation" bridge).
            int port = WorkerTestHost.GetFreePort();
            using IHost host = BuildWorkerHost(port, services => services.AddHealthChecks()
                .AddCheck("live-check", () => HealthCheckResult.Healthy(), tags: ["live"])
                .AddCheck("ready-check", () => HealthCheckResult.Healthy(), tags: ["ready"]));
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act
                using HttpResponseMessage healthz = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
                using HttpResponseMessage ready = await client.GetAsync(new Uri("/ready", UriKind.Relative));

                // Assert
                Assert.Multiple(async () =>
                {
                    Assert.That(healthz.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(await healthz.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
                    Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(await ready.Content.ReadAsStringAsync(), Is.EqualTo("Healthy"));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task Probes_AnswerOnTheConfiguredPortAndPaths()
        {
            // Arrange — overridden port AND overridden paths (Cloudstrap:HealthChecks owns paths, D-3)
            int port = WorkerTestHost.GetFreePort();
            Dictionary<string, string?> settings = WorkerTestHost.ValidSettings();
            settings["Cloudstrap:Worker:HealthPort"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings["Cloudstrap:Worker:HealthListenAddress"] = "localhost";
            settings["Cloudstrap:HealthChecks:LivenessPath"] = "/alive";
            settings["Cloudstrap:HealthChecks:ReadinessPath"] = "/accepting";
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(settings);
            builder.AddCloudstrapWorker();
            using IHost host = builder.Build();
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act + Assert — the overridden paths answer; the defaults 404
                Assert.Multiple(async () =>
                {
                    Assert.That((await client.GetAsync(new Uri("/alive", UriKind.Relative))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That((await client.GetAsync(new Uri("/accepting", UriKind.Relative))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That((await client.GetAsync(new Uri("/healthz", UriKind.Relative))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                    Assert.That((await client.GetAsync(new Uri("/ready", UriKind.Relative))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task Listener_BindsOnlyTheConfiguredPort()
        {
            // Arrange
            int port = WorkerTestHost.GetFreePort();
            using IHost host = BuildWorkerHost(port);
            await host.StartAsync();

            try
            {
                // Act — the listener's composed address set (the assertion seam: no live probing of
                // ports this suite does not own)
                WorkerHealthListener listener = host.Services.GetServices<IHostedService>()
                    .OfType<WorkerHealthListener>()
                    .Single();

                // Assert — exactly one bound address, carrying exactly the configured port
                Assert.That(listener.BoundAddresses, Has.Count.EqualTo(1));
                Assert.That(listener.BoundAddresses[0], Does.EndWith($":{port}"));
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task Probe_UnknownPathOnTheHealthPort_Returns404()
        {
            // Arrange
            int port = WorkerTestHost.GetFreePort();
            using IHost host = BuildWorkerHost(port);
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                // Act
                using HttpResponseMessage response = await client.GetAsync(
                    new Uri("/definitely-not-a-probe", UriKind.Relative));

                // Assert — the health port exposes exactly the two probe endpoints and nothing else
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public void WorkerAssembly_OwnsNoProbeEvaluationLogic()
        {
            // Assert — the source's unconditional-200 HealthCheckService is provably not ported:
            // this package implements no check, no publisher, and no probe-evaluation type.
            IEnumerable<Type> types = typeof(WorkerOptions).Assembly.GetTypes();
            Assert.Multiple(() =>
            {
                Assert.That(
                    types.Where(type => typeof(IHealthCheck).IsAssignableFrom(type)
                        || typeof(IHealthCheckPublisher).IsAssignableFrom(type)),
                    Is.Empty);
                Assert.That(
                    types.Where(type => type.Name.Contains("Probe", StringComparison.OrdinalIgnoreCase)
                        || type.Name.Contains("HealthCheckService", StringComparison.OrdinalIgnoreCase)),
                    Is.Empty);
            });
        }

        private static IHost BuildWorkerHost(int port, Action<IServiceCollection>? configureServices = null)
        {
            Dictionary<string, string?> settings = WorkerTestHost.ValidSettings();
            settings["Cloudstrap:Worker:HealthPort"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            settings["Cloudstrap:Worker:HealthListenAddress"] = "localhost";
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(settings);
            builder.AddCloudstrapWorker();
            configureServices?.Invoke(builder.Services);
            return builder.Build();
        }
    }
}
