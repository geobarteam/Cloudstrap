namespace Cloudstrap.Hangfire
{
    using global::Hangfire.SqlServer;

    /// <summary>
    /// Composes the SQL Server storage options: the Hangfire recommended settings, then the configured values,
    /// then the consumer's hatch, which runs last.
    /// </summary>
    internal static class SqlServerStorageOptionsFactory
    {
        /// <summary>Creates the storage options.</summary>
        /// <param name="storage">The <c>Cloudstrap:Hangfire:Storage</c> values.</param>
        /// <param name="prepareSchema">The effective schema-preparation posture.</param>
        /// <param name="hatch">The consumer's <see cref="CloudstrapHangfireConfigurator.SqlServer"/> hatch, if any.</param>
        /// <returns>The composed options.</returns>
        public static SqlServerStorageOptions Create(
            HangfireStorageOptions storage,
            bool prepareSchema,
            Action<SqlServerStorageOptions>? hatch)
        {
            SqlServerStorageOptions options = new()
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
                PrepareSchemaIfNecessary = prepareSchema,

                // The provider this package ships (D-4), made explicit instead of the Hangfire reflection probe.
                SqlClientFactory = Microsoft.Data.SqlClient.SqlClientFactory.Instance,
            };

            if (storage.SchemaName is { } schemaName)
            {
                options.SchemaName = schemaName;
            }

            hatch?.Invoke(options);
            return options;
        }
    }
}
