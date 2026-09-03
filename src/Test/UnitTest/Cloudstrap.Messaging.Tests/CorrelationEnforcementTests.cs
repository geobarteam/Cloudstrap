namespace Cloudstrap.Messaging.Tests
{
    using Cloudstrap.Core;
    using Cloudstrap.Messaging.Tests.Fixtures;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;
    using Wolverine;

    /// <summary>
    /// Correlation enforcement (AC-MSG10, D-5) on Core's shipped <c>Cloudstrap:Correlation:Message</c> options
    /// and #2's attributes: uncorrelated handling is blocked with a typed, logged error naming the header and
    /// the handler; <c>AllowNoCorrelation</c> and <c>ExcludeMessageHandlers</c> exempt; sending without a
    /// correlation id while enforcement is on is blocked too. Local transport — no SQL, no network.
    /// </summary>
    [TestFixture]
    public sealed class CorrelationEnforcementTests
    {
        private const string _requireAllKey = "Cloudstrap:Correlation:Message:RequireForAllMessageHandlers";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan _settle = TimeSpan.FromSeconds(1);
        private static readonly string[] _expectedExclusions = ["Contoso.Excluded"];

        [Test]
        public async Task Enforcement_RequireForAllOn_MessageWithoutCorrelation_IsBlockedWithTypedErrorNamingHeaderAndHandler()
        {
            // Arrange
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings[_requireAllKey] = "true";
            (IHost host, CapturingLoggerProvider logs) = BuildHost(settings);
            await host.StartAsync();

            // Act — inline invocation carries no correlation header: the handler must be blocked.
            Exception? failure = await InvokeAsync(host, new EnforcedCommand("blocked"));
            await host.StopAsync();

            // Assert
            string[] messages = [.. logs.Entries.Where(e => e.Level >= LogLevel.Error).Select(e => e.Message + " " + e.Exception)];
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null, "a typed error");
                Assert.That(failure!.ToString(), Does.Contain("X-Correlation-ID").And.Contain(nameof(EnforcedCommandHandler)));
                Assert.That(messages, Has.Some.Contains("X-Correlation-ID").And.Some.Contains(nameof(EnforcedCommandHandler)));
                Assert.That(host.Services.GetRequiredService<InvocationRecorder>().Received, Is.Empty, "handler never ran");
            });
        }

        [Test]
        public async Task Enforcement_CorrelationRequiredOnHandlerHierarchy_BlocksWithoutTheFlag()
        {
            // Arrange — RequireForAll off; the requirement sits on the handler's base class.
            (IHost host, CapturingLoggerProvider logs) = BuildHost(MessagingTestHost.ValidSettings());
            await host.StartAsync();

            // Act
            Exception? failure = await InvokeAsync(host, new RequiredCommand("blocked"));
            await host.StopAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure!.ToString(), Does.Contain(nameof(DerivedRequiredHandler)));
                Assert.That(logs.Entries.Select(e => e.Message), Has.Some.Contains(nameof(DerivedRequiredHandler)));
                Assert.That(host.Services.GetRequiredService<InvocationRecorder>().Received, Is.Empty);
            });
        }

        [Test]
        public async Task Enforcement_AllowNoCorrelationOnHandler_Exempts()
        {
            // Arrange — RequireForAll on; the handler carries [AllowNoCorrelation].
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings[_requireAllKey] = "true";
            (IHost host, _) = BuildHost(settings);
            await host.StartAsync();

            // Act
            Exception? failure = await InvokeAsync(host, new ExemptCommand("allowed"));
            await host.StopAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Null);
                Assert.That(host.Services.GetRequiredService<InvocationRecorder>().Received, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task Enforcement_ExcludeMessageHandlersList_Exempts()
        {
            // Arrange — RequireForAll on; the handler's full type name is excluded by configuration.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings[_requireAllKey] = "true";
            settings["Cloudstrap:Correlation:Message:ExcludeMessageHandlers:0"] = typeof(ExcludedCommandHandler).FullName;
            (IHost host, _) = BuildHost(settings);
            await host.StartAsync();

            // Act
            Exception? failure = await InvokeAsync(host, new ExcludedCommand("allowed"));
            await host.StopAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Null);
                Assert.That(host.Services.GetRequiredService<InvocationRecorder>().Received, Has.Count.EqualTo(1));
            });
        }

        [Test]
        public async Task Enforcement_OutgoingSendWithoutCorrelation_WhenRequired_IsBlocked()
        {
            // Arrange — RequireForAll on; the sender has no ambient correlation id.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings[_requireAllKey] = "true";
            (IHost host, _) = BuildHost(settings);
            await host.StartAsync();

            // Act
            Exception? failure = null;
            try
            {
                using IServiceScope scope = host.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new EnforcedCommand("unsent"));
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            await Task.Delay(_settle);
            await host.StopAsync();

            // Assert — blocked at the send, naming the header; nothing reached the handler.
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null);
                Assert.That(failure!.ToString(), Does.Contain("X-Correlation-ID"));
                Assert.That(host.Services.GetRequiredService<InvocationRecorder>().Received, Is.Empty);
            });
        }

        [Test]
        public async Task Enforcement_CorrelatedSend_WhenRequired_ReachesTheHandler()
        {
            // Arrange — the positive path under RequireForAll: a correlated send goes through.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings[_requireAllKey] = "true";
            (IHost host, _) = BuildHost(settings);
            await host.StartAsync();

            // Act
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-ok";
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new EnforcedCommand("sent"));
            }

            object received = await host.Services.GetRequiredService<InvocationRecorder>().WaitForNextAsync(_timeout);
            await host.StopAsync();

            // Assert
            Assert.That(received, Is.EqualTo(new EnforcedCommand("sent")));
        }

        [Test]
        public void Enforcement_BindsCoresShippedCorrelationMessageOptionsFromTheSiblingSection()
        {
            // Arrange — the sibling section drives Core's shipped CorrelationMessageOptions; no new type exists.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings[_requireAllKey] = "true";
            settings["Cloudstrap:Correlation:Message:ExcludeMessageHandlers:0"] = "Contoso.Excluded";
            (IHost host, _) = BuildHost(settings);

            // Act
            CorrelationMessageOptions message = host.Services.GetRequiredService<IOptions<CorrelationOptions>>().Value.Message;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(message.RequireForAllMessageHandlers, Is.True);
                Assert.That(message.ExcludeMessageHandlers, Is.EqualTo(_expectedExclusions));
                Assert.That(typeof(HostApplicationBuilderExtensions).Assembly.GetType("Cloudstrap.Messaging.MessageCorrelationOptions"), Is.Null);
            });
        }

        private static (IHost Host, CapturingLoggerProvider Logs) BuildHost(Dictionary<string, string?> settings)
        {
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.AddCloudstrapMessaging();
            return (builder.Build(), logs);
        }

        private static async Task<Exception?> InvokeAsync(IHost host, object message)
        {
            try
            {
                using IServiceScope scope = host.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(message);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
