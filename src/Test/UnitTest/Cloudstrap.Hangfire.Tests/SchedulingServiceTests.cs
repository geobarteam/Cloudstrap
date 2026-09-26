namespace Cloudstrap.Hangfire.Tests
{
    using Cloudstrap.Hangfire.Tests.Fakes;
    using Cloudstrap.Hangfire.Tests.Infrastructure;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using global::Hangfire;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// The processing host's startup (AC-HF3, finding 1): scheduling runs before the Hangfire server, logs the
    /// posture and one line per job, and a failure faults startup so the server never starts.
    /// </summary>
    [TestFixture]
    public sealed class SchedulingServiceTests
    {
        [Test]
        public async Task StartAsync_LogsThePostureLine_AndOneLinePerJob_WithIdCronAndTimeZone_NeverTheConnectionString()
        {
            // Arrange
            (IHost host, RecordingRecurringJobManager manager, CapturingLoggerProvider logs, _) = BuildHost();
            using (host)
            using (logs)
            {
                RecurringTaskSchedulingService service = host.Services.GetServices<IHostedService>().OfType<RecurringTaskSchedulingService>().Single();

                // Act
                await service.StartAsync(CancellationToken.None);

                // Assert
                string[] summary = [.. logs.Entries
                    .Where(entry => entry.Category == "Cloudstrap.Hangfire" && entry.Level == LogLevel.Information)
                    .Select(entry => entry.Message)];
                Assert.Multiple(() =>
                {
                    Assert.That(manager.Added, Is.Not.Empty);
                    Assert.That(summary, Has.Some.Contains("run server True").And.Some.Contains("prepare schema False"));
                    Assert.That(summary, Has.Some.Contains("'NightlyCleanupTask'").And.Contains("'0 3 * * *'").And.Contains("'UTC'"));
                    Assert.That(summary, Has.Some.Contains("'DisabledInCodeTask' disabled"));
                    Assert.That(logs.Entries.Select(entry => entry.Message), Has.None.Contains("fixture-password"));
                    Assert.That(logs.Entries.Select(entry => entry.Message), Has.None.Contains("unreachable.invalid"));
                });
            }
        }

        [Test]
        public void StartAsync_WhenSchedulingThrows_TheExceptionPropagates()
        {
            // Arrange
            (IHost host, RecordingRecurringJobManager manager, CapturingLoggerProvider logs, _) = BuildHost();
            using (host)
            using (logs)
            {
                manager.FailEveryAddOrUpdate = true;
                RecurringTaskSchedulingService service = host.Services.GetServices<IHostedService>().OfType<RecurringTaskSchedulingService>().Single();

                // Act & Assert
                Assert.That(() => service.StartAsync(CancellationToken.None), Throws.Exception);
            }
        }

        [Test]
        public async Task HostStart_WhenSchedulingFails_FaultsStartup_AndTheHangfireServerNeverStarts()
        {
            // Arrange
            (IHost host, RecordingRecurringJobManager manager, CapturingLoggerProvider logs, FakeJobStorage storage) = BuildHost();
            using (host)
            using (logs)
            {
                manager.FailEveryAddOrUpdate = true;

                // Act
                Assert.That(() => host.StartAsync(), Throws.Exception);
                await Task.Delay(TimeSpan.FromMilliseconds(500));

                // Assert — a started server announces itself through the storage; nothing touched it.
                Assert.That(storage.ConnectionsOpened, Is.Zero);
            }
        }

        private static (IHost Host, RecordingRecurringJobManager Manager, CapturingLoggerProvider Logs, FakeJobStorage Storage) BuildHost()
        {
            FakeJobStorage storage = new();
            RecordingRecurringJobManager manager = new();
            CapturingLoggerProvider logs = new();
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();
            builder.Logging.AddProvider(logs);
            builder.AddCloudstrapHangfire(hangfire => hangfire.Storage = configuration => configuration.UseStorage(storage));

            // Registered after the call: the last registration wins, replacing the Hangfire manager.
            builder.Services.AddSingleton<IRecurringJobManager>(manager);
            return (builder.Build(), manager, logs, storage);
        }
    }
}
