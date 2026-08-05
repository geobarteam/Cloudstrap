namespace Cloudstrap.Extensions.Tests
{
    using Azure.Core;
    using Azure.Identity;
    using Azure.Storage.Blobs;
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-E13: one call yields the container client the application works against, named by convention from
    /// the consumer's own system name. Constructing a client performs no I/O, so every assertion here stays
    /// offline.
    /// </summary>
    [TestFixture]
    public sealed class AddCloudstrapBlobStorageTests
    {
        [Test]
        public void Resolve_WithBlobServiceUri_YieldsClientForDefaultContainerName()
        {
            // Arrange
            Dictionary<string, string?> config = ApplicationSection();
            config["Cloudstrap:Storage:BlobServiceUri"] = "https://contosostore.blob.core.windows.net/";

            // Act
            BlobContainerClient client = ResolveClient(config);

            // Assert — the convention is the consumer's SystemName, lowercased
            Assert.Multiple(() =>
            {
                Assert.That(client.Name, Is.EqualTo("contoso"));
                Assert.That(
                    client.Uri,
                    Is.EqualTo(new Uri("https://contosostore.blob.core.windows.net/contoso")));
            });
        }

        [Test]
        public void Resolve_WithExplicitContainerName_ExplicitWins()
        {
            // Arrange
            Dictionary<string, string?> config = ApplicationSection();
            config["Cloudstrap:Storage:BlobServiceUri"] = "https://contosostore.blob.core.windows.net/";
            config["Cloudstrap:Storage:ContainerName"] = "orders";

            // Act
            BlobContainerClient client = ResolveClient(config);

            // Assert
            Assert.That(client.Name, Is.EqualTo("orders"));
        }

        [Test]
        public void Resolve_WithConnectionString_WinsOverBlobServiceUri()
        {
            // Arrange — the dev-storage path is an explicit setting, never an inferred environment
            Dictionary<string, string?> config = ApplicationSection();
            config["Cloudstrap:Storage:BlobServiceUri"] = "https://contosostore.blob.core.windows.net/";
            config["Cloudstrap:Storage:ConnectionString"] = "UseDevelopmentStorage=true";

            // Act
            BlobContainerClient client = ResolveClient(config);

            // Assert
            Assert.That(client.AccountName, Is.EqualTo("devstoreaccount1"));
        }

        [Test]
        public void Resolve_WithPlatformConventionConnectionString_IsHonored()
        {
            // Arrange — the standard ConnectionStrings: name, so platform tooling can supply it
            Dictionary<string, string?> config = ApplicationSection();
            config["ConnectionStrings:CloudstrapStorage"] = "UseDevelopmentStorage=true";

            // Act
            BlobContainerClient client = ResolveClient(config);

            // Assert
            Assert.That(client.AccountName, Is.EqualTo("devstoreaccount1"));
        }

        [Test]
        public void Startup_WithNeitherUriNorConnectionString_FailsNamingTheKey()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(ApplicationSection());
            builder.AddCloudstrapBlobStorage();
            using IHost host = builder.Build();

            // Act
            OptionsValidationException? exception = Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.CurrentContext.CancellationToken));

            // Assert
            Assert.That(exception!.Message, Does.Contain("Cloudstrap:Storage:BlobServiceUri"));
        }

        [Test]
        public void SelectCredential_DefaultsToDefaultAzureCredential_HookWins()
        {
            // Arrange
            TokenCredential stub = new StubTokenCredential();

            // Act
            TokenCredential fallback = BlobStorageRegistration.SelectCredential(new AzureCredentialSettings());
            TokenCredential supplied = BlobStorageRegistration.SelectCredential(
                new AzureCredentialSettings { Credential = stub });

            // Assert — constructed, never invoked; a supplied credential always wins
            Assert.Multiple(() =>
            {
                Assert.That(fallback, Is.InstanceOf<DefaultAzureCredential>());
                Assert.That(supplied, Is.SameAs(stub));
            });
        }

        [Test]
        public void Resolve_WithCreateFlagDefault_PerformsNoNetworkCall()
        {
            // Arrange — an account that does not exist; resolution must still succeed
            Dictionary<string, string?> config = ApplicationSection();
            config["Cloudstrap:Storage:BlobServiceUri"] = "https://doesnotexist.blob.core.windows.net/";

            // Act
            BlobContainerClient client = ResolveClient(config);

            // Assert — container creation is strictly opt-in
            Assert.That(client.Name, Is.EqualTo("contoso"));
        }

        [Test]
        public void AddCloudstrapBlobStorage_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            IHostApplicationBuilder builder = null!;

            // Act + Assert
            Assert.That(() => builder.AddCloudstrapBlobStorage(), Throws.ArgumentNullException);
        }

        [Test]
        public void SectionName_IsRootedUnderCloudstrap()
        {
            // Arrange & Act & Assert
            Assert.That(StorageOptions.SectionName, Is.EqualTo("Cloudstrap:Storage"));
        }

        private static BlobContainerClient ResolveClient(Dictionary<string, string?> config)
        {
            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            builder.AddCloudstrapBlobStorage();
            IHost host = builder.Build();

            return host.Services.GetRequiredService<BlobContainerClient>();
        }

        private static Dictionary<string, string?> ApplicationSection() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private sealed class StubTokenCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
                throw new NotSupportedException("The test credential is never invoked.");

            public override ValueTask<AccessToken> GetTokenAsync(
                TokenRequestContext requestContext,
                CancellationToken cancellationToken) =>
                throw new NotSupportedException("The test credential is never invoked.");
        }
    }
}
