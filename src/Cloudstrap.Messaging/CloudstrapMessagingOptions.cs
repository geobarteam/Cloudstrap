namespace Cloudstrap.Messaging
{
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Settings of the Cloudstrap messaging node, bound from the <c>Cloudstrap:Messaging</c> configuration
    /// section. Every value has a working default: a host with no section at all runs an in-process node.
    /// </summary>
    /// <remarks>
    /// No setting in this graph carries a secret. Connection strings are resolved by <em>name</em> through the
    /// standard <c>ConnectionStrings:</c> section and Azure Service Bus authenticates with
    /// <c>DefaultAzureCredential</c>; validation failures and log lines name configuration keys, never values.
    /// </remarks>
    public sealed class CloudstrapMessagingOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Messaging";

        /// <summary>
        /// Gets or sets the transport the node moves messages over.
        /// </summary>
        /// <value>The transport. Defaults to <see cref="MessagingTransport.Local"/>.</value>
        public MessagingTransport Transport { get; set; } = MessagingTransport.Local;

        /// <summary>
        /// Gets or sets the node's endpoint identity: the name of its inbox queue and of the subscriptions it
        /// creates on event topics.
        /// </summary>
        /// <value>
        /// The endpoint name, or <see langword="null"/> to use the workload name computed by
        /// <c>Cloudstrap:Application</c> (<c>{system}-{subsystem}-{type}</c>).
        /// </value>
        public string? EndpointName
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the node creates its own queues, topics, subscriptions
        /// and durability tables at startup.
        /// </summary>
        /// <value>
        /// <see langword="true"/> to provision, <see langword="false"/> to expect the resources to exist, or
        /// <see langword="null"/> to provision only in the <c>Development</c> environment. An explicit value
        /// always wins; the effective value is stated in the startup summary log line.
        /// </value>
        public bool? AutoProvision
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the Azure Service Bus settings, used when <see cref="Transport"/> is
        /// <see cref="MessagingTransport.AzureServiceBus"/>.
        /// </summary>
        /// <value>The Azure Service Bus settings. Never <see langword="null"/>.</value>
        public AzureServiceBusOptions AzureServiceBus { get; set; } = new();

        /// <summary>
        /// Gets or sets the SQL Server transport settings, used when <see cref="Transport"/> is
        /// <see cref="MessagingTransport.SqlServer"/>.
        /// </summary>
        /// <value>The SQL Server transport settings. Never <see langword="null"/>.</value>
        public SqlTransportOptions SqlTransport { get; set; } = new();

        /// <summary>
        /// Gets or sets the durable inbox/outbox settings, used once a durability provider is chosen on the
        /// <see cref="CloudstrapMessagingBuilder"/>.
        /// </summary>
        /// <value>The durability settings. Never <see langword="null"/>.</value>
        public DurabilityOptions Durability { get; set; } = new();

        /// <summary>
        /// Gets or sets the retry ladder applied to failing handlers before a message is dead-lettered.
        /// </summary>
        /// <value>The retry settings. Never <see langword="null"/>.</value>
        [ValidateObjectMembers]
        public RetryOptions Retries { get; set; } = new();

        /// <summary>
        /// Gets or sets the dead-letter settings.
        /// </summary>
        /// <value>The dead-letter settings. Never <see langword="null"/>.</value>
        public DeadLetterOptions DeadLetter { get; set; } = new();

        /// <summary>
        /// Gets the command routing map: each key is a message namespace or type-name prefix, each value the
        /// endpoint (workload) name whose queue commands matching that prefix are sent to. Events are never
        /// routed through this map — they publish to a topic per event type.
        /// </summary>
        /// <value>The routing map. Empty by default: commands without a destination are handled locally.</value>
        /// <remarks>
        /// The configuration binder <em>adds</em> to this dictionary: entries set in code and entries read from
        /// configuration merge, and a key present in both takes the configuration value. Configuration cannot
        /// remove an entry added in code.
        /// </remarks>
        public IDictionary<string, string> Destinations { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
