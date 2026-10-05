namespace Cloudstrap.Demo.E2E.Tests.Infrastructure
{
    using Cloudstrap.TestInfrastructure;

    /// <summary>
    /// The suite's single SQL forwarding site (#8 D-4/AC-TS9): when the SQL override
    /// (<see cref="SqlServerTestDatabase.EnvironmentVariable"/>) is set, every SQL-backed demo host the suite boots receives the override's server, credentials and options on the demo
    /// catalog as <c>ConnectionStrings:DefaultConnection</c>; when unset, nothing is forwarded and the demo
    /// <c>appsettings.json</c> LocalDB connection stays authoritative.
    /// </summary>
    internal static class DemoDatabase
    {
        /// <summary>The catalog the demo apps' <c>appsettings.json</c> name, and the hosts create at startup.</summary>
        public const string Catalog = "CloudstrapDemo";

        /// <summary>Appends the connection-string override to a host's command-line arguments when one is set.</summary>
        /// <param name="arguments">The host's command-line arguments.</param>
        public static void AddConnectionStringOverride(ICollection<string> arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            string? connectionString = SqlServerTestDatabase.OverrideConnectionStringFor(Catalog);
            if (connectionString is not null)
            {
                arguments.Add("--ConnectionStrings:DefaultConnection=" + connectionString);
            }
        }
    }
}
