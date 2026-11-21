namespace Cloudstrap.Hangfire.Tests
{
    using Cloudstrap.Hangfire.Tests.Fixtures;
    using Cloudstrap.Hangfire.Tests.Fixtures.Tasks;
    using Cloudstrap.Hangfire.Tests.Infrastructure;
    using global::Hangfire;
    using global::Hangfire.Common;
    using global::Hangfire.Storage;
    using global::Hangfire.Storage.Monitoring;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Testing;
    using NUnit.Framework;

    /// <summary>
    /// The package on real SQL Server (spec D-3: LocalDB, or <c>CLOUDSTRAP_TEST_SQL</c>): schema preparation,
    /// scheduling and a triggered run through the real server, reconciliation, the real distributed lock, and the
    /// storage-only host reading the processing host's jobs from the same schema.
    /// </summary>
    [TestFixture]
    public sealed class SqlServerStorageTests
    {
        private static readonly string[] _configuredQueues = ["default", "critical"];
        private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(30);

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            await TestDatabase.Instance.ResetAsync();
        }

        [SetUp]
        public void SetUp()
        {
            TaskRunLog.Reset();
            while (GatedTask.Started.Wait(0))
            {
            }

            while (GatedTask.Gate.Wait(0))
            {
            }
        }

        [Test]
        public async Task PrepareSchemaTrue_InstallsTheHangfireTables_InTheConfiguredSchema()
        {
            // Arrange
            using IHost host = HangfireSqlHost.CreateBuilder().Build();

            // Act
            await host.StartAsync();
            await host.StopAsync();

            // Assert
            Assert.Multiple(async () =>
            {
                Assert.That(await TestDatabase.Instance.TableExistsAsync(HangfireSqlHost.Schema, "Job"), Is.True);
                Assert.That(await TestDatabase.Instance.TableExistsAsync(HangfireSqlHost.Schema, "Hash"), Is.True);
            });
        }

        [Test]
        public void PrepareSchemaFalse_AgainstAMissingSchema_FaultsProcessingHostStartup()
        {
            // Arrange — a schema nobody installed.
            using IHost host = HangfireSqlHost.CreateBuilder(schema: "hangfire_missing", prepareSchema: false).Build();

            // Act & Assert
            Assert.That(() => host.StartAsync(), Throws.Exception);
        }

        [Test]
        public async Task ProcessingHost_SchedulesItsTasks_AndATriggeredJobRunsThroughTheRealServer()
        {
            // Arrange
            using IHost host = HangfireSqlHost.CreateBuilder().Build();
            await host.StartAsync();

            try
            {
                RecurringJobDto stored = RecurringJobs(host).Single(job => job.Id == "recording-task");

                // Act
                host.Services.GetRequiredService<IRecurringJobManager>().Trigger("recording-task");
                bool ran = await HangfireSqlHost.WaitUntilAsync(() => TaskRunLog.Runs.Contains("recording-task"), _deadline);

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(stored.Cron, Is.EqualTo("0 0 1 1 *"));
                    Assert.That(stored.Job?.Type, Is.EqualTo(typeof(RecurringTaskRunner)));
                    Assert.That(ran, Is.True, "The triggered job never ran through the server.");
                    Assert.That(TaskRunLog.Runs.Count(run => run == "recording-task"), Is.EqualTo(1));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task ProcessingHost_ServerOptions_AreVisibleOnTheLiveServer()
        {
            // Arrange
            using IHost host = HangfireSqlHost.CreateBuilder().Build();
            await host.StartAsync();

            try
            {
                IMonitoringApi monitoring = host.Services.GetRequiredService<JobStorage>().GetMonitoringApi();

                // Act
                bool announced = await HangfireSqlHost.WaitUntilAsync(() => monitoring.Servers().Count == 1, _deadline);
                ServerDto server = monitoring.Servers().Single();

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(announced, Is.True);
                    Assert.That(server.WorkersCount, Is.EqualTo(HangfireSqlHost.WorkerCount));
                    Assert.That(server.Queues, Is.EquivalentTo(_configuredQueues));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task Reconciliation_OnRealStorage_RemovesTheOrphan_KeepsTheForeignJob()
        {
            // Arrange — host A declares one extra task, then stops; a foreign job is added next to it.
            HostApplicationBuilder builderA = HangfireSqlHost.CreateBuilder();
            builderA.Services.AddTransient<IBackgroundRecurringTask, SameIdTask<First>>();
            using (IHost hostA = builderA.Build())
            {
                await hostA.StartAsync();
                hostA.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate(
                    "foreign-job",
                    Job.FromExpression(() => ForeignJobs.Run()),
                    "0 0 1 1 *",
                    new RecurringJobOptions());
                Assert.That(RecurringJobs(hostA).Select(job => job.Id), Does.Contain("duplicate-id").And.Contain("foreign-job"));
                await hostA.StopAsync();
            }

            // Act — host B declares fewer tasks.
            using IHost hostB = HangfireSqlHost.CreateBuilder().Build();
            await hostB.StartAsync();
            string[] remaining = [.. RecurringJobs(hostB).Select(job => job.Id)];
            await hostB.StopAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(remaining, Does.Not.Contain("duplicate-id"));
                Assert.That(remaining, Does.Contain("foreign-job"));
                Assert.That(remaining, Does.Contain("recording-task"));
            });
        }

        [Test]
        public async Task OverlapLock_OnRealStorage_SecondTriggerIsSkippedWhileTheFirstRunHoldsTheLock()
        {
            // Arrange
            FakeLogCollector logs = new();
            using IHost host = HangfireSqlHost.CreateBuilder(logs).Build();
            await host.StartAsync();
            IRecurringJobManager manager = host.Services.GetRequiredService<IRecurringJobManager>();

            try
            {
                // Act — the first run blocks inside the lock; the second trigger finds it taken.
                manager.Trigger(nameof(GatedTask));
                Assert.That(await GatedTask.Started.WaitAsync(_deadline), Is.True, "The first run never started.");

                manager.Trigger(nameof(GatedTask));
                bool skipped = await HangfireSqlHost.WaitUntilAsync(
                    () => logs.GetSnapshot().Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains($"{nameof(GatedTask)} skipped", StringComparison.Ordinal)),
                    _deadline);
                GatedTask.Gate.Release();

                // After release, a third trigger runs: the lock was freed.
                manager.Trigger(nameof(GatedTask));
                bool thirdStarted = await GatedTask.Started.WaitAsync(_deadline);
                GatedTask.Gate.Release();

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(skipped, Is.True, "The second trigger was not skipped.");
                    Assert.That(thirdStarted, Is.True, "The lock was not released after the first run.");
                    Assert.That(GatedTask.Started.CurrentCount, Is.Zero, "More runs started than expected.");
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public async Task StorageOnlyHost_OnTheSameSchema_ListsTheProcessingHostsJobs_AndRegistersNoServer()
        {
            // Arrange
            using IHost processing = HangfireSqlHost.CreateBuilder().Build();
            await processing.StartAsync();

            try
            {
                string[] declared = [.. processing.Services.GetServices<IBackgroundRecurringTask>().Select(task => task.JobId)];
                using IHost storageOnly = HangfireSqlHost.CreateBuilder(runServer: false).Build();
                await storageOnly.StartAsync();
                IMonitoringApi monitoring = storageOnly.Services.GetRequiredService<JobStorage>().GetMonitoringApi();
                await HangfireSqlHost.WaitUntilAsync(() => monitoring.Servers().Count == 1, _deadline);

                // Act
                RecurringJobDto[] seen = [.. RecurringJobs(storageOnly)];
                int servers = monitoring.Servers().Count;
                await storageOnly.StopAsync();

                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(seen.Select(job => job.Id), Is.SupersetOf(declared.Where(id => id != nameof(DisabledInCodeTask))));
                    Assert.That(seen.Where(job => declared.Contains(job.Id)).Select(job => job.Job?.Type), Is.All.EqualTo(typeof(RecurringTaskRunner)));
                    Assert.That(servers, Is.EqualTo(1), "Only the processing host announces a server.");
                });
            }
            finally
            {
                await processing.StopAsync();
            }
        }

        private static List<RecurringJobDto> RecurringJobs(IHost host)
        {
            using IStorageConnection connection = host.Services.GetRequiredService<JobStorage>().GetConnection();
            return connection.GetRecurringJobs();
        }

        /// <summary>A job type owned by some other component.</summary>
        public static class ForeignJobs
        {
            public static void Run()
            {
            }
        }
    }
}
