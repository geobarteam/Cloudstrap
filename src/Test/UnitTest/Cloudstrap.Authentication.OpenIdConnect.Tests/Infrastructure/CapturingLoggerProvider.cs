namespace Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure
{
    using System.Collections.Concurrent;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Captures every log entry the host writes, so log assertions can count exact occurrences and
    /// prove the absence of secrets.
    /// </summary>
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = [];

        /// <summary>
        /// Gets the captured entries, in order.
        /// </summary>
        public IReadOnlyCollection<(LogLevel Level, string Message)> Entries => _entries;

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly ConcurrentQueue<(LogLevel, string)> _entries;

            public CapturingLogger(ConcurrentQueue<(LogLevel, string)> entries)
            {
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);
                _entries.Enqueue((logLevel, formatter(state, exception)));
            }
        }
    }
}
