namespace Cloudstrap.BlazorCommon.Tests.Fixtures
{
    /// <summary>
    /// Happy-path convention target: public, concrete, <c>ViewModel</c> suffix. Implements the
    /// package's own <see cref="IViewModel"/> as well, so one fixture also proves a
    /// convention-registered class is reachable through the band contract.
    /// </summary>
    public sealed class SampleViewModel : ISampleViewModel, IViewModel
    {
        /// <inheritdoc />
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
