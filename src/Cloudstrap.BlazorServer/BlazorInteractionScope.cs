namespace Cloudstrap.BlazorServer
{
    using System.Diagnostics;
    using Cloudstrap.Observability.Correlation;

    /// <summary>
    /// The scope an interaction runs in: disposal stops the interaction activity and restores the ambient
    /// activity and correlation identifier captured at the start. Restore-to-previous per scope keeps
    /// nesting stack-safe; a second dispose is a no-op and nothing here ever throws.
    /// </summary>
    internal sealed class BlazorInteractionScope : IDisposable
    {
        private readonly Activity? _activity;
        private readonly Activity? _previousActivity;
        private readonly string? _previousCorrelationId;
        private readonly ICorrelationContextAccessor _correlationContextAccessor;
        private bool _disposed;

        internal BlazorInteractionScope(
            Activity? activity,
            Activity? previousActivity,
            string? previousCorrelationId,
            ICorrelationContextAccessor correlationContextAccessor)
        {
            _activity = activity;
            _previousActivity = previousActivity;
            _previousCorrelationId = previousCorrelationId;
            _correlationContextAccessor = correlationContextAccessor;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _activity?.Dispose();
            Activity.Current = _previousActivity;
            _correlationContextAccessor.CorrelationId = _previousCorrelationId;
        }
    }
}
