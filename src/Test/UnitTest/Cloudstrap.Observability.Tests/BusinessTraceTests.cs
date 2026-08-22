namespace Cloudstrap.Observability.Tests
{
    using System.Diagnostics;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;

    [TestFixture]
    public sealed class BusinessTraceTests
    {
        private TextWriter _originalConsoleOutput = null!;
        private StringWriter _consoleOutput = null!;

        [SetUp]
        public void SetUp()
        {
            _originalConsoleOutput = Console.Out;
            _consoleOutput = new StringWriter();
            Console.SetOut(_consoleOutput);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(_originalConsoleOutput);
            _consoleOutput.Dispose();
        }

        [Test]
        public void StartSpan_WithListener_CreatesActivityFromBusinessSourceWithComponentTag()
        {
            // Arrange
            List<Activity> started = [];
            using ActivityListener listener = ListenToBusinessSource(started);
            using ServiceProvider provider = BuildBusinessTraceProvider();
            IBusinessTrace trace = provider.GetRequiredService<IBusinessTrace>();

            // Act
            using (IBusinessTraceScope scope = trace.StartSpan("Contoso.Orders.Submit", "OrderService"))
            {
                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(scope.IsRecording, Is.True);
                    Assert.That(started, Has.Count.EqualTo(1));
                    Assert.That(started[0].Source.Name, Is.EqualTo(CloudstrapActivitySources.Business));
                    Assert.That(started[0].DisplayName, Is.EqualTo("Contoso.Orders.Submit"));
                    Assert.That(
                        started[0].TagObjects.Single(tag => tag.Key == "cloudstrap.business.component").Value,
                        Is.EqualTo("OrderService"));
                });
            }
        }

        [Test]
        public void StartSpan_WithoutListener_ReturnsNonRecordingScopeAndDoesNotThrow()
        {
            // Arrange — no listener anywhere: a disabled pipeline never breaks consumers
            using ServiceProvider provider = BuildBusinessTraceProvider();
            IBusinessTrace trace = provider.GetRequiredService<IBusinessTrace>();

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                using IBusinessTraceScope scope = trace.StartSpan("Contoso.Orders.Submit", "OrderService");
                Assert.That(scope.IsRecording, Is.False);
                scope.SetOutcome("succeeded");
            });
        }

        [Test]
        public void SetOutcome_OnRecordingScope_SetsOutcomeTag()
        {
            // Arrange
            List<Activity> started = [];
            using ActivityListener listener = ListenToBusinessSource(started);
            using ServiceProvider provider = BuildBusinessTraceProvider();
            IBusinessTrace trace = provider.GetRequiredService<IBusinessTrace>();

            // Act
            using (IBusinessTraceScope scope = trace.StartSpan("Contoso.Orders.Submit", "OrderService"))
            {
                scope.SetOutcome("succeeded");
            }

            // Assert
            Assert.That(
                started[0].TagObjects.Single(tag => tag.Key == "cloudstrap.business.outcome").Value,
                Is.EqualTo("succeeded"));
        }

        [Test]
        public void Dispose_OnRecordingScope_StopsTheActivity()
        {
            // Arrange
            List<Activity> started = [];
            List<Activity> stopped = [];
            using ActivityListener listener = ListenToBusinessSource(started, stopped);
            using ServiceProvider provider = BuildBusinessTraceProvider();
            IBusinessTrace trace = provider.GetRequiredService<IBusinessTrace>();

            // Act
            IBusinessTraceScope scope = trace.StartSpan("Contoso.Orders.Submit", "OrderService");
            scope.Dispose();

            // Assert
            Assert.That(stopped.Select(activity => activity.DisplayName), Does.Contain("Contoso.Orders.Submit"));
        }

        [Test]
        public void UseCloudstrapObservability_OwnerMode_ExportsBusinessSpanThroughPipeline()
        {
            // Arrange — no consumer AddSource: the pipeline pre-wires Cloudstrap.Business
            List<Activity> exported = [];
            HostApplicationBuilder builder = CreateBuilder(ConsoleModeValid());
            builder.UseCloudstrapObservability(options =>
                options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(exported));
            using IHost host = builder.Build();
            TracerProvider tracerProvider = host.Services.GetRequiredService<TracerProvider>();
            IBusinessTrace trace = host.Services.GetRequiredService<IBusinessTrace>();

            // Act
            using (IBusinessTraceScope scope = trace.StartSpan("Contoso.Orders.Submit", "OrderService"))
            {
                scope.SetOutcome("succeeded");
            }

            tracerProvider.ForceFlush();

            // Assert
            Activity? businessSpan = exported.Find(activity => activity.DisplayName == "Contoso.Orders.Submit");
            Assert.That(businessSpan, Is.Not.Null);
            Assert.That(
                businessSpan!.TagObjects.Single(tag => tag.Key == "cloudstrap.business.outcome").Value,
                Is.EqualTo("succeeded"));
        }

        [Test]
        public async Task UseCloudstrapObservability_WithModeDisabled_ResolvesBusinessTraceAndCorrelation()
        {
            // Arrange — the composite AC-B1 proof
            HostApplicationBuilder builder = CreateBuilder(MinimalValid());
            builder.UseCloudstrapObservability();
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            try
            {
                // Assert
                List<string?> providers =
                    [.. host.Services.GetServices<ILoggerProvider>().Select(p => p.GetType().FullName)];
                Assert.Multiple(() =>
                {
                    Assert.That(host.Services.GetService<IBusinessTrace>(), Is.Not.Null);
                    Assert.That(host.Services.GetService<ICorrelationContextAccessor>(), Is.Not.Null);
                    Assert.That(host.Services.GetService<ICorrelationSource>(), Is.Not.Null);
                    Assert.That(host.Services.GetService<TracerProvider>(), Is.Null);
                    Assert.That(host.Services.GetService<MeterProvider>(), Is.Null);
                    Assert.That(providers, Has.Some.Contains("Serilog"));
                });
            }
            finally
            {
                await host.StopAsync();
            }
        }

        [Test]
        public void HealthCheckTags_HoldTheSharedVocabulary()
        {
            // Assert — the cross-package contract, pinned by test
            Assert.Multiple(() =>
            {
                Assert.That(CloudstrapHealthCheckTags.Liveness, Is.EqualTo("live"));
                Assert.That(CloudstrapHealthCheckTags.Readiness, Is.EqualTo("ready"));
            });
        }

        private static ActivityListener ListenToBusinessSource(
            List<Activity> started,
            List<Activity>? stopped = null)
        {
            ActivityListener listener = new()
            {
                ShouldListenTo = source => source.Name == CloudstrapActivitySources.Business,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = started.Add,
            };

            if (stopped is not null)
            {
                listener.ActivityStopped = stopped.Add;
            }

            ActivitySource.AddActivityListener(listener);

            return listener;
        }

        private static ServiceProvider BuildBusinessTraceProvider()
        {
            ServiceCollection services = new();
            services.AddCloudstrapBusinessTrace();

            return services.BuildServiceProvider();
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static Dictionary<string, string?> ConsoleModeValid()
        {
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:OpenTelemetry:Mode"] = "Console";
            return values;
        }

        private static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> values)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { DisableDefaults = true });
            builder.Configuration.AddInMemoryCollection(values);

            return builder;
        }
    }
}
