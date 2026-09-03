namespace Cloudstrap.Messaging.Tests
{
    using System.Diagnostics;
    using Cloudstrap.Messaging.Tests.Fixtures;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using OpenTelemetry;
    using OpenTelemetry.Trace;
    using Wolverine;

    /// <summary>
    /// The business correlation id across the hop (AC-MSG9): stamped on the envelope under
    /// <c>Cloudstrap:Correlation:HeaderName</c> from the accessor, read back into the handler's accessor,
    /// with W3C <c>traceparent</c> flowing independently, and a fresh scope when nothing arrives and
    /// enforcement is off (AC-MSG10's off half). Local transport — no SQL, no network.
    /// </summary>
    [TestFixture]
    public sealed class CorrelationFlowTests
    {
        private const string _defaultHeader = "X-Correlation-ID";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

        [Test]
        public async Task Correlation_AccessorValueOnSend_ArrivesInTheRemoteHandlersAccessor()
        {
            // Arrange
            using IHost host = BuildHost(MessagingTestHost.ValidSettings());
            await host.StartAsync();

            // Act — the sender's ambient correlation id, then a publish.
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-123";
            await PublishAsync(host, new CorrelatedCommand("hello"));
            CorrelationObservation observed = await ObserveAsync(host);
            await host.StopAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(observed.AccessorValue, Is.EqualTo("corr-123"));
                Assert.That(observed.Headers, Does.ContainKey(_defaultHeader).WithValue("corr-123"));
            });
        }

        [Test]
        public async Task Correlation_ConfiguredHeaderName_IsUsedOnTheEnvelope()
        {
            // Arrange — #2's setting, redefined nowhere: one option drives both sides.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Correlation:HeaderName"] = "X-CUSTOM-ID";
            using IHost host = BuildHost(settings);
            await host.StartAsync();

            // Act
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-custom";
            await PublishAsync(host, new CorrelatedCommand("custom"));
            CorrelationObservation observed = await ObserveAsync(host);
            await host.StopAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(observed.AccessorValue, Is.EqualTo("corr-custom"));
                Assert.That(observed.Headers, Does.ContainKey("X-CUSTOM-ID").WithValue("corr-custom"));
                Assert.That(observed.Headers, Does.Not.ContainKey(_defaultHeader));
            });
        }

        [Test]
        public async Task Correlation_TraceparentFlowsIndependentlyOfTheBusinessHeader()
        {
            // Arrange — the host owns an OTel pipeline; no business correlation id is set at all.
            List<Activity> exported = [];
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource("Contoso.Test").AddInMemoryExporter(exported));
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();
            await host.StartAsync();
            using ActivitySource source = new("Contoso.Test");

            // Act — publish inside a test-owned root activity.
            ActivityTraceId rootTraceId;
            using (Activity root = source.StartActivity("test-root")!)
            {
                rootTraceId = root.TraceId;
                await PublishAsync(host, new CorrelatedCommand("traced"));
                await ObserveAsync(host);
            }

            await Task.Delay(500);
            await host.StopAsync();

            // Assert — the handler-side Wolverine span belongs to the root's trace; no business header needed.
            Assert.That(
                exported.Where(activity => activity.Source.Name == "Wolverine").Select(activity => activity.TraceId),
                Does.Contain(rootTraceId));
        }

        [Test]
        public async Task Correlation_NoInboundValue_EnforcementOff_HandlerRunsWithFreshScope()
        {
            // Arrange — nothing set, nothing enforced.
            using IHost host = BuildHost(MessagingTestHost.ValidSettings());
            await host.StartAsync();

            // Act
            await PublishAsync(host, new CorrelatedCommand("fresh"));
            CorrelationObservation observed = await ObserveAsync(host);
            await host.StopAsync();

            // Assert — the handler ran with a fresh (null) scope and no correlation header.
            Assert.Multiple(() =>
            {
                Assert.That(observed.AccessorValue, Is.Null);
                Assert.That(observed.Headers, Does.Not.ContainKey(_defaultHeader));
            });
        }

        private static IHost BuildHost(Dictionary<string, string?> settings)
        {
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging();
            return builder.Build();
        }

        private static async Task PublishAsync(IHost host, object message)
        {
            using IServiceScope scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(message);
        }

        private static async Task<CorrelationObservation> ObserveAsync(IHost host)
        {
            object observed = await host.Services.GetRequiredService<InvocationRecorder>().WaitForNextAsync(_timeout);
            return (CorrelationObservation)observed;
        }
    }
}
