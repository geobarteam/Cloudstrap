namespace Cloudstrap.Messaging
{
    /// <summary>
    /// Durable inbox/outbox settings, bound from <c>Cloudstrap:Messaging:Durability</c>, applied once a
    /// durability provider is chosen (<c>UseSqlServer</c> on the <see cref="CloudstrapMessagingBuilder"/>).
    /// </summary>
    public sealed class DurabilityOptions
    {
        /// <summary>
        /// Gets or sets the name of the <c>ConnectionStrings:</c> entry the message store lives on. The value is
        /// a name, never the connection string itself.
        /// </summary>
        /// <value>The connection string name. Defaults to <c>DefaultConnection</c>.</value>
        public string ConnectionStringName { get; set; } = "DefaultConnection";

        /// <summary>
        /// Gets or sets the schema holding the inbox, outbox and dead-letter tables. Each workload gets its own
        /// schema, so several workloads share one database without collision — the isolation unit is a schema,
        /// not a table-name prefix.
        /// </summary>
        /// <value>
        /// The schema name, or <see langword="null"/> to derive it from the workload name (lowercased, every
        /// non-alphanumeric character replaced by <c>_</c>: <c>contoso-orders-worker</c> becomes
        /// <c>contoso_orders_worker</c>).
        /// </value>
        public string? SchemaName
        {
            get; set;
        }
    }
}
