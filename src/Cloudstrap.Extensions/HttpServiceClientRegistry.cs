namespace Cloudstrap.Extensions
{
    /// <summary>
    /// The set of client names registered through
    /// <see cref="ServiceCollectionExtensions.AddCloudstrapHttpServiceClient{TInterface, TImplementation}"/>.
    /// Held as a singleton instance in the service collection it belongs to — never static — so two
    /// containers built in the same process never see each other's clients.
    /// </summary>
    /// <remarks>
    /// The registry exists so validation stays scoped: a consumer's own named
    /// <see cref="Core.HttpClientServiceOptions"/> instances are not subject to Cloudstrap's rules.
    /// </remarks>
    internal sealed class HttpServiceClientRegistry
    {
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);

        /// <summary>
        /// Records a client name.
        /// </summary>
        /// <param name="name">The client name.</param>
        /// <returns><see langword="true"/> when this is the first registration of that name.</returns>
        public bool Add(string name) => _names.Add(name);

        /// <summary>
        /// Determines whether a client name was registered through the Cloudstrap entry point.
        /// </summary>
        /// <param name="name">The client name.</param>
        /// <returns><see langword="true"/> when the name is Cloudstrap's.</returns>
        public bool Contains(string name) => _names.Contains(name);
    }
}
