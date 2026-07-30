namespace Cloudstrap.Observability
{
    using System.Diagnostics;

    /// <summary>
    /// The default <see cref="IBusinessTraceScope"/>: wraps the underlying activity, or nothing at all when
    /// the pipeline is disabled or the span was sampled out — every member is then a safe no-op.
    /// </summary>
    internal sealed class BusinessTraceScope : IBusinessTraceScope
    {
        internal const string OutcomeTagKey = "cloudstrap.business.outcome";

        private readonly Activity? _activity;

        internal BusinessTraceScope(Activity? activity)
        {
            _activity = activity;
        }

        public bool IsRecording => _activity?.IsAllDataRequested ?? false;

        public void SetOutcome(string outcome)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

            _activity?.SetTag(OutcomeTagKey, outcome);
        }

        public void Dispose() => _activity?.Dispose();
    }
}
