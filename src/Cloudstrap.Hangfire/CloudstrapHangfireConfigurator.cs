namespace Cloudstrap.Hangfire
{
    using System.Reflection;
    using global::Hangfire;
    using global::Hangfire.SqlServer;

    /// <summary>
    /// Code-level hooks over the configuration-driven defaults, supplied to
    /// <see cref="HostApplicationBuilderExtensions.AddCloudstrapHangfire"/>. Each hatch runs after the Cloudstrap
    /// defaults it adjusts, so what it sets wins.
    /// </summary>
    public sealed class CloudstrapHangfireConfigurator
    {
        /// <summary>
        /// Gets or sets a value indicating whether this host is a processing host: it runs the Hangfire server
        /// <em>and</em> schedules the declared recurring tasks at startup.
        /// </summary>
        /// <value>
        /// <see langword="true"/> (the default) for a processing host; <see langword="false"/> for a storage-only
        /// host (dashboard and <c>IBackgroundJobClient</c> enqueueing) that never dequeues, schedules or
        /// reconciles. Run exactly one processing host (one task set) per storage schema.
        /// </value>
        public bool RunServer { get; set; } = true;

        /// <summary>
        /// Gets the assemblies scanned for <see cref="IBackgroundRecurringTask"/> implementations.
        /// </summary>
        /// <value>Empty by default, meaning the entry assembly. Any entry replaces that default.</value>
        public IList<Assembly> TaskAssemblies { get; } = [];

        /// <summary>
        /// Gets or sets a delegate that tunes the SQL Server storage options. It runs last, over the Cloudstrap
        /// defaults and the <c>Cloudstrap:Hangfire:Storage</c> values.
        /// </summary>
        /// <value>The delegate, or <see langword="null"/> to keep the defaults.</value>
        public Action<SqlServerStorageOptions>? SqlServer
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a delegate that brings its own storage (for example another storage provider). When set,
        /// SQL Server is not configured: the <c>Cloudstrap:Hangfire:Storage</c> values and <see cref="SqlServer"/>
        /// are ignored and no connection string is required.
        /// </summary>
        /// <value>The delegate, or <see langword="null"/> to use SQL Server storage.</value>
        /// <remarks>
        /// The delegate also receives the global configuration after the serializer defaults are applied, so it
        /// can change them; a storage-only host and a processing host must then agree on the change.
        /// </remarks>
        public Action<IGlobalConfiguration>? Storage
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a delegate with final say over the processing server options (for example the server
        /// name). It runs after the <c>Cloudstrap:Hangfire:Server</c> values; unused on a storage-only host.
        /// </summary>
        /// <value>The delegate, or <see langword="null"/> to keep the defaults.</value>
        public Action<BackgroundJobServerOptions>? Server
        {
            get; set;
        }
    }
}
