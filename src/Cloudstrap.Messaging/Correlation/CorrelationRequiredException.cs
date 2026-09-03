namespace Cloudstrap.Messaging
{
    /// <summary>
    /// Thrown when correlation enforcement blocks a message: a handler that requires a correlation id received
    /// a message without the configured header, or a message was sent without an ambient correlation id while
    /// every handler requires one. The message names the header and, on the receiving side, the handler —
    /// never the payload. Failed messages are dead-lettered without retries.
    /// </summary>
    public sealed class CorrelationRequiredException : InvalidOperationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CorrelationRequiredException"/> class.
        /// </summary>
        public CorrelationRequiredException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrelationRequiredException"/> class with a message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public CorrelationRequiredException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CorrelationRequiredException"/> class with a message
        /// and an inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public CorrelationRequiredException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
