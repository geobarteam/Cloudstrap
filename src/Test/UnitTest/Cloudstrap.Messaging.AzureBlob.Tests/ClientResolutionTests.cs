namespace Cloudstrap.Messaging.AzureBlob.Tests
{
    using System.Reflection;
    using Azure.Core;
    using Azure.Storage.Blobs;
    using Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;

    /// <summary>
    /// The client ladder (AC-CK8, DL-9): a client handed in through the settings, else a registered
    /// <c>BlobServiceClient</c>, else the container client <c>AddCloudstrapBlobStorage</c> registered — whose
    /// account the claim-check container is opened on — else a startup failure naming both routes. No
    /// credential and no second client are ever constructed. Every assertion is on constructed clients,
    /// offline.
    /// </summary>
    [TestFixture]
    public sealed class ClientResolutionTests
    {
        [Test]
        public void Resolve_NoBlobClientRegistered_StartupFailsNamingAddCloudstrapBlobStorageAndTheRegisterAClientAlternative()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck();
            using IHost host = builder.Build();

            // Act
            Exception? failure = Assert.CatchAsync(() => host.StartAsync());

            // Assert
            string text = failure!.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("AddCloudstrapBlobStorage"));
                Assert.That(text, Does.Contain("BlobServiceClient"));
            });
        }

        [Test]
        public async Task Resolve_RegisteredBlobContainerClient_OpensTheClaimCheckContainerOnTheSameAccount()
        {
            // Arrange — route (b): the application container client, as AddCloudstrapBlobStorage registers it.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            ClaimCheckTestHost.RegisterApplicationContainerClient(builder.Services);
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck();
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            ClaimCheckClientResolution? resolution = host.Services.GetRequiredService<AzureBlobClaimCheckRegistrationState>().Resolution;
            await host.StopAsync();

            // Assert
            Assert.That(resolution, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(resolution!.Container.Name, Is.EqualTo("contoso-claimcheck"));
                Assert.That(resolution.Container.AccountName, Is.EqualTo("devstoreaccount1"));
                Assert.That(resolution.Source, Is.EqualTo(ClaimCheckClientSource.BlobContainerClient));
            });
        }

        [Test]
        public async Task Resolve_RegisteredBlobServiceClient_IsPreferredOverTheContainerClient()
        {
            // Arrange — both registered; the service client (a consumer's or a platform's) is on another account.
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            ClaimCheckTestHost.RegisterApplicationContainerClient(builder.Services);
            builder.Services.AddSingleton(new BlobServiceClient(ClaimCheckTestHost.ConnectionStringFor("other")));
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck();
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            ClaimCheckClientResolution? resolution = host.Services.GetRequiredService<AzureBlobClaimCheckRegistrationState>().Resolution;
            await host.StopAsync();

            // Assert
            Assert.That(resolution, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(resolution!.Container.Name, Is.EqualTo("contoso-claimcheck"));
                Assert.That(resolution.Container.AccountName, Is.EqualTo("other"));
                Assert.That(resolution.Source, Is.EqualTo(ClaimCheckClientSource.BlobServiceClient));
            });
        }

        [Test]
        public async Task Resolve_CodeLevelContainerClient_WinsOverEverything_AndIgnoresTheContainerNameSetting()
        {
            // Arrange
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:ClaimCheck:ContainerName"] = "ignored-name";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);
            ClaimCheckTestHost.RegisterApplicationContainerClient(builder.Services);
            builder.Services.AddSingleton(new BlobServiceClient(ClaimCheckTestHost.ConnectionStringFor("other")));
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck(claimCheck =>
                claimCheck.ContainerClient = new BlobContainerClient(ClaimCheckTestHost.DevelopmentStorage, "handed-in"));
            using IHost host = builder.Build();

            // Act
            await host.StartAsync();
            ClaimCheckClientResolution? resolution = host.Services.GetRequiredService<AzureBlobClaimCheckRegistrationState>().Resolution;
            await host.StopAsync();

            // Assert
            Assert.That(resolution, Is.Not.Null);
            string posture = logs.Entries.Single(entry => entry.Category == "Cloudstrap.Messaging.AzureBlob").Message;
            Assert.Multiple(() =>
            {
                Assert.That(resolution!.Container.Name, Is.EqualTo("handed-in"));
                Assert.That(resolution.Source, Is.EqualTo(ClaimCheckClientSource.Code));
                Assert.That(posture, Does.Contain("ContainerName setting ignored"));
            });
        }

        [Test]
        public async Task Resolve_ConstructsNoCredentialAndNoSecondClient()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());
            ClaimCheckTestHost.FactoryCounter factory = ClaimCheckTestHost.RegisterApplicationContainerClient(builder.Services);
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck();
            using IHost host = builder.Build();
            Assembly leaf = typeof(AzureBlobClaimCheckOptions).Assembly;

            // Act
            await host.StartAsync();
            await host.StopAsync();
            string[] referenced = [.. leaf.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty)];
            string[] credentialMembers =
            [
                .. leaf.GetTypes().SelectMany(type => type
                    .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(member => member switch
                    {
                        FieldInfo field => typeof(TokenCredential).IsAssignableFrom(field.FieldType),
                        PropertyInfo property => typeof(TokenCredential).IsAssignableFrom(property.PropertyType),
                        MethodInfo method => method.GetParameters().Any(p => typeof(TokenCredential).IsAssignableFrom(p.ParameterType)),
                        _ => false,
                    })
                    .Select(member => $"{type.FullName}.{member.Name}")),
            ];

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(referenced, Has.None.StartsWith("Azure.Identity"));
                Assert.That(credentialMembers, Is.Empty);
                Assert.That(factory.Invocations, Is.EqualTo(1), "the registered client is resolved once and reused");
            });
        }
    }
}
