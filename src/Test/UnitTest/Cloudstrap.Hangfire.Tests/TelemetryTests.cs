namespace Cloudstrap.Hangfire.Tests
{
    using System.Diagnostics;
    using Cloudstrap.Hangfire.Tests.Fakes;
    using Cloudstrap.Hangfire.Tests.Fixtures.Tasks;
    using Cloudstrap.Hangfire.Tests.Infrastructure;
    using global::Hangfire;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging.Abstractions;
    using NUnit.Framework;
    using OpenTelemetry;
    using OpenTelemetry.Trace;

    /// <summary>
    /// Run spans (AC-HF15): each run is one <c>RecurringTask {jobId}</c> span on the contributed
    /// <c>Cloudstrap.Hangfire</c> source, reaching whatever pipeline the host owns, and inert without one.
    /// </summary>
    [TestFixture]
    public sealed class TelemetryTests
    {
        [Test]
        public async Task RunAsync_EmitsOneSpanNamedRecurringTaskJobId_WithTheJobIdTag_StatusOk()
        {
            // Arrange
            List<Activity> spans = [];
            using ActivityListener listener = Listen(spans);
            RecurringTaskRunner runner = CreateRunner(new NightlyCleanupTask());

            // Act
            await runner.RunAsync(nameof(NightlyCleanupTask), CancellationToken.None);

            // Assert
            Activity span = spans.Single();
            Assert.Multiple(() =>
            {
                Assert.That(span.DisplayName, Is.EqualTo("RecurringTask NightlyCleanupTask"));
                Assert.That(span.GetTagItem("cloudstrap.hangfire.job_id"), Is.EqualTo(nameof(NightlyCleanupTask)));
                Assert.That(span.Status, Is.EqualTo(ActivityStatusCode.Ok));
            });
        }

        [Test]
        public void RunAsync_TaskThrows_SpanStatusIsError()
        {
            // Arrange
            List<Activity> spans = [];
            using ActivityListener listener = Listen(spans);
            RecurringTaskRunner runner = CreateRunner(new ThrowingTask());

            // Act
            Assert.That(() => runner.RunAsync(nameof(ThrowingTask), CancellationToken.None), Throws.Exception);

            // Assert
            Assert.That(spans.Single().Status, Is.EqualTo(ActivityStatusCode.Error));
        }

        [Test]
        public async Task ConsumerOwnedPipeline_ReceivesTheSpan_ThroughTheContributedSource()
        {
            // Arrange — the consumer owns the pipeline; no Cloudstrap observability is involved.
            List<Activity> exported = [];
            FakeJobStorage storage = new();
            HostApplicationBuilder builder = HangfireTestHost.CreateBuilder();
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
            builder.AddCloudstrapHangfire(hangfire =>
            {
                hangfire.RunServer = false;
                hangfire.Storage = configuration => configuration.UseStorage(storage);
            });
            using IHost host = builder.Build();
            _ = host.Services.GetRequiredService<TracerProvider>();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<RecurringTaskRunner>().RunAsync(nameof(NightlyCleanupTask), CancellationToken.None);
            }

            // Assert
            Assert.That(exported.Select(activity => activity.DisplayName), Does.Contain("RecurringTask NightlyCleanupTask"));
        }

        [Test]
        public async Task NoPipeline_RunAsyncStillRuns_NoOp()
        {
            // Arrange — no listener at all.
            RecurringTaskRunner runner = CreateRunner(new TokenCapturingTask());
            using CancellationTokenSource cancellation = new();

            // Act
            await runner.RunAsync(nameof(TokenCapturingTask), cancellation.Token);

            // Assert
            Assert.That(TokenCapturingTask.LastToken, Is.EqualTo(cancellation.Token));
        }

        private static ActivityListener Listen(List<Activity> spans)
        {
            ActivityListener listener = new()
            {
                ShouldListenTo = source => source.Name == CloudstrapHangfireActivitySources.RecurringTask,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = spans.Add,
            };
            ActivitySource.AddActivityListener(listener);
            return listener;
        }

        private static RecurringTaskRunner CreateRunner(params IBackgroundRecurringTask[] tasks)
        {
            return new RecurringTaskRunner(tasks, new FakeJobStorage(), NullLogger<RecurringTaskRunner>.Instance);
        }
    }
}
