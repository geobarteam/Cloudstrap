namespace Cloudstrap.Messaging.Tests.Infrastructure
{
    using Cloudstrap.TestInfrastructure;

    /// <summary>The suite's own SQL Server catalog (#8 DD-3): dropped and recreated by the durability and transactional fixtures.</summary>
    internal static class TestDatabase
    {
        /// <summary>Gets the suite's test database on catalog <c>CloudstrapMessagingTests</c>.</summary>
        public static SqlServerTestDatabase Instance { get; } = new("CloudstrapMessagingTests");
    }
}
