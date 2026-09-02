namespace Cloudstrap.Messaging.Tests.Fixtures
{
    /// <summary>A fixture command — a plain record, no Wolverine or Cloudstrap dependency.</summary>
    public sealed record PingCommand(string Text);

    /// <summary>The fixture handler for <see cref="PingCommand"/>: a plain Wolverine handler, discovered by convention.</summary>
    public static class PingCommandHandler
    {
        /// <summary>Records the handled command.</summary>
        public static void Handle(PingCommand command, InvocationRecorder recorder)
        {
            recorder.Record(command);
        }
    }
}
