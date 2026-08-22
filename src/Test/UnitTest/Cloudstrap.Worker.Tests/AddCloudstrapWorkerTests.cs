namespace Cloudstrap.Worker.Tests
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Cloudstrap.Worker.Tests.Infrastructure;
    using Microsoft.AspNetCore.Hosting.Server;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// Registration semantics of <c>AddCloudstrapWorker</c> (AC-WK1, AC-WK2's default-port clause,
    /// AC-WK5's registration half): eager fail-fast, idempotent core/correlation, additive stock
    /// health-check builder, the listener registered exactly once and only when enabled, bound and
    /// validated <c>Cloudstrap:Worker</c> options with a winning callback, and no ASP.NET pipeline
    /// on the worker application itself.
    /// </summary>
    [TestFixture]
    public sealed class AddCloudstrapWorkerTests
    {
        [Test]
        public void AddCloudstrapWorker_OnNullBuilder_ThrowsArgumentNullException()
        {
            IHostApplicationBuilder builder = null!;

            Assert.That(
                () => builder.AddCloudstrapWorker(),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void AddCloudstrapWorker_WithInvalidCloudstrapSection_ThrowsAtTheCall()
        {
            // Arrange — no Cloudstrap section at all: the eager read must fail at the call, before
            // the host is ever built (AC-WK1).
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();

            // Act + Assert
            Assert.That(
                () => builder.AddCloudstrapWorker(),
                Throws.TypeOf<ConfigurationValidationException>());
        }

        [Test]
        public void AddCloudstrapWorker_RegistersCoreOptionsCorrelationAndTheListener()
        {
            // Arrange
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(WorkerTestHost.ValidSettings());

            // Act
            builder.AddCloudstrapWorker();
            using IHost host = builder.Build();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(host.Services.GetRequiredService<IOptions<CloudstrapOptions>>().Value, Is.Not.Null);
                Assert.That(host.Services.GetRequiredService<IOptions<HealthChecksOptions>>().Value.Enabled, Is.True);
                Assert.That(host.Services.GetService<ICorrelationContextAccessor>(), Is.Not.Null);
                Assert.That(CountListenerRegistrations(builder.Services), Is.EqualTo(1));
            });
        }

        [Test]
        public void AddCloudstrapWorker_ConsumerHealthCheckRegistrationIsAdditive()
        {
            // Arrange — consumer checks registered before AND after the call must land in the same
            // stock registry (the Aspire-posture additive builder).
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(WorkerTestHost.ValidSettings());
            builder.Services.AddHealthChecks()
                .AddCheck("before", () => HealthCheckResult.Healthy());

            // Act
            builder.AddCloudstrapWorker();
            builder.Services.AddHealthChecks()
                .AddCheck("after", () => HealthCheckResult.Healthy());
            using IHost host = builder.Build();

            // Assert
            IReadOnlyList<string> names = [.. host.Services
                .GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value
                .Registrations.Select(registration => registration.Name)];
            Assert.Multiple(() =>
            {
                Assert.That(names, Does.Contain("before"));
                Assert.That(names, Does.Contain("after"));
            });
        }

        [Test]
        public void AddCloudstrapWorker_CalledTwice_RegistersExactlyOneListener()
        {
            // Arrange
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(WorkerTestHost.ValidSettings());

            // Act — the second call is a no-op (the builder.Properties run-once marker)
            builder.AddCloudstrapWorker();
            builder.AddCloudstrapWorker();

            // Assert
            Assert.That(CountListenerRegistrations(builder.Services), Is.EqualTo(1));
        }

        [Test]
        public void AddCloudstrapWorker_BuiltHost_HasNoAspNetRequestPipeline()
        {
            // Arrange
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(WorkerTestHost.ValidSettings());

            // Act
            builder.AddCloudstrapWorker();
            using IHost host = builder.Build();

            // Assert — the worker app itself gets no ASP.NET pipeline: the D-1 Kestrel lives inside
            // the listener's inner host, never in the application's own container (AC-WK1).
            Assert.That(host.Services.GetService<IServer>(), Is.Null);
        }

        [Test]
        public void WorkerOptions_Defaults_Port9000AllInterfaces()
        {
            // Arrange — no Cloudstrap:Worker section at all
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(WorkerTestHost.ValidSettings());

            // Act
            builder.AddCloudstrapWorker();
            using IHost host = builder.Build();
            WorkerOptions options = host.Services.GetRequiredService<IOptions<WorkerOptions>>().Value;

            // Assert — the founding-spec default port, asserted as the bound value (the suite never
            // live-binds 9000), and the all-interfaces container default.
            Assert.Multiple(() =>
            {
                Assert.That(options.HealthPort, Is.EqualTo(9000));
                Assert.That(options.HealthListenAddress, Is.EqualTo("*"));
            });
        }

        [Test]
        public void WorkerOptions_ConfigureCallback_RunsAfterBindingAndWins()
        {
            // Arrange — configuration sets one port, the callback another: the callback wins.
            Dictionary<string, string?> settings = WorkerTestHost.ValidSettings();
            settings["Cloudstrap:Worker:HealthPort"] = "1234";
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(settings);

            // Act
            builder.AddCloudstrapWorker(options => options.HealthPort = 2345);
            using IHost host = builder.Build();

            // Assert
            Assert.That(
                host.Services.GetRequiredService<IOptions<WorkerOptions>>().Value.HealthPort,
                Is.EqualTo(2345));
        }

        [Test]
        public void AddCloudstrapWorker_HealthPortOutOfRange_FailsStartupNamingTheMember()
        {
            // Arrange — an out-of-range port; health checks disabled so the failure isolated here is
            // ValidateOnStart, not a bind attempt.
            Dictionary<string, string?> settings = WorkerTestHost.ValidSettings();
            settings["Cloudstrap:Worker:HealthPort"] = "0";
            settings["Cloudstrap:HealthChecks:Enabled"] = "false";
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(settings);
            builder.AddCloudstrapWorker();
            using IHost host = builder.Build();

            // Act + Assert
            Assert.That(
                () => host.StartAsync(),
                Throws.TypeOf<OptionsValidationException>().With.Message.Contains(nameof(WorkerOptions.HealthPort)));
        }

        [Test]
        public void AddCloudstrapWorker_WithHealthChecksDisabled_RegistersNoListener()
        {
            // Arrange — the Cloudstrap:HealthChecks kill switch (owned by Core, consumed here — D-3)
            Dictionary<string, string?> settings = WorkerTestHost.ValidSettings();
            settings["Cloudstrap:HealthChecks:Enabled"] = "false";
            HostApplicationBuilder builder = WorkerTestHost.CreateBuilder(settings);

            // Act
            builder.AddCloudstrapWorker();

            // Assert — no listener registration at all; the host builds and runs otherwise unaffected
            Assert.That(CountListenerRegistrations(builder.Services), Is.Zero);
            using IHost host = builder.Build();
            Assert.That(host, Is.Not.Null);
        }

        private static int CountListenerRegistrations(IServiceCollection services)
        {
            return services.Count(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(WorkerHealthListener));
        }
    }
}
