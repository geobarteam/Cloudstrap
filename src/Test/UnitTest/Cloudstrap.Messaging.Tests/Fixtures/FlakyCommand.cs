namespace Cloudstrap.Messaging.Tests.Fixtures
{
    using System.Collections.Concurrent;

    /// <summary>A fixture command whose handler fails the first <paramref name="FailuresBeforeSuccess"/> attempts.</summary>
    public sealed record FlakyCommand(string Id, int FailuresBeforeSuccess);

    /// <summary>Counts handler attempts per message id, so a test can assert on the retry ladder.</summary>
    public sealed class AttemptCounter
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);

        /// <summary>Records one more attempt for the id and returns the attempt number (1-based).</summary>
        public int Increment(string id)
        {
            return _attempts.AddOrUpdate(id, 1, (_, current) => current + 1);
        }

        /// <summary>Gets the attempts recorded for the id.</summary>
        public int AttemptsFor(string id)
        {
            return _attempts.TryGetValue(id, out int attempts) ? attempts : 0;
        }
    }

    /// <summary>The transiently failing fixture handler: throws until the configured attempt, then records the side effect.</summary>
    public static class FlakyCommandHandler
    {
        /// <summary>Handles the command, failing the first N attempts.</summary>
        public static void Handle(FlakyCommand command, AttemptCounter attempts, InvocationRecorder recorder)
        {
            int attempt = attempts.Increment(command.Id);
            if (attempt <= command.FailuresBeforeSuccess)
            {
                throw new InvalidOperationException($"Attempt {attempt} of {command.Id} fails on purpose.");
            }

            recorder.Record(command);
        }
    }
}
