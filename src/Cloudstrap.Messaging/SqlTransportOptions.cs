namespace Cloudstrap.Messaging
{
    /// <summary>
    /// SQL Server transport settings, bound from <c>Cloudstrap:Messaging:SqlTransport</c>, used when the
    /// transport is <see cref="MessagingTransport.SqlServer"/>.
    /// </summary>
    public sealed class SqlTransportOptions
    {
        /// <summary>
        /// Gets or sets the name of the <c>ConnectionStrings:</c> entry the queue tables live on. The value is a
        /// name, never the connection string itself.
        /// </summary>
        /// <value>The connection string name. Defaults to <c>DefaultConnection</c>.</value>
        public string ConnectionStringName { get; set; } = "DefaultConnection";

        /// <summary>
        /// Gets or sets the schema holding the queue tables. Every node exchanging messages over one database
        /// must use the same transport schema — sender and listener share the queue tables.
        /// </summary>
        /// <value>The schema name, or <see langword="null"/> to use the engine's default schema.</value>
        public string? SchemaName
        {
            get; set;
        }
    }
}
