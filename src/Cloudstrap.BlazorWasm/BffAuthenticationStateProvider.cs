namespace Cloudstrap.BlazorWasm
{
    using System.Net.Http.Json;
    using System.Security.Claims;
    using Microsoft.AspNetCore.Components.Authorization;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// The default BFF authentication state: fetches the configured user endpoint once through the
    /// named auth client, caches the result until <see cref="ClearAuthenticationState"/>, captures
    /// the XSRF token from the configured response header, and yields the anonymous principal on
    /// every failure mode — a browser client never throws over auth state.
    /// </summary>
    internal sealed class BffAuthenticationStateProvider
        : AuthenticationStateProvider, IBffAuthenticationStateProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IAntiforgeryTokenStore _tokenStore;
        private readonly CloudstrapBlazorWasmOptions _options;
        private readonly AuthenticationState _anonymousState;
        private bool _initialized;
        private AuthenticationState? _cachedState;

        public BffAuthenticationStateProvider(
            IHttpClientFactory httpClientFactory,
            IAntiforgeryTokenStore tokenStore,
            IOptions<CloudstrapBlazorWasmOptions> options)
        {
            ArgumentNullException.ThrowIfNull(httpClientFactory);
            ArgumentNullException.ThrowIfNull(tokenStore);
            ArgumentNullException.ThrowIfNull(options);

            _tokenStore = tokenStore;
            _options = options.Value;
            _httpClient = httpClientFactory.CreateClient(_options.AuthHttpClientName);
            _anonymousState = new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            if (_initialized && _cachedState is not null)
            {
                return _cachedState;
            }

            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(
                    new Uri(_options.UserEndpointPath, UriKind.Relative));
                response.EnsureSuccessStatusCode();

                // Capture the XSRF request token so CookieHandler can attach it on mutating calls.
                if (response.Headers.TryGetValues(_options.XsrfHeaderName, out IEnumerable<string>? xsrfValues))
                {
                    _tokenStore.Token = xsrfValues.FirstOrDefault();
                }

                UserInfo? userInfo = await response.Content.ReadFromJsonAsync<UserInfo>();

                if (userInfo is null || !userInfo.IsAuthenticated)
                {
                    return Cache(_anonymousState);
                }

                var claims = new List<Claim>();

                if (!string.IsNullOrEmpty(userInfo.UserName))
                {
                    claims.Add(new Claim(ClaimTypes.Name, userInfo.UserName));
                }

                if (userInfo.Claims is not null)
                {
                    foreach (ClaimDto claim in userInfo.Claims)
                    {
                        claims.Add(new Claim(claim.Type, claim.Value));
                    }
                }

                var identity = new ClaimsIdentity(claims, "BffCookie");

                return Cache(new AuthenticationState(new ClaimsPrincipal(identity)));
            }
            catch (HttpRequestException)
            {
                // Network error or server unavailable — anonymous, never a throw.
                return Cache(_anonymousState);
            }
        }

        public void ClearAuthenticationState()
        {
            _initialized = false;
            _cachedState = null;
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        private AuthenticationState Cache(AuthenticationState state)
        {
            _cachedState = state;
            _initialized = true;

            return state;
        }
    }
}
