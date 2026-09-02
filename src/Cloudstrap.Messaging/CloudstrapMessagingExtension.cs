namespace Cloudstrap.Messaging
{
    using Wolverine;

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

        public CloudstrapMessagingExtension(MessagingRegistrationState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            _state = state;
        }

        /// <inheritdoc />
        public void Configure(WolverineOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            // The consumer's escape hatch: final say over identity, transport, conventions and policies.
            _state.Configurator.Wolverine?.Invoke(options);

            // The fallback failure rule — after the consumer's rules, so theirs match first.
            RetryLadder.Apply(options, _state.Messaging.Retries);
        }
    }
}
