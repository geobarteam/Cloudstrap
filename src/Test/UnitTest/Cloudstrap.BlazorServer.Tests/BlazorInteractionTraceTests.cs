namespace Cloudstrap.BlazorServer.Tests
{
    using System.Diagnostics;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using OpenTelemetry.Trace;

    /// <summary>
    /// Pins the D-9 interaction trace: a detached, correlated root span with full restore semantics, an
    /// honest no-op without a listener, and an activity source contributed additively to whatever
    /// OpenTelemetry pipeline the host owns — never a pipeline of the package's own (AC-BS5, AC-BS6).
    /// </summary>
    [TestFixture]
    public sealed class BlazorInteractionTraceTests
    {
        [TearDown]
        public void TearDown()
        {
            // No test leaks an ambient activity into the next.
            while (Activity.Current is not null)
            {
                Activity current = Activity.Current;
                current.Stop();

                if (ReferenceEquals(Activity.Current, current))
                {
                    Activity.Current = null;
                }
            }
        }

        [Test]
        public void StartInteraction_UnderAnAmbientActivity_StartsANewRootDetachedFromIt()
        {
            // Arrange — a fake hub activity is ambient, the way SignalR leaves one on the circuit
            List<Activity> started = [];
            using ActivityListener listener = ListenToInteractionSource(started);
            using BlazorInteractionTrace trace = CreateTrace(out _, out _);
            using Activity ambient = StartAmbientActivity();

            // Act
            using (trace.StartInteraction("checkout"))
            {
                // Assert
                Assert.Multiple(() =>
                {
                    Assert.That(started, Has.Count.EqualTo(1));
                    Assert.That(started[0].Source.Name, Is.EqualTo(BlazorServerActivitySources.Interaction));
                    Assert.That(started[0].DisplayName, Is.EqualTo("checkout"));
                    Assert.That(started[0].Parent, Is.Null);
                    Assert.That(started[0].TraceId, Is.Not.EqualTo(ambient.TraceId));
                });
            }
        }

        [Test]
        public void StartInteraction_PointsTheAmbientCorrelationIdAtTheInteractionTraceId()
        {
            // Arrange
            List<Activity> started = [];
            using ActivityListener listener = ListenToInteractionSource(started);
            using BlazorInteractionTrace trace = CreateTrace(out FakeCorrelationAccessor accessor, out _);

            // Act
            using (trace.StartInteraction("checkout"))
            {
                // Assert — the outbound header follows through #2's correlation handler
                Assert.That(accessor.CorrelationId, Is.EqualTo(started[0].TraceId.ToString()));
            }
        }

        [Test]
        public void StartInteraction_AChildStartedInsideTheScope_ParentsUnderTheInteractionRoot()
        {
            // Arrange
            List<Activity> started = [];
            using ActivityListener listener = ListenToInteractionSource(started);
            using BlazorInteractionTrace trace = CreateTrace(out _, out _);
            using Activity ambient = StartAmbientActivity();

            // Act
            using (trace.StartInteraction("checkout"))
            {
                using Activity child = new Activity("dependency-call").Start();

                // Assert — outbound work inside the scope belongs to the interaction, not the dropped hub trace
                Assert.That(child.TraceId, Is.EqualTo(started[0].TraceId));
            }
        }

        [Test]
        public void Dispose_RestoresThePreviousActivityAndCorrelationId()
        {
            // Arrange
            List<Activity> started = [];
            using ActivityListener listener = ListenToInteractionSource(started);
            using BlazorInteractionTrace trace = CreateTrace(out FakeCorrelationAccessor accessor, out _);
            using Activity ambient = StartAmbientActivity();
            accessor.CorrelationId = "previous-correlation";

            // Act
            IDisposable scope = trace.StartInteraction("checkout");
            scope.Dispose();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(Activity.Current, Is.SameAs(ambient));
                Assert.That(accessor.CorrelationId, Is.EqualTo("previous-correlation"));
                Assert.That(scope.Dispose, Throws.Nothing);
            });
        }

        [Test]
        public void StartInteraction_WithNoListener_IsASafeNoOpThatStillSetsAFreshCorrelationId()
        {
            // Arrange — no listener anywhere: a disabled pipeline never breaks consumers
            using BlazorInteractionTrace trace = CreateTrace(
                out FakeCorrelationAccessor accessor,
                out FakeCorrelationSource source);

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                using (trace.StartInteraction("checkout"))
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(Activity.Current, Is.Null);
                        Assert.That(accessor.CorrelationId, Is.EqualTo(source.LastGenerated));
                        Assert.That(accessor.CorrelationId, Is.Not.Null.And.Not.Empty);
                    });
                }
            });
        }

        [Test]
        public void StartInteraction_WithBlankName_ThrowsArgumentException()
        {
            // Arrange
            using BlazorInteractionTrace trace = CreateTrace(out _, out _);

            // Act & Assert
            Assert.That(() => trace.StartInteraction(" "), Throws.ArgumentException);
        }

        [Test]
        public void AddCloudstrapBlazorServer_RegistersTheInteractionTraceSingleton()
        {
            // Arrange & Act
            WebApplicationBuilder builder = CreateBuilder();
            builder.AddCloudstrapBlazorServer();

            // Assert — one descriptor, singleton, and a consumer's own registration wins
            List<ServiceDescriptor> descriptors =
                [.. builder.Services.Where(d => d.ServiceType == typeof(IBlazorInteractionTrace))];
            WebApplicationBuilder overridden = CreateBuilder();
            FakeInteractionTrace consumers = new();
            overridden.Services.AddSingleton<IBlazorInteractionTrace>(consumers);
            overridden.AddCloudstrapBlazorServer();

            Assert.Multiple(() =>
            {
                Assert.That(descriptors, Has.Count.EqualTo(1));
                Assert.That(descriptors[0].Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
                Assert.That(descriptors[0].ImplementationType, Is.EqualTo(typeof(BlazorInteractionTrace)));
                Assert.That(
                    overridden.Services.Count(d => d.ServiceType == typeof(IBlazorInteractionTrace)),
                    Is.EqualTo(1));
            });
        }

        [Test]
        public void AddCloudstrapBlazorServer_CreatesNoOpenTelemetryPipelineOfItsOwn()
        {
            // Arrange & Act — the composite alone on a plain builder
            WebApplicationBuilder builder = CreateBuilder();
            builder.AddCloudstrapBlazorServer();

            // Assert — no provider, no telemetry hosted service, no exporter: AC-BS6 structurally
            Assert.Multiple(() =>
            {
                Assert.That(
                    builder.Services.Any(d => d.ServiceType == typeof(TracerProvider)),
                    Is.False);
                Assert.That(
                    builder.Services.Any(d =>
                        d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType?.FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true),
                    Is.False);
                Assert.That(
                    builder.Services.Any(d =>
                        d.ImplementationType?.FullName?.Contains("Exporter", StringComparison.Ordinal) == true),
                    Is.False);
            });
        }

        [Test]
        public async Task InteractionSource_IsContributedAdditivelyToAHostOwnedPipeline()
        {
            // Arrange — the host owns the pipeline, Aspire-ServiceDefaults-style; the package only contributes
            List<Activity> exported = [];
            WebApplicationBuilder builder = CreateBuilder();
            builder.AddCloudstrapBlazorServer();
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
            WebApplication app = builder.Build();

            await using (app.ConfigureAwait(false))
            {
                TracerProvider tracerProvider = app.Services.GetRequiredService<TracerProvider>();
                IBlazorInteractionTrace trace = app.Services.GetRequiredService<IBlazorInteractionTrace>();

                // Act
                using (trace.StartInteraction("checkout"))
                {
                }

                tracerProvider.ForceFlush();

                // Assert — exactly one root span from the interaction source landed in the host's exporter
                List<Activity> interactions =
                    [.. exported.Where(a => a.Source.Name == BlazorServerActivitySources.Interaction)];
                Assert.Multiple(() =>
                {
                    Assert.That(interactions, Has.Count.EqualTo(1));
                    Assert.That(interactions[0].DisplayName, Is.EqualTo("checkout"));
                    Assert.That(interactions[0].Parent, Is.Null);
                    Assert.That(interactions[0].ParentId, Is.Null);
                });
            }
        }

        private static BlazorInteractionTrace CreateTrace(
            out FakeCorrelationAccessor accessor,
            out FakeCorrelationSource source)
        {
            accessor = new FakeCorrelationAccessor();
            source = new FakeCorrelationSource();

            return new BlazorInteractionTrace(accessor, source);
        }

        private static Activity StartAmbientActivity()
        {
            // Started directly, the way the SignalR hub leaves an ambient activity — no listener required.
            Activity ambient = new("hub-invocation");
            ambient.Start();

            return ambient;
        }

        private static ActivityListener ListenToInteractionSource(List<Activity> started)
        {
            ActivityListener listener = new()
            {
                ShouldListenTo = source => source.Name == BlazorServerActivitySources.Interaction,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = started.Add,
            };
            ActivitySource.AddActivityListener(listener);

            return listener;
        }

        private static WebApplicationBuilder CreateBuilder()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Production",
                ApplicationName = "Cloudstrap.BlazorServer",
            });

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "Contoso",
                ["Cloudstrap:Application:SubsystemName"] = "Catalog",
                ["Cloudstrap:Application:SubsystemType"] = "Web",
                ["Logging:LogLevel:Default"] = "Warning",
            });

            return builder;
        }

        private sealed class FakeCorrelationAccessor : ICorrelationContextAccessor
        {
            public string? CorrelationId
            {
                get; set;
            }
        }

        private sealed class FakeCorrelationSource : ICorrelationSource
        {
            public string? LastGenerated
            {
                get; private set;
            }

            public string GenerateCorrelation()
            {
                LastGenerated = Guid.NewGuid().ToString("N");

                return LastGenerated;
            }
        }

        private sealed class FakeInteractionTrace : IBlazorInteractionTrace
        {
            public IDisposable StartInteraction(string interactionName) => new FakeScope();

            private sealed class FakeScope : IDisposable
            {
                public void Dispose()
                {
                    // Nothing to release.
                }
            }
        }
    }
}
