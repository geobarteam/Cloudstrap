namespace Cloudstrap.Hangfire.Tests.Infrastructure
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Logging.Testing;

    /// <summary>
    /// Builds real hosts on the suite's <c>CloudstrapHangfireTests</c> SQL Server catalog (LocalDB, or the <c>CLOUDSTRAP_TEST_SQL</c> server): Hangfire in
    /// its own <see cref="Schema"/>, two workers on the <c>default</c> and <c>critical</c> queues, and short
    /// polling and shutdown intervals so triggers are picked up in seconds.
    /// </summary>
    internal static class HangfireSqlHost
    {
        /// <summary>The schema the storage tests install Hangfire into.</summary>
        public const string Schema = "hangfire_tests";

        /// <summary>The worker count every processing host is configured with.</summary>
        public const int WorkerCount = 2;

        /// <summary>Creates a host builder on the test database.</summary>
        /// <param name="logs">A provider capturing the host's log entries, if any.</param>
        /// <param name="runServer">Whether the host is a processing host.</param>
        /// <param name="schema">The Hangfire schema.</param>
        /// <param name="prepareSchema">Whether Hangfire installs its schema at startup.</param>
        public static HostApplicationBuilder CreateBuilder(
            FakeLogCollector? logs = null,
            bool runServer = true,
            string schema = Schema,
            bool prepareSchema = true)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Production,
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDatabase.Instance.ConnectionString,
                ["Cloudstrap:Hangfire:Storage:SchemaName"] = schema,
                ["Cloudstrap:Hangfire:Storage:PrepareSchema"] = prepareSchema ? "true" : "false",
                ["Cloudstrap:Hangfire:Server:WorkerCount"] = WorkerCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Cloudstrap:Hangfire:Server:Queues:0"] = "default",
                ["Cloudstrap:Hangfire:Server:Queues:1"] = "critical",
            });
            builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(10));
            if (logs is not null)
            {
                builder.Logging.AddProvider(new FakeLoggerProvider(logs));
            }

            builder.AddCloudstrapHangfire(hangfire =>
            {
                hangfire.RunServer = runServer;
                hangfire.Server = server =>
                {
                    server.SchedulePollingInterval = TimeSpan.FromSeconds(1);
                    server.ShutdownTimeout = TimeSpan.FromSeconds(5);
                };
            });
            return builder;
        }

        /// <summary>Polls a condition until it holds or the deadline passes.</summary>
        /// <param name="condition">The condition.</param>
        /// <param name="timeout">The deadline.</param>
        /// <returns><see langword="true"/> when the condition held in time.</returns>
        public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(200);
            }

            return condition();
        }
    }
}
