namespace Cloudstrap.Proxy
{
    using Cloudstrap.Core;
    using Cloudstrap.Extensions;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Yarp.ReverseProxy.Forwarder;

    /// <summary>
    /// Mechanic (e): YARP's forwarder client factory with Cloudstrap's handler chain. Outer to inner: the correlation
    /// handler, the token handler of the cluster's mode (none in <see cref="ProxyTokenMode.None"/>), then YARP's
    /// <see cref="SocketsHttpHandler"/>. Every forwarded request carries the token, or none is sent at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// YARP builds a cluster's client when the cluster is loaded: at startup for the initial configuration, and on a
    /// configuration reload for a new or changed cluster. The token provider seam is resolved at that moment, never at
    /// registration time, so a missing provider fails startup (or the reload) naming the cluster, the metadata key and
    /// the package to install.
    /// </para>
    /// <para>
    /// No resilience or timeout handler is added, and handlers from <c>ConfigureHttpClientDefaults</c> do not apply:
    /// YARP's client does not come from <c>IHttpClientFactory</c> (AC-PX9). <c>traceparent</c> is YARP's own
    /// propagation on the outgoing hop; no OpenTelemetry pipeline is registered here.
    /// </para>
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
                    ProxyMetadataReader.ReadTokenMode(context.OldMetadata),
                    ProxyMetadataReader.ReadTokenMode(context.NewMetadata),
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
            ProxyTokenMode mode = ProxyMetadataReader.ResolveTokenMode(
                context.ClusterId,
                context.NewMetadata,
                _options.Value.DefaultTokenMode);

            HttpMessageHandler tokenized = mode switch
            {
                ProxyTokenMode.None => inner,
                ProxyTokenMode.Client => Chain(context.ClusterId, CreateClientTokenHandler(context.ClusterId), inner),
                ProxyTokenMode.User => Chain(context.ClusterId, CreateUserTokenHandler(context.ClusterId), inner),

                // Unreachable once the options validator has run; kept so an undefined value never forwards.
                _ => throw new InvalidOperationException(
                    $"Proxy cluster '{context.ClusterId}' resolved the undefined token mode '{mode}'. Use one of:"
                    + $" {ProxyMetadataReader.AllowedTokenModes}."),
            };

            return CreateCorrelationHandler(tokenized);
        }

        /// <summary>
        /// The outermost handler: the front door's ambient correlation id, in the header
        /// <see cref="CorrelationOptions.HeaderName"/> names, set only when the request does not carry one already
        /// (a caller-sent value that YARP copied is kept). It reads nothing but the correlation id and logs nothing.
        /// </summary>
        private CorrelationHttpDelegatingHandler CreateCorrelationHandler(HttpMessageHandler inner)
        {
            return new CorrelationHttpDelegatingHandler(
                _services.GetRequiredService<ICorrelationContextAccessor>(),
                _services.GetRequiredService<IOptions<CorrelationOptions>>())
            {
                InnerHandler = inner,
            };
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

        private DelegatingHandler CreateUserTokenHandler(string clusterId)
        {
            IUserAccessTokenHandlerProvider provider = _services.GetService<IUserAccessTokenHandlerProvider>()
                ?? throw new InvalidOperationException(
                    $"Proxy cluster '{clusterId}' attaches the signed-in user's token ('{ProxyMetadataKeys.TokenMode}' ="
                    + $" '{nameof(ProxyTokenMode.User)}'), but no {nameof(IUserAccessTokenHandlerProvider)} is registered."
                    + " Reference Cloudstrap.Authentication.OpenIdConnect and call AddCloudstrapOpenIdConnect, or change"
                    + $" the cluster metadata '{ProxyMetadataKeys.TokenMode}'. No request is sent without the token.");

            return provider.CreateUserTokenHandler(clusterId, ProxyClusterTokenOptions.Read(_configuration, clusterId));
        }
    }
}
