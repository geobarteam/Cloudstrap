namespace Cloudstrap.Messaging.Tests.Fixtures
{
    using Cloudstrap.Messaging.Tests.Fixtures.Contracts;
    using Wolverine;

    /// <summary>A fixture command whose handler stages an <see cref="Order"/> and cascades an event, then optionally throws.</summary>
    public sealed record StageOrderCommand(Guid OrderId, string Description, bool Fail);

    /// <summary>
    /// The transactional fixture handler: a plain Wolverine handler taking the <see cref="OrdersDbContext"/>
    /// and the bus — the transactional integration wraps entity write and outgoing message in one transaction.
    /// </summary>
    public static class StageOrderCommandHandler
    {
        /// <summary>Stages the order, publishes the event, and fails when asked to.</summary>
        public static async Task Handle(StageOrderCommand command, OrdersDbContext db, IMessageBus bus)
        {
            db.Orders.Add(new Order { Id = command.OrderId, Description = command.Description });
            await bus.PublishAsync(new OrderPlacedEvent(command.OrderId));

            if (command.Fail)
            {
                throw new InvalidOperationException("The handler fails after staging the entity and the message.");
            }
        }
    }

    /// <summary>Records every <see cref="OrderPlacedEvent"/> that reaches this node.</summary>
    public static class OrderPlacedHandler
    {
        /// <summary>Records the handled event.</summary>
        public static void Handle(OrderPlacedEvent placed, InvocationRecorder recorder)
        {
            recorder.Record(placed);
        }
    }
}
