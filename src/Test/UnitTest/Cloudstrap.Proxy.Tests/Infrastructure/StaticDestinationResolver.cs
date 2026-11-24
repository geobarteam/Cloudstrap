namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using Yarp.ReverseProxy.Configuration;
    using Yarp.ReverseProxy.ServiceDiscovery;

    /// <summary>
    /// A test-local <see cref="IDestinationResolver"/>, standing in for service discovery
    /// (<c>Microsoft.Extensions.ServiceDiscovery.Yarp</c> plugs into the same hook): every destination whose address
    /// is <c>logicalAddress</c> is resolved to <c>physicalAddress</c>; any other destination is kept as configured.
    /// </summary>
    /// <param name="logicalAddress">The configured, logical destination address, for example <c>http://downstream-api/</c>.</param>
    /// <param name="physicalAddress">The address it resolves to, for example the echo server's loopback address.</param>
    internal sealed class StaticDestinationResolver(Uri logicalAddress, Uri physicalAddress) : IDestinationResolver
    {
        private int _resolvedCount;

        /// <summary>Gets how many times YARP asked this resolver to resolve a cluster's destinations.</summary>
        public int ResolvedCount => Volatile.Read(ref _resolvedCount);

        /// <inheritdoc />
        public ValueTask<ResolvedDestinationCollection> ResolveDestinationsAsync(
            IReadOnlyDictionary<string, DestinationConfig> destinations,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destinations);
            Interlocked.Increment(ref _resolvedCount);

            Dictionary<string, DestinationConfig> resolved = destinations.ToDictionary(
                destination => destination.Key,
                destination => Uri.TryCreate(destination.Value.Address, UriKind.Absolute, out Uri? address) && address == logicalAddress
                    ? destination.Value with
                    {
                        Address = physicalAddress.ToString()
                    }
                    : destination.Value);

            return ValueTask.FromResult(new ResolvedDestinationCollection(resolved, null));
        }
    }
}
