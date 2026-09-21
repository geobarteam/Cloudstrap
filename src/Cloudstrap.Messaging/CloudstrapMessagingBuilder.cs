namespace Cloudstrap.Messaging
{
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Hosting;
    using Wolverine;
    using Wolverine.EntityFrameworkCore;
    using Wolverine.SqlServer;

    /// <summary>
    /// The builder <see cref="HostApplicationBuilderExtensions.AddCloudstrapMessaging"/> returns: the seam on
    /// which durability providers and the transactional EF Core integration are chosen.
    /// </summary>
    /// <remarks>
    /// This type is public and sealed on purpose: a future durability provider (PostgreSQL, for example) arrives
    /// as an extension method on this builder from its own leaf package — additively, with no signature change
    /// here. Builder calls run at registration time, before the host is built, and compose in any order. A leaf
    /// package shapes the engine itself through <see cref="ConfigureEngine"/>, the one door into the deferred
    /// bootstrap.
    /// </remarks>
    public sealed class CloudstrapMessagingBuilder
    {
        private const string _sqlServerProvider = "SQL Server";

        internal CloudstrapMessagingBuilder(IHostApplicationBuilder hostBuilder, MessagingRegistrationState state)
        {
            HostBuilder = hostBuilder;
            State = state;
        }

        /// <summary>
        /// Gets the host application builder the messaging node was registered on.
        /// </summary>
        /// <value>The host application builder.</value>
        public IHostApplicationBuilder HostBuilder
        {
            get;
        }

        internal MessagingRegistrationState State
        {
            get;
        }

        /// <summary>
        /// Gives the node a durable inbox/outbox on SQL Server: every listener, sending endpoint and local
        /// queue becomes durable, and failed messages land in the store's queryable dead-letter table.
        /// </summary>
        /// <param name="connectionStringName">
        /// The <c>ConnectionStrings:</c> entry the message store lives on, or <see langword="null"/> to use
        /// <see cref="DurabilityOptions.ConnectionStringName"/> (<c>DefaultConnection</c> by default). A name,
        /// never the connection string itself.
        /// </param>
        /// <returns>The same builder, so calls can be chained.</returns>
        /// <exception cref="InvalidOperationException">
        /// The named connection string does not resolve (the failure names the configuration key, never a
        /// value); a durability provider was already chosen; or, with the SQL Server transport, the durability
        /// connection string name differs from the transport's — the store lives on the transport's database.
        /// </exception>
        /// <remarks>
        /// <para>
        /// The store's tables land in the schema named by <see cref="DurabilityOptions.SchemaName"/>, or by
        /// default in a schema derived from the workload name (<c>contoso-orders-worker</c> →
        /// <c>contoso_orders_worker</c>), so several workloads share one database without collision. The
        /// isolation unit is a schema, not a table-name prefix.
        /// </para>
        /// <para>
        /// Wolverine creates the schema and tables at startup when auto-provisioning is on
        /// (<see cref="CloudstrapMessagingOptions.AutoProvision"/>); otherwise they are expected to exist.
        /// </para>
        /// </remarks>
        public CloudstrapMessagingBuilder UseSqlServer(string? connectionStringName = null)
        {
            if (State.DurabilityProvider is not null)
            {
                throw new InvalidOperationException(
                    $"A durability provider was already chosen for this node ({State.DurabilityProvider}); " +
                    $"call {nameof(UseSqlServer)} once.");
            }

            WolverineOptions options = State.Wolverine
                ?? throw new InvalidOperationException("The messaging node has not been registered on this host.");

            string name = connectionStringName ?? State.Messaging.Durability.ConnectionStringName;
            const string key = $"{CloudstrapMessagingOptions.SectionName}:Durability:ConnectionStringName";

            if (State.Messaging.Transport == MessagingTransport.SqlServer)
            {
                // The SQL Server transport already carries the store on its own database.
                if (!string.Equals(name, State.Messaging.SqlTransport.ConnectionStringName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"With the {nameof(MessagingTransport.SqlServer)} transport the message store lives on the " +
                        $"transport's database: '{key}' must name the same connection string as " +
                        $"'{CloudstrapMessagingOptions.SectionName}:SqlTransport:ConnectionStringName'.");
                }
            }
            else
            {
                string connectionString = MessagingTransportSetup.ResolveConnectionString(HostBuilder.Configuration, name, key);
                options.PersistMessagesWithSqlServer(connectionString, State.DurabilitySchemaName);
            }

            options.Policies.UseDurableInboxOnAllListeners();
            options.Policies.UseDurableOutboxOnAllSendingEndpoints();
            options.Policies.UseDurableLocalQueues();

            State.DurabilityProvider = _sqlServerProvider;
            State.MessageStore = $"{_sqlServerProvider} (schema '{State.DurabilitySchemaName}')";
            return this;
        }

        /// <summary>
        /// Registers <typeparamref name="TDbContext"/> wired into Wolverine's shared-transaction EF Core
        /// integration: a handler taking the context commits its entity changes and its outgoing messages in
        /// one database transaction, and non-handler code (an HTTP endpoint, for example) gets the same
        /// guarantee through <c>IDbContextOutbox&lt;TDbContext&gt;</c>.
        /// </summary>
        /// <typeparam name="TDbContext">The consumer's <see cref="DbContext"/> type.</typeparam>
        /// <param name="optionsAction">
        /// Configures the context — the database provider, interceptors, conventions — exactly as with
        /// <c>AddDbContext</c>. The provider must target the database the message store lives on.
        /// </param>
        /// <returns>The same builder, so calls can be chained.</returns>
        /// <remarks>
        /// <para>
        /// Handler path: transactional middleware is applied automatically to handlers that take
        /// <typeparamref name="TDbContext"/>; entity writes, cascaded messages and messages sent through the
        /// injected <c>IMessageBus</c> are committed together, and dispatch happens only after the commit. A
        /// handler that throws commits nothing.
        /// </para>
        /// <para>
        /// HTTP path: resolve <c>IDbContextOutbox&lt;TDbContext&gt;</c>, stage entities on its
        /// <c>DbContext</c>, send or publish through it, then call <c>SaveChangesAndFlushMessagesAsync</c>.
        /// An envelope committed but not yet dispatched (a crash in between) is recovered by the next node that
        /// starts on the store.
        /// </para>
        /// <para>
        /// A durability provider is required: without <see cref="UseSqlServer"/> the host fails at startup with
        /// a message naming it. The two calls compose in any order.
        /// </para>
        /// </remarks>
        public CloudstrapMessagingBuilder AddCloudstrapTransactionalMessaging<TDbContext>(
            Action<DbContextOptionsBuilder>? optionsAction = null)
            where TDbContext : DbContext
        {
            WolverineOptions options = State.Wolverine
                ?? throw new InvalidOperationException("The messaging node has not been registered on this host.");

            HostBuilder.Services.AddDbContextWithWolverineIntegration<TDbContext>(
                builder => optionsAction?.Invoke(builder),
                State.DurabilitySchemaName);

            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AutoApplyTransactions();

            State.TransactionalDbContexts.Add(typeof(TDbContext));
            return this;
        }

        /// <summary>
        /// Registers an engine contribution: a delegate a leaf package (the Azure Blob claim check, a future
        /// PostgreSQL durability provider) uses to shape the engine's <see cref="WolverineOptions"/> at
        /// bootstrap without reaching into this package's internals.
        /// </summary>
        /// <param name="contribution">
        /// The contribution. It receives the host's root <see cref="IServiceProvider"/> — so it can resolve what
        /// the host registered, a blob client for example — and the engine options to shape.
        /// </param>
        /// <returns>The same builder, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="contribution"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// Ordering contract: contributions run when the host starts, in registration order, after the
        /// Cloudstrap defaults and the correlation rule, <em>before</em> the consumer's
        /// <see cref="CloudstrapMessagingConfigurator.Wolverine"/> delegate and <em>before</em> the default
        /// retry ladder. A contribution's exception-specific failure rule therefore matches ahead of the
        /// catch-all ladder, and the consumer keeps final say over everything a contribution set.
        /// </para>
        /// <para>
        /// Not idempotent by design: each call appends another contribution. The documented consumer door
        /// remains <see cref="CloudstrapMessagingConfigurator.Wolverine"/>, which runs after all contributions.
        /// </para>
        /// <para>
        /// Wolverine forbids service registrations at this point of the bootstrap: a contribution adjusts the
        /// options only. Register services on the <see cref="HostBuilder"/> service collection at registration
        /// time instead, and resolve them from the provider the contribution receives.
        /// </para>
        /// </remarks>
        public CloudstrapMessagingBuilder ConfigureEngine(Action<IServiceProvider, WolverineOptions> contribution)
        {
            ArgumentNullException.ThrowIfNull(contribution);

            State.EngineContributions.Add(contribution);
            return this;
        }
    }
}
