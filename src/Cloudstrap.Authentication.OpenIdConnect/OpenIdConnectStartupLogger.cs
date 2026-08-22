namespace Cloudstrap.Authentication.OpenIdConnect
{
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// States the sign-in posture once at host startup — schemes in force, where tokens live, and
    /// (once they exist) whether the opt-in endpoints were mapped — so the integration is visible, not
    /// magic. Names only; never a credential, code or token value.
    /// </summary>
    internal sealed class OpenIdConnectStartupLogger : IHostedService
    {
        private readonly IOptions<CloudstrapOpenIdConnectOptions> _options;
        private readonly AuthenticationEndpointsState _endpointsState;
        private readonly IAuthenticationSchemeProvider _schemeProvider;
        private readonly ILogger<OpenIdConnectStartupLogger> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenIdConnectStartupLogger"/> class.
        /// </summary>
        /// <param name="options">The bound Cloudstrap options.</param>
        /// <param name="endpointsState">The endpoint-mapping state the mapper records into.</param>
        /// <param name="schemeProvider">The scheme provider, probed for the bearer scheme.</param>
        /// <param name="logger">The logger to write to.</param>
        public OpenIdConnectStartupLogger(
            IOptions<CloudstrapOpenIdConnectOptions> options,
            AuthenticationEndpointsState endpointsState,
            IAuthenticationSchemeProvider schemeProvider,
            ILogger<OpenIdConnectStartupLogger> logger)
        {
            _options = options;
            _endpointsState = endpointsState;
            _schemeProvider = schemeProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            OpenIdConnectLog.SignInPostureInForce(
                _logger,
                CloudstrapOpenIdConnect.CookieScheme,
                CloudstrapOpenIdConnect.ChallengeScheme,
                _options.Value.Cookie.Name);

            if (_endpointsState.EndpointsMapped)
            {
                OpenIdConnectLog.AuthenticationEndpointsMapped(
                    _logger,
                    _endpointsState.LoginPath!,
                    _endpointsState.LogoutPath!);
            }
            else
            {
                OpenIdConnectLog.AuthenticationEndpointsNotMapped(_logger);
            }

            if (await _schemeProvider.GetSchemeAsync(BearerCoexistence.BearerSchemeName) is not null)
            {
                OpenIdConnectLog.BearerCoexistenceActive(_logger);
            }
            else
            {
                OpenIdConnectLog.BearerCoexistenceInert(_logger);
            }
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
