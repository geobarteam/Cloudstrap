namespace Cloudstrap.Messaging.AzureBlob.Tests
{
    using Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NUnit.Framework;

    /// <summary>
    /// The <c>Cloudstrap:Messaging:ClaimCheck</c> options pipeline: the 200 KiB default and the
    /// <c>{SystemName}-claimcheck</c> container (DL-6, DL-9), and every misconfiguration failing at the call
    /// naming the exact key and never a value (AC-CK9).
    /// </summary>
    [TestFixture]
    public sealed class OptionsValidationTests
    {
        [Test]
        public void Options_Defaults_Are200KiBAndTheSystemNameClaimCheckContainer()
        {
            // Arrange
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(MessagingTestHost.ValidSettings());

            // Act
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck();
            AzureBlobClaimCheckRegistrationState state = RegisteredState(builder);

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(state.Options.OffloadThresholdBytes, Is.EqualTo(204_800));
                Assert.That(state.ContainerName, Is.EqualTo("contoso-claimcheck"));
            });
        }

        [Test]
        public void Options_ThresholdZeroOrNegative_FailsNamingTheThresholdKey()
        {
            // Arrange
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:ClaimCheck:OffloadThresholdBytes"] = "0";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CloudstrapMessagingBuilder messaging = builder.AddCloudstrapMessaging();

            // Act
            Exception? failure = Assert.Catch(() => messaging.UseAzureBlobClaimCheck());

            // Assert
            Assert.That(failure!.ToString(), Does.Contain("'Cloudstrap:Messaging:ClaimCheck:OffloadThresholdBytes'"));
        }

        [Test]
        public void Options_InvalidContainerName_FailsNamingTheContainerKey_NeverTheValue()
        {
            // Arrange
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:ClaimCheck:ContainerName"] = "Bad_Name--x";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CloudstrapMessagingBuilder messaging = builder.AddCloudstrapMessaging();

            // Act
            Exception? failure = Assert.Catch(() => messaging.UseAzureBlobClaimCheck());

            // Assert
            string text = failure!.ToString();
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain("'Cloudstrap:Messaging:ClaimCheck:ContainerName'"));
                Assert.That(text, Does.Not.Contain("Bad_Name--x"));
            });
        }

        [Test]
        public void Options_SystemNameThatYieldsAnInvalidDefaultContainer_FailsNamingTheContainerKey()
        {
            // Arrange — an underscore is legal in a system name but not in a container name.
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Application:SystemName"] = "a_b";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CloudstrapMessagingBuilder messaging = builder.AddCloudstrapMessaging();

            // Act
            Exception? failure = Assert.Catch(() => messaging.UseAzureBlobClaimCheck());

            // Assert — the fix is the ContainerName key.
            Assert.That(failure!.ToString(), Does.Contain("'Cloudstrap:Messaging:ClaimCheck:ContainerName'"));
        }

        [Test]
        public void Options_ContainerNameOverride_Wins()
        {
            // Arrange
            Dictionary<string, string?> settings = MessagingTestHost.ValidSettings();
            settings["Cloudstrap:Messaging:ClaimCheck:ContainerName"] = "exports-claimcheck";
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);

            // Act
            builder.AddCloudstrapMessaging().UseAzureBlobClaimCheck();

            // Assert
            Assert.That(RegisteredState(builder).ContainerName, Is.EqualTo("exports-claimcheck"));
        }

        private static AzureBlobClaimCheckRegistrationState RegisteredState(HostApplicationBuilder builder)
        {
            ServiceDescriptor descriptor = builder.Services.Single(d => d.ServiceType == typeof(AzureBlobClaimCheckRegistrationState));
            return (AzureBlobClaimCheckRegistrationState)descriptor.ImplementationInstance!;
        }
    }
}
