namespace Cloudstrap.Demo.Worker
{
    using Cloudstrap.Demo.Contracts;
    using Cloudstrap.Demo.Worker.Data;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Logging;
    using Wolverine;

    /// <summary>
    /// The consumer side of the messaging demo (deliverable #14): a plain Wolverine handler — no Cloudstrap
    /// base class, no marker interface — discovered by convention in this host's entry assembly. Taking
    /// <see cref="WorkerDbContext"/> makes it transactional: the row update and the message's inbox record
    /// commit together, and a failure commits neither (AC-MSG7 live).
    /// </summary>
    public sealed partial class PlaceOrderCommandHandler(ILogger<PlaceOrderCommandHandler> logger)
    {
        /// <summary>
        /// Marks the order processed and records the correlation id that flowed from the Api's HTTP request
        /// through the envelope into this host's accessor (AC-MSG9 live). Logs the message <b>type and id,
        /// never the payload</b> (AC-MSG6 posture).
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="envelope">The incoming envelope, for the message id.</param>
        /// <param name="db">The transactional context.</param>
        /// <param name="correlation">The ambient correlation populated by the messaging middleware.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public async Task Handle(
            PlaceOrderCommand command,
            Envelope envelope,
            WorkerDbContext db,
            ICorrelationContextAccessor correlation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            ArgumentNullException.ThrowIfNull(envelope);
            ArgumentNullException.ThrowIfNull(db);
            ArgumentNullException.ThrowIfNull(correlation);

            LogHandling(envelope.MessageType ?? nameof(PlaceOrderCommand), envelope.Id);

            Order? order = await db.Orders.FindAsync([command.OrderId], cancellationToken);
            if (order is null)
            {
                LogUnknownOrder(envelope.Id);
                return;
            }

            order.Status = "Processed";
            order.ProcessedCorrelationId = correlation.CorrelationId;
        }

        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Handling {MessageType} {MessageId}")]
        private partial void LogHandling(string messageType, Guid messageId);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Message {MessageId} names an order this host does not know; nothing to do")]
        private partial void LogUnknownOrder(Guid messageId);
    }
}
