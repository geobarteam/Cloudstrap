namespace Cloudstrap.Mvc.Tests.Infrastructure
{
    using Microsoft.Extensions.Logging;

    /// <summary>One captured log entry.</summary>
    /// <param name="Category">The logger category.</param>
    /// <param name="Level">The level the entry was written at.</param>
    /// <param name="Message">The formatted message.</param>
    /// <param name="Exception">The exception attached to the entry, if any.</param>
    internal sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    /// <summary>
    /// A logger provider recording everything written through it, so a test can assert that an exception was
    /// logged server-side exactly once.
    /// </summary>
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<CapturedLogEntry> _entries = [];
        private readonly Lock _gate = new();

        /// <summary>
        /// Gets a snapshot of everything captured so far.
        /// </summary>
        public IReadOnlyList<CapturedLogEntry> Entries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _entries];
                }
            }
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(categoryName, this);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        private void Add(CapturedLogEntry entry)
        {
            lock (_gate)
            {
                _entries.Add(entry);
            }
        }

        private sealed class CapturingLogger(string category, CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                provider.Add(new CapturedLogEntry(category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}
