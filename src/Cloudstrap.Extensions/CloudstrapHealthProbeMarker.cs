namespace Cloudstrap.Extensions
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.Primitives;

    /// <summary>
    /// An endpoint data source contributing no endpoints, added to an application the moment
    /// <see cref="EndpointRouteBuilderExtensions.MapCloudstrapHealthChecks"/> maps its probes. Its presence
    /// is how a second call recognizes that the probes are already mapped.
    /// </summary>
    /// <remarks>
    /// A marker is used rather than a search through the application's existing endpoints because
    /// enumerating another data source would build its endpoints early, freezing conventions the consumer
    /// may still be adding. Marking is per application, so no state outlives the container it belongs to.
    /// </remarks>
    internal sealed class CloudstrapHealthProbeMarker : EndpointDataSource
    {
        /// <inheritdoc/>
        public override IReadOnlyList<Endpoint> Endpoints => [];

        /// <inheritdoc/>
        public override IChangeToken GetChangeToken() => NeverChangesToken.Instance;

        /// <summary>
        /// The change token of a data source whose (empty) endpoint set can never change.
        /// </summary>
        private sealed class NeverChangesToken : IChangeToken, IDisposable
        {
            public static NeverChangesToken Instance
            {
                get;
            } = new();

            public bool ActiveChangeCallbacks => false;

            public bool HasChanged => false;

            public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => this;

            public void Dispose()
            {
                // Nothing to release: the callback was never registered.
            }
        }
    }
}
