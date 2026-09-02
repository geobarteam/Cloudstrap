namespace Cloudstrap.Messaging.Tests
{
    using System.Diagnostics;
    using Cloudstrap.Messaging.Tests.Fixtures;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using OpenTelemetry;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;
    using Wolverine;

    /// <summary>
    /// Additive telemetry (AC-MSG11, the Aspire-coexistence posture): Wolverine's spans land in whatever
    /// OpenTelemetry pipeline the host owns, the package registers no exporter and no provider of its own,
    /// and a host without any pipeline still starts.
    /// </summary>
    [TestFixture]
    public sealed class ObservabilityTests
    {
        private static readonly TimeSpan _handlerTimeout = TimeSpan.FromSeconds(15);

        [Test]
        public async Task Messaging_HostWithOtelPipeline_WolverineSpansAppearInThatPipeline()
        {
            // Arrange — the host owns its pipeline; Cloudstrap only contributes the Wolverine source.
            List<Activity> exported = [];
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();
            await host.StartAsync();

            // Act — one publish/handle round trip.
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new PingCommand("traced"));
            }

            await host.Services.GetRequiredService<InvocationRecorder>().WaitForNextAsync(_handlerTimeout);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await host.StopAsync();

            // Assert
            Assert.That(exported.Select(activity => activity.Source.Name), Does.Contain("Wolverine"));
        }

        [Test]
        public void Messaging_RegistersNoExporterAndNoSecondTracerProvider()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());

            // Act
            builder.AddCloudstrapMessaging();

            // Assert — the tripwire: no exporter, no TracerProvider, no MeterProvider registered by the package.
            string[] descriptors = [.. builder.Services.Select(descriptor =>
                $"{descriptor.ServiceType.FullName}|{descriptor.ImplementationType?.FullName}|{descriptor.ImplementationInstance?.GetType().FullName}")];
            Assert.Multiple(() =>
            {
                Assert.That(descriptors, Has.None.Contains("Exporter"));
                Assert.That(builder.Services.Any(d => d.ServiceType == typeof(TracerProvider)), Is.False);
                Assert.That(builder.Services.Any(d => d.ServiceType == typeof(MeterProvider)), Is.False);
            });
        }

        [Test]
        public async Task Messaging_NoOtelPipeline_HostStillStarts()
        {
            // Arrange — no OpenTelemetry registration at all.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(new PingCommand("quiet"));
            }

            await host.StopAsync();

            // Assert
            Assert.That(host.Services.GetRequiredService<InvocationRecorder>().Received, Has.Count.EqualTo(1));
        }
    }
}
