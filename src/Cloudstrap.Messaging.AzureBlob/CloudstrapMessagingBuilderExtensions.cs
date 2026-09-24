namespace Cloudstrap.Messaging.AzureBlob
{
    using Azure;
    using Cloudstrap.Core;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Wolverine.ClaimCheck.AzureBlobStorage;
    using Wolverine.ErrorHandling;
    using Wolverine.Persistence;

    /// <summary>
    /// Adds the Azure Blob claim check to a Cloudstrap messaging node.
    /// </summary>
    public static class CloudstrapMessagingBuilderExtensions
    {
        /// <summary>
        /// Turns the node into a large-message node: any message whose serialized body exceeds
        /// <see cref="AzureBlobClaimCheckOptions.OffloadThresholdBytes"/> is stored in a dedicated blob
        /// container and only a reference travels through the transport; the receiving handler sees the
        /// whole message and knows nothing of blobs.
        /// </summary>
        /// <param name="builder">The messaging builder returned by <c>AddCloudstrapMessaging</c>.</param>
        /// <param name="configure">
        /// Optional code-level hooks: <see cref="AzureBlobClaimCheckSettings.ContainerClient"/> hands in the
        /// container client (wins over anything registered); <see cref="AzureBlobClaimCheckSettings.ClaimCheck"/>
        /// adjusts Wolverine's claim-check configuration last.
        /// </param>
        /// <returns>The same builder, so calls can be chained.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The method was already called on this node (a node carries exactly one claim check).
        /// </exception>
        /// <exception cref="ConfigurationValidationException">
        /// The <c>Cloudstrap:Messaging:ClaimCheck</c> section is invalid: a non-positive threshold, or a
        /// container name (configured, or derived from <c>Cloudstrap:Application:SystemName</c>) that breaks
        /// Azure's naming rules. The failure names the key, never a value.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Nothing about the storage account lives in this package. The client is resolved when the host
        /// starts from what the host already registered, in this order: the client handed in through the
        /// settings; a registered <c>BlobServiceClient</c>; the <c>BlobContainerClient</c> registered by
        /// <c>AddCloudstrapBlobStorage</c> (<c>Cloudstrap:Storage</c>), on whose account the claim-check
        /// container is opened. With none of these the host fails at startup naming the routes. No
        /// credential is constructed here.
        /// </para>
        /// <para>
        /// The payload container (<c>{SystemName}-claimcheck</c> by default) is created on first use — on the
        /// first upload that finds it missing — independently of <c>Cloudstrap:Messaging:AutoProvision</c>: the
        /// identity needs container-create and blob read/write rights, or the container is provisioned ahead
        /// through infrastructure-as-code. Payloads are retained after handling (a message may be retried or replayed
        /// from the dead-letter table); expire stale ones with a lifecycle-management policy on the container.
        /// </para>
        /// <para>
        /// The section is read and validated eagerly at this call and again at host startup. The call
        /// composes in any order with <c>UseSqlServer</c> and <c>AddCloudstrapTransactionalMessaging</c>. One
        /// startup log line states the posture: container, threshold and client source — never a URI or a
        /// connection string. The store is also registered as Wolverine's <see cref="IClaimCheckStore"/>, so
        /// consumer code can resolve it for ad-hoc operations.
        /// </para>
        /// </remarks>
        public static CloudstrapMessagingBuilder UseAzureBlobClaimCheck(
            this CloudstrapMessagingBuilder builder,
            Action<AzureBlobClaimCheckSettings>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            IServiceCollection services = builder.HostBuilder.Services;
            if (services.Any(descriptor => descriptor.ServiceType == typeof(AzureBlobClaimCheckRegistrationState)))
            {
                throw new InvalidOperationException(
                    $"{nameof(UseAzureBlobClaimCheck)} was already called on this messaging node. A node carries exactly " +
                    $"one claim check: call {nameof(UseAzureBlobClaimCheck)} once and shape it through the settings delegate.");
            }

            AzureBlobClaimCheckSettings settings = new();
            configure?.Invoke(settings);

            // Eager reads, the suite's fail-fast convention: the identity (already validated by the node) and
            // the claim-check section are bound and validated at the call, before the host is built.
            IConfiguration configuration = builder.HostBuilder.Configuration;
            ApplicationOptions application = configuration.GetCloudstrapOptions().Application;
            AzureBlobClaimCheckOptionsValidator validator = new(application);
            AzureBlobClaimCheckOptions options = BindAndValidate(configuration, validator);
            string containerName = AzureBlobClaimCheckOptionsValidator.EffectiveContainerName(options, application);

            AzureBlobClaimCheckRegistrationState state = new(options, settings, containerName);
            services.AddSingleton(state);
            services.AddOptions<AzureBlobClaimCheckOptions>()
                .Bind(configuration.GetSection(AzureBlobClaimCheckOptions.SectionName))
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AzureBlobClaimCheckOptions>>(validator));

            // The store lives in the container so the client ladder can see what the host registered.
            // Wolverine's claim check picks a DI-registered store up through its deferred-store path, which
            // is also the one path its bootstrap allows from inside a container-registered extension.
            services.AddSingleton<IClaimCheckStore>(provider =>
            {
                ClaimCheckClientResolution resolution = ClaimCheckClientResolver.Resolve(provider, settings, containerName);
                state.Resolution = resolution;
                ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(ClaimCheckPostureLogger.Category);
                return new ClaimCheckStore(new AzureBlobClaimCheckStore(resolution.Container), resolution.Container, logger);
            });

            // The deferred part: at engine bootstrap, run the ladder now — so a node with no account fails at
            // startup naming the routes, not at its first large message — and hand Wolverine its native
            // claim check: the DI store, the whole-body threshold, then the consumer's adjustments.
            builder.ConfigureEngine((provider, engine) =>
            {
                _ = provider.GetRequiredService<IClaimCheckStore>();
                ClaimCheckClientResolution resolution = state.Resolution
                    ?? throw new InvalidOperationException(
                        $"Another {nameof(IClaimCheckStore)} registration replaced the Azure Blob claim-check store; " +
                        $"remove it, or drop {nameof(UseAzureBlobClaimCheck)} and configure Wolverine's claim check yourself.");

                engine.UseClaimCheck(claimCheck =>
                {
                    claimCheck.AutoOffloadPayloadsLargerThan(options.OffloadThresholdBytes);
                    settings.ClaimCheck?.Invoke(claimCheck);
                });

                // A missing payload is deterministic: dead-letter without retries (DL-1). Wolverine already
                // dead-letters a failure raised while the body is restored during deserialization, before any
                // rule runs; this rule keeps the posture for a 404 raised anywhere else, ahead of the ladder.
                engine.Policies
                    .OnException<RequestFailedException>(failure => failure.Status == 404, "claim-check payload missing (HTTP 404)")
                    .MoveToErrorQueue();

                ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(ClaimCheckPostureLogger.Category);
                ClaimCheckPostureLogger.LogPosture(logger, resolution, options.OffloadThresholdBytes);
            });

            return builder;
        }

        private static AzureBlobClaimCheckOptions BindAndValidate(
            IConfiguration configuration,
            AzureBlobClaimCheckOptionsValidator validator)
        {
            AzureBlobClaimCheckOptions options = configuration
                .GetSection(AzureBlobClaimCheckOptions.SectionName)
                .Get<AzureBlobClaimCheckOptions>() ?? new AzureBlobClaimCheckOptions();

            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);
            if (result.Failed)
            {
                throw new ConfigurationValidationException(
                    $"The '{AzureBlobClaimCheckOptions.SectionName}' configuration section is invalid.",
                    result.Failures ?? []);
            }

            return options;
        }
    }
}
