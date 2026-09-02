namespace Cloudstrap.Messaging.Tests.Infrastructure
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// Builds real generic hosts for the messaging tests: a valid in-memory <c>Cloudstrap:Application</c>
    /// section with neutral fixture values (workload name <c>contoso-orders-worker</c>) and nothing else —
    /// the zero-configuration default is the behavior under test.
    /// </summary>
    internal static class MessagingTestHost
    {
        /// <summary>The workload name the <see cref="ValidSettings"/> section computes.</summary>
        public const string WorkloadName = "contoso-orders-worker";

        /// <summary>Returns a valid in-memory <c>Cloudstrap:Application</c> section with neutral fixture values.</summary>
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
        public static HostApplicationBuilder CreateBuilder(IDictionary<string, string?> settings)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(settings);
            return builder;
        }
    }
}
