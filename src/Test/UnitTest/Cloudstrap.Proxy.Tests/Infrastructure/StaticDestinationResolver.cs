namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using Yarp.ReverseProxy.Configuration;
    using Yarp.ReverseProxy.ServiceDiscovery;

    /// <summary>
    /// A test-local <see cref="IDestinationResolver"/>, standing in for service discovery
    /// (<c>Microsoft.Extensions.ServiceDiscovery.Yarp</c> plugs into the same hook): every destination whose address is
    /// a known logical address is resolved to its physical address, or removed when that address resolves to nothing;
    /// any other destination is kept as configured.
    /// </summary>
    internal sealed class StaticDestinationResolver : IDestinationResolver
    {
        private readonly IReadOnlyDictionary<Uri, Uri?> _map;
        private int _resolvedCount;
        private int _lastDestinationCount = -1;

        /// <summary>Initializes a new instance of the <see cref="StaticDestinationResolver"/> class with one mapping.</summary>
        /// <param name="logicalAddress">The configured, logical destination address, for example <c>http://downstream-api/</c>.</param>
        /// <param name="physicalAddress">
        /// The address it resolves to, for example the echo server's loopback address, or <see langword="null"/> when the
        /// logical address resolves to no destination at all.
        /// </param>
        public StaticDestinationResolver(Uri logicalAddress, Uri? physicalAddress)
            : this(new Dictionary<Uri, Uri?> { [logicalAddress] = physicalAddress })
        {
        }

        /// <summary>Initializes a new instance of the <see cref="StaticDestinationResolver"/> class.</summary>
        /// <param name="map">Logical address to physical address; a <see langword="null"/> value resolves to nothing.</param>
        public StaticDestinationResolver(IReadOnlyDictionary<Uri, Uri?> map)
        {
            _map = map;
        }

        /// <summary>Gets how many times YARP asked this resolver to resolve a cluster's destinations.</summary>
        public int ResolvedCount => Volatile.Read(ref _resolvedCount);

        /// <summary>Gets how many destinations the last resolution returned, or -1 before the first one.</summary>
        public int LastDestinationCount => Volatile.Read(ref _lastDestinationCount);

        /// <inheritdoc />
        public ValueTask<ResolvedDestinationCollection> ResolveDestinationsAsync(
            IReadOnlyDictionary<string, DestinationConfig> destinations,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destinations);
            Interlocked.Increment(ref _resolvedCount);

            Dictionary<string, DestinationConfig> resolved = [];
            foreach (KeyValuePair<string, DestinationConfig> destination in destinations)
            {
                if (!Uri.TryCreate(destination.Value.Address, UriKind.Absolute, out Uri? address)
                    || !_map.TryGetValue(address, out Uri? physical))
                {
                    resolved[destination.Key] = destination.Value;
                }
                else if (physical is not null)
                {
                    resolved[destination.Key] = destination.Value with
                    {
                        Address = physical.ToString()
                    };
                }
            }

            Volatile.Write(ref _lastDestinationCount, resolved.Count);
            return ValueTask.FromResult(new ResolvedDestinationCollection(resolved, null));
        }
    }
}
