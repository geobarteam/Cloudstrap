namespace Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure
{
    using Azure.Storage.Blobs;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Offline blob-client doubles for the registration and resolution tests: constructing a client contacts
    /// nothing, so a test can assert on what the ladder produced (container name, account) without storage.
    /// </summary>
    internal static class ClaimCheckTestHost
    {
        /// <summary>The Azurite / storage-emulator shortcut connection string (account <c>devstoreaccount1</c>).</summary>
        public const string DevelopmentStorage = "UseDevelopmentStorage=true";

        /// <summary>The application container <c>AddCloudstrapBlobStorage</c> would register for the fixture system.</summary>
        public const string ApplicationContainer = "contoso";

        /// <summary>Builds a connection string for a fictitious account, so a second account can be told apart.</summary>
        public static string ConnectionStringFor(string accountName)
        {
            string key = Convert.ToBase64String(new byte[32]);
            return $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={key};EndpointSuffix=core.windows.net";
        }

        /// <summary>
        /// Registers the <see cref="BlobContainerClient"/> <c>AddCloudstrapBlobStorage</c> would register,
        /// counting how many times the factory runs.
        /// </summary>
        public static FactoryCounter RegisterApplicationContainerClient(IServiceCollection services)
        {
            FactoryCounter counter = new();
            services.AddSingleton(_ =>
            {
                counter.Invocations++;
                return new BlobContainerClient(DevelopmentStorage, ApplicationContainer);
            });
            return counter;
        }

        /// <summary>Counts factory invocations.</summary>
        public sealed class FactoryCounter
        {
            /// <summary>Gets or sets how many times the factory ran.</summary>
            public int Invocations
            {
                get; set;
            }
        }
    }
}
