namespace Cloudstrap.Messaging.Tests
{
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using JasperFx;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using Wolverine;

    /// <summary>
    /// The provisioning contract (AC-MSG12): with no explicit value the node provisions its own resources in
    /// <c>Development</c> only; an explicit value wins in either direction; the effective value is stated in
    /// the startup summary line. Asserted on the local transport — no SQL, no Azure.
    /// </summary>
    [TestFixture]
    public sealed class AutoProvisionTests
    {
        [Test]
        public async Task AutoProvision_NullInDevelopment_IsOn()
        {
            // Arrange
            (IHost host, CapturingLoggerProvider logs) = BuildHost(Environments.Development, explicitValue: null);
            using (host)
            {
                // Act
                await host.StartAsync();
                await host.StopAsync();

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(
                        host.Services.GetRequiredService<WolverineOptions>().AutoBuildMessageStorageOnStartup,
                        Is.EqualTo(AutoCreate.CreateOrUpdate));
                    Assert.That(SummaryLine(logs), Does.Contain("auto-provision: True"));
                });
            }
        }

        [Test]
        public async Task AutoProvision_NullInProduction_IsOff()
        {
            // Arrange
            (IHost host, CapturingLoggerProvider logs) = BuildHost(Environments.Production, explicitValue: null);
            using (host)
            {
                // Act
                await host.StartAsync();
                await host.StopAsync();

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(
                        host.Services.GetRequiredService<WolverineOptions>().AutoBuildMessageStorageOnStartup,
                        Is.EqualTo(AutoCreate.None));
                    Assert.That(SummaryLine(logs), Does.Contain("auto-provision: False"));
                });
            }
        }

        [Test]
        public async Task AutoProvision_ExplicitValue_WinsInEitherDirection()
        {
            // Arrange — off in Development, on in Production: the explicit value always wins.
            (IHost developmentOff, CapturingLoggerProvider developmentLogs) = BuildHost(Environments.Development, explicitValue: false);
            (IHost productionOn, CapturingLoggerProvider productionLogs) = BuildHost(Environments.Production, explicitValue: true);
            using (developmentOff)
            using (productionOn)
            {
                // Act
                await developmentOff.StartAsync();
                await developmentOff.StopAsync();
                await productionOn.StartAsync();
                await productionOn.StopAsync();

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(
                        developmentOff.Services.GetRequiredService<WolverineOptions>().AutoBuildMessageStorageOnStartup,
                        Is.EqualTo(AutoCreate.None));
                    Assert.That(SummaryLine(developmentLogs), Does.Contain("auto-provision: False"));
                    Assert.That(
                        productionOn.Services.GetRequiredService<WolverineOptions>().AutoBuildMessageStorageOnStartup,
                        Is.EqualTo(AutoCreate.CreateOrUpdate));
                    Assert.That(SummaryLine(productionLogs), Does.Contain("auto-provision: True"));
                });
            }
        }

        private static (IHost Host, CapturingLoggerProvider Logs) BuildHost(string environment, bool? explicitValue)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { EnvironmentName = environment });
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            if (explicitValue is not null)
            {
                settings["Cloudstrap:Messaging:AutoProvision"] = explicitValue.Value ? "true" : "false";
            }

            builder.Configuration.AddInMemoryCollection(settings);
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);
            builder.AddCloudstrapMessaging();
            return (builder.Build(), logs);
        }

        private static string SummaryLine(CapturingLoggerProvider logs)
        {
            return logs.Entries
                .Single(entry => entry.Category == typeof(MessagingStartupSummaryLogger).FullName)
                .Message;
        }
    }
}
