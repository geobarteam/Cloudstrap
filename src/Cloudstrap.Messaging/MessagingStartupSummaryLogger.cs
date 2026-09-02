namespace Cloudstrap.Messaging
{
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Wolverine;

    /// <summary>
    /// Writes the one startup log line stating the messaging posture in force: transport, endpoint identity,
    /// routing conventions and every <c>Destinations</c> entry, durability and dead-letter posture, and the
    /// effective auto-provisioning value. Values that could be secrets — connection strings, namespaces —
    /// never appear; keys and names only.
    /// </summary>
    internal sealed partial class MessagingStartupSummaryLogger : IHostedService
    {
        private readonly ILogger<MessagingStartupSummaryLogger> _logger;
        private readonly MessagingRegistrationState _state;
        private readonly WolverineOptions _wolverine;

        public MessagingStartupSummaryLogger(
            ILogger<MessagingStartupSummaryLogger> logger,
            MessagingRegistrationState state,
            WolverineOptions wolverine)
        {
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(state);
            ArgumentNullException.ThrowIfNull(wolverine);

            _logger = logger;
            _state = state;
            _wolverine = wolverine;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!_logger.IsEnabled(LogLevel.Information))
            {
                return Task.CompletedTask;
            }

            CloudstrapMessagingOptions messaging = _state.Messaging;
            string destinations = messaging.Destinations.Count == 0
                ? "(none)"
                : string.Join(", ", messaging.Destinations.Select(entry => $"{entry.Key} -> {entry.Value}"));
            string durability = _state.MessageStore ?? "none (buffered, non-durable)";
            string deadLetter = _state.MessageStore is null
                ? DescribeDeadLetter(messaging.Transport, _state.DeadLetterQueueName)
                : "message store dead-letter table";
            LogSummary(_wolverine.ServiceName, messaging.Transport, destinations, durability, deadLetter, _state.AutoProvision);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private static string DescribeDeadLetter(MessagingTransport transport, string errorQueueName)
        {
            return transport == MessagingTransport.Local
                ? "in-process"
                : $"transport error queue '{errorQueueName}'";
        }

        [LoggerMessage(
            EventId = 1,
            Level = LogLevel.Information,
            Message = "Cloudstrap messaging node '{EndpointName}' on transport {Transport}: routing by suffix conventions " +
                      "(*Command/*Event/*Message), destinations [{Destinations}]; durability: {Durability}; " +
                      "dead-letter: {DeadLetter}; auto-provision: {AutoProvision}")]
        private partial void LogSummary(
            string endpointName,
            MessagingTransport transport,
            string destinations,
            string durability,
            string deadLetter,
            bool autoProvision);
    }
}
