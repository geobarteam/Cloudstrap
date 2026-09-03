namespace Cloudstrap.Messaging
{
    using System.Reflection;
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using JasperFx;
    using JasperFx.CodeGeneration;
    using JasperFx.CodeGeneration.Frames;
    using Wolverine.Configuration;
    using Wolverine.Runtime.Handlers;

    /// <summary>
    /// The single enforcement rule (spec finding 5): a handler chain requires a correlation id when
    /// <see cref="CorrelationMessageOptions.RequireForAllMessageHandlers"/> is on or any of its handlers —
    /// method, class or a base class — carries <see cref="CorrelationRequiredAttribute"/>, unless a handler
    /// carries <see cref="AllowNoCorrelationAttribute"/> or its full type name is listed in
    /// <see cref="CorrelationMessageOptions.ExcludeMessageHandlers"/>. Decided once at bootstrap, recorded in
    /// the <see cref="CorrelationEnforcementRegistry"/> the middleware consults per message.
    /// </summary>
    internal sealed class CorrelationEnforcementPolicy : IHandlerPolicy
    {
        private readonly CorrelationEnforcementRegistry _registry;
        private readonly CorrelationMessageOptions _options;

        public CorrelationEnforcementPolicy(CorrelationEnforcementRegistry registry, CorrelationMessageOptions options)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(options);

            _registry = registry;
            _options = options;
        }

        /// <inheritdoc />
        public void Apply(IReadOnlyList<HandlerChain> chains, GenerationRules rules, IServiceContainer container)
        {
            ArgumentNullException.ThrowIfNull(chains);

            foreach (HandlerChain chain in chains)
            {
                MethodCall[] handlers = chain.HandlerCalls();
                if (handlers.Length == 0)
                {
                    continue;
                }

                bool required = _options.RequireForAllMessageHandlers || handlers.Any(Requires);
                bool exempt = handlers.Any(handler => IsExempt(handler, _options.ExcludeMessageHandlers));

                if (required && !exempt)
                {
                    _registry.Require(chain.MessageType, string.Join(", ", handlers.Select(h => h.HandlerType.FullName)));
                }
            }
        }

        private static bool Requires(MethodCall handler)
        {
            return handler.Method.GetCustomAttribute<CorrelationRequiredAttribute>(inherit: true) is not null
                || handler.HandlerType.GetCustomAttribute<CorrelationRequiredAttribute>(inherit: true) is not null;
        }

        private static bool IsExempt(MethodCall handler, IList<string> excluded)
        {
            return handler.Method.GetCustomAttribute<AllowNoCorrelationAttribute>(inherit: true) is not null
                || handler.HandlerType.GetCustomAttribute<AllowNoCorrelationAttribute>(inherit: true) is not null
                || (handler.HandlerType.FullName is { } name && excluded.Contains(name, StringComparer.Ordinal));
        }
    }
}
