namespace Cloudstrap.BlazorServer
{
    /// <summary>
    /// Makes circuit-originated work visible: wraps a user interaction — a button handler, a form submit —
    /// in a fresh root trace of its own, because the SignalR hub trace the circuit runs under is dropped by
    /// Cloudstrap's hub sampler and anything parented under it would vanish with it.
    /// </summary>
    public interface IBlazorInteractionTrace
    {
        /// <summary>
        /// Starts an interaction scope: a new root activity named <paramref name="interactionName"/>,
        /// deliberately detached from whatever activity is ambient, with the ambient correlation identifier
        /// pointed at the new trace so outbound HTTP calls made inside the scope carry it.
        /// </summary>
        /// <param name="interactionName">The interaction's span name, for example <c>"whoami"</c>.</param>
        /// <returns>
        /// A scope that, when disposed, stops the interaction activity and restores the previous ambient
        /// activity and correlation identifier. Disposal never throws and tolerates being called twice.
        /// </returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="interactionName"/> is <see langword="null"/>, empty or white-space.
        /// </exception>
        /// <remarks>
        /// <para>
        /// Work started inside the scope — child activities, traced outbound calls — parents under the
        /// interaction root, and the ambient correlation identifier equals the interaction's trace
        /// identifier for the scope's lifetime.
        /// </para>
        /// <para>
        /// Without any trace listener the call is a safe no-op that still establishes a fresh correlation
        /// identifier, so the outbound correlation header stays stable when tracing is off.
        /// </para>
        /// <para>
        /// A scope that is never disposed simply ends with the circuit's async context; nothing leaks
        /// across circuits and nothing throws.
        /// </para>
        /// </remarks>
        IDisposable StartInteraction(string interactionName);
    }
}
