namespace Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure
{
    using System.Net;

    /// <summary>
    /// A typed service client contract, the neutral stand-in for a consumer's own interface.
    /// </summary>
    internal interface ICatalogClient
    {
        HttpClient Client
        {
            get;
        }
    }

    internal sealed class CatalogClient : ICatalogClient
    {
        public CatalogClient(HttpClient client)
        {
            Client = client;
        }

        public HttpClient Client
        {
            get;
        }
    }

    /// <summary>
    /// Terminates the handler chain and records what reached the wire. It answers 200 with the
    /// received bearer token as the body, so an application endpoint can relay exactly the token the
    /// peer saw and a test can decode it.
    /// </summary>
    internal sealed class CapturingPrimaryHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest
        {
            get; private set;
        }

        public int RequestCount
        {
            get; private set;
        }

        public List<string?> SeenBearerTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            RequestCount++;
            string? token = request.Headers.Authorization?.Parameter;
            SeenBearerTokens.Add(token);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(token ?? string.Empty),
            });
        }
    }

    /// <summary>
    /// Scripts the AC-OIDC renewal downstream: 401 for whatever bearer token it sees first, 200 for
    /// any renewed one — so exactly one forced renewal is the only way to a successful response.
    /// </summary>
    internal sealed class UnauthorizedForFirstTokenHandler : HttpMessageHandler
    {
        private string? _firstToken;

        public int RequestCount
        {
            get; private set;
        }

        public List<string?> SeenBearerTokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            string? token = request.Headers.Authorization?.Parameter;
            SeenBearerTokens.Add(token);
            _firstToken ??= token;

            return Task.FromResult(new HttpResponseMessage(
                string.Equals(token, _firstToken, StringComparison.Ordinal)
                    ? HttpStatusCode.Unauthorized
                    : HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// Captures the additional handlers a named client's pipeline is built from, after every builder
    /// action has run — the supported way to observe the materialized chain (ordering, duplication).
    /// </summary>
    internal sealed class HandlerChainCapture : Microsoft.Extensions.Http.IHttpMessageHandlerBuilderFilter
    {
        private readonly string _clientName;
        private List<Type> _handlerTypes = [];

        public HandlerChainCapture(string clientName)
        {
            _clientName = clientName;
        }

        public IReadOnlyList<Type> HandlerTypes => _handlerTypes;

        public Action<Microsoft.Extensions.Http.HttpMessageHandlerBuilder> Configure(
            Action<Microsoft.Extensions.Http.HttpMessageHandlerBuilder> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return builder =>
            {
                next(builder);

                if (string.Equals(builder.Name, _clientName, StringComparison.Ordinal))
                {
                    _handlerTypes = [.. builder.AdditionalHandlers.Select(handler => handler.GetType())];
                }
            };
        }
    }

    /// <summary>
    /// Wraps the identity provider's in-process handler with a kill switch, so a test can sign in
    /// normally and then make the provider unreachable for the refresh that follows.
    /// </summary>
    internal sealed class BreakableHandler : DelegatingHandler
    {
        public BreakableHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public bool Broken
        {
            get; set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Broken)
            {
                throw new HttpRequestException("Connection refused (test double).");
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A consumer handler added through <c>ConfigureHttpClientDefaults</c>: counts how often the inner
    /// chain below the token handler actually ran — the observable for "the 401 renewal re-executes
    /// the intact inner chain, neither duplicated nor bypassed" (AC-ASP3 posture).
    /// </summary>
    internal sealed class CountingMarkerHandler : DelegatingHandler
    {
        public int InvocationCount
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            InvocationCount++;

            return base.SendAsync(request, cancellationToken);
        }
    }
}
