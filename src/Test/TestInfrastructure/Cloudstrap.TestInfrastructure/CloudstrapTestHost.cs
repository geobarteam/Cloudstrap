namespace Cloudstrap.TestInfrastructure
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// Builds real generic hosts for Cloudstrap's own suites: a valid in-memory <c>Cloudstrap:Application</c> section
    /// with neutral fixture values (workload name <c>contoso-orders-worker</c>) and nothing else, so a suite's
    /// zero-configuration default is what it observes.
    /// </summary>
    public static class CloudstrapTestHost
    {
        /// <summary>The workload name the <see cref="ValidSettings"/> section computes.</summary>
        public const string WorkloadName = "contoso-orders-worker";

        /// <summary>Returns a fresh, valid in-memory <c>Cloudstrap:Application</c> section with neutral fixture values.</summary>
        /// <returns>A new dictionary the caller may extend.</returns>
        public static Dictionary<string, string?> ValidSettings()
        {
            return new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "contoso",
                ["Cloudstrap:Application:SubsystemName"] = "orders",
                ["Cloudstrap:Application:SubsystemType"] = "worker",
            };
        }

        /// <summary>Creates a generic-host builder carrying the given in-memory settings.</summary>
        /// <param name="settings">The in-memory configuration.</param>
        /// <returns>The host builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
        public static HostApplicationBuilder CreateBuilder(IDictionary<string, string?> settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(settings);
            return builder;
        }
    }
}
