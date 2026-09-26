namespace Cloudstrap.Hangfire
{
    using System.Reflection;
    using Cloudstrap.Core;
    using global::Hangfire;
    using global::Hangfire.SqlServer;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using OpenTelemetry.Trace;

    /// <summary>
    /// Bootstraps Hangfire on the generic host: storage, recurring-task discovery and, on a processing host,
    /// the server and startup scheduling.
    /// </summary>
    public static class HostApplicationBuilderExtensions
    {
        /// <summary>
        /// Registers Hangfire driven by the <c>Cloudstrap:Hangfire</c> section: SQL Server storage on a named
        /// connection string, discovery of <see cref="IBackgroundRecurringTask"/> implementations, and — on a
        /// processing host — the Hangfire server plus the scheduling of every declared task at startup.
        /// </summary>
        /// <param name="builder">The host application builder to configure.</param>
        /// <param name="configure">
        /// Optional code-level hooks: <see cref="CloudstrapHangfireConfigurator.RunServer"/> chooses the host
        /// role; the <c>SqlServer</c>, <c>Storage</c> and <c>Server</c> hatches run last over the defaults.
        /// </param>
        /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The method was already called on this host, or the connection string named by
        /// <c>Cloudstrap:Hangfire:Storage:ConnectionStringName</c> does not resolve. The failure names the key and
        /// never echoes a value.
        /// </exception>
        /// <exception cref="ConfigurationValidationException">The <c>Cloudstrap:Hangfire</c> section is invalid.</exception>
        /// <remarks>
        /// <para>
        /// Golden rules: run exactly one processing host (one task set) per storage schema — independent
        /// processing hosts sharing a database each set their own <c>Storage:SchemaName</c>; every other host
        /// (a dashboard host, an API that only enqueues) sets <c>RunServer = false</c>.
        /// </para>
        /// <para>
        /// The section is read and validated eagerly: an invalid section throws at this call, before the host is
        /// built, and the same rules run again at host startup. Add late configuration sources <em>before</em>
        /// calling this method.
        /// </para>
        /// <para>
        /// For fire-and-forget or delayed work, inject the Hangfire <c>IBackgroundJobClient</c>, which this
        /// method registers; there is no Cloudstrap facade over it. Such jobs run on a processing host that can
        /// resolve their target type.
        /// </para>
        /// </remarks>
        public static IHostApplicationBuilder AddCloudstrapHangfire(
            this IHostApplicationBuilder builder,
            Action<CloudstrapHangfireConfigurator>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(HangfireRegistrationState)))
            {
                throw new InvalidOperationException(
                    $"{nameof(AddCloudstrapHangfire)} was already called on this host. Call it once; choose the host " +
                    $"role and the hatches through its {nameof(CloudstrapHangfireConfigurator)}.");
            }

            CloudstrapHangfireConfigurator configurator = new();
            configure?.Invoke(configurator);

            HangfireOptions options = BindAndValidate(builder.Configuration);
            bool customStorage = configurator.Storage is not null;
            string? connectionString = customStorage ? null : ResolveConnectionString(builder.Configuration, options.Storage);

            // Schema preparation: the explicit value wins; otherwise only Development prepares its own schema.
            bool prepareSchema = options.Storage.PrepareSchema ?? builder.Environment.IsDevelopment();
            Assembly[] taskAssemblies = ResolveTaskAssemblies(configurator);

            HangfireRegistrationState state = new(options, configurator.RunServer, customStorage, prepareSchema, taskAssemblies);
            builder.Services.AddSingleton(state);
            builder.Services.AddOptions<HangfireOptions>()
                .Bind(builder.Configuration.GetSection(HangfireOptions.SectionName))
                .ValidateOnStart();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<HangfireOptions>, HangfireOptionsValidator>());

            builder.Services.AddHangfire((_, configuration) =>
            {
                // The simple assembly-name serializer keeps stored type names version-free, so a storage-only host
                // and a processing host on different builds read each other's jobs.
                configuration
                    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings();

                if (configurator.Storage is { } custom)
                {
                    custom(configuration);
                }
                else
                {
                    SqlServerStorageOptions storageOptions = SqlServerStorageOptionsFactory.Create(
                        options.Storage,
                        prepareSchema,
                        configurator.SqlServer);
                    configuration.UseSqlServerStorage(connectionString, storageOptions);
                }
            });

            RegisterTasks(builder.Services, taskAssemblies);
            builder.Services.TryAddScoped<RecurringTaskRunner>();
            builder.Services.TryAddScoped<RecurringJobsScheduler>();

            if (configurator.RunServer)
            {
                // Hosted services start in registration order: scheduling completes before the server dequeues.
                builder.Services.AddHostedService<RecurringTaskSchedulingService>();
                builder.Services.AddHangfireServer((_, server) =>
                    ServerOptionsComposer.Apply(server, options.Server, configurator.Server));
            }

            // Telemetry is additive: the span source joins whatever tracer pipeline the host builds. No exporter and
            // no provider are registered here; without a pipeline the source is inert.
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
                tracing.AddSource(CloudstrapHangfireActivitySources.RecurringTask));

            return builder;
        }

        private static HangfireOptions BindAndValidate(IConfiguration configuration)
        {
            HangfireOptions options = configuration
                .GetSection(HangfireOptions.SectionName)
                .Get<HangfireOptions>() ?? new HangfireOptions();

            ValidateOptionsResult result = new HangfireOptionsValidator().Validate(Options.DefaultName, options);
            if (result.Failed)
            {
                throw new ConfigurationValidationException(
                    $"The '{HangfireOptions.SectionName}' configuration section is invalid.",
                    result.Failures ?? []);
            }

            return options;
        }

        private static string ResolveConnectionString(IConfiguration configuration, HangfireStorageOptions storage)
        {
            string? connectionString = configuration.GetConnectionString(storage.ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"The Hangfire storage connection string does not resolve: add a " +
                    $"'ConnectionStrings:{storage.ConnectionStringName}' entry, or point " +
                    $"'{HangfireOptions.SectionName}:Storage:ConnectionStringName' at an existing one.");
            }

            return connectionString;
        }

        private static Assembly[] ResolveTaskAssemblies(CloudstrapHangfireConfigurator configurator)
        {
            if (configurator.TaskAssemblies.Count > 0)
            {
                return [.. configurator.TaskAssemblies.Distinct()];
            }

            // The library would otherwise be the scan root; tasks live in the host.
            Assembly? entryAssembly = Assembly.GetEntryAssembly();
            return entryAssembly is null ? [] : [entryAssembly];
        }

        /// <summary>
        /// Registers every concrete task (public or internal) as the interface only, transient. Registrations are
        /// de-duplicated per implementation type, so a task the consumer also registers by hand is not declared twice.
        /// </summary>
        private static void RegisterTasks(IServiceCollection services, Assembly[] assemblies)
        {
            ServiceCollection scanned = [];
            scanned.Scan(scan => scan
                .FromAssemblies(assemblies)
                .AddClasses(classes => classes.AssignableTo<IBackgroundRecurringTask>(), publicOnly: false)
                .As<IBackgroundRecurringTask>()
                .WithTransientLifetime());

            foreach (ServiceDescriptor descriptor in scanned)
            {
                if (descriptor.ImplementationType is { IsGenericTypeDefinition: false })
                {
                    services.TryAddEnumerable(descriptor);
                }
            }
        }
    }
}
