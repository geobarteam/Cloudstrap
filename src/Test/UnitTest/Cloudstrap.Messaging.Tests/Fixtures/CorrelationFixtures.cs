namespace Cloudstrap.Messaging.Tests.Fixtures
{
    using Cloudstrap.Observability.Correlation;
    using Wolverine;

    /// <summary>What a handler observed about correlation: the accessor's value and the envelope's headers.</summary>
    public sealed record CorrelationObservation(string? AccessorValue, IReadOnlyDictionary<string, string?> Headers);

    /// <summary>A fixture command whose handler reports the correlation it observed.</summary>
    public sealed record CorrelatedCommand(string Text);

    /// <summary>Records the accessor value and the envelope headers seen while handling.</summary>
    public static class CorrelatedCommandHandler
    {
        /// <summary>Records the correlation observation.</summary>
        public static void Handle(
            CorrelatedCommand command,
            Envelope envelope,
            ICorrelationContextAccessor accessor,
            InvocationRecorder recorder)
        {
            recorder.Record(new CorrelationObservation(
                accessor.CorrelationId,
                envelope.Headers.ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase)));
        }
    }

    /// <summary>A fixture command handled by a plain handler — enforced only under RequireForAllMessageHandlers.</summary>
    public sealed record EnforcedCommand(string Text);

    /// <summary>The plain fixture handler.</summary>
    public static class EnforcedCommandHandler
    {
        /// <summary>Records the handled command.</summary>
        public static void Handle(EnforcedCommand command, InvocationRecorder recorder)
        {
            recorder.Record(command);
        }
    }

    /// <summary>A fixture command whose handler hierarchy declares the requirement on a base class.</summary>
    public sealed record RequiredCommand(string Text);

    /// <summary>The base class carrying the requirement — the attribute walk must find it on the derived handler.</summary>
    [CorrelationRequired]
    public abstract class RequiredHandlerBase
    {
    }

    /// <summary>The derived fixture handler: inherits the requirement, declares nothing itself.</summary>
    public sealed class DerivedRequiredHandler : RequiredHandlerBase
    {
        /// <summary>Records the handled command.</summary>
        public static void Handle(RequiredCommand command, InvocationRecorder recorder)
        {
            recorder.Record(command);
        }
    }

    /// <summary>A fixture command whose handler opts out of enforcement with the attribute.</summary>
    public sealed record ExemptCommand(string Text);

    /// <summary>The exempt fixture handler.</summary>
    [AllowNoCorrelation]
    public static class ExemptCommandHandler
    {
        /// <summary>Records the handled command.</summary>
        public static void Handle(ExemptCommand command, InvocationRecorder recorder)
        {
            recorder.Record(command);
        }
    }

    /// <summary>A fixture command whose handler is exempted by configuration (ExcludeMessageHandlers).</summary>
    public sealed record ExcludedCommand(string Text);

    /// <summary>The configuration-excluded fixture handler.</summary>
    public static class ExcludedCommandHandler
    {
        /// <summary>Records the handled command.</summary>
        public static void Handle(ExcludedCommand command, InvocationRecorder recorder)
        {
            recorder.Record(command);
        }
    }
}
