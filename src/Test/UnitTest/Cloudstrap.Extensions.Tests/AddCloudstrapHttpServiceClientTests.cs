namespace Cloudstrap.Extensions.Tests
{
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    [TestFixture]
    public sealed class AddCloudstrapHttpServiceClientTests
    {
        [Test]
        public void AddCloudstrapHttpServiceClient_WithConfiguredSection_ResolvesTypedClientWithBaseAddressAndTimeout()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(TestHostBuilder.CatalogSection());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

            // Act
            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(client.Client.BaseAddress, Is.EqualTo(new Uri("https://catalog.contoso.example/")));
                Assert.That(client.Client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
            });
        }

        [Test]
        public void AddCloudstrapHttpServiceClient_WithoutName_DefaultsToInterfaceNameMinusLeadingI()
        {
            // Arrange — the section is named after the interface minus its leading 'I'
            HostApplicationBuilder builder = TestHostBuilder.Create(
                TestHostBuilder.CatalogSection(sectionName: "CatalogClient"));
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>();

            // Act
            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Assert
            Assert.That(client.Client.BaseAddress, Is.EqualTo(new Uri("https://catalog.contoso.example/")));
        }

        [Test]
        public async Task AddCloudstrapHttpServiceClient_SendsCorrelationHeaderExactlyOnce()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(TestHostBuilder.CatalogSection());
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);

            using IHost host = builder.Build();
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-1";
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert
            Assert.That(capturing.LastRequest!.Headers.GetValues("X-Correlation-ID"), Has.Exactly(1).Items);
        }

        [Test]
        public async Task AddCloudstrapHttpServiceClient_WithDefaultsLevelCorrelationRegistration_DoesNotStackASecondHandler()
        {
            // Arrange — the consumer already correlates every client through ConfigureHttpClientDefaults
            HostApplicationBuilder builder = TestHostBuilder.Create(TestHostBuilder.CatalogSection());
            CapturingPrimaryHandler capturing = new();
            builder.Services.ConfigureHttpClientDefaults(clientBuilder =>
                clientBuilder.AddCloudstrapCorrelationHandler());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);

            using IHost host = builder.Build();
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-2";
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert
            Assert.That(capturing.LastRequest!.Headers.GetValues("X-Correlation-ID"), Has.Exactly(1).Items);
        }

        [Test]
        public void AddCloudstrapHttpServiceClient_MissingSection_FailsStartupNamingTheSection()
        {
            // Arrange — no Cloudstrap:HttpClients:Catalog section at all
            HostApplicationBuilder builder = TestHostBuilder.Create();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            using IHost host = builder.Build();

            // Act
            OptionsValidationException? exception =
                Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.CurrentContext.CancellationToken));

            // Assert
            Assert.That(exception!.Message, Does.Contain("Cloudstrap:HttpClients:Catalog"));
        }

        [Test]
        public void AddCloudstrapHttpServiceClient_RelativeBaseAddress_FailsNamingTheKey()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(
                TestHostBuilder.CatalogSection(baseAddress: "catalog"));
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");
            using IHost host = builder.Build();

            // Act
            OptionsValidationException? exception =
                Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.CurrentContext.CancellationToken));

            // Assert
            Assert.That(exception!.Message, Does.Contain("Cloudstrap:HttpClients:Catalog:BaseAddress"));
        }

        [Test]
        public void AddCloudstrapHttpServiceClient_ConsumerHooks_RunAfterCloudstrapWiring()
        {
            // Arrange — the hook overrides a value Cloudstrap itself applied
            HostApplicationBuilder builder = TestHostBuilder.Create(TestHostBuilder.CatalogSection());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>(
                "Catalog",
                configureClient: client => client.Timeout = TimeSpan.FromSeconds(11));

            // Act
            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Assert
            Assert.That(client.Client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(11)));
        }

        [Test]
        public async Task AddCloudstrapHttpServiceClient_CalledTwiceForTheSameName_StillResolvesAndSendsOneCorrelationHeader()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(TestHostBuilder.CatalogSection());
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog");

            using IHost host = builder.Build();
            host.Services.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "corr-3";
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(client.Client.BaseAddress, Is.EqualTo(new Uri("https://catalog.contoso.example/")));
                Assert.That(capturing.LastRequest!.Headers.GetValues("X-Correlation-ID"), Has.Exactly(1).Items);
            });
        }

        [Test]
        public void AddCloudstrapHttpServiceClient_OnNullServices_ThrowsArgumentNullException()
        {
            // Arrange
            IServiceCollection services = null!;

            // Act + Assert
            Assert.That(
                () => services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog"),
                Throws.ArgumentNullException);
        }
    }
}
