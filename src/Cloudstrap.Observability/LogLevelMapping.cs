namespace Cloudstrap.Observability
{
    using Microsoft.Extensions.Logging;
    using Serilog.Events;

    /// <summary>
    /// Maps <see cref="LogLevel"/> values to their Serilog <see cref="LogEventLevel"/> equivalents.
    /// </summary>
    internal static class LogLevelMapping
    {
        /// <summary>
        /// Maps the six writable <see cref="LogLevel"/> values to their Serilog equivalents.
        /// <see cref="LogLevel.None"/> has no Serilog equivalent — it means nothing is written, which callers
        /// implement by adding no sinks — and is rejected here.
        /// </summary>
        /// <param name="level">The level to map.</param>
        /// <returns>The Serilog level.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> has no Serilog equivalent.</exception>
        public static LogEventLevel ToLogEventLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => LogEventLevel.Verbose,
            LogLevel.Debug => LogEventLevel.Debug,
            LogLevel.Information => LogEventLevel.Information,
            LogLevel.Warning => LogEventLevel.Warning,
            LogLevel.Error => LogEventLevel.Error,
            LogLevel.Critical => LogEventLevel.Fatal,
            _ => throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                $"{nameof(LogLevel.None)} writes nothing and maps to no Serilog level."),
        };
    }
}
