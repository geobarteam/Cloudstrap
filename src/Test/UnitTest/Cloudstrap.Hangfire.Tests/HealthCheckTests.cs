namespace Cloudstrap.Hangfire.Tests
{
    using Cloudstrap.Hangfire.Tests.Fakes;
    using Cloudstrap.Hangfire.Tests.Infrastructure;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using global::Hangfire;
    using global::Hangfire.Storage.Monitoring;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// The storage health check (AC-HF14, DD-3): opt-in and additive on the stock builder, named <c>hangfire</c>
    /// and tagged <c>ready</c> by default, reporting statistics when storage answers and the registration's
    /// failure status when it does not — never a connection string or an exception message.
    /// </summary>
    [TestFixture]
    public sealed class HealthCheckTests
    {
        private static readonly string[] _readyTag = ["ready"];
        private static readonly string[] _jobsTag = ["jobs"];
        private static readonly string[] _additiveOrder = ["before", "hangfire", "after"];

        [Test]
        public void Defaults_NameHangfire_TagReady_FailureUnhealthy()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();

            // Act
            builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck();
            HealthCheckRegistration registration = Registrations(builder).Single();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(registration.Name, Is.EqualTo("hangfire"));
                Assert.That(registration.Tags, Is.EquivalentTo(_readyTag));
                Assert.That(registration.FailureStatus, Is.EqualTo(HealthStatus.Unhealthy));
            });
        }

        [Test]
        public void Overrides_NameFailureStatusAndTags_AreHonored()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();

            // Act
            builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck("jobs-storage", HealthStatus.Degraded, ["jobs"]);
            HealthCheckRegistration registration = Registrations(builder).Single();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(registration.Name, Is.EqualTo("jobs-storage"));
                Assert.That(registration.Tags, Is.EquivalentTo(_jobsTag));
                Assert.That(registration.FailureStatus, Is.EqualTo(HealthStatus.Degraded));
            });
        }

        [Test]
        public async Task StorageAnswers_ReportsHealthy_WithServersRecurringEnqueuedFailedData()
        {
            // Arrange
            FakeJobStorage storage = new();
            storage.Monitoring.Statistics = new StatisticsDto { Servers = 2, Recurring = 5, Enqueued = 7, Failed = 1 };

            // Act
            HealthReportEntry entry = await CheckAsync(storage);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(entry.Status, Is.EqualTo(HealthStatus.Healthy));
                Assert.That(entry.Data["servers"], Is.EqualTo(2L));
                Assert.That(entry.Data["recurring"], Is.EqualTo(5L));
                Assert.That(entry.Data["enqueued"], Is.EqualTo(7L));
                Assert.That(entry.Data["failed"], Is.EqualTo(1L));
            });
        }

        [TestCase(null, HealthStatus.Unhealthy)]
        [TestCase(HealthStatus.Degraded, HealthStatus.Degraded)]
        public async Task StorageThrows_ReportsTheRegistrationsFailureStatus_DegradedWhenConfigured(HealthStatus? configured, HealthStatus expected)
        {
            // Arrange
            FakeJobStorage storage = new();
            storage.Monitoring.StatisticsFailure = new InvalidOperationException("storage down");

            // Act
            HealthReportEntry entry = await CheckAsync(storage, configured);

            // Assert
            Assert.That(entry.Status, Is.EqualTo(expected));
        }

        [Test]
        public async Task StorageThrows_ResultNeverContainsTheConnectionStringOrTheExceptionMessage()
        {
            // Arrange
            FakeJobStorage storage = new();
            storage.Monitoring.StatisticsFailure = new InvalidOperationException("Login failed: Server=secret-host;Password=p4ss");

            // Act
            HealthReportEntry entry = await CheckAsync(storage);
            string everything = $"{entry.Description} {string.Join(" ", entry.Data.Select(pair => $"{pair.Key}={pair.Value}"))}";

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(entry.Status, Is.EqualTo(HealthStatus.Unhealthy));
                Assert.That(everything, Does.Not.Contain("secret-host").And.Not.Contain("p4ss"));
                Assert.That(entry.Exception, Is.Null);
                Assert.That(entry.Description, Does.Contain(nameof(InvalidOperationException)));
            });
        }

        [Test]
        public void Registration_IsAdditive_ExistingChecksBeforeAndAfterAreKept()
        {
            // Arrange
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();

            // Act
            builder.Services.AddHealthChecks()
                .AddCheck("before", () => HealthCheckResult.Healthy())
                .AddCloudstrapHangfireHealthCheck()
                .AddCheck("after", () => HealthCheckResult.Healthy());

            // Assert
            Assert.That(Registrations(builder).Select(registration => registration.Name), Is.EqualTo(_additiveOrder));
        }

        [Test]
        public async Task RealStorage_OnLocalDb_ReportsHealthy()
        {
            // Arrange — a storage-only host installs the schema on the D-3 test database.
            await SqlServerTestDatabase.ResetAsync();
            HostApplicationBuilder builder = HangfireSqlHost.CreateBuilder(runServer: false, schema: "hangfire_health");
            builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck();
            using IHost host = builder.Build();

            // Act
            HealthReport report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();

            // Assert
            Assert.That(report.Entries["hangfire"].Status, Is.EqualTo(HealthStatus.Healthy), report.Entries["hangfire"].Description);
        }

        [Test]
        public async Task UnreachableServer_ReportsTheFailureStatus()
        {
            // Arrange — the default test settings point at a server that never answers, PrepareSchema off.
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();
            builder.AddCloudstrapHangfire(hangfire => hangfire.RunServer = false);
            builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck();
            using IHost host = builder.Build();

            // Act
            HealthReport report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            HealthReportEntry entry = report.Entries["hangfire"];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(entry.Status, Is.EqualTo(HealthStatus.Unhealthy));
                Assert.That(entry.Description, Does.Not.Contain("unreachable.invalid").And.Not.Contain("fixture-password"));
            });
        }

        private static async Task<HealthReportEntry> CheckAsync(FakeJobStorage storage, HealthStatus? failureStatus = null)
        {
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();
            builder.AddCloudstrapHangfire(hangfire =>
            {
                hangfire.RunServer = false;
                hangfire.Storage = configuration => configuration.UseStorage(storage);
            });
            builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck(failureStatus: failureStatus);
            using IHost host = builder.Build();
            HealthReport report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            return report.Entries["hangfire"];
        }

        private static List<HealthCheckRegistration> Registrations(HostApplicationBuilder builder)
        {
            using ServiceProvider provider = builder.Services.BuildServiceProvider();
            return provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ToList();
        }
    }
}
