namespace Cloudstrap.Authentication.ClientCredentials
{
    using Duende.AccessTokenManagement;
    using Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Code-level hooks for the parts of the integration that configuration values cannot express. Every
    /// convention has an override: these delegates run after Cloudstrap's own wiring.
    /// </summary>
    public sealed class CloudstrapClientCredentialsConfigurator
    {
        /// <summary>
        /// Gets or sets the hook shaping the Duende <see cref="ClientCredentialsClient"/> this package
        /// registers. It runs <em>last</em>, after the configuration-bound values are applied, so it
        /// always has the final say.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> for the configuration-bound values alone.</value>
        public Action<ClientCredentialsClient>? Client
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook shaping Duende's <see cref="ClientCredentialsTokenManagementOptions"/> —
        /// cache key prefix, cache lifetime buffer and related knobs.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> for Duende's defaults.</value>
        public Action<ClientCredentialsTokenManagementOptions>? TokenManagement
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets the hook applied to the token backchannel's <see cref="IHttpClientBuilder"/> —
        /// for a proxy, extra handlers, or a test server's in-process handler.
        /// </summary>
        /// <value>The hook, or <see langword="null"/> for an unmodified client.</value>
        /// <remarks>
        /// The hook applies to the default-named backchannel client
        /// (<see cref="CloudstrapClientCredentialsOptions.DefaultBackchannelHttpClientName"/>). When
        /// <c>BackchannelHttpClientName</c> is overridden in configuration, configure the renamed client
        /// with the standard <c>AddHttpClient(name)</c> registration instead.
        /// </remarks>
        public Action<IHttpClientBuilder>? Backchannel
        {
            get; set;
        }
    }
}
