namespace Cloudstrap.Worker
{
    using System.Globalization;
    using Cloudstrap.Extensions;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Hosting.Server;
    using Microsoft.AspNetCore.Hosting.Server.Features;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Hosts the worker's health probe endpoints on their own minimal Kestrel side-host, so a
    /// generic host serves the standard Cloudstrap container probes without gaining an ASP.NET
    /// pipeline of its own.
    /// </summary>
    /// <remarks>
    /// The inner host bridges three services from the parent host: its <see cref="IConfiguration"/>
    /// (so <c>MapCloudstrapHealthChecks</c> reads the real <c>Cloudstrap:HealthChecks</c> section),
    /// its <see cref="HealthCheckService"/> (so the probes evaluate the checks the consumer
    /// registered on the parent host — this package owns zero probe-evaluation logic), and its
    /// <see cref="ILoggerFactory"/> (so Kestrel's lifecycle logs flow through the host's logging).
    /// A bind failure propagates out of <see cref="StartAsync"/> and faults host startup — a worker
    /// never runs silently unprobed.
    /// </remarks>
    internal sealed class WorkerHealthListener : IHostedService
    {
        private readonly IOptions<WorkerOptions> _options;
        private readonly IConfiguration _configuration;
        private readonly HealthCheckService _healthCheckService;
        private readonly ILoggerFactory _loggerFactory;
        private WebApplication? _app;

        /// <summary>
        /// Initializes a new instance of the <see cref="WorkerHealthListener"/> class.
        /// </summary>
        /// <param name="options">The worker settings carrying the listener's port and address.</param>
        /// <param name="configuration">The parent host's configuration, bridged into the inner host.</param>
        /// <param name="healthCheckService">The parent host's health check service, bridged so the probes evaluate the host's registered checks.</param>
        /// <param name="loggerFactory">The parent host's logger factory, bridged so the inner host logs through it.</param>
        public WorkerHealthListener(
            IOptions<WorkerOptions> options,
            IConfiguration configuration,
            HealthCheckService healthCheckService,
            ILoggerFactory loggerFactory)
        {
            _options = options;
            _configuration = configuration;
            _healthCheckService = healthCheckService;
            _loggerFactory = loggerFactory;
        }

        /// <summary>The inner host's bound addresses — the test seam proving what was bound.</summary>
        public IReadOnlyList<string> BoundAddresses =>
            [.. _app?.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses ?? []];

        /// <summary>
        /// Composes the listener URL from the options: the default <c>"*"</c> address composes an
        /// all-interfaces binding, <c>"localhost"</c> composes loopback-only.
        /// </summary>
        /// <param name="options">The worker settings carrying the address and port.</param>
        /// <returns>The URL the inner host binds.</returns>
        public static string ComposeUrl(WorkerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            return string.Create(
                CultureInfo.InvariantCulture,
                $"http://{options.HealthListenAddress}:{options.HealthPort}");
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            WorkerOptions options = _options.Value;
            string url = ComposeUrl(options);

            WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
            builder.WebHost.UseKestrelCore();
            // Merged as a source (never replacing the inner host's own IConfiguration registration),
            // so MapCloudstrapHealthChecks reads the real Cloudstrap:HealthChecks section while the
            // inner host's own hosting settings stay intact.
            builder.Configuration.AddConfiguration(_configuration);
            builder.Services.AddRoutingCore();
            builder.Services.AddLogging();
            builder.Services.AddSingleton(_healthCheckService);
            builder.Services.AddSingleton(_loggerFactory);

            WebApplication app = builder.Build();
            app.Urls.Add(url);
            app.UseRouting();
            app.MapCloudstrapHealthChecks();

            // No catch: a bind failure (occupied port, forbidden address) propagates and faults
            // host startup — the fail-fast posture replacing the source's swallow-all catch.
            await app.StartAsync(cancellationToken);
            _app = app;
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_app is not null)
            {
                await _app.StopAsync(cancellationToken);
                await _app.DisposeAsync();
                _app = null;
            }
        }
    }
}
