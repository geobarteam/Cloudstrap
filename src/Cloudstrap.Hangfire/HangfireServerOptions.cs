namespace Cloudstrap.Hangfire
{
    /// <summary>
    /// Processing server settings, bound from <c>Cloudstrap:Hangfire:Server</c> and applied on a processing host
    /// only. Unset values keep the Hangfire defaults.
    /// </summary>
    public sealed class HangfireServerOptions
    {
        /// <summary>
        /// Gets or sets the number of worker threads processing jobs.
        /// </summary>
        /// <value>The worker count (at least 1), or <see langword="null"/> for the Hangfire default.</value>
        public int? WorkerCount
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the queues the server dequeues from, in priority order.
        /// </summary>
        /// <value>The queue names (no empty entries), or <see langword="null"/> for the <c>default</c> queue only.</value>
#pragma warning disable CA1819 // A configuration-bound array: the binder's native shape for an ordered list.
        public string[]? Queues
        {
            get; set;
        }
#pragma warning restore CA1819
    }
}
