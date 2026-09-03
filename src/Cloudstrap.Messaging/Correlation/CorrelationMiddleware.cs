namespace Cloudstrap.Messaging
{
    using System.ComponentModel;
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Wolverine;

    /// <summary>
    /// Infrastructure: the receive-side correlation middleware Wolverine applies to every handler. It reads the
    /// configured header (<c>Cloudstrap:Correlation:HeaderName</c>) from the incoming envelope into the ambient
    /// <see cref="ICorrelationContextAccessor"/> — a fresh (null) scope when the header is absent — and blocks
    /// handling with a <see cref="CorrelationRequiredException"/> when the handler requires a correlation id
    /// and none arrived. Public only because Wolverine's generated code calls it; not intended for consumer use.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static partial class CorrelationMiddleware
    {
        /// <summary>
        /// Runs before the handler: populates the accessor and enforces the requirement.
        /// </summary>
        /// <param name="envelope">The incoming envelope.</param>
        /// <param name="accessor">The ambient correlation accessor.</param>
        /// <param name="options">The correlation options carrying the header name.</param>
        /// <param name="registry">The enforcement registry.</param>
        /// <param name="logger">The logger.</param>
        /// <exception cref="CorrelationRequiredException">The handler requires a correlation id and none arrived.</exception>
        public static void Before(
            Envelope envelope,
            ICorrelationContextAccessor accessor,
            IOptions<CorrelationOptions> options,
            CorrelationEnforcementRegistry registry,
            ILogger<CorrelationEnforcementRegistry> logger)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            ArgumentNullException.ThrowIfNull(accessor);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(logger);

            string header = options.Value.HeaderName;
            string? correlationId = envelope.Headers.TryGetValue(header, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;

            accessor.CorrelationId = correlationId;

            if (correlationId is null && registry.IsEnforced(envelope.Message?.GetType(), out string handlers))
            {
                string messageType = envelope.Message?.GetType().FullName ?? envelope.MessageType ?? "(unknown)";
                LogBlocked(logger, messageType, handlers, header);
                throw new CorrelationRequiredException(
                    $"Handling of message '{messageType}' by '{handlers}' was blocked: the message carries no " +
                    $"'{header}' correlation header and the handler requires one.");
            }
        }

        /// <summary>
        /// Runs after the handler, success or failure: clears the ambient correlation scope.
        /// </summary>
        /// <param name="accessor">The ambient correlation accessor.</param>
        public static void Finally(ICorrelationContextAccessor accessor)
        {
            ArgumentNullException.ThrowIfNull(accessor);

            accessor.CorrelationId = null;
        }

        [LoggerMessage(
            EventId = 2,
            Level = LogLevel.Error,
            Message = "Handling of message '{MessageType}' by '{HandlerType}' was blocked: the message carries no '{Header}' " +
                      "correlation header and the handler requires one")]
        private static partial void LogBlocked(ILogger logger, string messageType, string handlerType, string header);
    }
}
