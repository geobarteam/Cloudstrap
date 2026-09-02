namespace Cloudstrap.Messaging
{
    using Azure.Identity;
    using Microsoft.Extensions.Configuration;
    using Wolverine;
    using Wolverine.AzureServiceBus;
    using Wolverine.SqlServer;
    using Wolverine.SqlServer.Transport;

    /// <summary>
    /// Applies the selected transport to the engine at registration time. Broker transports register
    /// services (message stores, transport singletons), which Wolverine only allows while the host's service
    /// collection is still open — so this runs inside the registration call, on the eagerly bound options.
    /// </summary>
    internal static class MessagingTransportSetup
    {
        /// <summary>
        /// Configures the transport named by <see cref="CloudstrapMessagingOptions.Transport"/>.
        /// </summary>
        /// <param name="options">The engine options to configure.</param>
        /// <param name="state">The registration state carrying the bound options and the endpoint name.</param>
        /// <param name="configuration">The host configuration, consulted for <c>ConnectionStrings:</c> entries.</param>
        /// <exception cref="InvalidOperationException">A connection string named by the options does not resolve.</exception>
        public static void Apply(WolverineOptions options, MessagingRegistrationState state, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(configuration);

            switch (state.Messaging.Transport)
            {
                case MessagingTransport.SqlServer:
                    ApplySqlServer(options, state, configuration);
                    break;

                case MessagingTransport.AzureServiceBus:
                    ApplyAzureServiceBus(options, state, configuration);
                    break;

                case MessagingTransport.Local:
                default:
                    break;
            }
        }

        /// <summary>
        /// Resolves a connection string by name, failing with a message that names the configuration key and
        /// never echoes a value.
        /// </summary>
        /// <param name="configuration">The host configuration.</param>
        /// <param name="name">The <c>ConnectionStrings:</c> entry name.</param>
        /// <param name="configurationKey">The key that named the entry, for the failure message.</param>
        /// <returns>The connection string.</returns>
        /// <exception cref="InvalidOperationException">The entry does not resolve.</exception>
        public static string ResolveConnectionString(IConfiguration configuration, string? name, string configurationKey)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            string? connectionString = string.IsNullOrWhiteSpace(name) ? null : configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"'{configurationKey}' names a connection string that does not resolve: add a " +
                    $"'ConnectionStrings:{name}' entry to the configuration.");
            }

            return connectionString;
        }

        private static void ApplySqlServer(WolverineOptions options, MessagingRegistrationState state, IConfiguration configuration)
        {
            // The SQL Server transport rides on the SQL Server message store: queue tables in the transport
            // schema (shared by every node exchanging messages over the database), the inbox/outbox in this
            // workload's own schema.
            string connectionString = ResolveConnectionString(
                configuration,
                state.Messaging.SqlTransport.ConnectionStringName,
                $"{CloudstrapMessagingOptions.SectionName}:SqlTransport:ConnectionStringName");
            options.UseSqlServerPersistenceAndTransport(
                connectionString,
                schema: state.DurabilitySchemaName,
                transportSchema: state.Messaging.SqlTransport.SchemaName);
            options.ListenToSqlServerQueue(state.EndpointName);
            foreach (SqlServerTransport transport in options.Transports.OfType<SqlServerTransport>())
            {
                transport.AutoProvision = state.AutoProvision;
            }

            state.MessageStore = $"SQL Server (schema '{state.DurabilitySchemaName}', the transport's database)";
        }

        private static void ApplyAzureServiceBus(WolverineOptions options, MessagingRegistrationState state, IConfiguration configuration)
        {
            // Platform credentials by default: the namespace with DefaultAzureCredential (environment,
            // workload identity, managed identity). The named connection string is the emulator fallback.
            AzureServiceBusOptions serviceBus = state.Messaging.AzureServiceBus;
            if (!string.IsNullOrWhiteSpace(serviceBus.FullyQualifiedNamespace))
            {
                options.UseAzureServiceBus(serviceBus.FullyQualifiedNamespace, new DefaultAzureCredential());
            }
            else
            {
                options.UseAzureServiceBus(ResolveConnectionString(
                    configuration,
                    serviceBus.ConnectionStringName,
                    $"{CloudstrapMessagingOptions.SectionName}:AzureServiceBus:ConnectionStringName"));
            }

            // D-1: this node's inbox queue is its endpoint name; D-2: the transport-level error queue keeps
            // the {SystemName}-error naming convention.
            options.ListenToAzureServiceBusQueue(state.EndpointName);
            foreach (AzureServiceBusTransport transport in options.Transports.OfType<AzureServiceBusTransport>())
            {
                transport.AutoProvision = state.AutoProvision;
                transport.Queues[transport.MaybeCorrectName(state.EndpointName)].DeadLetterQueueName = state.DeadLetterQueueName;
            }
        }
    }
}
