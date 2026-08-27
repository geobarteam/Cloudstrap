namespace Cloudstrap.BlazorServer
{
    using System.Diagnostics;
    using Cloudstrap.Observability.Correlation;

    /// <summary>
    /// The default <see cref="IBlazorInteractionTrace"/>: a singleton owning the
    /// <c>Cloudstrap.BlazorServer.Interaction</c> activity source, disposed with the container at shutdown.
    /// </summary>
    internal sealed class BlazorInteractionTrace : IBlazorInteractionTrace, IDisposable
    {
        private readonly ActivitySource _activitySource = new(BlazorServerActivitySources.Interaction);
        private readonly ICorrelationContextAccessor _correlationContextAccessor;
        private readonly ICorrelationSource _correlationSource;

        public BlazorInteractionTrace(
            ICorrelationContextAccessor correlationContextAccessor,
            ICorrelationSource correlationSource)
        {
            ArgumentNullException.ThrowIfNull(correlationContextAccessor);
            ArgumentNullException.ThrowIfNull(correlationSource);

            _correlationContextAccessor = correlationContextAccessor;
            _correlationSource = correlationSource;
        }

        public IDisposable StartInteraction(string interactionName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(interactionName);

            Activity? previousActivity = Activity.Current;
            string? previousCorrelationId = _correlationContextAccessor.CorrelationId;

            // Detached on purpose: the ambient hub activity is the trace the hub sampler drops, and a child
            // of a dropped trace vanishes with it. Clearing the ambient activity makes the next start a root.
            Activity.Current = null;

            Activity? activity = _activitySource.StartActivity(interactionName);

            // With no listener there is no activity — the correlation identifier is then freshly generated,
            // so the outbound header stays stable while tracing is off.
            _correlationContextAccessor.CorrelationId =
                activity?.TraceId.ToString() ?? _correlationSource.GenerateCorrelation();

            return new BlazorInteractionScope(
                activity,
                previousActivity,
                previousCorrelationId,
                _correlationContextAccessor);
        }

        public void Dispose() => _activitySource.Dispose();
    }
}
