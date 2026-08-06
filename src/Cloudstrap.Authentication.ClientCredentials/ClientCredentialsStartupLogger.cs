namespace Cloudstrap.Authentication.ClientCredentials
{
    using Duende.AccessTokenManagement;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// States the credential type in force at host startup — a consumer-registered client assertion, a
    /// client secret, or none — so secret-free operation is visible, not magic (D-1). Names only; never
    /// a credential value.
    /// </summary>
    internal sealed class ClientCredentialsStartupLogger : IHostedService
    {
        private readonly IOptions<CloudstrapClientCredentialsOptions> _options;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ClientCredentialsStartupLogger> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientCredentialsStartupLogger"/> class.
        /// </summary>
        /// <param name="options">The bound Cloudstrap options.</param>
        /// <param name="serviceProvider">The container, probed for a consumer assertion service.</param>
        /// <param name="logger">The logger to write to.</param>
        public ClientCredentialsStartupLogger(
            IOptions<CloudstrapClientCredentialsOptions> options,
            IServiceProvider serviceProvider,
            ILogger<ClientCredentialsStartupLogger> logger)
        {
            _options = options;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            CloudstrapClientCredentialsOptions options = _options.Value;
            IClientAssertionService? assertionService = _serviceProvider.GetService<IClientAssertionService>();
            bool consumerAssertion = assertionService is not null
                && assertionService.GetType().Assembly != typeof(IClientCredentialsTokenManager).Assembly;

            string credentialType = consumerAssertion
                ? $"a consumer-registered client assertion ({assertionService!.GetType().Name})"
                : string.IsNullOrEmpty(options.ClientSecret)
                    ? "no static credential — register an IClientAssertionService or configure"
                        + " 'Cloudstrap:ClientCredentials:ClientSecret'"
                    : "a client secret";

            ClientCredentialsLog.CredentialTypeInForce(_logger, credentialType);
            ClientCredentialsLog.TokenCacheModeInForce(_logger, options.TokenCache);

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
