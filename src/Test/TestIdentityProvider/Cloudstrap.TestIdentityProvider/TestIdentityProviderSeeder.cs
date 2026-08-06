namespace Cloudstrap.TestIdentityProvider
{
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using OpenIddict.Abstractions;
    using static OpenIddict.Abstractions.OpenIddictConstants;

    /// <summary>
    /// Creates the in-memory store schema and seeds the configured clients before the host starts
    /// serving requests.
    /// </summary>
    internal sealed class TestIdentityProviderSeeder : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestIdentityProviderSeeder"/> class.
        /// </summary>
        /// <param name="serviceProvider">The root service provider used to create the seeding scope.</param>
        public TestIdentityProviderSeeder(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

            TestIdentityProviderDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<TestIdentityProviderDbContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);

            TestIdentityProviderOptions options =
                scope.ServiceProvider.GetRequiredService<IOptions<TestIdentityProviderOptions>>().Value;
            IOpenIddictApplicationManager applicationManager =
                scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            foreach (TestIdentityProviderClient client in options.Clients)
            {
                OpenIddictApplicationDescriptor descriptor = new()
                {
                    ClientId = client.ClientId,
                    ClientSecret = client.ClientSecret,
                    DisplayName = client.ClientId,
                };

                descriptor.Permissions.Add(Permissions.Endpoints.Token);
                descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);

                foreach (string scopeName in client.Scopes)
                {
                    descriptor.Permissions.Add(Permissions.Prefixes.Scope + scopeName);
                }

                await applicationManager.CreateAsync(descriptor, cancellationToken);
            }
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
