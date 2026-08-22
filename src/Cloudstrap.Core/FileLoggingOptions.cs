namespace Cloudstrap.Core
{
    /// <summary>
    /// File logging sink settings, bound from the <c>Cloudstrap:Logging:File</c> configuration section.
    /// </summary>
    public sealed class FileLoggingOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether log events are written to the file system.
        /// </summary>
        /// <value><see langword="true"/> when file logging is enabled. Defaults to <see langword="false"/>.</value>
        public bool Enabled
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the folder log files are written to. There is no default: the path is required
        /// whenever <see cref="Enabled"/> is <see langword="true"/>, and validation fails without it.
        /// </summary>
        /// <value>The log folder path, or <see langword="null"/> when file logging is disabled.</value>
        public string? Path
        {
            get; set;
        }
    }
}
