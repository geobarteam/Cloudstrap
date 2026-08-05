namespace Cloudstrap.Extensions.Tests
{
    using System.Net;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Http;
    using NUnit.Framework;

    /// <summary>
    /// AC-ASP3: a Cloudstrap typed client coexists with resilience the consumer applied through
    /// <c>ConfigureHttpClientDefaults</c> — one layer, contributed entirely by the consumer.
    /// </summary>
    [TestFixture]
    public sealed class HttpServiceClientResilienceTests
    {
        [Test]
        public async Task AddCloudstrapHttpServiceClient_WithDefaultsLevelStandardResilience_ClientWorksWithSingleResilienceLayer()
        {
            // Arrange
            HostApplicationBuilder builder = TestHostBuilder.Create(
                TestHostBuilder.CatalogSection(timeout: "00:01:00"));
            HandlerChainCapture capture = new("Catalog");
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture);
            builder.Services.ConfigureHttpClientDefaults(clientBuilder =>
                clientBuilder.AddStandardResilienceHandler());
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);

            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — the client works, and exactly one resilience layer is present: the consumer's
            Assert.Multiple(() =>
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(capture.ResilienceHandlerCount, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task AddCloudstrapHttpServiceClient_Alone_AddsNoResilienceHandler()
        {
            // Arrange — no consumer resilience anywhere
            HostApplicationBuilder builder = TestHostBuilder.Create(TestHostBuilder.CatalogSection());
            HandlerChainCapture capture = new("Catalog");
            CapturingPrimaryHandler capturing = new();
            builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(capture);
            builder.Services.AddCloudstrapHttpServiceClient<ICatalogClient, CatalogClient>("Catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing);

            using IHost host = builder.Build();
            ICatalogClient client = host.Services.GetRequiredService<ICatalogClient>();

            // Act
            using HttpResponseMessage response = await client.Client.GetAsync(new Uri("orders", UriKind.Relative));

            // Assert — Cloudstrap never contributes resilience of its own
            Assert.That(capture.ResilienceHandlerCount, Is.Zero);
        }
    }
}
