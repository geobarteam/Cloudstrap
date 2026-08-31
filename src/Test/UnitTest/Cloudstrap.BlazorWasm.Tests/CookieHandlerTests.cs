namespace Cloudstrap.BlazorWasm.Tests
{
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// Pins the cookie+XSRF pipeline's request-side contract: browser credentials on every request,
    /// XSRF attachment on mutating calls only, from the one configured header name (AC-BW2, D-3).
    /// </summary>
    [TestFixture]
    public sealed class CookieHandlerTests
    {
        private const string _defaultHeaderName = "X-XSRF-TOKEN";
        private const string _testToken = "test-xsrf-token";

        [Test]
        public async Task SendAsync_OnAnyRequest_SetsBrowserRequestCredentialsInclude()
        {
            // Arrange
            var store = new FakeTokenStore { Token = _testToken };
            using var get = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/test");
            using var post = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/test");

            // Act
            (await SendAsync(store, get)).Dispose();
            (await SendAsync(store, post)).Dispose();

            // Assert — the WebAssembly fetch option carries credentials: include on GET and POST alike
            Assert.Multiple(() =>
            {
                Assert.That(FetchCredentialsOption(get), Is.EqualTo("include"));
                Assert.That(FetchCredentialsOption(post), Is.EqualTo("include"));
            });
        }

        [Test]
        public async Task SendAsync_GetRequest_DoesNotAttachTheXsrfHeader()
        {
            // Arrange — token present: the method, not the store, decides
            var store = new FakeTokenStore { Token = _testToken };
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://localhost/api/test");

            // Act
            (await SendAsync(store, request)).Dispose();

            // Assert
            Assert.That(request.Headers.Contains(_defaultHeaderName), Is.False);
        }

        [TestCase("POST")]
        [TestCase("PUT")]
        [TestCase("DELETE")]
        [TestCase("PATCH")]
        public async Task SendAsync_MutatingRequest_WithToken_AttachesTheXsrfHeader(string method)
        {
            // Arrange
            var store = new FakeTokenStore { Token = _testToken };
            using var request = new HttpRequestMessage(new HttpMethod(method), "https://localhost/api/test");

            // Act
            (await SendAsync(store, request)).Dispose();

            // Assert
            Assert.That(request.Headers.GetValues(_defaultHeaderName).Single(), Is.EqualTo(_testToken));
        }

        [Test]
        public async Task SendAsync_MutatingRequest_WithoutToken_DoesNotAttachTheHeader()
        {
            // Arrange — empty store: nothing to attach
            var store = new FakeTokenStore();
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/test");

            // Act
            (await SendAsync(store, request)).Dispose();

            // Assert
            Assert.That(request.Headers.Contains(_defaultHeaderName), Is.False);
        }

        [Test]
        public async Task SendAsync_WithOverriddenXsrfHeaderName_AttachesTheConfiguredName()
        {
            // Arrange — the D-3 fix: attachment reads the configured name, impossible in the source
            var store = new FakeTokenStore { Token = _testToken };
            IOptions<CloudstrapBlazorWasmOptions> options = Options.Create(
                new CloudstrapBlazorWasmOptions { XsrfHeaderName = "X-CUSTOM-XSRF" });
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/test");

            // Act
            (await SendAsync(store, request, options)).Dispose();

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(request.Headers.GetValues("X-CUSTOM-XSRF").Single(), Is.EqualTo(_testToken));
                Assert.That(request.Headers.Contains(_defaultHeaderName), Is.False);
            });
        }

        [Test]
        public async Task SendAsync_RequestAlreadyCarryingTheHeader_ReplacesItWithTheStoresToken()
        {
            // Arrange — the replace-semantics edge case: the pipeline is the last writer
            var store = new FakeTokenStore { Token = _testToken };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/test");
            request.Headers.Add(_defaultHeaderName, "stale-caller-token");

            // Act
            (await SendAsync(store, request)).Dispose();

            // Assert
            Assert.That(request.Headers.GetValues(_defaultHeaderName).Single(), Is.EqualTo(_testToken));
        }

        [Test]
        public void Ctor_NullTokenStore_ThrowsArgumentNullException()
        {
            Assert.That(() => new CookieHandler(null!), Throws.ArgumentNullException);
        }

        private static async Task<HttpResponseMessage> SendAsync(
            IAntiforgeryTokenStore store,
            HttpRequestMessage request,
            IOptions<CloudstrapBlazorWasmOptions>? options = null)
        {
            using var handler = new CookieHandler(store, options) { InnerHandler = new StubInnerHandler() };
            using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);

            return await invoker.SendAsync(request, TestContext.CurrentContext.CancellationToken);
        }

        private static object? FetchCredentialsOption(HttpRequestMessage request)
        {
            IDictionary<string, object>? fetchOptions = request.Options
                .Select(option => option.Value)
                .OfType<IDictionary<string, object>>()
                .FirstOrDefault(dictionary => dictionary.ContainsKey("credentials"));

            return fetchOptions?["credentials"];
        }

        private sealed class FakeTokenStore : IAntiforgeryTokenStore
        {
            public string? Token
            {
                get; set;
            }
        }

        private sealed class StubInnerHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
