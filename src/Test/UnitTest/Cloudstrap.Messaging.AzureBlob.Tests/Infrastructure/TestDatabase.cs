namespace Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure
{
    using Cloudstrap.TestInfrastructure;

    /// <summary>The suite's own SQL Server catalog (#8 DD-3): dropped and recreated by the claim-check round-trip, outbox and missing-payload fixtures.</summary>
    internal static class TestDatabase
    {
        /// <summary>Gets the suite's test database on catalog <c>CloudstrapClaimCheckTests</c>.</summary>
        public static SqlServerTestDatabase Instance { get; } = new("CloudstrapClaimCheckTests");
    }
}
