namespace Cloudstrap.Messaging.AzureBlob.Tests.Infrastructure
{
    using System.Diagnostics;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using OpenTelemetry;
    using OpenTelemetry.Trace;
    using Wolverine;

    /// <summary>
    /// Two in-process nodes on one SQL Server database (spec D-3 — LocalDB by default), the #14 two-host
    /// pattern: a sender workload (<c>contoso-orders-api</c>) and a listener workload
    /// (<c>contoso-orders-worker</c>) on the SQL Server transport with a shared transport schema, the
    /// contracts namespace routed to the listener's queue, conventional local routing switched off on the
    /// sender, and <strong>one</strong> in-memory blob "account" handed to both nodes' claim checks. Local
    /// queues hand the message object over without serializing, so the offload can only be observed
    /// across a real transport hop.
    /// </summary>
    internal sealed class TwoNodeHarness : IAsyncDisposable
    {
        private const string _contractsNamespace = "Cloudstrap.Messaging.AzureBlob.Tests.Fixtures.Contracts";
        private const string _transportSchema = "claimcheck_transport";

        private TwoNodeHarness(IHost sender, IHost listener, CapturingLoggerProvider listenerLogs, List<Activity> listenerActivities)
        {
            Sender = sender;
            Listener = listener;
            ListenerLogs = listenerLogs;
            ListenerActivities = listenerActivities;
        }

        /// <summary>Gets the sending node.</summary>
        public IHost Sender
        {
            get;
        }

        /// <summary>Gets the listening node.</summary>
        public IHost Listener
        {
            get;
        }

        /// <summary>Gets everything the listener logged.</summary>
        public CapturingLoggerProvider ListenerLogs
        {
            get;
        }

        /// <summary>Gets the activities the listener's own OpenTelemetry pipeline exported.</summary>
        public List<Activity> ListenerActivities
        {
            get;
        }

        /// <summary>Gets the listener's recorder.</summary>
        public InvocationRecorder ListenerRecorder => Listener.Services.GetRequiredService<InvocationRecorder>();

        /// <summary>Gets the listener's attempt counter.</summary>
        public AttemptCounter ListenerAttempts => Listener.Services.GetRequiredService<AttemptCounter>();

        /// <summary>Builds and starts both nodes.</summary>
        /// <param name="blobs">The shared blob-account double both claim checks store through.</param>
        /// <param name="offloadThresholdBytes">An explicit threshold for both nodes, or <see langword="null"/> for the default.</param>
        /// <param name="listenerSettings">Extra listener settings (retries, for example).</param>
        /// <param name="listenerWolverine">The listener's consumer-level <c>Wolverine</c> delegate (final say), if any.</param>
        public static async Task<TwoNodeHarness> StartAsync(
            InMemoryBlobContainerClient blobs,
            long? offloadThresholdBytes = null,
            Action<Dictionary<string, string?>>? listenerSettings = null,
            Action<WolverineOptions>? listenerWolverine = null)
        {
            Dictionary<string, string?> sender = Settings("api", offloadThresholdBytes);
            sender[$"Cloudstrap:Messaging:Destinations:{_contractsNamespace}"] = "contoso-orders-worker";
            Dictionary<string, string?> listener = Settings("worker", offloadThresholdBytes);
            listenerSettings?.Invoke(listener);

            List<Activity> activities = [];
            IHost listenerHost = Build(
                listener,
                blobs,
                out CapturingLoggerProvider logs,
                builder => builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(activities)),
                listenerWolverine);
            IHost senderHost = Build(sender, blobs, out _, wolverine: options => options.Policies.DisableConventionalLocalRouting());

            await listenerHost.StartAsync();
            await senderHost.StartAsync();
            return new TwoNodeHarness(senderHost, listenerHost, logs, activities);
        }

        /// <summary>Sends a command from the sending node.</summary>
        public async Task SendAsync(object command)
        {
            using IServiceScope scope = Sender.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().SendAsync(command);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Sender.StopAsync();
            await Listener.StopAsync();
            Sender.Dispose();
            Listener.Dispose();
        }

        private static Dictionary<string, string?> Settings(string subsystemType, long? offloadThresholdBytes)
        {
            Dictionary<string, string?> settings = new()
            {
                ["Cloudstrap:Application:SystemName"] = "contoso",
                ["Cloudstrap:Application:SubsystemName"] = "orders",
                ["Cloudstrap:Application:SubsystemType"] = subsystemType,
                ["Cloudstrap:Messaging:Transport"] = "SqlServer",
                ["Cloudstrap:Messaging:SqlTransport:SchemaName"] = _transportSchema,
                ["Cloudstrap:Messaging:AutoProvision"] = "true",
                ["ConnectionStrings:DefaultConnection"] = SqlServerTestDatabase.ConnectionString,
            };
            if (offloadThresholdBytes is not null)
            {
                settings["Cloudstrap:Messaging:ClaimCheck:OffloadThresholdBytes"] = offloadThresholdBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return settings;
        }

        private static IHost Build(
            Dictionary<string, string?> settings,
            InMemoryBlobContainerClient blobs,
            out CapturingLoggerProvider logs,
            Action<HostApplicationBuilder>? extra = null,
            Action<WolverineOptions>? wolverine = null)
        {
            HostApplicationBuilder builder = MessagingTestHost.CreateBuilder(settings);
            CapturingLoggerProvider provider = new();
            builder.Logging.AddProvider(provider);
            builder.Services.AddSingleton<InvocationRecorder>();
            builder.Services.AddSingleton<AttemptCounter>();
            extra?.Invoke(builder);
            builder.AddCloudstrapMessaging(configurator => configurator.Wolverine = options =>
                {
                    options.Durability.Mode = DurabilityMode.Solo;
                    wolverine?.Invoke(options);
                })
                .UseSqlServer()
                .UseAzureBlobClaimCheck(claimCheck => claimCheck.ContainerClient = blobs);
            logs = provider;
            return builder.Build();
        }
    }
}
