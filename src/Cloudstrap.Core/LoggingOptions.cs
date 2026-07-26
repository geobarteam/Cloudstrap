namespace Cloudstrap.Core
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Logging settings, bound from the <c>Cloudstrap:Logging</c> configuration section.
    /// </summary>
    public sealed class LoggingOptions
    {
        /// <summary>
        /// The configuration section these options are bound from.
        /// </summary>
        public const string SectionName = "Cloudstrap:Logging";

        /// <summary>
        /// Gets or sets the minimum level events must reach to be written.
        /// </summary>
        /// <value>The minimum log level. Defaults to <see cref="LogLevel.Information"/>.</value>
        public LogLevel Level { get; set; } = LogLevel.Information;

        /// <summary>
        /// Gets the per-source minimum level overrides, keyed by source-context prefix
        /// (for example <c>Microsoft.AspNetCore</c>).
        /// </summary>
        /// <value>The source-context prefix to minimum level map. Empty when nothing is overridden.</value>
        public Dictionary<string, LogLevel> LevelOverrides { get; } = [];

        /// <summary>
        /// Gets the static properties attached to every log event, keyed by property name.
        /// </summary>
        /// <value>The enrichment property map. Empty when no enrichment is configured.</value>
        public Dictionary<string, string> EnrichProperties { get; } = [];

        /// <summary>
        /// Gets or sets the console sink settings.
        /// </summary>
        /// <value>The console sink settings. Never <see langword="null"/>.</value>
        public ConsoleLoggingOptions Console { get; set; } = new();

        /// <summary>
        /// Gets or sets the file sink settings.
        /// </summary>
        /// <value>The file sink settings. Never <see langword="null"/>.</value>
        public FileLoggingOptions File { get; set; } = new();
    }
}
