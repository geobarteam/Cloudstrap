namespace Cloudstrap.Extensions
{
    using Cloudstrap.Core;
    using Cloudstrap.Observability;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Registers the health check that probes one typed client's peer, and reconciles it with the settings
    /// bound for that client.
    /// </summary>
    /// <remarks>
    /// Two phases are needed because the check's name and whether it exists at all come from configuration,
    /// which cannot be read while services are being registered. The registration is therefore added under
    /// the conventional name, and this <see cref="IConfigureOptions{TOptions}"/> — registered after it, so it
    /// runs after it — drops the check when the client did not ask for one and renames it when the client
    /// configured a prefix.
    /// </remarks>
    internal sealed class DependencyHealthCheckSetup : IConfigureOptions<HealthCheckServiceOptions>
    {
        private const string _nameSuffix = "-liveness";

        private static readonly TimeSpan _probeTimeout = TimeSpan.FromSeconds(2);

        private readonly string _clientName;
        private readonly IOptionsMonitor<HttpClientServiceOptions> _clientOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="DependencyHealthCheckSetup"/> class.
        /// </summary>
        /// <param name="clientName">The logical client name whose check this reconciles.</param>
        /// <param name="clientOptions">The bound per-client settings.</param>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public DependencyHealthCheckSetup(
            string clientName,
            IOptionsMonitor<HttpClientServiceOptions> clientOptions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
            ArgumentNullException.ThrowIfNull(clientOptions);

            _clientName = clientName;
            _clientOptions = clientOptions;
        }

        /// <summary>
        /// Adds the peer probe for one client to the stock health check builder, plus the reconciliation that
        /// applies the client's settings to it.
        /// </summary>
        /// <param name="services">The service collection to register into.</param>
        /// <param name="clientName">The logical client name.</param>
        public static void Register(IServiceCollection services, string clientName)
        {
            services.AddHealthChecks().AddUrlGroup(
                uriProvider: serviceProvider => ProbeUri(serviceProvider, clientName),
                name: ConventionName(clientName),
                failureStatus: HealthStatus.Unhealthy,
                tags: [CloudstrapHealthCheckTags.Readiness],
                configureClient: (_, client) => client.Timeout = _probeTimeout);

            services.AddSingleton<IConfigureOptions<HealthCheckServiceOptions>>(serviceProvider =>
                new DependencyHealthCheckSetup(
                    clientName,
                    serviceProvider.GetRequiredService<IOptionsMonitor<HttpClientServiceOptions>>()));
        }

        /// <inheritdoc/>
        public void Configure(HealthCheckServiceOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            string conventionName = ConventionName(_clientName);
            HealthCheckRegistration? registration = options.Registrations.FirstOrDefault(
                candidate => string.Equals(candidate.Name, conventionName, StringComparison.Ordinal));

            if (registration is null)
            {
                return;
            }

            HttpClientServiceOptions client = _clientOptions.Get(_clientName);

            if (!client.EnableHealthCheck || client.BaseAddress is null)
            {
                options.Registrations.Remove(registration);

                return;
            }

            if (!string.IsNullOrWhiteSpace(client.HealthCheckPrefix))
            {
                registration.Name = $"{client.HealthCheckPrefix}{_nameSuffix}";
            }
        }

        /// <summary>
        /// Builds the check name a client gets when it configures no prefix. It is also the name of the
        /// probe's own <see cref="HttpClient"/>, which is how a consumer overrides the probe's transport.
        /// </summary>
        /// <param name="clientName">The logical client name.</param>
        /// <returns>The conventional check name.</returns>
        private static string ConventionName(string clientName) => $"{clientName}{_nameSuffix}";

        /// <summary>
        /// Resolves the URI probed for a client, deferred to the moment the check runs so it reflects the
        /// bound configuration.
        /// </summary>
        /// <param name="serviceProvider">The provider the settings are read from.</param>
        /// <param name="clientName">The logical client name.</param>
        /// <returns>The absolute URI of the peer's health endpoint.</returns>
        private static Uri ProbeUri(IServiceProvider serviceProvider, string clientName)
        {
            HttpClientServiceOptions client = serviceProvider
                .GetRequiredService<IOptionsMonitor<HttpClientServiceOptions>>()
                .Get(clientName);

            return new Uri(client.BaseAddress!, client.HealthCheckPath);
        }
    }
}
