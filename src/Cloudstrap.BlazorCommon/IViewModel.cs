namespace Cloudstrap.BlazorCommon
{
    /// <summary>
    /// Initialization contract for Blazor page ViewModels: the page awaits
    /// <see cref="InitializeAsync"/> once, typically from <c>OnInitializedAsync</c>, before
    /// rendering its data.
    /// </summary>
    /// <remarks>
    /// Implementations own cancellation: when the token is already cancelled or fires mid-flight,
    /// the implementation is expected to stop its work promptly (for example by passing the token to
    /// every downstream call) rather than relying on the caller to abandon the task.
    /// </remarks>
    public interface IViewModel
    {
        /// <summary>
        /// Loads the ViewModel's initial state.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the initialization work.</param>
        /// <returns>A task that completes when the initial state is loaded.</returns>
        Task InitializeAsync(CancellationToken cancellationToken = default);
    }
}
