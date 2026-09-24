namespace Cloudstrap.Messaging.AzureBlob.Tests.Fixtures
{
    using System.Collections.Concurrent;
    using Cloudstrap.Messaging.AzureBlob.Tests.Fixtures.Contracts;
    using Cloudstrap.Observability.Correlation;
    using Wolverine;

    /// <summary>What a handler observed: the rehydrated message, the envelope headers and the correlation id.</summary>
    public sealed record ExportObservation(object Message, IReadOnlyDictionary<string, string?> Headers, string? CorrelationId);

    /// <summary>Counts handler attempts per message id, so a test can assert on the retry ladder.</summary>
    public sealed class AttemptCounter
    {
        private readonly ConcurrentDictionary<Guid, int> _attempts = new();

        /// <summary>Records one more attempt for the id and returns the attempt number (1-based).</summary>
        public int Increment(Guid id)
        {
            return _attempts.AddOrUpdate(id, 1, (_, current) => current + 1);
        }

        /// <summary>Gets the attempts recorded for the id.</summary>
        public int AttemptsFor(Guid id)
        {
            return _attempts.TryGetValue(id, out int attempts) ? attempts : 0;
        }
    }

    /// <summary>The fixture handler for <see cref="ExportReadyCommand"/>: a plain Wolverine handler that knows nothing of blobs.</summary>
    public static class ExportReadyCommandHandler
    {
        /// <summary>Records the rehydrated command, its headers and the correlation it arrived under.</summary>
        public static void Handle(ExportReadyCommand command, Envelope envelope, ICorrelationContextAccessor accessor, InvocationRecorder recorder)
        {
            recorder.Record(ExportObservations.Observe(command, envelope, accessor));
        }
    }

    /// <summary>The fixture handler for <see cref="SmallCommand"/>.</summary>
    public static class SmallCommandHandler
    {
        /// <summary>Records the small command, its headers and the correlation it arrived under.</summary>
        public static void Handle(SmallCommand command, Envelope envelope, ICorrelationContextAccessor accessor, InvocationRecorder recorder)
        {
            recorder.Record(ExportObservations.Observe(command, envelope, accessor));
        }
    }

    /// <summary>The transiently failing fixture handler for <see cref="FlakyExportCommand"/>.</summary>
    public static class FlakyExportCommandHandler
    {
        /// <summary>Fails the first N attempts, then records the side effect exactly once.</summary>
        public static void Handle(FlakyExportCommand command, Envelope envelope, ICorrelationContextAccessor accessor, AttemptCounter attempts, InvocationRecorder recorder)
        {
            int attempt = attempts.Increment(command.Id);
            if (attempt <= command.FailuresBeforeSuccess)
            {
                throw new InvalidOperationException($"Attempt {attempt} of {command.Id} fails on purpose.");
            }

            recorder.Record(ExportObservations.Observe(command, envelope, accessor));
        }
    }

    /// <summary>Builds the observation a handler records.</summary>
    public static class ExportObservations
    {
        /// <summary>Captures the message, the envelope headers and the accessor's correlation id.</summary>
        public static ExportObservation Observe(object message, Envelope envelope, ICorrelationContextAccessor accessor)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            ArgumentNullException.ThrowIfNull(accessor);

            return new ExportObservation(
                message,
                envelope.Headers.ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase),
                accessor.CorrelationId);
        }
    }
}
