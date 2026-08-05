namespace Cloudstrap.Observability
{
    using System.Globalization;
    using Cloudstrap.Core;
    using Microsoft.Extensions.Logging;
    using Serilog;
    using Serilog.Filters;

    /// <summary>
    /// The one place the Cloudstrap Serilog console/file shape lives: levels, per-source overrides,
    /// enrichment and sinks, all driven by <see cref="LoggingOptions"/>. Shared by the bootstrap logger
    /// and the host logging path.
    /// </summary>
    internal static class SerilogPipeline
    {
        private const string _outputTemplate =
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext} {TraceId} {SpanId} {Message:lj}{NewLine}{Exception}";

        private const long _fileSizeLimitBytes = 10 * 1024 * 1024;
        private const int _retainedFileCountLimit = 20;
        private const string _fileNamePattern = "log-.log";

        /// <summary>
        /// Applies the Cloudstrap logging shape to the supplied configuration: minimum level and per-source
        /// overrides from <c>Cloudstrap:Logging</c>, log-context and static-property enrichment, a console
        /// sink when enabled, and a daily-rolling file sink directly under the configured folder when enabled.
        /// When the configured level is <see cref="LogLevel.None"/> no sinks are added and nothing is written.
        /// </summary>
        /// <param name="loggerConfiguration">The Serilog configuration to shape.</param>
        /// <param name="options">The Cloudstrap settings that drive the shape.</param>
        public static void Configure(LoggerConfiguration loggerConfiguration, CloudstrapOptions options)
        {
            ArgumentNullException.ThrowIfNull(loggerConfiguration);
            ArgumentNullException.ThrowIfNull(options);

            LoggingOptions logging = options.Logging;

            if (logging.Level == LogLevel.None)
            {
                return;
            }

            loggerConfiguration.MinimumLevel.Is(LogLevelMapping.ToLogEventLevel(logging.Level));

            foreach (KeyValuePair<string, LogLevel> levelOverride in logging.LevelOverrides)
            {
                if (levelOverride.Value == LogLevel.None)
                {
                    loggerConfiguration.Filter.ByExcluding(Matching.FromSource(levelOverride.Key));
                }
                else
                {
                    loggerConfiguration.MinimumLevel.Override(
                        levelOverride.Key,
                        LogLevelMapping.ToLogEventLevel(levelOverride.Value));
                }
            }

            loggerConfiguration.Enrich.FromLogContext();

            foreach (KeyValuePair<string, string> property in logging.EnrichProperties)
            {
                loggerConfiguration.Enrich.WithProperty(property.Key, property.Value);
            }

            if (logging.Console.Enabled)
            {
                loggerConfiguration.WriteTo.Console(
                    outputTemplate: _outputTemplate,
                    formatProvider: CultureInfo.InvariantCulture);
            }

            if (logging.File.Enabled)
            {
                loggerConfiguration.WriteTo.File(
                    Path.Combine(logging.File.Path!, _fileNamePattern),
                    outputTemplate: _outputTemplate,
                    formatProvider: CultureInfo.InvariantCulture,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: _fileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    retainedFileCountLimit: _retainedFileCountLimit,
                    shared: true);
            }
        }
    }
}
