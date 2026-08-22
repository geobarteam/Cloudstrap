namespace Cloudstrap.Observability
{
    using Cloudstrap.Core;
    using Microsoft.Extensions.Logging;
    using Serilog;
    using Serilog.Extensions.Logging;

    /// <summary>
    /// Creates loggers for code that runs before the host — and therefore before the host's logging
    /// pipeline — exists, such as configuration loading and startup diagnostics.
    /// </summary>
    public static class CloudstrapBootstrapLogger
    {
        /// <summary>
        /// Creates a self-contained logger factory that writes through the Cloudstrap Serilog shape —
        /// console and file sinks, minimum level, per-source overrides and enrichment — driven by the
        /// supplied settings.
        /// </summary>
        /// <param name="options">The Cloudstrap settings that drive the sink and level shape.</param>
        /// <returns>A logger factory backed by a Serilog bootstrap logger.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// <para>
        /// The returned factory is independent of the host logging pipeline: the global <c>Log.Logger</c> is
        /// never set, and disposing the factory flushes and closes only its own sinks. Dispose it after the
        /// host is built — from that point the host's own logging, wired by <c>UseCloudstrapObservability</c>,
        /// takes over.
        /// </para>
        /// <para>
        /// When <c>Cloudstrap:Logging:Level</c> is <see cref="LogLevel.None"/> the factory writes nothing.
        /// </para>
        /// </remarks>
        public static ILoggerFactory Create(CloudstrapOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            LoggerConfiguration loggerConfiguration = new();
            SerilogPipeline.Configure(loggerConfiguration, options);

            return new SerilogLoggerFactory(loggerConfiguration.CreateBootstrapLogger(), dispose: true);
        }
    }
}
