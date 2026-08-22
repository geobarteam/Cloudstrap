namespace Cloudstrap.Extensions.Tests
{
    using Azure.Core;
    using Azure.Identity;
    using Azure.Security.KeyVault.Secrets;
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// AC-E2/AC-E3/AC-E4: the call belongs in every <c>Program.cs</c> unconditionally — configuration alone
    /// decides whether a vault is contacted, and a half-configured vault stops the application rather than
    /// letting it start with missing secrets.
    /// </summary>
    /// <remarks>
    /// No test may let the enabled path add the real configuration source: <c>ConfigurationManager</c> builds
    /// providers eagerly, so that would contact a vault. The enabled path's decisions are asserted through
    /// the composition seam instead, and the source addition itself is covered by the README's manual
    /// procedure.
    /// </remarks>
    [TestFixture]
    public sealed class AddCloudstrapKeyVaultTests
    {
        [Test]
        public void AddCloudstrapKeyVault_SectionAbsent_IsANoOpReturningTheSameBuilder()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(ApplicationSection());
            int sourceCountBefore = builder.Configuration.Sources.Count;

            // Act
            IHostApplicationBuilder returned = builder.AddCloudstrapKeyVault();

            // Assert — nothing was added, and nothing Azure-touching ran
            Assert.Multiple(() =>
            {
                Assert.That(builder.Configuration.Sources, Has.Count.EqualTo(sourceCountBefore));
                Assert.That(returned, Is.SameAs(builder));
            });
        }

        [Test]
        public void AddCloudstrapKeyVault_EnabledFalse_IsANoOp()
        {
            // Arrange — the shape of a developer machine's configuration
            Dictionary<string, string?> config = ApplicationSection();
            config["Cloudstrap:KeyVault:Enabled"] = "false";
            config["Cloudstrap:KeyVault:VaultUri"] = "https://contoso-vault.vault.azure.net/";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);
            int sourceCountBefore = builder.Configuration.Sources.Count;

            // Act
            builder.AddCloudstrapKeyVault();

            // Assert
            Assert.That(builder.Configuration.Sources, Has.Count.EqualTo(sourceCountBefore));
        }

        [Test]
        public void AddCloudstrapKeyVault_EnabledWithoutVaultUri_ThrowsConfigurationValidationExceptionNamingTheKey()
        {
            // Arrange
            Dictionary<string, string?> config = ApplicationSection();
            config["Cloudstrap:KeyVault:Enabled"] = "true";

            HostApplicationBuilder builder = TestHostBuilder.Create(config);

            // Act
            ConfigurationValidationException? exception =
                Assert.Throws<ConfigurationValidationException>(() => builder.AddCloudstrapKeyVault());

            // Assert — enabling a vault without naming it stops the application, never a silent skip
            Assert.That(exception!.Message, Does.Contain("Cloudstrap:KeyVault:VaultUri"));
        }

        [Test]
        public void Compose_WithNoHook_UsesDefaultAzureCredentialAndWorkloadNamePrefix()
        {
            // Arrange
            KeyVaultOptions options = EnabledOptions();

            // Act
            KeyVaultSource source = KeyVaultRegistration.Compose(
                options,
                new ApplicationOptions { SystemName = "Contoso", SubsystemName = "Orders", SubsystemType = "Api" },
                new KeyVaultConnectionSettings());

            // Assert — one credential type everywhere, and the prefix follows the workload naming convention
            Assert.Multiple(() =>
            {
                Assert.That(source.Credential, Is.InstanceOf<DefaultAzureCredential>());
                Assert.That(source.Options.Manager.Load(new SecretProperties("contoso-orders-api-Foo")), Is.True);
                Assert.That(source.Options.Manager.Load(new SecretProperties("other-Foo")), Is.False);
                Assert.That(source.VaultUri, Is.EqualTo(new Uri("https://contoso-vault.vault.azure.net/")));
            });
        }

        [Test]
        public void Compose_WithExplicitPrefix_ExplicitWins()
        {
            // Arrange
            KeyVaultOptions options = EnabledOptions();
            options.SecretPrefix = "shared";

            // Act
            KeyVaultSource source = KeyVaultRegistration.Compose(options, ContosoApplication(), new KeyVaultConnectionSettings());

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(source.Options.Manager.Load(new SecretProperties("shared-Foo")), Is.True);
                Assert.That(source.Options.Manager.Load(new SecretProperties("contoso-orders-api-Foo")), Is.False);
            });
        }

        [Test]
        public void Compose_WithEmptyPrefix_DisablesFiltering()
        {
            // Arrange
            KeyVaultOptions options = EnabledOptions();
            options.SecretPrefix = string.Empty;

            // Act
            KeyVaultSource source = KeyVaultRegistration.Compose(options, ContosoApplication(), new KeyVaultConnectionSettings());

            // Assert
            Assert.That(source.Options.Manager.Load(new SecretProperties("anything-at-all")), Is.True);
        }

        [Test]
        public void Compose_WithHookCredential_HookWins()
        {
            // Arrange — a consumer supplying its own credential (constructed, never invoked)
            TokenCredential stub = new StubTokenCredential();
            KeyVaultConnectionSettings settings = new() { Credential = stub };

            // Act
            KeyVaultSource source = KeyVaultRegistration.Compose(EnabledOptions(), ContosoApplication(), settings);

            // Assert
            Assert.That(source.Credential, Is.SameAs(stub));
        }

        [Test]
        public void Compose_WithReloadInterval_PassesItThrough()
        {
            // Arrange
            KeyVaultConnectionSettings settings = new() { ReloadInterval = TimeSpan.FromMinutes(15) };

            // Act
            KeyVaultSource source = KeyVaultRegistration.Compose(EnabledOptions(), ContosoApplication(), settings);

            // Assert
            Assert.That(source.Options.ReloadInterval, Is.EqualTo(TimeSpan.FromMinutes(15)));
        }

        [Test]
        public void AddCloudstrapKeyVault_CalledTwiceWhileDisabled_StaysIdempotent()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(ApplicationSection());
            int sourceCountBefore = builder.Configuration.Sources.Count;

            // Act
            builder.AddCloudstrapKeyVault();
            builder.AddCloudstrapKeyVault();

            // Assert
            Assert.That(builder.Configuration.Sources, Has.Count.EqualTo(sourceCountBefore));
        }

        [Test]
        public void AddCloudstrapKeyVault_OnNullBuilder_ThrowsArgumentNullException()
        {
            // Arrange
            IHostApplicationBuilder builder = null!;

            // Act + Assert
            Assert.That(() => builder.AddCloudstrapKeyVault(), Throws.ArgumentNullException);
        }

        private static Dictionary<string, string?> ApplicationSection() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static ApplicationOptions ContosoApplication() => new()
        {
            SystemName = "Contoso",
            SubsystemName = "Orders",
            SubsystemType = "Api",
        };

        private static KeyVaultOptions EnabledOptions() => new()
        {
            Enabled = true,
            VaultUri = new Uri("https://contoso-vault.vault.azure.net/"),
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
