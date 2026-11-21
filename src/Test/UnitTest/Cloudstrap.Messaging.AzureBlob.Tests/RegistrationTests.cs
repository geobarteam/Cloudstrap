namespace Cloudstrap.Messaging.AzureBlob.Tests
{
    using Azure.Storage.Blobs;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures;
    using Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure;
    using Cloudstrap.TestInfrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Testing;
    using NUnit.Framework;
    using Wolverine;

    /// <summary>
    /// Registration semantics of <c>UseAzureBlobClaimCheck</c>: guard clauses, the one-claim-check-per-node
    /// fail-fast (AC-CK14), composition with the durability calls, the no-leaf-call posture (AC-CK7) and the
    /// one posture log line that never carries a connection string (AC-CK10). Everything here is offline.
    /// </summary>
    [TestFixture]
    public sealed class RegistrationTests
    {
        private static readonly TimeSpan _handlerTimeout = TimeSpan.FromSeconds(30);

        [Test]
        public void UseAzureBlobClaimCheck_OnNullBuilder_ThrowsArgumentNullException()
        {
            CloudstrapMessagingBuilder builder = null!;

            Assert.That(
                () => builder.UseAzureBlobClaimCheck(),
                Throws.ArgumentNullException.With.Property("ParamName").EqualTo("builder"));
        }

        [Test]
        public void UseAzureBlobClaimCheck_CalledTwice_ThrowsNamingTheDuplicateCall()
        {
            // Arrange
            HostApplicationBuilder builder = CloudstrapTestHost.CreateBuilder(CloudstrapTestHost.ValidSettings());
            CloudstrapMessagingBuilder messaging = builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck();

            // Act + Assert — at the call site, earlier than startup (AC-CK14).
            Assert.That(
                () => messaging.UseAzureBlobClaimCheck(),
                Throws.InvalidOperationException.With.Message.Contains("UseAzureBlobClaimCheck"));
        }

        [Test]
        public void UseAzureBlobClaimCheck_ReturnsTheSameBuilder_AndComposesInAnyOrderWithUseSqlServerCalls()
        {
            // Arrange — a durability connection string the registration-time path resolves by name; no SQL is
            // touched because neither host is started.
            Dictionary<string, string?> settings = CloudstrapTestHost.ValidSettings();
            settings["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=never-opened;Integrated Security=true;";
            HostApplicationBuilder first = CloudstrapTestHost.CreateBuilder(settings);
            HostApplicationBuilder second = CloudstrapTestHost.CreateBuilder(settings);
            CloudstrapMessagingBuilder firstMessaging = first.AddCloudstrapMessaging();
            CloudstrapMessagingBuilder secondMessaging = second.AddCloudstrapMessaging();

            // Act
            CloudstrapMessagingBuilder firstResult = firstMessaging.UseSqlServer().UseAzureBlobClaimCheck();
            CloudstrapMessagingBuilder secondResult = secondMessaging.UseAzureBlobClaimCheck().UseSqlServer();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(firstResult, Is.SameAs(firstMessaging));
                Assert.That(secondResult, Is.SameAs(secondMessaging));
            });
        }

        [Test]
        public async Task AddCloudstrapMessaging_WithoutTheLeafCall_ResolvesNoBlobClient()
        {
            // Arrange — blob-client factories that fail loudly if anything resolves them; a 1 MB body.
            HostApplicationBuilder builder = CloudstrapTestHost.CreateBuilder(CloudstrapTestHost.ValidSettings());
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.Services.AddSingleton<BlobContainerClient>(_ => throw new InvalidOperationException("no leaf call, no blob client"));
            builder.Services.AddSingleton<BlobServiceClient>(_ => throw new InvalidOperationException("no leaf call, no blob client"));
            builder.AddCloudstrapMessaging();
            using IHost host = builder.Build();
            await host.StartAsync();
            string content = new('x', 1024 * 1024);

            // Act
            using (IServiceScope scope = host.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(new LargePayloadCommand(content));
            }

            object received = await host.Services.GetRequiredService<InvocationRecorder>().WaitForNextAsync(_handlerTimeout);
            await host.StopAsync();

            // Assert — the message travelled whole; neither factory was ever invoked (AC-CK7).
            Assert.That(received, Is.EqualTo(new LargePayloadCommand(content)));
        }

        [Test]
        public async Task UseAzureBlobClaimCheck_StartupLogsOnePostureLine_NamingContainerThresholdAndSource_NeverTheConnectionString()
        {
            // Arrange — the container client AddCloudstrapBlobStorage would register (route b of the ladder).
            HostApplicationBuilder builder = CloudstrapTestHost.CreateBuilder(CloudstrapTestHost.ValidSettings());
            FakeLogCollector logs = new();
            builder.Logging.AddProvider(new FakeLoggerProvider(logs));
            ClaimCheckTestHost.RegisterApplicationContainerClient(builder.Services);
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck();
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            await host.StopAsync();

            // Assert
            FakeLogRecord[] posture = [.. logs.GetSnapshot().Where(entry => entry.Category == "Cloudstrap.Messaging.AzureBlob")];
            Assert.Multiple(() =>
            {
                Assert.That(posture, Has.Length.EqualTo(1));
                Assert.That(posture[0].Message, Does.Contain("contoso-claimcheck"));
                Assert.That(posture[0].Message, Does.Contain("204800"));
                Assert.That(posture[0].Message, Does.Contain("AddCloudstrapBlobStorage"));
                Assert.That(logs.GetSnapshot().Select(entry => entry.Message), Has.None.Contains("devstoreaccount1"));
                Assert.That(logs.GetSnapshot().Select(entry => entry.Message), Has.None.Contains("UseDevelopmentStorage"));
            });
        }
    }
}
