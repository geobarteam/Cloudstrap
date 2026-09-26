namespace Cloudstrap.Hangfire.Tests
{
    using Cloudstrap.Core;
    using Cloudstrap.Hangfire.Tests.Fakes;
    using Cloudstrap.Hangfire.Tests.Fixtures.Tasks;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using global::Hangfire.Common;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// Startup scheduling (AC-HF3 mocked, AC-HF4, AC-HF5, AC-HF6, AC-HF8 mocked): every enabled task is stored
    /// against the dispatcher, configuration wins over code, misconfiguration fails before the first write, and
    /// undeclared dispatcher-owned jobs are reconciled away — with no SQL Server.
    /// </summary>
    [TestFixture]
    public sealed class RecurringJobsSchedulerTests
    {
        private static readonly Type[] _dispatcherParameters = [typeof(string), typeof(CancellationToken)];
        private static readonly string[] _bothDisabled = [nameof(NightlyCleanupTask), nameof(DisabledInCodeTask)];
        private static readonly string[] _bothRetired = ["RetiredTask", "AnotherRetiredTask"];

        private RecordingRecurringJobManager _manager = null!;
        private FakeJobStorage _storage = null!;
        private CapturingLoggerProvider _logs = null!;
        private ILoggerFactory _loggerFactory = null!;

        [SetUp]
        public void SetUp()
        {
            _manager = new RecordingRecurringJobManager();
            _storage = new FakeJobStorage();
            _logs = new CapturingLoggerProvider();
            _loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        }

        [TearDown]
        public void TearDown()
        {
            _loggerFactory.Dispose();
            _logs.Dispose();
        }

        [Test]
        public void ScheduleAll_EnabledTasks_AreAddedAgainstTheDispatcher_NeverTheImplementationType()
        {
            // Arrange
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask(), new InternalReportTask()]);

            // Act
            scheduler.ScheduleAll();

            // Assert
            RecurringJobCall nightly = _manager.Added.Single(call => call.RecurringJobId == nameof(NightlyCleanupTask));
            RecurringJobCall report = _manager.Added.Single(call => call.RecurringJobId == nameof(InternalReportTask));
            Assert.Multiple(() =>
            {
                Assert.That(nightly.Job!.Type, Is.EqualTo(typeof(RecurringTaskRunner)));
                Assert.That(nightly.Job.Method.Name, Is.EqualTo(nameof(RecurringTaskRunner.RunAsync)));
                Assert.That(nightly.Job.Method.GetParameters().Select(p => p.ParameterType), Is.EqualTo(_dispatcherParameters));
                Assert.That(nightly.Job.Args[0], Is.EqualTo(nameof(NightlyCleanupTask)));
                Assert.That(nightly.Cron, Is.EqualTo("0 3 * * *"));
                Assert.That(nightly.Job.Queue, Is.EqualTo("default"));
                Assert.That(nightly.Options!.TimeZone, Is.EqualTo(TimeZoneInfo.Utc));
                Assert.That(report.Job!.Queue, Is.EqualTo("reports"));
            });
        }

        [Test]
        public void ScheduleAll_ConfigCronTimeZoneEnabled_WinOverCode_ForAnyCaseOfTheId()
        {
            // Arrange — the id is written in another case than the task's JobId.
            HangfireOptions options = new();
            options.Jobs["NIGHTLYCLEANUPTASK"] = new HangfireJobOptions { Cron = "0 4 * * *", TimeZone = "Europe/Brussels" };
            options.Jobs["disabledincodetask"] = new HangfireJobOptions { Enabled = true };
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask(), new DisabledInCodeTask()], options);

            // Act
            scheduler.ScheduleAll();

            // Assert
            RecurringJobCall nightly = _manager.Added.Single(call => call.RecurringJobId == nameof(NightlyCleanupTask));
            Assert.Multiple(() =>
            {
                Assert.That(nightly.Cron, Is.EqualTo("0 4 * * *"));
                Assert.That(nightly.Options!.TimeZone.Id, Is.EqualTo(TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels").Id));
                Assert.That(_manager.Added.Select(call => call.RecurringJobId), Does.Contain(nameof(DisabledInCodeTask)));
            });
        }

        [Test]
        public void ScheduleAll_EmptyConfigValues_FallBackToTheTasksOwn()
        {
            // Arrange
            HangfireOptions options = new();
            options.Jobs[nameof(NightlyCleanupTask)] = new HangfireJobOptions { Cron = " ", TimeZone = string.Empty };
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask()], options);

            // Act
            scheduler.ScheduleAll();

            // Assert
            RecurringJobCall nightly = _manager.Added.Single();
            Assert.Multiple(() =>
            {
                Assert.That(nightly.Cron, Is.EqualTo("0 3 * * *"));
                Assert.That(nightly.Options!.TimeZone, Is.EqualTo(TimeZoneInfo.Utc));
            });
        }

        [Test]
        public void ScheduleAll_DisabledByConfigOrByCode_RemovesIfExists_AndSchedulesNothing()
        {
            // Arrange
            HangfireOptions options = new();
            options.Jobs[nameof(NightlyCleanupTask)] = new HangfireJobOptions { Enabled = false };
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask(), new DisabledInCodeTask()], options);

            // Act
            scheduler.ScheduleAll();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(_manager.Added, Is.Empty);
                Assert.That(_manager.Removed, Is.EquivalentTo(_bothDisabled));
            });
        }

        [Test]
        public void ScheduleAll_DuplicateJobIds_FailsNamingTheIdAndBothTypes_WritesAndRemovesNothing()
        {
            // Arrange
            _storage.Connection.AddRecurringJob("orphan", Job.FromExpression<RecurringTaskRunner>(runner => runner.RunAsync("orphan", CancellationToken.None)));
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask(), new SameIdTask<First>(), new SameIdTask<Second>()]);

            // Act
            ConfigurationValidationException? failure = Assert.Throws<ConfigurationValidationException>(() => scheduler.ScheduleAll());

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Does.Contain("'duplicate-id'"));
                Assert.That(failure.Message, Does.Contain(typeof(SameIdTask<First>).FullName));
                Assert.That(failure.Message, Does.Contain(typeof(SameIdTask<Second>).FullName));
                Assert.That(_manager.Calls, Is.Empty);
            });
        }

        [Test]
        public void ScheduleAll_UnknownTimeZone_FailsNamingJobAndValueWithTheIanaHint_NotAsACronError()
        {
            // Arrange
            HangfireOptions options = new();
            options.Jobs[nameof(NightlyCleanupTask)] = new HangfireJobOptions { TimeZone = "Not/AZone" };
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask()], options);

            // Act
            ConfigurationValidationException? failure = Assert.Throws<ConfigurationValidationException>(() => scheduler.ScheduleAll());

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Does.Contain(nameof(NightlyCleanupTask)));
                Assert.That(failure.Message, Does.Contain("'Not/AZone'"));
                Assert.That(failure.Message, Does.Contain("'Cloudstrap:Hangfire:Jobs:NightlyCleanupTask:TimeZone'"));
                Assert.That(failure.Message, Does.Contain("IANA"));
                Assert.That(failure.Message, Does.Not.Contain("cron").IgnoreCase);
            });
        }

        [Test]
        public void ScheduleAll_TimeZoneValidation_CoversEveryTaskBeforeTheFirstWrite()
        {
            // Arrange — the second task is the invalid one.
            HangfireOptions options = new();
            options.Jobs[nameof(InternalReportTask)] = new HangfireJobOptions { TimeZone = "Not/AZone" };
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask(), new InternalReportTask()], options);

            // Act & Assert
            Assert.Multiple(() =>
            {
                Assert.That(() => scheduler.ScheduleAll(), Throws.TypeOf<ConfigurationValidationException>());
                Assert.That(_manager.Calls, Is.Empty);
            });
        }

        [Test]
        public void ScheduleAll_InvalidConfiguredCron_FailsNamingJobAndValue()
        {
            // Arrange
            HangfireOptions options = new();
            options.Jobs[nameof(NightlyCleanupTask)] = new HangfireJobOptions { Cron = "every night" };
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask()], options);

            // Act
            ConfigurationValidationException? failure = Assert.Throws<ConfigurationValidationException>(() => scheduler.ScheduleAll());

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure!.Message, Does.Contain(nameof(NightlyCleanupTask)));
                Assert.That(failure.Message, Does.Contain("'every night'"));
                Assert.That(failure.Message, Does.Contain("'Cloudstrap:Hangfire:Jobs:NightlyCleanupTask:Cron'"));
                Assert.That(failure.Message, Does.Not.Contain("time zone").IgnoreCase);
                Assert.That(failure.InnerException, Is.TypeOf<ArgumentException>());
            });
        }

        [Test]
        public void ScheduleAll_UndeclaredDispatcherOwnedJobs_AreRemoved_OneInfoLogEach()
        {
            // Arrange
            _storage.Connection.AddRecurringJob("RetiredTask", DispatcherJob("RetiredTask"));
            _storage.Connection.AddRecurringJob("AnotherRetiredTask", DispatcherJob("AnotherRetiredTask"));
            _storage.Connection.AddRecurringJob(nameof(NightlyCleanupTask), DispatcherJob(nameof(NightlyCleanupTask)));
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask()]);

            // Act
            scheduler.ScheduleAll();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(_manager.Removed, Is.EquivalentTo(_bothRetired));
                Assert.That(InfoLogsContaining("RetiredTask"), Has.Count.EqualTo(2));
                Assert.That(_storage.Connection.DisposeCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void ScheduleAll_ForeignAndUnloadableJobs_AreNeverTouched()
        {
            // Arrange — another component's job (another type) and a job whose type cannot be loaded.
            _storage.Connection.AddRecurringJob("foreign", Job.FromExpression(() => ForeignComponent.Run()));
            _storage.Connection.AddRecurringJob("unloadable", job: null);
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask()]);

            // Act
            scheduler.ScheduleAll();

            // Assert
            Assert.That(_manager.Removed, Is.Empty);
        }

        [Test]
        public void ScheduleAll_ZeroDeclaredTasks_SkipsReconciliationEntirely_OneInfoLog()
        {
            // Arrange
            _storage.Connection.AddRecurringJob("RetiredTask", DispatcherJob("RetiredTask"));
            RecurringJobsScheduler scheduler = CreateScheduler([]);

            // Act
            scheduler.ScheduleAll();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(_manager.Calls, Is.Empty);
                Assert.That(_storage.ConnectionsOpened, Is.Zero);
                Assert.That(InfoLogsContaining("reconciliation"), Has.Count.EqualTo(1));
            });
        }

        [Test]
        public void ScheduleAll_JobsOverrideForAnUndeclaredId_LogsOneWarning()
        {
            // Arrange
            HangfireOptions options = new();
            options.Jobs["TypoTask"] = new HangfireJobOptions { Enabled = false };
            RecurringJobsScheduler scheduler = CreateScheduler([new NightlyCleanupTask()], options);

            // Act
            scheduler.ScheduleAll();

            // Assert
            CapturedLogEntry[] warnings = [.. _logs.Entries.Where(entry => entry.Level == LogLevel.Warning)];
            Assert.Multiple(() =>
            {
                Assert.That(warnings, Has.Length.EqualTo(1));
                Assert.That(warnings[0].Message, Does.Contain("TypoTask"));
            });
        }

        [Test]
        public void ScheduleAll_ReturnsOneEntryPerTask_ForTheStartupSummary()
        {
            // Arrange
            RecurringJobsScheduler scheduler = CreateScheduler([new InternalReportTask(), new DisabledInCodeTask()]);

            // Act
            IReadOnlyList<ScheduledRecurringJob> scheduled = scheduler.ScheduleAll();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(scheduled, Has.Count.EqualTo(2));
                Assert.That(scheduled.Single(job => job.JobId == nameof(InternalReportTask)).Queue, Is.EqualTo("reports"));
                Assert.That(scheduled.Single(job => job.JobId == nameof(DisabledInCodeTask)).Enabled, Is.False);
            });
        }

        private static Job DispatcherJob(string jobId)
        {
            return Job.FromExpression<RecurringTaskRunner>(runner => runner.RunAsync(jobId, CancellationToken.None));
        }

        private RecurringJobsScheduler CreateScheduler(IBackgroundRecurringTask[] tasks, HangfireOptions? options = null)
        {
            return new RecurringJobsScheduler(
                _manager,
                tasks,
                _storage,
                Microsoft.Extensions.Options.Options.Create(options ?? new HangfireOptions()),
                _loggerFactory.CreateLogger<RecurringJobsScheduler>());
        }

        private List<CapturedLogEntry> InfoLogsContaining(string fragment)
        {
            return [.. _logs.Entries.Where(entry => entry.Level == LogLevel.Information && entry.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))];
        }

        /// <summary>A job type owned by some other component (never the dispatcher).</summary>
        public sealed class ForeignComponent
        {
            public static void Run()
            {
            }
        }
    }
}
