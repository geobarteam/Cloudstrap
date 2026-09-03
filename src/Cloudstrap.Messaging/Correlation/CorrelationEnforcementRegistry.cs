namespace Cloudstrap.Messaging
{
    using System.Collections.Concurrent;
    using System.ComponentModel;

    /// <summary>
    /// Infrastructure: the message types whose handlers require a correlation id, filled by the enforcement
    /// policy at bootstrap and consulted by the correlation middleware per message. Public only because
    /// Wolverine's generated handler code resolves it; not intended for consumer use.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class CorrelationEnforcementRegistry
    {
        private readonly ConcurrentDictionary<Type, string> _enforced = new();

        /// <summary>
        /// Records that the handlers named handle <paramref name="messageType"/> under enforcement.
        /// </summary>
        /// <param name="messageType">The message type.</param>
        /// <param name="handlerTypeNames">The full names of the enforcing handler types, for diagnostics.</param>
        public void Require(Type messageType, string handlerTypeNames)
        {
            ArgumentNullException.ThrowIfNull(messageType);
            ArgumentException.ThrowIfNullOrWhiteSpace(handlerTypeNames);

            _enforced[messageType] = handlerTypeNames;
        }

        /// <summary>
        /// Returns whether <paramref name="messageType"/> is handled under enforcement.
        /// </summary>
        /// <param name="messageType">The message type, or <see langword="null"/> for an unknown message.</param>
        /// <param name="handlerTypeNames">The enforcing handler type names when enforced.</param>
        /// <returns><see langword="true"/> when a correlation id is required to handle the type.</returns>
        public bool IsEnforced(Type? messageType, out string handlerTypeNames)
        {
            if (messageType is not null && _enforced.TryGetValue(messageType, out string? names))
            {
                handlerTypeNames = names;
                return true;
            }

            handlerTypeNames = string.Empty;
            return false;
        }
    }
}
