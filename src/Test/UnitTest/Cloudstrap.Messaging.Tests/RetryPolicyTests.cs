namespace Cloudstrap.Messaging.Tests
{
    using Cloudstrap.Messaging.Tests.Fixtures;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;
    using Wolverine;

    /// <summary>
    /// The retry ladder (AC-MSG5's local half): a transiently failing handler is retried immediately up to the
    /// configured count with exactly one side effect on eventual success, the immediate count is honored
    /// before the scheduled stage takes over, and the bound defaults are five and five.
    /// </summary>
    [TestFixture]
    public sealed class RetryPolicyTests
    {
        private static readonly TimeSpan _handlerTimeout = TimeSpan.FromSeconds(15);

        [Test]
        public async Task Retries_HandlerFailsFewerTimesThanImmediateCount_SucceedsWithExactlyOneSideEffect()
        {
            // Arrange — three immediate retries; the handler fails twice, then succeeds.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Retries:NumberOfImmediate"] = "3";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.Services.AddSingleton<AttemptCounter>();
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();
            await host.StartAsync();
            InvocationRecorder recorder = host.Services.GetRequiredService<InvocationRecorder>();
            AttemptCounter attempts = host.Services.GetRequiredService<AttemptCounter>();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new FlakyCommand("heals", FailuresBeforeSuccess: 2));
            }

            object received = await recorder.WaitForNextAsync(_handlerTimeout);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await host.StopAsync();

            // Assert — invoked exactly three times, the side effect recorded exactly once.
            Assert.Multiple(() =>
            {
                Assert.That(received, Is.TypeOf<FlakyCommand>());
                Assert.That(attempts.AttemptsFor("heals"), Is.EqualTo(3));
                Assert.That(recorder.Received, Is.Empty, "no second side effect");
            });
        }

        [Test]
        public async Task Retries_ConfiguredImmediateCount_IsHonored()
        {
            // Arrange — one immediate retry; a handler that fails twice cannot succeed inline.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Retries:NumberOfImmediate"] = "1";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.Services.AddSingleton<AttemptCounter>();
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();
            await host.StartAsync();
            InvocationRecorder recorder = host.Services.GetRequiredService<InvocationRecorder>();
            AttemptCounter attempts = host.Services.GetRequiredService<AttemptCounter>();

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .PublishAsync(new FlakyCommand("stubborn", FailuresBeforeSuccess: 2));
            }

            // Give the inline stage ample time; the scheduled stage starts seconds later.
            await Task.Delay(TimeSpan.FromSeconds(2));
            int inlineAttempts = attempts.AttemptsFor("stubborn");
            await host.StopAsync();

            // Assert — the first attempt plus exactly one immediate retry, then it left the inline stage.
            Assert.Multiple(() =>
            {
                Assert.That(inlineAttempts, Is.EqualTo(2));
                Assert.That(recorder.Received, Is.Empty);
            });
        }

        [Test]
        public void Retries_DefaultCounts_AreFiveAndFive()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();

            // Act
            RetryOptions retries = host.Services.GetRequiredService<IOptions<CloudstrapMessagingOptions>>().Value.Retries;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(retries.NumberOfImmediate, Is.EqualTo(5));
                Assert.That(retries.NumberOfDelayed, Is.EqualTo(5));
            });
        }
    }
}
