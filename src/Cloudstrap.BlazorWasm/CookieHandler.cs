namespace Cloudstrap.BlazorWasm
{
    using Microsoft.AspNetCore.Components.WebAssembly.Http;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// The cookie+XSRF pipeline: includes browser credentials (the BFF session cookie) with every
    /// request and attaches the stored XSRF token, under the configured header name, to mutating
    /// requests only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every client registered through the package carries this handler. It is public as an escape
    /// hatch: a consumer-owned <c>AddHttpClient(...)</c> chain can add it with
    /// <c>.AddHttpMessageHandler&lt;CookieHandler&gt;()</c> and ride the same pipeline.
    /// </para>
    /// <para>
    /// A header already present on the request is replaced — the pipeline is the last writer, so a
    /// stale caller-provided token never wins over the captured one.
    /// </para>
    /// </remarks>
    public sealed class CookieHandler : DelegatingHandler
    {
        private static readonly HashSet<HttpMethod> _mutatingMethods =
        [
            HttpMethod.Post,
            HttpMethod.Put,
            HttpMethod.Delete,
            HttpMethod.Patch,
        ];

        private readonly IAntiforgeryTokenStore _tokenStore;
        private readonly string _xsrfHeaderName;

        /// <summary>
        /// Initializes a new instance of the <see cref="CookieHandler"/> class.
        /// </summary>
        /// <param name="tokenStore">The antiforgery token store providing the current XSRF token.</param>
        /// <param name="options">
        /// The bound options carrying the XSRF header name; <see langword="null"/> (the outside-DI
        /// escape hatch) applies the defaults.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="tokenStore"/> is <see langword="null"/>.</exception>
        public CookieHandler(
            IAntiforgeryTokenStore tokenStore,
            IOptions<CloudstrapBlazorWasmOptions>? options = null)
        {
            ArgumentNullException.ThrowIfNull(tokenStore);

            _tokenStore = tokenStore;
            _xsrfHeaderName = (options?.Value ?? new CloudstrapBlazorWasmOptions()).XsrfHeaderName;
        }

        /// <summary>
        /// Sends the request with browser credentials included and, on mutating methods with a
        /// non-empty store, the configured XSRF header attached.
        /// </summary>
        /// <param name="request">The HTTP request message.</param>
        /// <param name="cancellationToken">A token to cancel the send.</param>
        /// <returns>The HTTP response message.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

            if (_mutatingMethods.Contains(request.Method) && !string.IsNullOrEmpty(_tokenStore.Token))
            {
                request.Headers.Remove(_xsrfHeaderName);
                request.Headers.Add(_xsrfHeaderName, _tokenStore.Token);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
