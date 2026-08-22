namespace Cloudstrap.Worker.Tests
{
    using System.Globalization;
    using System.Net;
    using Cloudstrap.Observability;
    using Cloudstrap.Worker.Tests.Infrastructure;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// AC-WK8's composition half: the worker bootstrap composes with #2's observability in both
    /// pipeline modes — this package adds no exporter and no pipeline of its own, so the
    /// Aspire-ServiceDefaults-beside-us shape (contribute mode) and the default owner mode both
    /// boot and serve probes. Exporter-duplication prevention remains #2's own tested contract.
    /// </summary>
    [TestFixture]
    public sealed class WorkerObservabilityCompositionTests
    {
        [Test]
        public async Task Worker_WithUseCloudstrapObservabilityContributeMode_BootsAndServesProbes()
        {
            await AssertBootsAndServesProbesAsync(ObservabilityPipelineMode.Contribute);
        }

        [Test]
        public async Task Worker_WithObservabilityOwnerMode_BootsAndServesProbes()
        {
            await AssertBootsAndServesProbesAsync(ObservabilityPipelineMode.Owner);
        }

        private static async Task AssertBootsAndServesProbesAsync(ObservabilityPipelineMode mode)
        {
            // Arrange — the explicit sibling-call composition (D-2), in the requested pipeline mode
            int port = WorkerTestHost.GetFreePort();
            Dictionary<string, string?> settings = WorkerTestHost.ValidSettings();
            settings["Cloudstrap:Worker:HealthPort"] = port.ToString(CultureInfo.InvariantCulture);
            settings["Cloudstrap:Worker:HealthListenAddress"] = "localhost";
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(settings);
            builder.UseCloudstrapObservability(options => options.PipelineMode = mode);
            builder.AddCloudstrapWorker();
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            using HttpClient client = WorkerTestHost.CreateProbeClient(port);

            try
            {
                using HttpResponseMessage healthz = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
                using HttpResponseMessage ready = await client.GetAsync(new Uri("/ready", UriKind.Relative));

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(healthz.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                    Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }
    }
}
