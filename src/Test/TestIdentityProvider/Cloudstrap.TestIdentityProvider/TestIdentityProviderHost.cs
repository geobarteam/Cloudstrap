namespace Cloudstrap.TestIdentityProvider
{
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// A disposable, self-contained host for the test identity provider, in either of its two modes:
    /// fully in-process over TestHost (no sockets), or on a real loopback address over Kestrel for
    /// end-to-end fixtures.
    /// </summary>
    public sealed class TestIdentityProviderHost : IDisposable
    {
        private readonly IHost _host;
        private readonly TestServer? _testServer;
        private readonly Counter _counter;

        private TestIdentityProviderHost(IHost host, TestServer? testServer, Uri baseAddress, Counter counter)
        {
            _host = host;
            _testServer = testServer;
            _counter = counter;
            BaseAddress = baseAddress;
            TokenEndpoint = new Uri(baseAddress, "connect/token");
        }

        /// <summary>
        /// Gets the base address the provider is reachable at.
        /// </summary>
        /// <value>The base address.</value>
        public Uri BaseAddress
        {
            get;
        }

        /// <summary>
        /// Gets the address of the token endpoint.
        /// </summary>
        /// <value>The token endpoint address.</value>
        public Uri TokenEndpoint
        {
            get;
        }

        /// <summary>
        /// Gets the number of requests the token endpoint has received — the hit counter caching and
        /// renewal tests assert against.
        /// </summary>
        /// <value>The token request count.</value>
        public int TokenRequestCount => Volatile.Read(ref _counter.Value);

        /// <summary>
        /// Starts an identity provider fully in-process on TestHost: real discovery, JWKS and token
        /// issuance with no sockets, reachable through <see cref="CreateHandler"/> and
        /// <see cref="CreateClient"/>.
        /// </summary>
        /// <param name="configure">An optional delegate configuring <see cref="TestIdentityProviderOptions"/>.</param>
        /// <returns>The running host.</returns>
        public static TestIdentityProviderHost StartInProcess(Action<TestIdentityProviderOptions>? configure = null)
        {
            Counter counter = new();
            IHost host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    ConfigureIdentityProviderHost(webHost, counter, configure);
                })
                .Build();

            try
            {
                host.Start();
                TestServer testServer = host.GetTestServer();

                return new TestIdentityProviderHost(host, testServer, testServer.BaseAddress, counter);
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Starts an identity provider on Kestrel at <c>http://127.0.0.1:{port}</c>, serving real HTTP
        /// for fixtures whose system under test runs out of process.
        /// </summary>
        /// <param name="port">The loopback port to listen on.</param>
        /// <param name="configure">An optional delegate configuring <see cref="TestIdentityProviderOptions"/>.</param>
        /// <returns>The running host.</returns>
        public static TestIdentityProviderHost StartLoopback(
            int port,
            Action<TestIdentityProviderOptions>? configure = null)
        {
            Counter counter = new();
            Uri baseAddress = new(FormattableString.Invariant($"http://127.0.0.1:{port}/"));
            IHost host = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseKestrel();
                    webHost.UseUrls(baseAddress.AbsoluteUri);
                    ConfigureIdentityProviderHost(webHost, counter, configure);
                })
                .Build();

            try
            {
                host.Start();

                return new TestIdentityProviderHost(host, testServer: null, baseAddress, counter);
            }
            catch
            {
                host.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a message handler that reaches the provider — the piece a test wires into another
        /// component (for example a token client's backchannel) so that component talks to this provider.
        /// </summary>
        /// <returns>
        /// The in-process TestHost handler, or a plain socket handler when the provider listens on
        /// loopback.
        /// </returns>
        public HttpMessageHandler CreateHandler() =>
            _testServer is not null ? _testServer.CreateHandler() : new SocketsHttpHandler();

        /// <summary>
        /// Creates an <see cref="HttpClient"/> whose base address is the provider's.
        /// </summary>
        /// <returns>The client. The caller owns and disposes it.</returns>
        public HttpClient CreateClient() =>
            _testServer is not null
                ? _testServer.CreateClient()
                : new HttpClient { BaseAddress = BaseAddress };

        /// <inheritdoc/>
        public void Dispose()
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        /// <summary>
        /// The shared wiring of both hosting modes: the provider services, the token-endpoint hit
        /// counter, and the pipeline OpenIddict's ASP.NET Core host needs.
        /// </summary>
        /// <param name="webHost">The web host builder being configured.</param>
        /// <param name="counter">The token-endpoint hit counter.</param>
        /// <param name="configure">The caller's options delegate.</param>
        private static void ConfigureIdentityProviderHost(
            IWebHostBuilder webHost,
            Counter counter,
            Action<TestIdentityProviderOptions>? configure)
        {
            webHost.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddCloudstrapTestIdentityProvider(configure);
            });
            webHost.Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    if (context.Request.Path == "/connect/token")
                    {
                        Interlocked.Increment(ref counter.Value);
                    }

                    await next(context);
                });
                app.UseRouting();
                app.UseAuthentication();
                app.UseEndpoints(static endpoints => endpoints.MapCloudstrapTestIdentityProvider());
            });
        }

        /// <summary>
        /// The token-endpoint hit counter, shared between the pipeline middleware and the host wrapper.
        /// </summary>
        private sealed class Counter
        {
            /// <summary>
            /// The number of token-endpoint requests observed.
            /// </summary>
            public int Value;
        }
    }
}
