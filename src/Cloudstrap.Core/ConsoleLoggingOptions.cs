namespace Cloudstrap.Core
{
    /// <summary>
    /// Console logging sink settings, bound from the <c>Cloudstrap:Logging:Console</c> configuration section.
    /// </summary>
    public sealed class ConsoleLoggingOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether log events are written to the console.
        /// </summary>
        /// <value><see langword="true"/> when console logging is enabled. Defaults to <see langword="true"/>.</value>
        public bool Enabled { get; set; } = true;
    }
}
