namespace Cloudstrap.Messaging.Tests
{
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;
    using Wolverine;

    /// <summary>
    /// Registration semantics of <c>AddCloudstrapMessaging</c>: guard clauses, the one-node-per-process
    /// fail-fast (AC-MSG14), the unknown-transport startup failure naming the exact key (AC-MSG2), and
    /// the returned builder plus the <c>Wolverine</c> escape hatch running last.
    /// </summary>
    [TestFixture]
    public sealed class RegistrationTests
    {
        [Test]
        public void AddCloudstrapMessaging_OnNullBuilder_ThrowsArgumentNullException()
        {
            IHostApplicationBuilder builder = null!;

            Assert.That(
                () => builder.AddCloudstrapMessaging(),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void AddCloudstrapMessaging_CalledTwice_ThrowsNamingTheDuplicateCall()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.AddCloudstrapMessaging();

            // Act + Assert — at the call site, earlier than startup, contractual (AC-MSG14).
            Assert.That(
                () => builder.AddCloudstrapMessaging(),
                Throws.InvalidOperationException.With.Message.Contains("AddCloudstrapMessaging"));
        }

        [Test]
        public void AddCloudstrapMessaging_UnknownTransportValue_FailsFastNamingTheTransportKey()
        {
            // Arrange — an unknown transport plus a sentinel secret that must never be echoed. The section is
            // read eagerly, so the failure lands at the call — before the host is even built (AC-MSG2).
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:Transport"] = "RabbitMQ";
            settings["ConnectionStrings:DefaultConnection"] = "Server=sentinel-secret-host;Password=sentinel-secret";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);

            // Act
            Exception? failure = Assert.Catch(() => builder.AddCloudstrapMessaging());

            // Assert
            Assert.That(failure, Is.Not.Null, "registration must fail on an unknown transport");
            string text = failure!.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("'Cloudstrap:Messaging:Transport'"));
                Assert.That(text, Does.Not.Contain("sentinel-secret"));
            });
        }

        [Test]
        public void AddCloudstrapMessaging_ReturnsTheBuilder_AndConfiguratorWolverineDelegateRuns()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());

            // Act — the escape hatch has final say: it overrides the workload-name identity.
            CloudstrapMessagingBuilder result = builder.AddCloudstrapMessaging(configurator =>
                configurator.Wolverine = options => options.ServiceName = "escape-hatch");
            using IHost host = builder.Build();
            WolverineOptions options = host.Services.GetRequiredService<WolverineOptions>();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Not.Null);
                Assert.That(options.ServiceName, Is.EqualTo("escape-hatch"));
            });
        }
    }
}
