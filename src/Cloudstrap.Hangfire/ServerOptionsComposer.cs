namespace Cloudstrap.Hangfire
{
    using global::Hangfire;

    /// <summary>
    /// Applies the <c>Cloudstrap:Hangfire:Server</c> values over the Hangfire server defaults, then the consumer's
    /// hatch, which runs last.
    /// </summary>
    internal static class ServerOptionsComposer
    {
        /// <summary>Applies the configured values and the hatch to the server options.</summary>
        /// <param name="server">The server options to adjust.</param>
        /// <param name="configured">The <c>Cloudstrap:Hangfire:Server</c> values.</param>
        /// <param name="hatch">The consumer's <see cref="CloudstrapHangfireConfigurator.Server"/> hatch, if any.</param>
        public static void Apply(
            BackgroundJobServerOptions server,
            HangfireServerOptions configured,
            Action<BackgroundJobServerOptions>? hatch)
        {
            if (configured.WorkerCount is { } workerCount)
            {
                server.WorkerCount = workerCount;
            }

            if (configured.Queues is { Length: > 0 } queues)
            {
                server.Queues = [.. queues];
            }

            hatch?.Invoke(server);
        }
    }
}
