namespace Cloudstrap.Proxy
{
    using Cloudstrap.Extensions;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Yarp.ReverseProxy.Forwarder;

    /// <summary>
    /// Mechanic (e): YARP's forwarder client factory with Cloudstrap's handler chain. Each cluster's client gets the
    /// token handler of the cluster's mode in front of YARP's <see cref="SocketsHttpHandler"/>, so every forwarded
    /// request carries the token, or none is sent at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// YARP builds a cluster's client when the cluster is loaded: at startup for the initial configuration, and on a
    /// configuration reload for a new or changed cluster. The token provider seam is resolved at that moment, never at
    /// registration time, so a missing provider fails startup (or the reload) naming the cluster, the metadata key and
    /// the package to install.
    /// </para>
    /// <para>No resilience or timeout handler is added (AC-PX9).</para>
    /// </remarks>
    internal sealed class CloudstrapForwarderHttpClientFactory : ForwarderHttpClientFactory
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly IOptions<CloudstrapProxyOptions> _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudstrapForwarderHttpClientFactory"/> class.
        /// </summary>
        /// <param name="logger">YARP's factory logger.</param>
        /// <param name="services">The container the token provider seams are resolved from, lazily.</param>
        /// <param name="configuration">The configuration the per-cluster token request parameters bind from.</param>
        /// <param name="options">The proxy options, for <see cref="CloudstrapProxyOptions.DefaultTokenMode"/>.</param>
        public CloudstrapForwarderHttpClientFactory(
            ILogger<ForwarderHttpClientFactory> logger,
            IServiceProvider services,
            IConfiguration configuration,
            IOptions<CloudstrapProxyOptions> options)
            : base(logger)
        {
            _services = services;
            _configuration = configuration;
            _options = options;
        }

        /// <inheritdoc />
        /// <remarks>
        /// A cluster whose <see cref="ProxyMetadataKeys.TokenMode"/> metadata changed gets a new client, so the new
        /// mode takes effect on reload.
        /// </remarks>
        protected override bool CanReuseOldClient(ForwarderHttpClientContext context)
        {
            return base.CanReuseOldClient(context)
                && string.Equals(
                    ReadTokenMode(context.OldMetadata),
                    ReadTokenMode(context.NewMetadata),
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">
        /// The cluster needs a token provider that is not registered, the provider returned a handler whose
        /// <see cref="DelegatingHandler.InnerHandler"/> is already set, or the cluster's token mode is not supported.
        /// </exception>
        protected override HttpMessageHandler WrapHandler(ForwarderHttpClientContext context, HttpMessageHandler handler)
        {
            HttpMessageHandler inner = base.WrapHandler(context, handler);
            ProxyTokenMode mode = ResolveTokenMode(context.ClusterId, context.NewMetadata);

            return mode switch
            {
                ProxyTokenMode.None => inner,
                ProxyTokenMode.Client => Chain(context.ClusterId, CreateClientTokenHandler(context.ClusterId), inner),
                _ => throw UnsupportedTokenMode(context.ClusterId, mode.ToString()),
            };
        }

        private static InvalidOperationException UnsupportedTokenMode(string clusterId, string value)
        {
            return new InvalidOperationException(
                $"Proxy cluster '{clusterId}' has '{ProxyMetadataKeys.TokenMode}' = '{value}', which is not a supported"
                + " token mode.");
        }

        private static string? ReadTokenMode(IReadOnlyDictionary<string, string>? metadata)
        {
            return metadata is not null && metadata.TryGetValue(ProxyMetadataKeys.TokenMode, out string? value) ? value : null;
        }

        /// <summary>
        /// Puts the token handler in front of YARP's handler. The seam contract forbids a pre-set inner handler: the
        /// proxy owns the chain, and silently replacing it would drop whatever the provider put there.
        /// </summary>
        private static DelegatingHandler Chain(string clusterId, DelegatingHandler tokenHandler, HttpMessageHandler inner)
        {
            if (tokenHandler.InnerHandler is not null)
            {
                throw new InvalidOperationException(
                    $"The token handler created for proxy cluster '{clusterId}' already has its"
                    + $" {nameof(DelegatingHandler.InnerHandler)} set. A token handler provider must return a new handler"
                    + " whose InnerHandler is null: the proxy owns the handler chain.");
            }

            tokenHandler.InnerHandler = inner;
            return tokenHandler;
        }

        private ProxyTokenMode ResolveTokenMode(string clusterId, IReadOnlyDictionary<string, string>? metadata)
        {
            string? value = ReadTokenMode(metadata);
            if (value is null)
            {
                return _options.Value.DefaultTokenMode;
            }

            return Enum.TryParse(value, ignoreCase: true, out ProxyTokenMode mode)
                ? mode
                : throw UnsupportedTokenMode(clusterId, value);
        }

        private DelegatingHandler CreateClientTokenHandler(string clusterId)
        {
            IClientAccessTokenHandlerProvider provider = _services.GetService<IClientAccessTokenHandlerProvider>()
                ?? throw new InvalidOperationException(
                    $"Proxy cluster '{clusterId}' attaches the application's token ('{ProxyMetadataKeys.TokenMode}' ="
                    + $" '{nameof(ProxyTokenMode.Client)}', the default), but no {nameof(IClientAccessTokenHandlerProvider)}"
                    + " is registered. Reference Cloudstrap.Authentication.ClientCredentials and call"
                    + " AddCloudstrapClientCredentials, or set the cluster metadata"
                    + $" '{ProxyMetadataKeys.TokenMode}' to '{nameof(ProxyTokenMode.None)}'. No request is sent without the"
                    + " token.");

            return provider.CreateClientTokenHandler(clusterId, ProxyClusterTokenOptions.Read(_configuration, clusterId));
        }
    }
}
