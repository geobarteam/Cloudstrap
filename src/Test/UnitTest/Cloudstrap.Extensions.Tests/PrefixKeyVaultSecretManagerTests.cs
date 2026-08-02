namespace Cloudstrap.Extensions.Tests
{
    using Azure.Security.KeyVault.Secrets;
    using NUnit.Framework;

    /// <summary>
    /// AC-E1/AC-E4 mechanics: the prefix filter is what lets many workloads share one vault, and the
    /// <c>--</c> convention is what lets a flat secret name address a nested configuration key. Exercised
    /// directly against secret fixtures — no vault, no network.
    /// </summary>
    [TestFixture]
    public sealed class PrefixKeyVaultSecretManagerTests
    {
        [Test]
        public void Load_SecretWithPrefix_IsLoaded()
        {
            // Arrange
            PrefixKeyVaultSecretManager manager = new("contoso-app");

            // Act
            bool loaded = manager.Load(new SecretProperties("contoso-app-Foo--Bar"));

            // Assert
            Assert.That(loaded, Is.True);
        }

        [Test]
        public void Load_SecretWithOtherPrefix_IsNotLoaded()
        {
            // Arrange — another workload's secret in the same vault
            PrefixKeyVaultSecretManager manager = new("contoso-app");

            // Act
            bool loaded = manager.Load(new SecretProperties("other-Baz"));

            // Assert
            Assert.That(loaded, Is.False);
        }

        [Test]
        public void Load_PrefixComparisonIsOrdinalAndCaseSensitive()
        {
            // Arrange — a culture-sensitive comparison would make this locale-dependent
            PrefixKeyVaultSecretManager manager = new("app");

            // Act
            bool loaded = manager.Load(new SecretProperties("APP-Foo"));

            // Assert
            Assert.That(loaded, Is.False);
        }

        [Test]
        public void GetKey_StripsPrefixAndMapsDoubleDashToColon()
        {
            // Arrange
            PrefixKeyVaultSecretManager manager = new("contoso-app");

            // Act
            string key = manager.GetKey(new KeyVaultSecret("contoso-app-Foo--Bar", "value"));

            // Assert
            Assert.That(key, Is.EqualTo("Foo:Bar"));
        }

        [Test]
        public void Load_WithEmptyPrefix_LoadsEverything()
        {
            // Arrange — the documented way to turn filtering off
            PrefixKeyVaultSecretManager manager = new(string.Empty);

            // Act
            bool loaded = manager.Load(new SecretProperties("other-Baz"));

            // Assert
            Assert.That(loaded, Is.True);
        }

        [Test]
        public void GetKey_WithEmptyPrefix_KeepsFullNameWithMapping()
        {
            // Arrange
            PrefixKeyVaultSecretManager manager = new(string.Empty);

            // Act
            string key = manager.GetKey(new KeyVaultSecret("other--Baz", "value"));

            // Assert — nothing is stripped, but the nesting convention still applies
            Assert.That(key, Is.EqualTo("other:Baz"));
        }
    }
}
