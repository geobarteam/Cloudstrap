namespace Cloudstrap.Messaging
{
    /// <summary>
    /// How the message conventions classify a message type. By default the type name's suffix decides:
    /// <c>*Command</c>, <c>*Event</c> or <c>*Message</c>; anything else is <see cref="None"/>.
    /// </summary>
    public enum MessageKind
    {
        /// <summary>
        /// Not classified: the conventions route nothing for it, so it is handled locally when a handler
        /// exists and otherwise needs an explicit route.
        /// </summary>
        None = 0,

        /// <summary>
        /// A command: sent to exactly one destination workload's queue, chosen through the
        /// <c>Destinations</c> map.
        /// </summary>
        Command = 1,

        /// <summary>
        /// An event: published to a topic per event type on Azure Service Bus (every consuming workload
        /// subscribes under its own name); routed through the <c>Destinations</c> map like a command on
        /// the SQL Server transport, which has queues only.
        /// </summary>
        Event = 2,

        /// <summary>
        /// A plain message: routed like a <see cref="Command"/>.
        /// </summary>
        Message = 3,
    }
}
