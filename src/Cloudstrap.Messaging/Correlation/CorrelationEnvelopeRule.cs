namespace Cloudstrap.Messaging
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Options;
    using Wolverine;

    /// <summary>
    /// The send-side half of correlation: stamps the configured header on every outgoing envelope from the
    /// ambient <see cref="ICorrelationContextAccessor"/> — whether sent from an HTTP request, a background
    /// job or inside a handler — and, while every handler requires a correlation id, blocks a send that has
    /// none with a <see cref="CorrelationRequiredException"/> (the kept outgoing validation point).
    /// </summary>
    internal sealed class CorrelationEnvelopeRule : IEnvelopeRule
    {
        private readonly ICorrelationContextAccessor _accessor;
        private readonly IOptions<CorrelationOptions> _options;

        public CorrelationEnvelopeRule(ICorrelationContextAccessor accessor, IOptions<CorrelationOptions> options)
        {
            ArgumentNullException.ThrowIfNull(accessor);
            ArgumentNullException.ThrowIfNull(options);

            _accessor = accessor;
            _options = options;
        }

        /// <inheritdoc />
        public void Modify(Envelope envelope)
        {
            Stamp(envelope);
        }

        /// <inheritdoc />
        public void ApplyCorrelation(IMessageContext originator, Envelope outgoing)
        {
            Stamp(outgoing);
        }

        private void Stamp(Envelope envelope)
        {
            ArgumentNullException.ThrowIfNull(envelope);

            CorrelationOptions options = _options.Value;
            string? correlationId = _accessor.CorrelationId;

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                envelope.Headers[options.HeaderName] = correlationId;
                return;
            }

            if (options.Message.RequireForAllMessageHandlers)
            {
                throw new CorrelationRequiredException(
                    $"Sending message '{envelope.Message?.GetType().FullName ?? envelope.MessageType}' was blocked: no " +
                    $"correlation id is set for the '{options.HeaderName}' header and every message handler requires one.");
            }
        }
    }
}
