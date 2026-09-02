namespace Cloudstrap.Messaging.Tests
{
    using Cloudstrap.Messaging.Tests.Fixtures;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using Wolverine;

    /// <summary>
    /// The zero-configuration default (AC-MSG1): one <c>AddCloudstrapMessaging()</c> call and no
    /// <c>Cloudstrap:Messaging</c> section turns a host into a working in-process messaging node whose
    /// endpoint identity is the workload name — no network, no SQL, no Azure.
    /// </summary>
    [TestFixture]
    public sealed class LocalNodeTests
    {
        private static readonly TimeSpan _handlerTimeout = TimeSpan.FromSeconds(30);

        [Test]
        public async Task AddCloudstrapMessaging_NoMessagingSection_HostStartsAndLocalMessageReachesItsHandler()
        {
            // Arrange — only Cloudstrap:Application is configured; no Cloudstrap:Messaging section at all.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();
            await host.StartAsync();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new PingCommand("hello"));
            }

            object received = await host.Services.GetRequiredService<InvocationRecorder>()
                .WaitForNextAsync(_handlerTimeout);
            await host.StopAsync();

            // Assert
            Assert.That(received, Is.EqualTo(new PingCommand("hello")));
        }

        [Test]
        public async Task AddCloudstrapMessaging_InvokeAsync_RunsTheHandlerInProcess()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();
            await host.StartAsync();
            InvocationRecorder recorder = host.Services.GetRequiredService<InvocationRecorder>();

            // Act — Wolverine as the in-process mediator: the handler has run when the call returns.
            using (IServiceScope scope = host.Services.CreateScope())
            {
                IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.InvokeAsync(new PingCommand("inline"));
            }

            await host.StopAsync();

            // Assert
            Assert.That(recorder.Received, Is.EqualTo(new[] { new PingCommand("inline") }));
        }

        [Test]
        public void AddCloudstrapMessaging_DefaultEndpointIdentity_IsTheWorkloadName()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.AddCloudstrapMessaging();

            // Act
            using IHost host = builder.Build();
            WolverineOptions options = host.Services.GetRequiredService<WolverineOptions>();

            // Assert
            Assert.That(options.ServiceName, Is.EqualTo(MessagingTestHost.WorkloadName));
        }

        [Test]
        public void AddCloudstrapMessaging_ExplicitEndpointName_WinsOverTheWorkloadName()
        {
            // Arrange
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:EndpointName"] = "contoso-orders-custom";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            builder.AddCloudstrapMessaging();

            // Act
            using IHost host = builder.Build();
            WolverineOptions options = host.Services.GetRequiredService<WolverineOptions>();

            // Assert
            Assert.That(options.ServiceName, Is.EqualTo("contoso-orders-custom"));
        }
    }
}
