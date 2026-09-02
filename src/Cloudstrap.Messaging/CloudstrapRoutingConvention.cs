namespace Cloudstrap.Messaging
{
    using Wolverine.AzureServiceBus;
    using Wolverine.AzureServiceBus.Internal;
    using Wolverine.Configuration;
    using Wolverine.Runtime;
    using Wolverine.Runtime.Routing;
    using Wolverine.SqlServer.Transport;

    /// <summary>
    /// The workload-centric routing convention (spec D-1) over the <see cref="MessageConventions"/>: on a
    /// broker transport, command-like types go to the destination workload's queue; on Azure Service Bus,
    /// events go to a topic per event type and this node subscribes to the topics of the events it handles
    /// under its own endpoint name. On the local transport nothing is routed — everything stays in process.
    /// Explicit routes set through the consumer's <c>Wolverine</c> delegate always win.
    /// </summary>
    internal sealed class CloudstrapRoutingConvention : IMessageRoutingConvention
    {
        private readonly MessageConventions _conventions;
        private readonly MessagingTransport _transport;
        private readonly string _endpointName;

        public CloudstrapRoutingConvention(MessageConventions conventions, MessagingTransport transport, string endpointName)
        {
            ArgumentNullException.ThrowIfNull(conventions);
            ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);

            _conventions = conventions;
            _transport = transport;
            _endpointName = endpointName;
        }

        /// <inheritdoc />
        public void DiscoverListeners(IWolverineRuntime runtime, IReadOnlyList<Type> handledMessageTypes)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            ArgumentNullException.ThrowIfNull(handledMessageTypes);

            // The inbox queue is configured with the transport; only Azure Service Bus adds per-event
            // subscriptions: one on each handled event's topic, named after this workload.
            if (_transport != MessagingTransport.AzureServiceBus)
            {
                return;
            }

            AzureServiceBusTransport? transport = runtime.Options.Transports.OfType<AzureServiceBusTransport>().FirstOrDefault();
            if (transport is null)
            {
                return;
            }

            foreach (Type messageType in handledMessageTypes)
            {
                if (_conventions.Classify(messageType) != MessageKind.Event)
                {
                    continue;
                }

                AzureServiceBusTopic topic = transport.Topics[transport.MaybeCorrectName(_conventions.TopicNameFor(messageType))];
                AzureServiceBusSubscription subscription = topic.FindOrCreateSubscription(transport.MaybeCorrectName(_endpointName));
                subscription.IsListener = true;
            }
        }

        /// <inheritdoc />
        public IEnumerable<Endpoint> DiscoverSenders(Type messageType, IWolverineRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(messageType);
            ArgumentNullException.ThrowIfNull(runtime);

            if (_transport == MessagingTransport.Local)
            {
                yield break;
            }

            MessageKind kind = _conventions.Classify(messageType);
            if (kind == MessageKind.None)
            {
                yield break;
            }

            if (_transport == MessagingTransport.AzureServiceBus)
            {
                AzureServiceBusTransport? transport = runtime.Options.Transports.OfType<AzureServiceBusTransport>().FirstOrDefault();
                if (transport is null)
                {
                    yield break;
                }

                if (kind == MessageKind.Event)
                {
                    yield return transport.Topics[transport.MaybeCorrectName(_conventions.TopicNameFor(messageType))];
                    yield break;
                }

                string? queue = _conventions.DestinationFor(messageType);
                if (queue is not null)
                {
                    yield return transport.Queues[transport.MaybeCorrectName(queue)];
                }

                yield break;
            }

            // SQL Server has queues only: commands and events alike go through the Destinations map.
            string? destination = _conventions.DestinationFor(messageType);
            if (destination is null)
            {
                yield break;
            }

            SqlServerTransport? sqlTransport = runtime.Options.Transports.OfType<SqlServerTransport>().FirstOrDefault();
            if (sqlTransport is not null)
            {
                yield return sqlTransport.Queues[sqlTransport.MaybeCorrectName(destination)];
            }
        }

        /// <inheritdoc />
        public void PreregisterSenders(IReadOnlyList<Type> handledMessageTypes, IWolverineRuntime runtime)
        {
            // Senders are discovered lazily per message type; nothing to pre-register.
        }

        /// <inheritdoc />
        public RoutingConventionDescriptor Describe(IWolverineRuntime runtime)
        {
            return new RoutingConventionDescriptor
            {
                Name = "Cloudstrap workload routing",
                Description = "Suffix classification (*Command/*Event/*Message); command-like types are sent to the " +
                              "destination workload's queue through the Cloudstrap:Messaging:Destinations map; on Azure " +
                              "Service Bus events publish to a topic per event type with a subscription per consuming workload.",
                TransportName = _transport.ToString(),
            };
        }
    }
}
