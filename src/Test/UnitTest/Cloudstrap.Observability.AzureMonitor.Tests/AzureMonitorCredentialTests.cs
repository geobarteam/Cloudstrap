namespace Cloudstrap.Observability.AzureMonitor.Tests
{
    using global::Azure.Core;
    using global::Azure.Identity;
    using global::Azure.Monitor.OpenTelemetry.Exporter;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using OpenTelemetry.Trace;

    /// <summary>
    /// Entra ID ingestion authentication is a per-environment setting with no code change, and a
    /// code-supplied credential always has the final say. Constructing a credential acquires no token, and
    /// no test here ever triggers one.
    /// </summary>
    [TestFixture]
    public sealed class AzureMonitorCredentialTests
    {
        private const string _dummyConnectionString =
            "InstrumentationKey=00000000-0000-0000-0000-000000000000";

        [Test]
        public void AddAzureMonitor_WithDefaultFlag_AttachesNoCredential()
        {
            // Arrange — connection-string local authentication is the default
            using IHost host = BuildHost(AzureMonitorModeValid(), out List<AzureMonitorExporterOptions> captured);

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert
            Assert.That(captured, Is.Not.Empty);
            Assert.That(captured.Select(exporter => exporter.Credential), Has.All.Null);
        }

        [Test]
        public void AddAzureMonitor_WithUseDefaultAzureCredential_AttachesDefaultAzureCredential()
        {
            // Arrange
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:AzureMonitor:UseDefaultAzureCredential"] = "true";
            using IHost host = BuildHost(values, out List<AzureMonitorExporterOptions> captured);

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert
            Assert.That(captured, Is.Not.Empty);
            Assert.That(
                captured.Select(exporter => exporter.Credential),
                Has.All.InstanceOf<DefaultAzureCredential>());
        }

        [Test]
        public async Task AddAzureMonitor_WithUseDefaultAzureCredential_SharesOneCredentialAcrossSignals()
        {
            // Arrange — a credential per signal would mean three independent token caches
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:AzureMonitor:UseDefaultAzureCredential"] = "true";
            using IHost host = BuildHost(values, out List<AzureMonitorExporterOptions> captured);

            // Act — force all three signals to materialize
            await host.StartAsync();
            try
            {
                host.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Contoso.Orders.Api")
                    .LogInformation("Force logger provider creation");
            }
            finally
            {
                await host.StopAsync();
            }

            // Assert
            Assert.That(captured, Has.Count.EqualTo(3));
            Assert.That(
                captured.Select(exporter => exporter.Credential),
                Has.All.SameAs(captured[0].Credential));
        }

        [Test]
        public void AddAzureMonitor_WithHookSuppliedCredential_WinsOverTheFlag()
        {
            // Arrange — the flag is on, but the consumer supplies its own credential in code
            StubTokenCredential supplied = new();
            Dictionary<string, string?> values = AzureMonitorModeValid();
            values["Cloudstrap:AzureMonitor:UseDefaultAzureCredential"] = "true";
            List<AzureMonitorExporterOptions> captured = [];
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability().AddAzureMonitor(exporter =>
            {
                exporter.DisableOfflineStorage = true;
                exporter.Credential = supplied;
                captured.Add(exporter);
            });
            using IHost host = builder.Build();

            // Act
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Assert — the hook runs last, so the code-supplied credential stands
            Assert.That(captured, Is.Not.Empty);
            Assert.That(captured.Select(exporter => exporter.Credential), Has.All.SameAs(supplied));
        }

        private static IHost BuildHost(
            Dictionary<string, string?> values,
            out List<AzureMonitorExporterOptions> captured)
        {
            List<AzureMonitorExporterOptions> capturedOptions = [];
            HostApplicationBuilder builder = CreateBuilder(values);
            builder.UseCloudstrapObservability().AddAzureMonitor(exporter =>
            {
                exporter.DisableOfflineStorage = true;
                capturedOptions.Add(exporter);
            });
            captured = capturedOptions;

            return builder.Build();
        }

        private static Dictionary<string, string?> AzureMonitorModeValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
            ["Cloudstrap:OpenTelemetry:Mode"] = "AzureMonitor",
            ["Cloudstrap:OpenTelemetry:EnableConsole"] = "false",
            ["Cloudstrap:AzureMonitor:ConnectionString"] = _dummyConnectionString,
        };

        private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);

            return builder;
        }

        /// <summary>
        /// A credential that exists only to be recognized by reference. Acquiring a token would mean
        /// contacting Entra ID, which no unit test does.
        /// </summary>
        private sealed class StubTokenCredential : TokenCredential
        {
            public override AccessToken GetToken(
                TokenRequestContext requestContext,
                CancellationToken cancellationToken)
                => throw new NotSupportedException("Tests never acquire a token.");

            public override ValueTask<AccessToken> GetTokenAsync(
                TokenRequestContext requestContext,
                CancellationToken cancellationToken)
                => throw new NotSupportedException("Tests never acquire a token.");
        }
    }
}
