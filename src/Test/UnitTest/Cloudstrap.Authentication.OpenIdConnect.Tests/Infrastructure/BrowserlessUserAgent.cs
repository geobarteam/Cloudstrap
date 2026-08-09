namespace Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure
{
    using System.Net;

    /// <summary>
    /// A minimal, honest browser stand-in (plan-level pick 1): routes each request to the right
    /// in-process TestServer by authority, keeps one cookie jar that honors <c>Secure</c>, <c>Path</c>
    /// and deletions, follows redirects across both hosts, and submits the two HTML forms of an
    /// interactive sign-in — the identity provider's login form and the <c>form_post</c> callback —
    /// by parsing the real markup rather than short-circuiting the protocol.
    /// </summary>
    internal sealed class BrowserlessUserAgent : IDisposable
    {
        private const int _maxRedirects = 10;

        private readonly Dictionary<string, HttpMessageInvoker> _hostsByAuthority;
        private readonly CookieContainer _cookies = new();
        private readonly List<HttpResponseMessage> _responses = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="BrowserlessUserAgent"/> class.
        /// </summary>
        /// <param name="applicationBase">The application's base address.</param>
        /// <param name="applicationHandler">The application TestServer's handler.</param>
        /// <param name="identityProviderBase">The identity provider's base address.</param>
        /// <param name="identityProviderHandler">The identity provider TestServer's handler.</param>
        public BrowserlessUserAgent(
            Uri applicationBase,
            HttpMessageHandler applicationHandler,
            Uri identityProviderBase,
            HttpMessageHandler identityProviderHandler)
        {
            _hostsByAuthority = new Dictionary<string, HttpMessageInvoker>(StringComparer.OrdinalIgnoreCase)
            {
                [applicationBase.Authority] = new HttpMessageInvoker(applicationHandler, disposeHandler: true),
                [identityProviderBase.Authority] = new HttpMessageInvoker(identityProviderHandler, disposeHandler: true),
            };
        }

        /// <summary>
        /// Gets every response this agent has received, in order — including intermediate redirect and
        /// form responses, so a test can inspect the exact <c>Set-Cookie</c> headers of any hop.
        /// </summary>
        public IReadOnlyList<HttpResponseMessage> Responses => _responses;

        /// <summary>
        /// Sends a request with the jar's cookies, optionally following redirects across both hosts.
        /// </summary>
        /// <param name="request">The request to send. The URI must be absolute.</param>
        /// <param name="followRedirects">Whether 3xx responses are followed with GET requests.</param>
        /// <returns>The final response.</returns>
        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, bool followRedirects = true)
        {
            HttpResponseMessage response = await SendOnceAsync(request);
            int hops = 0;

            while (followRedirects && IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                if (++hops > _maxRedirects)
                {
                    throw new InvalidOperationException("The redirect chain exceeded the redirect limit.");
                }

                Uri next = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(response.RequestMessage!.RequestUri!, response.Headers.Location);
                response = await SendOnceAsync(new HttpRequestMessage(HttpMethod.Get, next));
            }

            return response;
        }

        /// <summary>
        /// GETs a URL, following redirects.
        /// </summary>
        /// <param name="url">The absolute URL.</param>
        /// <returns>The final response.</returns>
        public Task<HttpResponseMessage> GetAsync(Uri url) =>
            SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

        /// <summary>
        /// GETs a URL without following redirects, so the raw challenge or redirect is inspectable.
        /// </summary>
        /// <param name="url">The absolute URL.</param>
        /// <returns>The immediate response.</returns>
        public Task<HttpResponseMessage> GetNoRedirectAsync(Uri url) =>
            SendAsync(new HttpRequestMessage(HttpMethod.Get, url), followRedirects: false);

        /// <summary>
        /// Performs the full interactive sign-in a person would: navigate to the start URL, get
        /// challenged to the identity provider, fill in the login form, and let the <c>form_post</c>
        /// callback carry the code back to the application.
        /// </summary>
        /// <param name="startUrl">The application URL to start at.</param>
        /// <param name="username">The username typed into the login form.</param>
        /// <param name="password">The password typed into the login form.</param>
        /// <returns>The final response, after the post-sign-in redirect.</returns>
        public async Task<HttpResponseMessage> SignInAsync(Uri startUrl, string username, string password)
        {
            HttpResponseMessage loginForm = await GetAsync(startUrl);
            HttpResponseMessage formPostPage = await SubmitLoginFormAsync(loginForm, username, password);

            return await SubmitFormAsync(formPostPage);
        }

        /// <summary>
        /// Fills the identity provider's login form with the given credentials and submits it,
        /// following redirects.
        /// </summary>
        /// <param name="loginFormResponse">The response carrying the login form.</param>
        /// <param name="username">The username.</param>
        /// <param name="password">The password.</param>
        /// <returns>The final response of the submission.</returns>
        public async Task<HttpResponseMessage> SubmitLoginFormAsync(
            HttpResponseMessage loginFormResponse,
            string username,
            string password)
        {
            string html = await loginFormResponse.Content.ReadAsStringAsync();
            Uri pageUri = loginFormResponse.RequestMessage!.RequestUri!;
            Dictionary<string, string> fields = ParseInputFields(html);
            fields["username"] = username;
            fields["password"] = password;

            using HttpRequestMessage request = new(HttpMethod.Post, ResolveFormAction(html, pageUri))
            {
                Content = new FormUrlEncodedContent(fields),
            };

            return await SendAsync(request);
        }

        /// <summary>
        /// Auto-submits an HTML form exactly as a browser would submit the <c>form_post</c> callback
        /// page: every input field, POSTed to the form's action, following redirects.
        /// </summary>
        /// <param name="formResponse">The response carrying the form.</param>
        /// <returns>The final response of the submission.</returns>
        public async Task<HttpResponseMessage> SubmitFormAsync(HttpResponseMessage formResponse)
        {
            string html = await formResponse.Content.ReadAsStringAsync();
            Uri pageUri = formResponse.RequestMessage!.RequestUri!;

            using HttpRequestMessage request = new(HttpMethod.Post, ResolveFormAction(html, pageUri))
            {
                Content = new FormUrlEncodedContent(ParseInputFields(html)),
            };

            return await SendAsync(request);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (HttpResponseMessage response in _responses)
            {
                response.Dispose();
            }

            foreach (HttpMessageInvoker invoker in _hostsByAuthority.Values)
            {
                invoker.Dispose();
            }
        }

        private static bool IsRedirect(HttpStatusCode statusCode) =>
            statusCode is HttpStatusCode.Found or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect;

        /// <summary>
        /// Extracts the form's action attribute and resolves it against the page the form came from.
        /// </summary>
        /// <param name="html">The page markup.</param>
        /// <param name="pageUri">The page's own URI.</param>
        /// <returns>The absolute action URI.</returns>
        internal static Uri ResolveFormAction(string html, Uri pageUri)
        {
            int formIndex = html.IndexOf("<form", StringComparison.OrdinalIgnoreCase);
            if (formIndex < 0)
            {
                throw new InvalidOperationException("The page carries no form to submit.");
            }

            string? action = ExtractAttribute(html, formIndex, "action");

            return action is null ? pageUri : new Uri(pageUri, action);
        }

        /// <summary>
        /// Collects every named input field of the page's first form, exactly as a browser serializes
        /// a form submission.
        /// </summary>
        /// <param name="html">The page markup.</param>
        /// <returns>The field values by name.</returns>
        internal static Dictionary<string, string> ParseInputFields(string html)
        {
            Dictionary<string, string> fields = new(StringComparer.Ordinal);
            int searchIndex = 0;

            while (true)
            {
                int inputIndex = html.IndexOf("<input", searchIndex, StringComparison.OrdinalIgnoreCase);
                if (inputIndex < 0)
                {
                    break;
                }

                string? name = ExtractAttribute(html, inputIndex, "name");
                if (name is not null)
                {
                    fields[name] = ExtractAttribute(html, inputIndex, "value") ?? string.Empty;
                }

                searchIndex = inputIndex + "<input".Length;
            }

            return fields;
        }

        /// <summary>
        /// Extracts one attribute value from the tag starting at the given index, handling both quote
        /// styles.
        /// </summary>
        /// <param name="html">The page markup.</param>
        /// <param name="tagIndex">The index the tag starts at.</param>
        /// <param name="attributeName">The attribute to extract.</param>
        /// <returns>The decoded value, or <see langword="null"/> when the tag lacks the attribute.</returns>
        private static string? ExtractAttribute(string html, int tagIndex, string attributeName)
        {
            int tagEnd = html.IndexOf('>', tagIndex);
            if (tagEnd < 0)
            {
                tagEnd = html.Length;
            }

            int attributeIndex = html.IndexOf(attributeName + "=", tagIndex, StringComparison.OrdinalIgnoreCase);
            if (attributeIndex < 0 || attributeIndex > tagEnd)
            {
                return null;
            }

            char quote = html[attributeIndex + attributeName.Length + 1];
            if (quote is not '"' and not '\'')
            {
                return null;
            }

            int valueStart = attributeIndex + attributeName.Length + 2;
            int valueEnd = html.IndexOf(quote, valueStart);

            return WebUtility.HtmlDecode(html[valueStart..valueEnd]);
        }

        /// <summary>
        /// Sends one request with the jar's cookies for its URI and records the response and any
        /// cookies it sets.
        /// </summary>
        /// <param name="request">The request to send.</param>
        /// <returns>The response.</returns>
        private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request)
        {
            Uri uri = request.RequestUri
                ?? throw new InvalidOperationException("The request must carry an absolute URI.");

            string cookieHeader = _cookies.GetCookieHeader(uri);
            if (cookieHeader.Length > 0)
            {
                request.Headers.Add("Cookie", cookieHeader);
            }

            if (!_hostsByAuthority.TryGetValue(uri.Authority, out HttpMessageInvoker? invoker))
            {
                throw new InvalidOperationException(
                    $"No in-process host is registered for authority '{uri.Authority}'.");
            }

            HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None);
            response.RequestMessage ??= request;

            if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? setCookies))
            {
                foreach (string value in setCookies)
                {
                    _cookies.SetCookies(uri, value);
                }
            }

            _responses.Add(response);

            return response;
        }
    }
}
