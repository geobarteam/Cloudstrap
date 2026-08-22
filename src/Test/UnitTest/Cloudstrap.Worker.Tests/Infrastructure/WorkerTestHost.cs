namespace Cloudstrap.Worker.Tests.Infrastructure
{
    using System.Net;
    using System.Net.Sockets;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// Builds real generic hosts for the worker tests: a valid in-memory <c>Cloudstrap</c> section
    /// with neutral fixture values, a free-loopback-port helper so the suite never binds port 9000,
    /// and an <see cref="HttpClient"/> factory targeting the listener under test.
    /// </summary>
    internal static class WorkerTestHost
    {
        /// <summary>Returns a valid in-memory <c>Cloudstrap</c> section with neutral fixture values.</summary>
        public static Dictionary<string, string?> ValidSettings()
        {
            return new Dictionary<string, string?>
            {
                ["Cloudstrap:Application:SystemName"] = "contoso",
                ["Cloudstrap:Application:SubsystemName"] = "widgets",
                ["Cloudstrap:Application:SubsystemType"] = "worker",
                ["Cloudstrap:Application:EnvironmentTier"] = "Local",
            };
        }

        /// <summary>Creates a generic-host builder carrying the given in-memory settings.</summary>
        public static HostApplicationBuilder CreateBuilder(IDictionary<string, string?> settings)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(settings);
            return builder;
        }

        /// <summary>Acquires a currently-free loopback port (bind port 0, read, release).</summary>
        public static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>Creates a client targeting the loopback listener on the given port.</summary>
        public static HttpClient CreateProbeClient(int port)
        {
            return new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        }
    }
}
