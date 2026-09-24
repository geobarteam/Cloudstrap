namespace Cloudstrap.Messaging.AzureBlob.Tests.Fixtures
{
    /// <summary>A fixture command carrying an arbitrarily large body; it references no messaging type.</summary>
    public sealed record LargePayloadCommand(string Content);

    /// <summary>The fixture handler: records the command it received, whole.</summary>
    public static class LargePayloadCommandHandler
    {
        /// <summary>Records the handled command.</summary>
        public static void Handle(LargePayloadCommand command, InvocationRecorder recorder)
        {
            recorder.Record(command);
        }
    }
}
