namespace Cloudstrap.BlazorWasm.Tests
{
    using System.Net;
    using System.Security.Claims;
    using Microsoft.AspNetCore.Components.Authorization;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// Pins the BFF authentication state contract, ported test-for-test from the source: the
    /// <c>BffCookie</c> principal, the anonymous failure ladder, XSRF capture from the configured
    /// header, the one-call cache and the clear-notify-refetch seam (AC-BW1, AC-BW3, AC-BW4, AC-BW6).
    /// </summary>
    [TestFixture]
    public sealed class BffAuthenticationStateProviderTests
    {
        private const string _baseAddress = "https://bff.example.com/";

        private const string _authenticatedBody = /*lang=json,strict*/ """
            {"isAuthenticated":true,"userName":"testuser","claims":[{"type":"sub","value":"user-1"},{"type":"email","value":"testuser@example.com"}]}
            """;

        [Test]
        public async Task GetAuthenticationStateAsync_AuthenticatedUser_ReturnsBffCookiePrincipalWithNameAndClaims()
        {
            // Arrange
            var handler = new StubHttpHandler { ResponseBody = _authenticatedBody };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);

            // Act
            AuthenticationState state = await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(state.User.Identity!.IsAuthenticated, Is.True);
                Assert.That(state.User.Identity!.AuthenticationType, Is.EqualTo("BffCookie"));
                Assert.That(state.User.Identity!.Name, Is.EqualTo("testuser"));
                Assert.That(state.User.FindFirst("sub")?.Value, Is.EqualTo("user-1"));
                Assert.That(state.User.FindFirst("email")?.Value, Is.EqualTo("testuser@example.com"));
            });
        }

        [Test]
        public async Task GetAuthenticationStateAsync_SignedOutUser_ReturnsAnonymous()
        {
            // Arrange
            var handler = new StubHttpHandler { ResponseBody = /*lang=json,strict*/ "{\"isAuthenticated\":false}" };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);

            // Act
            AuthenticationState state = await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.That(state.User.Identity!.IsAuthenticated, Is.False);
        }

        [Test]
        public async Task GetAuthenticationStateAsync_NullBody_ReturnsAnonymous()
        {
            // Arrange — the JSON literal null deserializes to a null DTO (source parity)
            var handler = new StubHttpHandler { ResponseBody = "null" };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);

            // Act
            AuthenticationState state = await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.That(state.User.Identity!.IsAuthenticated, Is.False);
        }

        [Test]
        public async Task GetAuthenticationStateAsync_HttpError_ReturnsAnonymous()
        {
            // Arrange
            var handler = new StubHttpHandler { StatusCode = HttpStatusCode.InternalServerError };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);

            // Act & Assert — no throw, anonymous
            AuthenticationState state = await provider.GetAuthenticationStateAsync();
            Assert.That(state.User.Identity!.IsAuthenticated, Is.False);
        }

        [Test]
        public async Task GetAuthenticationStateAsync_NetworkError_ReturnsAnonymous()
        {
            // Arrange
            var handler = new StubHttpHandler { ThrowNetworkError = true };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);

            // Act & Assert — the full failure ladder ends anonymous, never in a throw
            AuthenticationState state = await provider.GetAuthenticationStateAsync();
            Assert.That(state.User.Identity!.IsAuthenticated, Is.False);
        }

        [Test]
        public async Task GetAuthenticationStateAsync_XsrfResponseHeader_CapturesTheTokenIntoTheStore()
        {
            // Arrange
            var handler = new StubHttpHandler
            {
                ResponseBody = _authenticatedBody,
                ResponseHeaders = { ["X-XSRF-TOKEN"] = "issued-token" },
            };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out FakeTokenStore store);

            // Act
            await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.That(store.Token, Is.EqualTo("issued-token"));
        }

        [Test]
        public async Task GetAuthenticationStateAsync_WithOverriddenHeaderName_CapturesFromTheConfiguredHeader()
        {
            // Arrange — the same one option Step 1 proved on attachment, proven on capture (AC-BW6)
            var handler = new StubHttpHandler
            {
                ResponseBody = _authenticatedBody,
                ResponseHeaders = { ["X-CUSTOM-XSRF"] = "custom-token" },
            };
            var options = new CloudstrapBlazorWasmOptions { XsrfHeaderName = "X-CUSTOM-XSRF" };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out FakeTokenStore store, options);

            // Act
            await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.That(store.Token, Is.EqualTo("custom-token"));
        }

        [Test]
        public async Task GetAuthenticationStateAsync_UsesTheConfiguredUserEndpointPath()
        {
            // Arrange — default first
            var handler = new StubHttpHandler { ResponseBody = _authenticatedBody };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);
            await provider.GetAuthenticationStateAsync();

            var overriddenHandler = new StubHttpHandler { ResponseBody = _authenticatedBody };
            var options = new CloudstrapBlazorWasmOptions { UserEndpointPath = "session/me" };
            BffAuthenticationStateProvider overridden = CreateProvider(overriddenHandler, out _, options);
            await overridden.GetAuthenticationStateAsync();

            // Assert — resolved against the base address (D-2, AC-BW6)
            Assert.Multiple(() =>
            {
                Assert.That(
                    handler.Requests.Single().RequestUri,
                    Is.EqualTo(new Uri("https://bff.example.com/bff/user")));
                Assert.That(
                    overriddenHandler.Requests.Single().RequestUri,
                    Is.EqualTo(new Uri("https://bff.example.com/session/me")));
            });
        }

        [Test]
        public async Task GetAuthenticationStateAsync_CachedState_MakesExactlyOneHttpCall()
        {
            // Arrange
            var handler = new StubHttpHandler { ResponseBody = _authenticatedBody };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);

            // Act
            AuthenticationState first = await provider.GetAuthenticationStateAsync();
            AuthenticationState second = await provider.GetAuthenticationStateAsync();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(handler.Requests, Has.Count.EqualTo(1));
                Assert.That(second, Is.SameAs(first));
            });
        }

        [Test]
        public async Task ClearAuthenticationState_DropsTheCacheNotifiesAndRefetches()
        {
            // Arrange
            var handler = new StubHttpHandler { ResponseBody = _authenticatedBody };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);
            Task<AuthenticationState>? notified = null;
            provider.AuthenticationStateChanged += task => notified = task;
            await provider.GetAuthenticationStateAsync();

            // Act
            provider.ClearAuthenticationState();
            await provider.GetAuthenticationStateAsync();

            // Assert — exactly two HTTP calls across fetch → clear → fetch (AC-BW4)
            Assert.Multiple(() =>
            {
                Assert.That(notified, Is.Not.Null);
                Assert.That(handler.Requests, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void ClearAuthenticationState_BeforeAnyFetch_IsSafe()
        {
            // Arrange
            var handler = new StubHttpHandler { ResponseBody = _authenticatedBody };
            BffAuthenticationStateProvider provider = CreateProvider(handler, out _);
            Task<AuthenticationState>? notified = null;
            provider.AuthenticationStateChanged += task => notified = task;

            // Act & Assert — no throw, the notification fires (spec edge case)
            Assert.That(provider.ClearAuthenticationState, Throws.Nothing);
            Assert.That(notified, Is.Not.Null);
        }

        private static BffAuthenticationStateProvider CreateProvider(
            StubHttpHandler handler,
            out FakeTokenStore store,
            CloudstrapBlazorWasmOptions? options = null)
        {
            store = new FakeTokenStore();
            CloudstrapBlazorWasmOptions effective = options ?? new CloudstrapBlazorWasmOptions();

            return new BffAuthenticationStateProvider(
                new FakeHttpClientFactory(handler, effective.AuthHttpClientName),
                store,
                Options.Create(effective));
        }

        private sealed class FakeTokenStore : IAntiforgeryTokenStore
        {
            public string? Token
            {
                get; set;
            }
        }

        private sealed class FakeHttpClientFactory : IHttpClientFactory
        {
            private readonly StubHttpHandler _handler;
            private readonly string _expectedName;

            public FakeHttpClientFactory(StubHttpHandler handler, string expectedName)
            {
                _handler = handler;
                _expectedName = expectedName;
            }

            public HttpClient CreateClient(string name)
            {
                Assert.That(name, Is.EqualTo(_expectedName), "The provider must use the configured client name.");

                return new HttpClient(_handler, disposeHandler: false)
                {
                    BaseAddress = new Uri(_baseAddress),
                };
            }
        }

        private sealed class StubHttpHandler : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = [];

            public Dictionary<string, string> ResponseHeaders { get; } = [];

            public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

            public string? ResponseBody
            {
                get; set;
            }

            public bool ThrowNetworkError
            {
                get; set;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);

                if (ThrowNetworkError)
                {
                    throw new HttpRequestException("network unavailable");
                }

                var response = new HttpResponseMessage(StatusCode);
                foreach (KeyValuePair<string, string> header in ResponseHeaders)
                {
                    response.Headers.Add(header.Key, header.Value);
                }

                if (ResponseBody is not null)
                {
                    response.Content = new StringContent(ResponseBody, System.Text.Encoding.UTF8, "application/json");
                }

                return Task.FromResult(response);
            }
        }
    }
}
