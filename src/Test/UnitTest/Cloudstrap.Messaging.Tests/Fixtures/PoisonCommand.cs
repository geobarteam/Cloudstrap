namespace Cloudstrap.Messaging.Tests.Fixtures
{
    using Cloudstrap.Messaging.Tests.Fixtures.Contracts;

    /// <summary>A fixture command whose handler always fails; the payload carries a sentinel that must never be logged.</summary>
    public sealed record PoisonCommand(string Payload);

    /// <summary>The always-failing fixture handler. Its exception message deliberately omits the payload.</summary>
    public static class PoisonCommandHandler
    {
        /// <summary>Fails unconditionally.</summary>
        public static void Handle(PoisonCommand command)
        {
            throw new InvalidOperationException("This handler fails on purpose.");
        }
    }

    /// <summary>The fixture handler for the dependency-free <see cref="PlaceOrderCommand"/> contract.</summary>
    public static class PlaceOrderCommandHandler
    {
        /// <summary>Records the handled command.</summary>
        public static void Handle(PlaceOrderCommand command, InvocationRecorder recorder)
        {
            recorder.Record(command);
        }
    }
}
