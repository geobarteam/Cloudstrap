namespace Cloudstrap.Messaging
{
    /// <summary>
    /// The rules that classify and route message types by convention, so contract assemblies need no
    /// package reference at all. Each rule is a replaceable delegate: assign a new one to replace it, or wrap
    /// the current value to extend it. Supplied to the consumer through
    /// <see cref="CloudstrapMessagingConfigurator.Conventions"/> and resolvable from the container.
    /// </summary>
    public sealed class MessageConventions
    {
        private const string _commandSuffix = "Command";
        private const string _eventSuffix = "Event";
        private const string _messageSuffix = "Message";

        private MessageConventions(
            Func<Type, MessageKind> classify,
            Func<Type, string?> destinationFor,
            Func<Type, string> topicNameFor)
        {
            Classify = classify;
            DestinationFor = destinationFor;
            TopicNameFor = topicNameFor;
        }

        /// <summary>
        /// Gets or sets the rule that classifies a message type.
        /// </summary>
        /// <value>
        /// The classification rule. Defaults to the type-name suffix: <c>*Command</c>, <c>*Event</c>,
        /// <c>*Message</c>; anything else is <see cref="MessageKind.None"/>.
        /// </value>
        public Func<Type, MessageKind> Classify
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the rule that picks the destination endpoint (workload) name whose queue a
        /// command-like message type is sent to.
        /// </summary>
        /// <value>
        /// The destination rule, returning <see langword="null"/> when the type has no destination. Defaults
        /// to the <c>Cloudstrap:Messaging:Destinations</c> map: the entry whose key is the longest prefix of the
        /// type's full name wins.
        /// </value>
        public Func<Type, string?> DestinationFor
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the rule that names the topic an event type is published to on Azure Service Bus.
        /// </summary>
        /// <value>The topic-name rule. Defaults to the event type's full name.</value>
        public Func<Type, string> TopicNameFor
        {
            get; set;
        }

        /// <summary>
        /// Creates the default conventions over the supplied options.
        /// </summary>
        /// <param name="options">The bound messaging options, whose <c>Destinations</c> map backs the default destination rule.</param>
        /// <returns>The default conventions.</returns>
        internal static MessageConventions CreateDefault(CloudstrapMessagingOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            IDictionary<string, string> destinations = options.Destinations;

            return new MessageConventions(
                ClassifyBySuffix,
                type => LongestPrefixMatch(destinations, type),
                type => type.FullName ?? type.Name);
        }

        private static MessageKind ClassifyBySuffix(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            string name = type.Name;
            if (name.EndsWith(_commandSuffix, StringComparison.Ordinal))
            {
                return MessageKind.Command;
            }

            if (name.EndsWith(_eventSuffix, StringComparison.Ordinal))
            {
                return MessageKind.Event;
            }

            return name.EndsWith(_messageSuffix, StringComparison.Ordinal)
                ? MessageKind.Message
                : MessageKind.None;
        }

        private static string? LongestPrefixMatch(IDictionary<string, string> destinations, Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            string fullName = type.FullName ?? type.Name;
            string? destination = null;
            int longest = -1;

            foreach (KeyValuePair<string, string> entry in destinations)
            {
                if (entry.Key.Length > longest && fullName.StartsWith(entry.Key, StringComparison.Ordinal))
                {
                    longest = entry.Key.Length;
                    destination = entry.Value;
                }
            }

            return destination;
        }
    }
}
