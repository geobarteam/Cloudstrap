namespace Cloudstrap.Messaging
{
    using System.Reflection;
    using Cloudstrap.Core;
    using JasperFx;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using OpenTelemetry.Metrics;
    using OpenTelemetry.Trace;
    using Wolverine;

    /// <summary>
    /// Bootstraps the Cloudstrap messaging node on the generic host.
    /// </summary>
    public static class HostApplicationBuilderExtensions
    {
        /// <summary>The name Wolverine emits its <c>ActivitySource</c> spans and <c>Meter</c> instruments under.</summary>
        private const string _wolverineTelemetryName = "Wolverine";

        /// <summary>
        /// Turns the host into a Wolverine messaging node driven by the <c>Cloudstrap:Messaging</c> section:
        /// the selected transport (in-process by default), the workload-name endpoint identity, message
        /// conventions and routing, retries and dead-lettering, correlation and additive OpenTelemetry.
        /// </summary>
        /// <param name="builder">The host application builder to configure.</param>
        /// <param name="configure">
        /// Optional code-level hooks: <see cref="CloudstrapMessagingConfigurator.Conventions"/> adjusts the
        /// classification and routing rules; <see cref="CloudstrapMessagingConfigurator.Wolverine"/> runs
        /// last, with final say over the engine's options.
        /// </param>
        /// <returns>
        /// A <see cref="CloudstrapMessagingBuilder"/> on which a durability provider and the transactional
        /// EF Core integration are chosen.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The method was already called on this host (a process hosts exactly one messaging node), a
        /// configuration value cannot be bound (for example an unknown <c>Cloudstrap:Messaging:Transport</c>),
        /// or a connection string named by the options does not resolve. The failure names the offending key
        /// and never echoes a value.
        /// </exception>
        /// <exception cref="ConfigurationValidationException">
        /// The <c>Cloudstrap</c> or <c>Cloudstrap:Messaging</c> section is invalid.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Nothing happens until this method is called — no configuration flag toggles the node. With no
        /// <c>Cloudstrap:Messaging</c> section the node runs on the in-process transport: no network, no SQL,
        /// no Azure.
        /// </para>
        /// <para>
        /// The configuration is read and validated eagerly: an invalid section throws at this call, before
        /// the host is built, and the same rules run again at host startup. Add late configuration sources
        /// (for example <c>AddCloudstrapKeyVault</c>) <em>before</em> calling this method. The
        /// <c>Cloudstrap:Application</c> section is consumed for the endpoint identity (the workload name)
        /// and is validated here as well.
        /// </para>
        /// <para>
        /// Wolverine's own types stay first-class in consumer code: inject <c>IMessageBus</c> to send, publish
        /// or invoke, and write plain Wolverine handlers — there is no Cloudstrap facade over the bus. Handler
        /// discovery starts from the host's entry assembly; add further assemblies through the
        /// <c>Wolverine</c> delegate.
        /// </para>
        /// </remarks>
        public static CloudstrapMessagingBuilder AddCloudstrapMessaging(
            this IHostApplicationBuilder builder,
            Action<CloudstrapMessagingConfigurator>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(MessagingRegistrationState)))
            {
                throw new InvalidOperationException(
                    $"{nameof(AddCloudstrapMessaging)} was already called on this host. A process hosts exactly " +
                    $"one messaging node: call {nameof(AddCloudstrapMessaging)} once and compose everything else " +
                    $"through the returned {nameof(CloudstrapMessagingBuilder)} or the configurator.");
            }

            CloudstrapMessagingConfigurator configurator = new();
            configure?.Invoke(configurator);

            // Eager reads, the suite's fail-fast convention: the identity and the messaging section are bound
            // and validated at the call. Transports and durability register services, which Wolverine only
            // allows while the service collection is still open — so the engine is shaped here, not at startup.
            ApplicationOptions application = builder.Configuration.GetCloudstrapOptions().Application;
            CloudstrapMessagingOptions messaging = BindAndValidate(builder.Configuration);

            MessageConventions conventions = MessageConventions.CreateDefault(messaging);
            configurator.Conventions?.Invoke(conventions);

            // Provisioning: the explicit value wins; otherwise only Development creates its own resources.
            bool autoProvision = messaging.AutoProvision ?? builder.Environment.IsDevelopment();

            MessagingRegistrationState state = new(configurator, messaging, application, conventions, autoProvision);
            builder.Services.AddSingleton(state);
            builder.Services.AddSingleton(conventions);

            builder.Services.AddCloudstrapCore();
            builder.Services.AddOptions<CloudstrapMessagingOptions>()
                .Bind(builder.Configuration.GetSection(CloudstrapMessagingOptions.SectionName))
                .ValidateOnStart();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<CloudstrapMessagingOptions>, CloudstrapMessagingOptionsValidator>());

            builder.UseWolverine(options =>
            {
                state.Wolverine = options;

                // The engine infers its "application assembly" from the caller of UseWolverine — this
                // library. Handlers live in the host, so discovery is pointed at the entry assembly.
                Assembly? entryAssembly = Assembly.GetEntryAssembly();
                if (entryAssembly is not null)
                {
                    options.ApplicationAssembly = entryAssembly;
                }

                // Endpoint identity: the node's inbox queue and subscription name.
                options.ServiceName = state.EndpointName;

                options.AutoBuildMessageStorageOnStartup = autoProvision ? AutoCreate.CreateOrUpdate : AutoCreate.None;

                MessagingTransportSetup.Apply(options, state, builder.Configuration);

                // Routing by convention over the (possibly consumer-adjusted) MessageConventions.
                options.RouteWith(new CloudstrapRoutingConvention(conventions, messaging.Transport, state.EndpointName));
            });

            // The deferred tail: applied when the host starts, after every builder call — the consumer's
            // Wolverine delegate has the final say.
            builder.Services.AddWolverineExtension<CloudstrapMessagingExtension>();
            builder.Services.AddHostedService<MessagingStartupSummaryLogger>();

            // Telemetry is additive: the engine's activity source and meter are contributed to whatever
            // OpenTelemetry pipeline the host builds — Cloudstrap's, a consumer's, or Aspire ServiceDefaults'.
            // No exporter and no provider are registered here; without a pipeline these are inert.
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(_wolverineTelemetryName));
            builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter(_wolverineTelemetryName));

            return new CloudstrapMessagingBuilder(builder, state);
        }

        private static CloudstrapMessagingOptions BindAndValidate(IConfiguration configuration)
        {
            CloudstrapMessagingOptions options = configuration
                .GetSection(CloudstrapMessagingOptions.SectionName)
                .Get<CloudstrapMessagingOptions>() ?? new CloudstrapMessagingOptions();

            CloudstrapMessagingOptionsValidator validator = new(configuration);
            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);
            if (result.Failed)
            {
                throw new ConfigurationValidationException(
                    $"The '{CloudstrapMessagingOptions.SectionName}' configuration section is invalid.",
                    result.Failures ?? []);
            }

            return options;
        }
    }
}
