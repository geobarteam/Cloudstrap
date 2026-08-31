namespace Cloudstrap.BlazorWasm.Tests
{
    using System.Text.Json;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using Refit;

    /// <summary>
    /// Pins the one-line client registrations: typed and Refit clients ride the cookie+XSRF pipeline
    /// against one shared token store, with camelCase Refit defaults and per-registration overrides
    /// (AC-BW3, AC-BW5, AC-BW6's Refit half, D-6, DL-3).
    /// </summary>
    [TestFixture]
    public sealed class WasmClientRegistrationTests
    {
        private const string _baseAddress = "https://bff.example.com/";

        [Test]
        public void AddCloudstrapWasmHttpClient_RegistersStoreHandlerAndTypedClient_AndReturnsTheBuilder()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            IHttpClientBuilder builder = services.AddCloudstrapWasmHttpClient<TestTypedClient>(_baseAddress);

            // Assert
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(builder, Is.Not.Null);
                Assert.That(provider.GetService<IAntiforgeryTokenStore>(), Is.Not.Null);
                Assert.That(provider.GetService<CookieHandler>(), Is.Not.Null);
                Assert.That(provider.GetService<TestTypedClient>(), Is.Not.Null);
            });
        }

        [Test]
        public void AddCloudstrapWasmHttpClient_ConfigureClient_AppliesToTheResolvedClient()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddCloudstrapWasmHttpClient<TestTypedClient>(
                _baseAddress,
                client => client.Timeout = TimeSpan.FromSeconds(7));

            // Act
            using ServiceProvider provider = services.BuildServiceProvider();
            TestTypedClient typed = provider.GetRequiredService<TestTypedClient>();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(typed.HttpClient.BaseAddress, Is.EqualTo(new Uri(_baseAddress)));
                Assert.That(typed.HttpClient.Timeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
            });
        }

        [Test]
        public async Task AddCloudstrapWasmHttpClient_ResolvedClientPipeline_ContainsTheCookieHandler()
        {
            // Arrange — a stub primary handler captures what really leaves the pipeline
            var primary = new StubPrimaryHandler();
            var services = new ServiceCollection();
            services.AddCloudstrapWasmHttpClient<TestTypedClient>(_baseAddress)
                .ConfigurePrimaryHttpMessageHandler(() => primary);
            using ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IAntiforgeryTokenStore>().Token = "seeded-token";
            TestTypedClient typed = provider.GetRequiredService<TestTypedClient>();

            // Act
            using var response = await typed.HttpClient.PostAsync(
                new Uri("api/items", UriKind.Relative),
                content: null,
                TestContext.CurrentContext.CancellationToken);

            // Assert — the pipeline is really wired, not just registered
            Assert.That(
                primary.LastRequest!.Headers.GetValues("X-XSRF-TOKEN").Single(),
                Is.EqualTo("seeded-token"));
        }

        [Test]
        public async Task AddCloudstrapWasmRefitClient_ResolvesTheInterfaceAndCallsThroughTheCookiePipeline()
        {
            // Arrange
            var primary = new StubPrimaryHandler { ResponseBody = /*lang=json,strict*/ "[{\"name\":\"contoso\"}]" };
            var services = new ServiceCollection();
            services.AddCloudstrapWasmRefitClient<ITestApiClient>(_baseAddress)
                .ConfigurePrimaryHttpMessageHandler(() => primary);
            using ServiceProvider provider = services.BuildServiceProvider();
            provider.GetRequiredService<IAntiforgeryTokenStore>().Token = "seeded-token";
            ITestApiClient client = provider.GetRequiredService<ITestApiClient>();

            // Act
            List<ItemDto> items = await client.GetItemsAsync();
            primary.ResponseBody = null;
            await client.AddItemAsync(new ItemDto("fabrikam"));

            // Assert — camelCase deserialized case-insensitively into the PascalCase DTO (AC-BW5),
            // and the mutating call carried the XSRF header through the configured base address
            Assert.Multiple(() =>
            {
                Assert.That(items.Single().Name, Is.EqualTo("contoso"));
                Assert.That(
                    primary.LastRequest!.RequestUri,
                    Is.EqualTo(new Uri("https://bff.example.com/api/items")));
                Assert.That(
                    primary.LastRequest!.Headers.GetValues("X-XSRF-TOKEN").Single(),
                    Is.EqualTo("seeded-token"));
            });
        }

        [Test]
        public async Task AddCloudstrapWasmRefitClient_CustomRefitSettings_WinPerRegistration()
        {
            // Arrange — a serializer with no naming policy: the request body stays PascalCase,
            // observably different from the camelCase default
            var primary = new StubPrimaryHandler();
            var pascalCase = new RefitSettings
            {
                ContentSerializer = new SystemTextJsonContentSerializer(new JsonSerializerOptions()),
            };
            var services = new ServiceCollection();
            services.AddCloudstrapWasmRefitClient<ITestApiClient>(_baseAddress, pascalCase)
                .ConfigurePrimaryHttpMessageHandler(() => primary);
            using ServiceProvider provider = services.BuildServiceProvider();
            ITestApiClient client = provider.GetRequiredService<ITestApiClient>();

            // Act
            await client.AddItemAsync(new ItemDto("fabrikam"));

            // Assert
            Assert.That(primary.LastRequestBody, Does.Contain("\"Name\""));
        }

        [Test]
        public void BothHelpers_ShareOneSingletonTokenStore()
        {
            // Arrange & Act — one Http registration, one Refit registration
            var services = new ServiceCollection();
            services.AddCloudstrapWasmHttpClient<TestTypedClient>(_baseAddress);
            services.AddCloudstrapWasmRefitClient<ITestApiClient>(_baseAddress);

            // Assert — exactly one store descriptor and one resolved instance (AC-BW3's one-store half)
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(
                    services.Count(d => d.ServiceType == typeof(IAntiforgeryTokenStore)),
                    Is.EqualTo(1));
                Assert.That(
                    provider.GetRequiredService<IAntiforgeryTokenStore>(),
                    Is.SameAs(provider.GetRequiredService<IAntiforgeryTokenStore>()));
            });
        }

        [Test]
        public void Helpers_GuardClauses_Throw()
        {
            var services = new ServiceCollection();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => default(IServiceCollection)!.AddCloudstrapWasmHttpClient<TestTypedClient>(_baseAddress),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => services.AddCloudstrapWasmHttpClient<TestTypedClient>(" "),
                    Throws.ArgumentException);
                Assert.That(
                    () => default(IServiceCollection)!.AddCloudstrapWasmRefitClient<ITestApiClient>(_baseAddress),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => services.AddCloudstrapWasmRefitClient<ITestApiClient>(string.Empty),
                    Throws.ArgumentException);
            });
        }

        public interface ITestApiClient
        {
            [Get("/api/items")]
            Task<List<ItemDto>> GetItemsAsync(CancellationToken cancellationToken = default);

            [Post("/api/items")]
            Task AddItemAsync([Body] ItemDto item, CancellationToken cancellationToken = default);
        }

        public sealed record ItemDto(string Name);

        public sealed class TestTypedClient
        {
            public TestTypedClient(HttpClient httpClient) => HttpClient = httpClient;

            public HttpClient HttpClient
            {
                get;
            }
        }

        private sealed class StubPrimaryHandler : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest
            {
                get; private set;
            }

            public string? LastRequestBody
            {
                get; private set;
            }

            public string? ResponseBody
            {
                get; set;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastRequestBody = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
                if (ResponseBody is not null)
                {
                    response.Content = new StringContent(ResponseBody, System.Text.Encoding.UTF8, "application/json");
                }

                return response;
            }
        }
    }
}
