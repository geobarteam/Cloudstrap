namespace Cloudstrap.Hangfire
{
    using System.Reflection;

    /// <summary>
    /// The registration-time facts of the Hangfire integration: its presence in the service collection is what
    /// makes a second <c>AddCloudstrapHangfire</c> call fail fast, and it carries the effective posture to the
    /// startup summary and the dashboard mapping.
    /// </summary>
    internal sealed class HangfireRegistrationState
    {
        /// <summary>The Hangfire default schema, in force when <c>Storage:SchemaName</c> is unset (D-7).</summary>
        public const string DefaultSchemaName = "HangFire";

        public HangfireRegistrationState(
            HangfireOptions options,
            bool runServer,
            bool customStorage,
            bool prepareSchema,
            IReadOnlyList<Assembly> taskAssemblies)
        {
            Options = options;
            RunServer = runServer;
            CustomStorage = customStorage;
            PrepareSchema = prepareSchema;
            TaskAssemblies = taskAssemblies;
        }

        /// <summary>Gets the <c>Cloudstrap:Hangfire</c> section as bound at the registration call.</summary>
        public HangfireOptions Options
        {
            get;
        }

        /// <summary>Gets a value indicating whether this host is a processing host.</summary>
        public bool RunServer
        {
            get;
        }

        /// <summary>Gets a value indicating whether the consumer brought its own storage.</summary>
        public bool CustomStorage
        {
            get;
        }

        /// <summary>
        /// Gets a value indicating whether the schema is prepared at startup: the explicit
        /// <c>Storage:PrepareSchema</c> value, else <see langword="true"/> in <c>Development</c> only.
        /// </summary>
        public bool PrepareSchema
        {
            get;
        }

        /// <summary>Gets the assemblies that were scanned for tasks.</summary>
        public IReadOnlyList<Assembly> TaskAssemblies
        {
            get;
        }

        /// <summary>Gets the storage provider, as stated in the startup summary.</summary>
        public string StorageProvider => CustomStorage ? "custom" : "SqlServer";

        /// <summary>Gets the effective schema, as stated in the startup summary.</summary>
        public string SchemaName => CustomStorage ? "n/a" : Options.Storage.SchemaName ?? DefaultSchemaName;

        /// <summary>
        /// Gets or sets the path the dashboard was mapped at, or <see langword="null"/> while it is not mapped.
        /// </summary>
        public string? DashboardPath
        {
            get; set;
        }
    }
}
