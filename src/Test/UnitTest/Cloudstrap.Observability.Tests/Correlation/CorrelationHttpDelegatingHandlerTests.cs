namespace Cloudstrap.Observability.Tests.Correlation
{
    using System.Net;
    using Cloudstrap.Core;
    using Cloudstrap.Observability.Correlation;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    [TestFixture]
    public sealed class CorrelationHttpDelegatingHandlerTests
    {
        [Test]
        public async Task SendAsync_WithAmbientCorrelationId_AddsConfiguredHeader()
        {
            // Arrange
            (ServiceProvider provider, CapturingHandler capturing) = BuildProvider(MinimalValid());
            await using (provider)
            {
                provider.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "abc-123";
                HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("catalog");

                // Act
                using HttpResponseMessage response =
                    await client.GetAsync(new Uri("https://api.example.com/orders/42"));

                // Assert
                Assert.That(
                    capturing.LastRequest!.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.EqualTo("abc-123"));
            }
        }

        [Test]
        public async Task SendAsync_WithConfiguredHeaderName_UsesThatHeader()
        {
            // Arrange
            Dictionary<string, string?> values = MinimalValid();
            values["Cloudstrap:Correlation:HeaderName"] = "X-Request-ID";
            (ServiceProvider provider, CapturingHandler capturing) = BuildProvider(values);
            await using (provider)
            {
                provider.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "req-456";
                HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("catalog");

                // Act
                using HttpResponseMessage response =
                    await client.GetAsync(new Uri("https://api.example.com/orders/42"));

                // Assert
                Assert.That(
                    capturing.LastRequest!.Headers.GetValues("X-Request-ID").Single(),
                    Is.EqualTo("req-456"));
            }
        }

        [Test]
        public async Task Send_SynchronousPath_AddsHeaderToo()
        {
            // Arrange
            (ServiceProvider provider, CapturingHandler capturing) = BuildProvider(MinimalValid());
            await using (provider)
            {
                provider.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "sync-789";
                HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("catalog");
                using HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/orders/42");

                // Act
                using HttpResponseMessage response = client.Send(request);

                // Assert
                Assert.That(
                    capturing.LastRequest!.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.EqualTo("sync-789"));
            }
        }

        [Test]
        public async Task SendAsync_WithHeaderAlreadyPresent_DoesNotThrowOrDuplicate()
        {
            // Arrange — a pre-set header models a retried/re-sent request
            (ServiceProvider provider, CapturingHandler capturing) = BuildProvider(MinimalValid());
            await using (provider)
            {
                provider.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "ambient-value";
                HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("catalog");
                using HttpRequestMessage request = new(HttpMethod.Get, "https://api.example.com/orders/42");
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", "pre-set-value");

                // Act
                using HttpResponseMessage response = await client.SendAsync(request);

                // Assert — set-if-absent: the pre-set value survives untouched, exactly once
                Assert.That(
                    capturing.LastRequest!.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.EqualTo("pre-set-value"));
            }
        }

        [Test]
        public async Task SendAsync_WithoutAmbientCorrelationId_SendsNoCorrelationHeader()
        {
            // Arrange
            (ServiceProvider provider, CapturingHandler capturing) = BuildProvider(MinimalValid());
            await using (provider)
            {
                HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("catalog");

                // Act
                using HttpResponseMessage response =
                    await client.GetAsync(new Uri("https://api.example.com/orders/42"));

                // Assert
                Assert.That(capturing.LastRequest!.Headers.Contains("X-Correlation-ID"), Is.False);
            }
        }

        [Test]
        public async Task AddCloudstrapCorrelationHandler_CalledTwice_AddsSingleHeaderValue()
        {
            // Arrange
            (ServiceProvider provider, CapturingHandler capturing) = BuildProvider(
                MinimalValid(),
                builder => builder.AddCloudstrapCorrelationHandler());
            await using (provider)
            {
                provider.GetRequiredService<ICorrelationContextAccessor>().CorrelationId = "once-only";
                HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("catalog");

                // Act
                using HttpResponseMessage response =
                    await client.GetAsync(new Uri("https://api.example.com/orders/42"));

                // Assert — double registration must not stack a second handler
                Assert.That(
                    capturing.LastRequest!.Headers.GetValues("X-Correlation-ID").Single(),
                    Is.EqualTo("once-only"));
            }
        }

        private static Dictionary<string, string?> MinimalValid() => new()
        {
            ["Cloudstrap:Application:SystemName"] = "Contoso",
            ["Cloudstrap:Application:SubsystemName"] = "Orders",
            ["Cloudstrap:Application:SubsystemType"] = "Api",
        };

        private static (ServiceProvider Provider, CapturingHandler Capturing) BuildProvider(
            Dictionary<string, string?> configValues,
            Action<IHttpClientBuilder>? extraClientSetup = null)
        {
            CapturingHandler capturing = new();
            ServiceCollection services = new();
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection(configValues).Build());
            services.AddCloudstrapCore();
            services.AddCloudstrapCorrelation();

            IHttpClientBuilder clientBuilder = services.AddHttpClient("catalog")
                .ConfigurePrimaryHttpMessageHandler(() => capturing)
                .AddCloudstrapCorrelationHandler();
            extraClientSetup?.Invoke(clientBuilder);

            return (services.BuildServiceProvider(), capturing);
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest
            {
                get; private set;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                LastRequest = request;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            protected override HttpResponseMessage Send(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                LastRequest = request;

                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }
    }
}
