namespace Cloudstrap.Messaging
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Options;
    using Wolverine;
    using Wolverine.ErrorHandling;

    /// <summary>
    /// The deferred tail of the bootstrap: a Wolverine extension the engine applies to its
    /// <see cref="WolverineOptions"/> when the host starts, after every registration-time default and every
    /// builder call — so the consumer's <c>Wolverine</c> delegate runs last, with final say. The one thing
    /// appended after it is the default retry ladder, deliberately the last global failure rule so the
    /// delegate's exception-specific rules match first.
    /// </summary>
    /// <remarks>
    /// Wolverine forbids container-registered extensions from altering service registrations, which is why
    /// transports and durability are applied at registration time and only pure options adjustments live here.
    /// </remarks>
    internal sealed class CloudstrapMessagingExtension : IWolverineExtension
    {
        private readonly MessagingRegistrationState _state;
        private readonly ICorrelationContextAccessor _correlation;
        private readonly IOptions<CorrelationOptions> _correlationOptions;

        public CloudstrapMessagingExtension(
            MessagingRegistrationState state,
            ICorrelationContextAccessor correlation,
            IOptions<CorrelationOptions> correlationOptions)
        {
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(correlation);
            ArgumentNullException.ThrowIfNull(correlationOptions);

            _state = state;
            _correlation = correlation;
            _correlationOptions = correlationOptions;
        }

        /// <inheritdoc />
        public void Configure(WolverineOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            // The transactional EF integration needs a durable store; the check runs here, at startup, so
            // UseSqlServer() may be called before or after AddCloudstrapTransactionalMessaging().
            if (_state.TransactionalDbContexts.Count > 0 && _state.DurabilityProvider is null)
            {
                string contexts = string.Join(", ", _state.TransactionalDbContexts.Select(type => type.Name));
                throw new InvalidOperationException(
                    $"AddCloudstrapTransactionalMessaging<{contexts}> requires a durability provider: call " +
                    $"UseSqlServer() on the {nameof(CloudstrapMessagingBuilder)} returned by AddCloudstrapMessaging().");
            }

            // Send-side correlation: stamp the configured header on every outgoing envelope; a blocked
            // handler is dead-lettered without retries — the failure is deterministic.
            options.MetadataRules.Add(new CorrelationEnvelopeRule(_correlation, _correlationOptions));
            options.Policies.OnException<CorrelationRequiredException>().MoveToErrorQueue();

            // The consumer's escape hatch: final say over identity, transport, conventions and policies.
            _state.Configurator.Wolverine?.Invoke(options);

            // The fallback failure rule — after the consumer's rules, so theirs match first.
            RetryLadder.Apply(options, _state.Messaging.Retries);
        }
    }
}
