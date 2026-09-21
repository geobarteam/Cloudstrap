namespace Cloudstrap.Messaging.Tests
{
    using Cloudstrap.Messaging.Tests.Fixtures;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using Wolverine;
    using Wolverine.ErrorHandling;

    /// <summary>
    /// The leaf-extension door (deliverable #15, DL-8): <c>CloudstrapMessagingBuilder.ConfigureEngine</c>
    /// registers contributions that run in registration order, receive the host's service provider, and are
    /// applied <em>before</em> the consumer's <c>Wolverine</c> delegate and <em>before</em> the retry ladder —
    /// so a contribution's exception-specific failure rule dead-letters without retries while the consumer
    /// keeps final say and the ladder still catches everything else.
    /// </summary>
    [TestFixture]
    public sealed class EngineContributionTests
    {
        private static readonly TimeSpan _inlineStageWindow = TimeSpan.FromSeconds(2);
        private static readonly string[] _contributionThenConsumer = ["contribution", "consumer"];
        private static readonly string[] _firstThenSecond = ["first", "second"];

        [Test]
        public async Task ConfigureEngine_Contribution_RunsBeforeTheConsumersWolverineDelegate()
        {
            // Arrange — the contribution and the consumer both set the service name and record their turn.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            ContributionProbe probe = new();
            builder.Services.AddSingleton(probe);
            builder.AddCloudstrapMessaging(configurator => configurator.Wolverine = options =>
                {
                    probe.CallOrder.Add("consumer");
                    options.ServiceName = "consumer";
                })
                .ConfigureEngine((_, options) =>
                {
                    probe.CallOrder.Add("contribution");
                    options.ServiceName = "contribution";
                });
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            WolverineOptions options = host.Services.GetRequiredService<WolverineOptions>();
            await host.StopAsync();

            // Assert — the contribution ran first; the consumer kept final say.
            Assert.Multiple(() =>
            {
                Assert.That(probe.CallOrder, Is.EqualTo(_contributionThenConsumer));
                Assert.That(options.ServiceName, Is.EqualTo("consumer"));
            });
        }

        [Test]
        public async Task ConfigureEngine_ContributionsRun_InRegistrationOrder()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            ContributionProbe probe = new();
            builder.Services.AddSingleton(probe);
            builder.AddCloudstrapMessaging()
                .ConfigureEngine((_, _) => probe.CallOrder.Add("first"))
                .ConfigureEngine((_, _) => probe.CallOrder.Add("second"));
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            await host.StopAsync();

            // Assert
            Assert.That(probe.CallOrder, Is.EqualTo(_firstThenSecond));
        }

        [Test]
        public async Task ConfigureEngine_ContributionReceivesTheHostsServiceProvider()
        {
            // Arrange — a singleton registered on the host must be resolvable from inside the contribution.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.Services.AddSingleton<ContributionProbe>();
            builder.AddCloudstrapMessaging()
                .ConfigureEngine((services, _) =>
                {
                    ContributionProbe probe = services.GetRequiredService<ContributionProbe>();
                    probe.ResolvedByContribution = probe;
                });
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            ContributionProbe hostProbe = host.Services.GetRequiredService<ContributionProbe>();
            await host.StopAsync();

            // Assert
            Assert.That(hostProbe.ResolvedByContribution, Is.SameAs(hostProbe));
        }

        [Test]
        public async Task ConfigureEngine_ContributionFailureRule_PrecedesTheRetryLadder()
        {
            // Arrange — three immediate retries on the ladder; the contribution dead-letters one exception type.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Retries:NumberOfImmediate"] = "3";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.Services.AddSingleton<AttemptCounter>();
            builder.AddCloudstrapMessaging()
                .ConfigureEngine((_, options) =>
                    options.Policies.OnException<DeterministicFailureException>().MoveToErrorQueue());
            using IHost host = builder.Build();
            await host.StartAsync();
            AttemptCounter attempts = host.Services.GetRequiredService<AttemptCounter>();

            // Act — one deterministic failure, one transient failure that never heals inline.
            using (IServiceScope scope = host.Services.CreateScope())
            {
                IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await bus.PublishAsync(new DeterministicFailureCommand("deterministic"));
                await bus.PublishAsync(new FlakyCommand("transient", FailuresBeforeSuccess: 100));
            }

            await Task.Delay(_inlineStageWindow);
            int deterministicAttempts = attempts.AttemptsFor("deterministic");
            int transientAttempts = attempts.AttemptsFor("transient");
            await host.StopAsync();

            // Assert — the contribution's rule matched before the ladder (exactly one attempt); the ladder
            // still caught everything else (the first attempt plus three immediate retries).
            Assert.Multiple(() =>
            {
                Assert.That(deterministicAttempts, Is.EqualTo(1), "the contribution's rule dead-letters without retries");
                Assert.That(transientAttempts, Is.EqualTo(4), "the ladder still applies to every other exception");
            });
        }

        [Test]
        public void ConfigureEngine_NullContribution_ThrowsArgumentNullException()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            CloudstrapMessagingBuilder messaging = builder.AddCloudstrapMessaging();

            // Act & Assert
            Assert.That(() => messaging.ConfigureEngine(null!), Throws.ArgumentNullException);
        }
    }
}
