namespace Cloudstrap.Hangfire.Tests
{
    using System.Reflection;
    using Cloudstrap.Hangfire.Tests.Fakes;
    using Cloudstrap.Hangfire.Tests.Fixtures;
    using Cloudstrap.Hangfire.Tests.Fixtures.Tasks;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using global::Hangfire;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// The persisted dispatcher (AC-HF7 mocked, AC-HF16, mechanic (h)): it resolves the task by id, guards each run
    /// with a zero-wait distributed lock, logs start / completion / failure without payloads, and rethrows so
    /// Hangfire retries.
    /// </summary>
    [TestFixture]
    public sealed class RecurringTaskRunnerTests
    {
        private static readonly string[] _recordingLock = ["recurring-task:recording-task"];
        private static readonly string[] _recordingRun = ["recording-task"];

        private FakeJobStorage _storage = null!;
        private CapturingLoggerProvider _logs = null!;
        private ILoggerFactory _loggerFactory = null!;

        [SetUp]
        public void SetUp()
        {
            TaskRunLog.Reset();
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
        public async Task RunAsync_ResolvesTheTaskByJobId_AndPassesTheCancellationToken()
        {
            // Arrange
            using CancellationTokenSource cancellation = new();
            RecurringTaskRunner runner = CreateRunner(new NightlyCleanupTask(), new TokenCapturingTask());

            // Act
            await runner.RunAsync(nameof(TokenCapturingTask), cancellation.Token);

            // Assert
            Assert.That(TokenCapturingTask.LastToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public void RunAsync_UnknownJobId_ThrowsInvalidOperationExceptionNamingTheId()
        {
            // Arrange
            RecurringTaskRunner runner = CreateRunner(new NightlyCleanupTask());

            // Act & Assert
            Assert.That(
                () => runner.RunAsync("MissingTask", CancellationToken.None),
                Throws.InvalidOperationException.With.Message.Contains("'MissingTask'"));
        }

        [Test]
        public async Task RunAsync_OverlapGuarded_AcquiresRecurringTaskLockWithZeroWait_AndReleasesItAfterTheRun()
        {
            // Arrange
            RecurringTaskRunner runner = CreateRunner(new RecordingTask());

            // Act
            await runner.RunAsync("recording-task", CancellationToken.None);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(_storage.Connection.LockRequests, Is.EqualTo(_recordingLock));
                Assert.That(_storage.Connection.LockTimeouts, Is.EqualTo(new[] { TimeSpan.Zero }));
                Assert.That(_storage.Connection.LocksReleased, Is.EqualTo(1));
                Assert.That(_storage.Connection.DisposeCount, Is.EqualTo(1));
                Assert.That(TaskRunLog.Runs, Is.EqualTo(_recordingRun));
            });
        }

        [Test]
        public async Task RunAsync_LockBusy_SkipsTheRun_WithOneWarningNamingTheJob()
        {
            // Arrange
            _storage.Connection.LockAlwaysBusy = true;
            RecurringTaskRunner runner = CreateRunner(new RecordingTask());

            // Act
            await runner.RunAsync("recording-task", CancellationToken.None);

            // Assert
            CapturedLogEntry[] warnings = [.. _logs.Entries.Where(entry => entry.Level == LogLevel.Warning)];
            Assert.Multiple(() =>
            {
                Assert.That(TaskRunLog.Runs, Is.Empty);
                Assert.That(warnings, Has.Length.EqualTo(1));
                Assert.That(warnings[0].Message, Does.Contain("recording-task").And.Contain("skipped"));
            });
        }

        [Test]
        public async Task RunAsync_PreventOverlappingRunsFalse_TakesNoLock()
        {
            // Arrange
            RecurringTaskRunner runner = CreateRunner(new NoOverlapGuardTask());

            // Act
            await runner.RunAsync(nameof(NoOverlapGuardTask), CancellationToken.None);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(_storage.Connection.LockRequests, Is.Empty);
                Assert.That(_storage.ConnectionsOpened, Is.Zero);
                Assert.That(TaskRunLog.Runs, Is.EqualTo(new[] { nameof(NoOverlapGuardTask) }));
            });
        }

        [Test]
        public void RunAsync_TaskThrows_LogsFailedWithElapsedAndException_AndRethrows()
        {
            // Arrange
            RecurringTaskRunner runner = CreateRunner(new ThrowingTask());

            // Act
            Assert.That(() => runner.RunAsync(nameof(ThrowingTask), CancellationToken.None), Throws.InvalidOperationException);

            // Assert
            CapturedLogEntry failure = _logs.Entries.Single(entry => entry.Level == LogLevel.Error);
            Assert.Multiple(() =>
            {
                Assert.That(failure.Message, Does.Contain(nameof(ThrowingTask)).And.Contain("failed after").And.Contain("ms"));
                Assert.That(failure.Exception, Is.TypeOf<InvalidOperationException>());
                Assert.That(failure.Message, Does.Not.Contain("on purpose"), "The exception message stays inside the exception object.");
                Assert.That(_storage.Connection.LocksReleased, Is.EqualTo(1), "The lock is released after a failed run.");
            });
        }

        [Test]
        public async Task RunAsync_Completes_LogsStartedAndCompletedWithElapsed_NeverArgumentsOrPayloads()
        {
            // Arrange
            RecurringTaskRunner runner = CreateRunner(new TokenCapturingTask());

            // Act
            await runner.RunAsync(nameof(TokenCapturingTask), CancellationToken.None);

            // Assert
            string[] messages = [.. _logs.Entries.Select(entry => entry.Message)];
            Assert.Multiple(() =>
            {
                Assert.That(messages, Has.Some.EqualTo($"Recurring task {nameof(TokenCapturingTask)} started"));
                Assert.That(messages, Has.Some.Match($"^Recurring task {nameof(TokenCapturingTask)} completed in [0-9.,]+ ms$"));
                Assert.That(messages, Has.None.Contains(TokenCapturingTask.Sentinel));
            });
        }

        [Test]
        public void RunAsync_CarriesDisplayNameShowingTheJobId()
        {
            // Arrange
            MethodInfo runAsync = typeof(RecurringTaskRunner).GetMethod(nameof(RecurringTaskRunner.RunAsync))!;

            // Act
            JobDisplayNameAttribute? displayName = runAsync.GetCustomAttribute<JobDisplayNameAttribute>();

            // Assert
            Assert.That(displayName?.DisplayName, Is.EqualTo("{0}"));
        }

        private RecurringTaskRunner CreateRunner(params IBackgroundRecurringTask[] tasks)
        {
            return new RecurringTaskRunner(tasks, _storage, _loggerFactory.CreateLogger<RecurringTaskRunner>());
        }
    }
}
