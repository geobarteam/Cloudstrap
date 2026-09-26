namespace Cloudstrap.Hangfire
{
    /// <summary>
    /// Settings of the Cloudstrap Hangfire integration, bound from the <c>Cloudstrap:Hangfire</c> configuration
    /// section. Every value has a working default: a host with no section at all uses SQL Server storage on
    /// <c>ConnectionStrings:DefaultConnection</c>.
    /// </summary>
    /// <remarks>
    /// No setting in this graph carries a secret. The storage connection string is resolved by <em>name</em>
    /// through the standard <c>ConnectionStrings:</c> section; validation failures and log lines name
    /// configuration keys, never values.
    /// </remarks>
    public sealed class HangfireOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Hangfire";

        /// <summary>
        /// Gets or sets the job storage settings.
        /// </summary>
        /// <value>The storage settings. Never <see langword="null"/>.</value>
        public HangfireStorageOptions Storage { get; set; } = new();

        /// <summary>
        /// Gets or sets the processing server settings, applied on a processing host only.
        /// </summary>
        /// <value>The server settings. Never <see langword="null"/>.</value>
        public HangfireServerOptions Server { get; set; } = new();

        /// <summary>
        /// Gets or sets the dashboard settings, applied where the dashboard is mapped.
        /// </summary>
        /// <value>The dashboard settings. Never <see langword="null"/>.</value>
        public HangfireDashboardOptions Dashboard { get; set; } = new();

        /// <summary>
        /// Gets the per-job overrides, keyed by <see cref="IBackgroundRecurringTask.JobId"/> and looked up
        /// case-insensitively. A configured value wins over the task's own declaration.
        /// </summary>
        /// <value>The per-job overrides. Empty by default.</value>
        public IDictionary<string, HangfireJobOptions> Jobs
        {
            get;
        } =
            new Dictionary<string, HangfireJobOptions>(StringComparer.OrdinalIgnoreCase);
    }
}
