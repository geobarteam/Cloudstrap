namespace Cloudstrap.Messaging.Tests
{
    using System.Text.RegularExpressions;
    using Azure.Identity;
    using Cloudstrap.Messaging.Tests.Fixtures.Contracts;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using Wolverine;
    using Wolverine.AzureServiceBus;
    using Wolverine.AzureServiceBus.Internal;
    using Wolverine.Configuration;
    using Wolverine.Runtime;

    /// <summary>
    /// The Azure Service Bus transport by configuration alone (AC-MSG2's ASB clauses, AC-MSG3, D-1, D-2's
    /// transport-queue half). ⚠️ No network (AC-M3): every assertion is either a registration-time validation
    /// failure or an inspection of the configured, never-started transport state.
    /// </summary>
    [TestFixture]
    public sealed partial class AzureServiceBusConfigTests
    {
        private const string _contractsNamespace = "Cloudstrap.Messaging.Tests.Fixtures.Contracts";
        private const string _namespace = "contoso.servicebus.windows.net";
        private static readonly string[] _expectedCommandRoutes = ["asb://queue/contoso-billing-worker"];

        [Test]
        public void AsbTransport_NoNamespaceAndNoConnectionString_FailsFastNamingTheNamespaceKey()
        {
            // Arrange — the transport selected, nothing to connect with; a sentinel value that must not leak.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Transport"] = "AzureServiceBus";
            settings["ConnectionStrings:DefaultConnection"] = "Endpoint=sb://sentinel-secret.servicebus.windows.net/";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);

            // Act
            Exception? failure = Assert.Catch(() => builder.AddCloudstrapMessaging());

            // Assert
            string text = failure!.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("'Cloudstrap:Messaging:AzureServiceBus:FullyQualifiedNamespace'"));
                Assert.That(text, Does.Not.Contain("sentinel-secret"));
            });
        }

        [Test]
        public void AsbTransport_ConnectionStringNameThatDoesNotResolve_FailsFastNamingTheKey_NeverTheValue()
        {
            // Arrange — a connection string name that has no entry; a sentinel elsewhere that must not leak.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Transport"] = "AzureServiceBus";
            settings["Cloudstrap:Messaging:AzureServiceBus:ConnectionStringName"] = "ServiceBus";
            settings["ConnectionStrings:DefaultConnection"] = "Endpoint=sb://sentinel-secret.servicebus.windows.net/";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);

            // Act
            Exception? failure = Assert.Catch(() => builder.AddCloudstrapMessaging());

            // Assert
            string text = failure!.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("'Cloudstrap:Messaging:AzureServiceBus:ConnectionStringName'"));
                Assert.That(text, Does.Not.Contain("sentinel-secret"));
            });
        }

        [Test]
        public void AsbTransport_NamespaceSet_UsesDefaultAzureCredential_AndNoSecretBearingSettingExists()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(AsbSettings());
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();

            // Act
            AzureServiceBusTransport transport = Transport(host);
            string[] properties = [.. typeof(AzureServiceBusOptions).GetProperties().Select(property => property.Name)];

            // Assert — platform credentials, and no secret-bearing setting exists to configure (AC-MSG3).
            Assert.Multiple(() =>
            {
                Assert.That(transport.FullyQualifiedNamespace, Is.EqualTo(_namespace));
                Assert.That(transport.TokenCredential, Is.TypeOf<DefaultAzureCredential>());
                Assert.That(properties, Has.None.Matches<string>(name => SecretBearingName().IsMatch(name)));
            });
        }

        [Test]
        public void AsbTransport_D1Topology_CommandQueueTopicPerEventAndWorkloadSubscription_AreConfigured()
        {
            // Arrange — a Destinations entry for the contracts namespace; the host is built, never started.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(AsbSettings());
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();
            IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            CloudstrapRoutingConvention convention = new(
                host.Services.GetRequiredService<MessageConventions>(),
                MessagingTransport.AzureServiceBus,
                MessagingTestHost.WorkloadName);

            // Act — the convention's own discovery, driven against the unstarted transport model.
            string[] commandRoutes = [.. convention.DiscoverSenders(typeof(PlaceOrderCommand), runtime).Select(e => e.Uri.ToString())];
            string[] eventRoutes = [.. convention.DiscoverSenders(typeof(OrderPlacedEvent), runtime).Select(e => e.Uri.ToString())];
            convention.DiscoverListeners(runtime, [typeof(OrderPlacedEvent), typeof(PlaceOrderCommand)]);
            AzureServiceBusTransport transport = Transport(host);
            Endpoint[] listeners = [.. runtime.Options.Transports.AllEndpoints().Where(endpoint => endpoint.IsListener)];

            // Assert — D-1: command → destination workload queue; event → topic per event type; this node
            // listens on its own queue and subscribes to the event topic under its workload name.
            Assert.Multiple(() =>
            {
                Assert.That(commandRoutes, Is.EqualTo(_expectedCommandRoutes));
                Assert.That(eventRoutes, Is.EqualTo(new[] { $"asb://topic/{_contractsNamespace.ToLowerInvariant()}.orderplacedevent" }));
                Assert.That(transport.Queues.Contains(MessagingTestHost.WorkloadName), Is.True, "inbox queue");
                Assert.That(
                    listeners.Select(endpoint => endpoint.Uri.ToString()),
                    Does.Contain($"asb://topic/{_contractsNamespace.ToLowerInvariant()}.orderplacedevent/{MessagingTestHost.WorkloadName}"));
                Assert.That(
                    listeners.Select(endpoint => endpoint.Uri.ToString()),
                    Does.Contain($"asb://queue/{MessagingTestHost.WorkloadName}"));
            });
        }

        [Test]
        public void AsbTransport_TransportErrorQueueName_DefaultsToSystemNameError_AndDeadLetterQueueNameOverrides()
        {
            // Arrange — two hosts: the default and an explicit DeadLetter:QueueName.
            HostApplicationBuilder defaultBuilder = MessagingTestHost.CreateBuilder(AsbSettings());
            defaultBuilder.AddCloudstrapMessaging();
            Dictionary<string, string?> overridden = AsbSettings();
            overridden["Cloudstrap:Messaging:DeadLetter:QueueName"] = "contoso-poison";
            HostApplicationBuilder overriddenBuilder = MessagingTestHost.CreateBuilder(overridden);
            overriddenBuilder.AddCloudstrapMessaging();
            using IHost defaultHost = defaultBuilder.Build();
            using IHost overriddenHost = overriddenBuilder.Build();

            // Act
            AzureServiceBusQueue defaultInbox = Transport(defaultHost).Queues[MessagingTestHost.WorkloadName];
            AzureServiceBusQueue overriddenInbox = Transport(overriddenHost).Queues[MessagingTestHost.WorkloadName];

            // Assert — D-2's naming half: {SystemName}-error, overridable.
            Assert.Multiple(() =>
            {
                Assert.That(defaultInbox.DeadLetterQueueName, Is.EqualTo("contoso-error"));
                Assert.That(overriddenInbox.DeadLetterQueueName, Is.EqualTo("contoso-poison"));
            });
        }

        [GeneratedRegex("(?i)tenant|clientid|secret|password|key$")]
        private static partial Regex SecretBearingName();

        private static Dictionary<string, string?> AsbSettings()
        {
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Transport"] = "AzureServiceBus";
            settings["Cloudstrap:Messaging:AzureServiceBus:FullyQualifiedNamespace"] = _namespace;
            settings[$"Cloudstrap:Messaging:Destinations:{_contractsNamespace}"] = "contoso-billing-worker";
            return settings;
        }

        private static AzureServiceBusTransport Transport(IHost host)
        {
            return host.Services.GetRequiredService<WolverineOptions>().Transports.OfType<AzureServiceBusTransport>().Single();
        }
    }
}
