namespace Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure
{
    using Cloudstrap.TestIdentityProvider;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;

    /// <summary>
    /// The fixture idiom of this test project (plan mechanic (b)): a <see cref="HostApplicationBuilder"/>
    /// with an in-memory <c>Cloudstrap:</c> configuration, the real shipped typed-client registration, and
    /// the test identity provider hosted in-process as the token backchannel.
    /// </summary>
    internal static class ClientCredentialsTestHost
    {
        public const string ClientId = "contoso-service";
        public const string ClientSecret = "placeholder-not-a-real-secret";
        public const string ScopeName = "catalog.read";
        public const string Audience = "contoso-api";

        /// <summary>
        /// Starts the in-process identity provider with the standard <c>contoso-service</c> client.
        /// </summary>
        /// <param name="configure">An optional extra configuration step for the provider.</param>
        /// <returns>The running provider.</returns>
        public static TestIdentityProviderHost StartIdentityProvider(
            Action<TestIdentityProviderOptions>? configure = null) =>
            TestIdentityProviderHost.StartInProcess(options =>
            {
                options.Clients.Add(new TestIdentityProviderClient
                {
                    ClientId = ClientId,
                    ClientSecret = ClientSecret,
                    Scopes = { ScopeName },
                    Audiences = { Audience },
                });
                configure?.Invoke(options);
            });

        /// <summary>
        /// The standard configuration: one flagged <c>Catalog</c> client and a
        /// <c>Cloudstrap:ClientCredentials</c> section pointed at the given identity provider.
        /// </summary>
        /// <param name="identityProvider">The identity provider the section points at.</param>
        /// <returns>The configuration values.</returns>
        public static Dictionary<string, string?> DefaultConfig(TestIdentityProviderHost identityProvider) => new()
        {
            ["Cloudstrap:HttpClients:Catalog:BaseAddress"] = "https://catalog.contoso.example/",
            ["Cloudstrap:HttpClients:Catalog:AddClientAccessToken"] = "true",
            ["Cloudstrap:ClientCredentials:TokenEndpoint"] = identityProvider.TokenEndpoint.AbsoluteUri,
            ["Cloudstrap:ClientCredentials:ClientId"] = ClientId,
            ["Cloudstrap:ClientCredentials:ClientSecret"] = ClientSecret,
            ["Cloudstrap:ClientCredentials:Scope"] = ScopeName,
        };

        /// <summary>
        /// Builds an empty application host around the given in-memory configuration.
        /// </summary>
        /// <param name="config">The configuration values.</param>
        /// <returns>The host builder.</returns>
        public static HostApplicationBuilder CreateBuilder(Dictionary<string, string?> config)
        {
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Configuration.AddInMemoryCollection(config);

            return builder;
        }

        /// <summary>
        /// The configurator delegate wiring the token backchannel's primary handler to the in-process
        /// identity provider, so token requests reach it without sockets.
        /// </summary>
        /// <param name="identityProvider">The identity provider to route token requests to.</param>
        /// <returns>The configurator delegate to pass to <c>AddCloudstrapClientCredentials</c>.</returns>
        public static Action<CloudstrapClientCredentialsConfigurator> BackchannelTo(
            TestIdentityProviderHost identityProvider) =>
            configurator => configurator.Backchannel =
                http => http.ConfigurePrimaryHttpMessageHandler(identityProvider.CreateHandler);
    }
}
