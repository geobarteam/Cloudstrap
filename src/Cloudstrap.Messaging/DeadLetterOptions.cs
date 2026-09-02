namespace Cloudstrap.Messaging
{
    /// <summary>
    /// Dead-letter settings, bound from <c>Cloudstrap:Messaging:DeadLetter</c>. With a durability provider the
    /// dead letters land in the message store's queryable dead-letter table; the queue name applies wherever a
    /// transport-level error queue materializes instead.
    /// </summary>
    public sealed class DeadLetterOptions
    {
        /// <summary>
        /// Gets or sets the name of the transport-level error queue.
        /// </summary>
        /// <value>
        /// The queue name, or <see langword="null"/> to use <c>{SystemName}-error</c>, with the system name from
        /// <c>Cloudstrap:Application</c>.
        /// </value>
        public string? QueueName
        {
            get; set;
        }
    }
}
