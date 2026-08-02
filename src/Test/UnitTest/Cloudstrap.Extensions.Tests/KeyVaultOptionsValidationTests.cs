namespace Cloudstrap.Extensions.Tests
{
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-E2/AC-E3 rules: enabling KeyVault without a usable vault URI is a configuration error, while
    /// leaving it disabled is always valid — that is what makes the call safe to leave in every environment.
    /// </summary>
    [TestFixture]
    public sealed class KeyVaultOptionsValidationTests
    {
        [Test]
        public void Validate_EnabledWithoutVaultUri_FailsNamingTheKey()
        {
            // Arrange
            KeyVaultOptionsValidator validator = new();
            KeyVaultOptions options = new() { Enabled = true };

            // Act
            ValidateOptionsResult result = validator.Validate(name: null, options);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(result.Failed, Is.True);
                Assert.That(result.FailureMessage, Does.Contain("Cloudstrap:KeyVault:VaultUri"));
            });
        }

        [Test]
        public void Validate_EnabledWithRelativeVaultUri_FailsNamingTheKey()
        {
            // Arrange
            KeyVaultOptionsValidator validator = new();
            KeyVaultOptions options = new()
            {
                Enabled = true,
                VaultUri = new Uri("contoso-vault", UriKind.Relative),
            };

            // Act
            ValidateOptionsResult result = validator.Validate(name: null, options);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(result.Failed, Is.True);
                Assert.That(result.FailureMessage, Does.Contain("Cloudstrap:KeyVault:VaultUri"));
            });
        }

        [Test]
        public void Validate_DisabledWithoutVaultUri_Succeeds()
        {
            // Arrange — the shape every environment that does not use a vault has
            KeyVaultOptionsValidator validator = new();
            KeyVaultOptions options = new();

            // Act
            ValidateOptionsResult result = validator.Validate(name: null, options);

            // Assert
            Assert.That(result.Succeeded, Is.True);
        }

        [Test]
        public void Validate_EnabledWithAbsoluteVaultUri_Succeeds()
        {
            // Arrange
            KeyVaultOptionsValidator validator = new();
            KeyVaultOptions options = new()
            {
                Enabled = true,
                VaultUri = new Uri("https://contoso-vault.vault.azure.net/"),
            };

            // Act
            ValidateOptionsResult result = validator.Validate(name: null, options);

            // Assert
            Assert.That(result.Succeeded, Is.True);
        }

        [Test]
        public void SectionName_IsRootedUnderCloudstrap()
        {
            // Arrange & Act & Assert
            Assert.That(KeyVaultOptions.SectionName, Is.EqualTo("Cloudstrap:KeyVault"));
        }
    }
}
