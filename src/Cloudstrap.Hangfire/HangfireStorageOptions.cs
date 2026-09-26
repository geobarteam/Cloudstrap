namespace Cloudstrap.Hangfire
{
    /// <summary>
    /// Job storage settings, bound from <c>Cloudstrap:Hangfire:Storage</c>.
    /// </summary>
    public sealed class HangfireStorageOptions
    {
        /// <summary>
        /// Gets or sets the name of the <c>ConnectionStrings:</c> entry holding the SQL Server storage.
        /// </summary>
        /// <value>The connection string name. Defaults to <c>DefaultConnection</c>.</value>
        /// <remarks>
        /// Azure SQL with a managed identity works through the connection string itself
        /// (<c>Authentication=Active Directory Default</c>); there is no Cloudstrap credential setting.
        /// </remarks>
        public string ConnectionStringName { get; set; } = "DefaultConnection";

        /// <summary>
        /// Gets or sets the database schema the Hangfire tables live in.
        /// </summary>
        /// <value>
        /// The schema name, or <see langword="null"/> to use the Hangfire default (<c>HangFire</c>). Independent
        /// processing hosts sharing one database each set their own schema.
        /// </value>
        public string? SchemaName
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether Hangfire creates or migrates its schema at startup.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to prepare the schema, <see langword="false"/> to expect it to exist (installed
        /// by infrastructure-as-code from the Hangfire <c>Install.sql</c> script), or <see langword="null"/> to
        /// prepare it in the <c>Development</c> environment only. An explicit value always wins; the effective
        /// value is stated in the startup summary log line.
        /// </value>
        public bool? PrepareSchema
        {
            get; set;
        }
    }
}
