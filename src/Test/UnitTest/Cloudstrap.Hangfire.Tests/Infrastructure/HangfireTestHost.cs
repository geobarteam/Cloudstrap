namespace Cloudstrap.Hangfire.Tests.Infrastructure
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// Builds real generic hosts for the Hangfire tests: a valid <c>Cloudstrap:Application</c> section, an
    /// unreachable <c>ConnectionStrings:DefaultConnection</c> and <c>PrepareSchema = false</c>, so registration
    /// never opens a SQL connection.
    /// </summary>
    internal static class HangfireTestHost
    {
        /// <summary>A syntactically valid connection string to a host that never answers.</summary>
        public const string UnreachableConnectionString =
            "Server=unreachable.invalid;Database=hangfire_fixture;User Id=fixture;Password=fixture-password;Connect Timeout=1;";

        /// <summary>Returns the default in-memory settings.</summary>
        public static Dictionary<string, string?> ValidSettings()
        {
            return new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "contoso",
                ["Cloudstrap:Application:SubsystemName"] = "orders",
                ["Cloudstrap:Application:SubsystemType"] = "worker",
                ["ConnectionStrings:DefaultConnection"] = UnreachableConnectionString,
                ["Cloudstrap:Hangfire:Storage:PrepareSchema"] = "false",
            };
        }

        /// <summary>Creates a generic-host builder in the given environment carrying the given settings.</summary>
        public static HostApplicationBuilder CreateBuilder(
            IDictionary<string, string?>? settings = null,
            string environmentName = "Production")
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = environmentName,
            });
            builder.Configuration.AddInMemoryCollection(settings ?? ValidSettings());
            return builder;
        }
    }
}
