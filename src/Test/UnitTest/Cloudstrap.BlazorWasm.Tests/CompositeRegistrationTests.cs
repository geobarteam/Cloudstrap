namespace Cloudstrap.BlazorWasm.Tests
{
    using Microsoft.AspNetCore.Components.Authorization;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// Pins the composite's registration list through the internal testable seam (mechanic (a)):
    /// the provider behind both seams, options bound from <c>Cloudstrap:BlazorWasm</c> with the
    /// delegate winning, the wired auth client, authorization + cascading state, no localization,
    /// and TryAdd idempotence (AC-BW1's registration half, AC-BW6's client half, D-1, D-4, D-5).
    /// </summary>
    [TestFixture]
    public sealed class CompositeRegistrationTests
    {
        private const string _baseAddress = "https://bff.example.com/";

        [Test]
        public void AddCloudstrapBlazorWasmServices_RegistersTheProviderAsBothSeams()
        {
            // Arrange & Act
            ServiceCollection services = Compose();

            // Assert — one scoped instance behind both service types
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            AuthenticationStateProvider stateProvider =
                scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
            IBffAuthenticationStateProvider bffProvider =
                scope.ServiceProvider.GetRequiredService<IBffAuthenticationStateProvider>();

            Assert.Multiple(() =>
            {
                Assert.That(stateProvider, Is.InstanceOf<BffAuthenticationStateProvider>());
                Assert.That(bffProvider, Is.SameAs(stateProvider));
            });
        }

        [Test]
        public void AddCloudstrapBlazorWasmServices_BindsOptionsFromConfigurationAndTheDelegateWins()
        {
            // Arrange & Act — config sets one value, the delegate overrides it (D-4)
            ServiceCollection services = Compose(
                configuration: new Dictionary<string, string?>
                {
                    ["Cloudstrap:BlazorWasm:UserEndpointPath"] = "from-config/user",
                    ["Cloudstrap:BlazorWasm:XsrfHeaderName"] = "X-FROM-CONFIG",
                },
                configure: options => options.UserEndpointPath = "from-delegate/user");

            // Assert
            using ServiceProvider provider = services.BuildServiceProvider();
            CloudstrapBlazorWasmOptions options =
                provider.GetRequiredService<IOptions<CloudstrapBlazorWasmOptions>>().Value;
            Assert.Multiple(() =>
            {
                Assert.That(options.UserEndpointPath, Is.EqualTo("from-delegate/user"));
                Assert.That(options.XsrfHeaderName, Is.EqualTo("X-FROM-CONFIG"));
            });
        }

        [Test]
        public void AddCloudstrapBlazorWasmServices_NoConfigSection_AllDefaultsApply()
        {
            // Arrange & Act
            ServiceCollection services = Compose();

            // Assert — spec edge case: absent section, every default applies
            using ServiceProvider provider = services.BuildServiceProvider();
            CloudstrapBlazorWasmOptions options =
                provider.GetRequiredService<IOptions<CloudstrapBlazorWasmOptions>>().Value;
            Assert.Multiple(() =>
            {
                Assert.That(options.UserEndpointPath, Is.EqualTo("bff/user"));
                Assert.That(options.XsrfHeaderName, Is.EqualTo("X-XSRF-TOKEN"));
                Assert.That(options.AuthHttpClientName, Is.EqualTo("CloudstrapBffAuth"));
            });
        }

        [Test]
        public async Task AddCloudstrapBlazorWasmServices_AuthClient_UsesTheConfiguredNameAndBaseAddress()
        {
            // Arrange — a stub primary handler on the configured client name captures what leaves
            var primary = new CapturingPrimaryHandler();
            ServiceCollection services = Compose(
                configure: options => options.AuthHttpClientName = "CustomAuthClient");
            services.AddHttpClient("CustomAuthClient")
                .ConfigurePrimaryHttpMessageHandler(() => primary);
            using ServiceProvider provider = services.BuildServiceProvider();
            using IServiceScope scope = provider.CreateScope();
            provider.GetRequiredService<IAntiforgeryTokenStore>().Token = "seeded-token";

            // Act — the provider's fetch flows through the named client and its CookieHandler
            await scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>()
                .GetAuthenticationStateAsync();

            // Assert
            HttpRequestMessage request = primary.Requests.Single();
            Assert.Multiple(() =>
            {
                Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://bff.example.com/bff/user")));
                Assert.That(
                    request.Options.Any(option => option.Value is IDictionary<string, object> fetch
                        && fetch.ContainsKey("credentials")),
                    Is.True,
                    "The CookieHandler must be in the auth client's pipeline (AC-BW2).");
            });
        }

        [Test]
        public void AddCloudstrapBlazorWasmServices_RegistersAuthorizationCoreAndCascadingAuthenticationState()
        {
            // Arrange & Act
            ServiceCollection services = Compose();

            // Assert — D-5: the composite wires the Blazor auth plumbing end to end
            Assert.Multiple(() =>
            {
                Assert.That(
                    services.Any(d => d.ServiceType.Name == "IAuthorizationService"),
                    Is.True,
                    "AddAuthorizationCore's services must be present.");
                Assert.That(
                    services.Any(d => d.ServiceType.Name == "ICascadingValueSupplier"),
                    Is.True,
                    "AddCascadingAuthenticationState's supplier must be present.");
            });
        }

        [Test]
        public void AddCloudstrapBlazorWasmServices_RegistersNoLocalization()
        {
            // Arrange & Act
            ServiceCollection services = Compose();

            // Assert — D-1 made observable: the composite hides nothing
            Assert.That(services.Any(d => d.ServiceType.Name == "IStringLocalizerFactory"), Is.False);
        }

        [Test]
        public void AddCloudstrapBlazorWasmServices_CalledTwice_TryAddServicesRegisterOnce()
        {
            // Arrange & Act
            ServiceCollection services = Compose();
            services.AddCloudstrapBlazorWasmServices(_baseAddress, EmptyConfiguration(), configure: null);

            // Assert — spec edge case: repeat calls are additive-safe
            Assert.Multiple(() =>
            {
                Assert.That(services.Count(d => d.ServiceType == typeof(IAntiforgeryTokenStore)), Is.EqualTo(1));
                Assert.That(services.Count(d => d.ServiceType == typeof(CookieHandler)), Is.EqualTo(1));
                Assert.That(services.Count(d => d.ServiceType == typeof(BffAuthenticationStateProvider)), Is.EqualTo(1));
                Assert.That(services.Count(d => d.ServiceType == typeof(AuthenticationStateProvider)), Is.EqualTo(1));
            });
        }

        [Test]
        public void AddCloudstrapBlazorWasmServices_GuardClauses_Throw()
        {
            var services = new ServiceCollection();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => default(IServiceCollection)!.AddCloudstrapBlazorWasmServices(
                        _baseAddress, EmptyConfiguration(), configure: null),
                    Throws.ArgumentNullException);
                Assert.That(
                    () => services.AddCloudstrapBlazorWasmServices(" ", EmptyConfiguration(), configure: null),
                    Throws.ArgumentException);
                Assert.That(
                    () => services.AddCloudstrapBlazorWasmServices(_baseAddress, null!, configure: null),
                    Throws.ArgumentNullException);
            });
        }

        [Test]
        public void AddCloudstrapBlazorWasm_OnNullBuilder_ThrowsArgumentNullException()
        {
            Assert.That(
                () => WebAssemblyHostBuilderExtensions.AddCloudstrapBlazorWasm(null!),
                Throws.ArgumentNullException);
        }

        private static ServiceCollection Compose(
            Dictionary<string, string?>? configuration = null,
            Action<CloudstrapBlazorWasmOptions>? configure = null)
        {
            var services = new ServiceCollection();
            IConfiguration config = configuration is null
                ? EmptyConfiguration()
                : new ConfigurationBuilder().AddInMemoryCollection(configuration).Build();
            services.AddCloudstrapBlazorWasmServices(_baseAddress, config, configure);

            return services;
        }

        private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

        private sealed class CapturingPrimaryHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);

                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        /*lang=json,strict*/ "{\"isAuthenticated\":false}",
                        System.Text.Encoding.UTF8,
                        "application/json"),
                };

                return Task.FromResult(response);
            }
        }
    }
}
