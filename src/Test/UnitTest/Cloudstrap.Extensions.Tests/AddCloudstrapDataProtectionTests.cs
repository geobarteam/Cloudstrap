namespace Cloudstrap.Extensions.Tests
{
    using Azure.Core;
    using Microsoft.AspNetCore.DataProtection.KeyManagement;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;
    using FrameworkDataProtectionOptions = Microsoft.AspNetCore.DataProtection.DataProtectionOptions;

    /// <summary>
    /// AC-E12: scaled-out instances share one key ring, persisted to blob storage and encrypted with a
    /// KeyVault key, so cookies and antiforgery tokens survive a restart and work across replicas. Both URIs
    /// are required once enabled — an incomplete setup stops the application instead of silently falling
    /// back to keys that live and die with one container.
    /// </summary>
    [TestFixture]
    public sealed class AddCloudstrapDataProtectionTests
    {
        [Test]
        public void Enabled_WithBothUris_ConfiguresBlobRepositoryAndKeyVaultEncryptor()
        {
            // Arrange & Act
            using IHost host = BuildHost(EnabledConfig());
            KeyManagementOptions keyManagement =
                host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(
                    keyManagement.XmlRepository?.GetType().FullName,
                    Does.Contain("Azure.Extensions.AspNetCore.DataProtection.Blobs"));
                Assert.That(
                    keyManagement.XmlEncryptor?.GetType().FullName,
                    Does.Contain("Azure.Extensions.AspNetCore.DataProtection.Keys"));
            });
        }

        [Test]
        public void Enabled_SetsApplicationDiscriminatorFromWorkloadNameByDefault()
        {
            // Arrange & Act
            using IHost host = BuildHost(EnabledConfig());
            FrameworkDataProtectionOptions options =
                host.Services.GetRequiredService<IOptions<FrameworkDataProtectionOptions>>().Value;

            // Assert — applications sharing one storage account must not share one another's payloads
            Assert.That(options.ApplicationDiscriminator, Is.EqualTo("contoso-orders-api"));
        }

        [Test]
        public void Enabled_WithExplicitApplicationName_ExplicitWins()
        {
            // Arrange
            Dictionary<string, string?> config = EnabledConfig();
            config["Cloudstrap:DataProtection:ApplicationName"] = "contoso-shared";

            // Act
            using IHost host = BuildHost(config);
            FrameworkDataProtectionOptions options =
                host.Services.GetRequiredService<IOptions<FrameworkDataProtectionOptions>>().Value;

            // Assert — deliberately sharing a key ring between applications is the override's purpose
            Assert.That(options.ApplicationDiscriminator, Is.EqualTo("contoso-shared"));
        }

        [Test]
        public void Enabled_MissingKeysBlobUri_FailsStartupNamingTheKey()
        {
            // Arrange
            Dictionary<string, string?> config = EnabledConfig();
            config.Remove("Cloudstrap:DataProtection:KeysBlobUri");

            using IHost host = BuildHost(config);

            // Act
            OptionsValidationException? exception = Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.CurrentContext.CancellationToken));

            // Assert
            Assert.That(exception!.Message, Does.Contain("Cloudstrap:DataProtection:KeysBlobUri"));
        }

        [Test]
        public void Enabled_MissingKeyVaultKeyId_FailsStartupNamingTheKey()
        {
            // Arrange — keys at rest without encryption is never the silent fallback
            Dictionary<string, string?> config = EnabledConfig();
            config.Remove("Cloudstrap:DataProtection:KeyVaultKeyId");

            using IHost host = BuildHost(config);

            // Act
            OptionsValidationException? exception = Assert.ThrowsAsync<OptionsValidationException>(
                () => host.StartAsync(TestContext.CurrentContext.CancellationToken));

            // Assert
            Assert.That(exception!.Message, Does.Contain("Cloudstrap:DataProtection:KeyVaultKeyId"));
        }

        [Test]
        public void Disabled_ConfiguresNoXmlRepository()
        {
            // Arrange & Act — the call is a safe no-op, like the other Azure entry points
            using IHost host = BuildHost(ApplicationSection());
            KeyManagementOptions keyManagement =
                host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

            // Assert
            Assert.That(keyManagement.XmlRepository, Is.Null);
        }

        [Test]
        public void Enabled_WithHookCredential_UsesIt()
        {
            // Arrange — credential precedence itself is pinned by the shared seam's own tests; this proves
            // the entry point routes the hook into that seam rather than ignoring it
            bool hookInvoked = false;
            TokenCredential stub = new StubTokenCredential();

            // Act
            using IHost host = BuildHost(
                EnabledConfig(),
                settings =>
                {
                    hookInvoked = true;
                    settings.Credential = stub;
                });
            KeyManagementOptions keyManagement =
                host.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(hookInvoked, Is.True);
                Assert.That(keyManagement.XmlRepository, Is.Not.Null);
                Assert.That(keyManagement.XmlEncryptor, Is.Not.Null);
            });
        }

        [Test]
        public void AddCloudstrapDataProtection_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            IHostApplicationBuilder builder = null!;

            // Act + Assert
            Assert.That(() => builder.AddCloudstrapDataProtection(), Throws.ArgumentNullException);
        }

        [Test]
        public void SectionName_IsRootedUnderCloudstrap()
        {
            // Arrange & Act & Assert
            Assert.That(DataProtectionOptions.SectionName, Is.EqualTo("Cloudstrap:DataProtection"));
        }

        private static IHost BuildHost(
            Dictionary<string, string?> config,
            Action<AzureCredentialSettings>? configure = null)
        {
            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            builder.AddCloudstrapDataProtection(configure);

            return builder.Build();
        }

        private static Dictionary<string, string?> ApplicationSection() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static Dictionary<string, string?> EnabledConfig()
        {
            Dictionary<string, string?> config = ApplicationSection();
            config["Cloudstrap:DataProtection:Enabled"] = "true";
            config["Cloudstrap:DataProtection:KeysBlobUri"] =
                "https://contosostore.blob.core.windows.net/keys/keys.xml";
            config["Cloudstrap:DataProtection:KeyVaultKeyId"] =
                "https://contoso-vault.vault.azure.net/keys/dp-key";

            return config;
        }

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
