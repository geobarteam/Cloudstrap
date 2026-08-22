namespace Cloudstrap.Authentication.ClientCredentials.Tests.Infrastructure
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

    /// <summary>
    /// A second contract, used where a flagged and an unflagged client must be told apart.
    /// </summary>
    internal interface IOrdersClient
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

    internal sealed class OrdersClient : IOrdersClient
    {
        public OrdersClient(HttpClient client)
        {
            Client = client;
        }

        public HttpClient Client
        {
            get;
        }
    }

    /// <summary>
    /// Terminates the handler chain and records what reached the wire, so header assertions observe the
    /// fully materialized pipeline rather than a registration list.
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
            SeenBearerTokens.Add(request.Headers.Authorization?.Parameter);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// Scripts the AC-CC9 downstream: 401 for whatever bearer token it sees first, 200 for any renewed
    /// one — so exactly one token refresh is the only way to a successful response.
    /// </summary>
    internal sealed class UnauthorizedForFirstTokenHandler : HttpMessageHandler
    {
        private string? _firstToken;

        public int RequestCount
        {
            get; private set;
        }

        public List<bool> SawCorrelationHeader { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            SawCorrelationHeader.Add(request.Headers.Contains("X-Correlation-ID"));
            string? token = request.Headers.Authorization?.Parameter;
            _firstToken ??= token;

            return Task.FromResult(new HttpResponseMessage(
                string.Equals(token, _firstToken, StringComparison.Ordinal)
                    ? HttpStatusCode.Unauthorized
                    : HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// A consumer-added marker that counts how often the inner chain below the token handler actually
    /// ran — the observable for "the 401 refresh re-executes the inner chain".
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

        public int ResilienceHandlerCount => _handlerTypes.Count(
            type => type.Namespace?.StartsWith("Microsoft.Extensions.Http.Resilience", StringComparison.Ordinal) == true);

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
    /// A backchannel double answering every token request with a fixed status code and no body — the
    /// failing-IdP arm of AC-CC8.
    /// </summary>
    internal sealed class StatusCodeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StatusCodeHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        public int RequestCount
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    /// <summary>
    /// A backchannel double that cannot be reached at all — the unreachable-IdP arm of AC-CC8.
    /// </summary>
    internal sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused (test double).");
    }

    /// <summary>
    /// A backchannel double standing in for a token endpoint that supports client assertions: captures
    /// the submitted form and answers with a placeholder token — teaching the test identity provider
    /// <c>private_key_jwt</c> belongs to no deliverable; the assertion-carrying request is the
    /// observable (AC-CC11).
    /// </summary>
    internal sealed class FormCapturingTokenEndpointHandler : HttpMessageHandler
    {
        public Dictionary<string, string> LastForm { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            LastForm = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .ToDictionary(
                    parts => Uri.UnescapeDataString(parts[0]),
                    parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty,
                    StringComparer.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"placeholder-access-token","token_type":"Bearer","expires_in":3600}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    /// <summary>
    /// A consumer-registered client assertion service: records every invocation and supplies a
    /// placeholder signed-JWT assertion (AC-CC11, D-1).
    /// </summary>
    internal sealed class RecordingClientAssertionService : Duende.AccessTokenManagement.IClientAssertionService
    {
        public int InvocationCount
        {
            get; private set;
        }

        public Task<Duende.IdentityModel.Client.ClientAssertion?> GetClientAssertionAsync(
            Duende.AccessTokenManagement.ClientCredentialsClientName? clientName = null,
            Duende.AccessTokenManagement.TokenRequestParameters? parameters = null,
            CancellationToken ct = default)
        {
            InvocationCount++;

            return Task.FromResult<Duende.IdentityModel.Client.ClientAssertion?>(
                new Duende.IdentityModel.Client.ClientAssertion
                {
                    Type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                    Value = "placeholder-client-assertion-jwt",
                });
        }
    }

    /// <summary>
    /// An application-registered <c>IDistributedCache</c> that records every write — the observable for
    /// "nothing token-shaped ever reaches the application's caches" (AC-CC12).
    /// </summary>
    internal sealed class RecordingDistributedCache : Microsoft.Extensions.Caching.Distributed.IDistributedCache
    {
        public int WriteCount
        {
            get; private set;
        }

        public List<string> WrittenKeys { get; } = [];

        public byte[]? Get(string key) => null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult<byte[]?>(null);

        public void Set(string key, byte[] value, Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options)
        {
            WriteCount++;
            WrittenKeys.Add(key);
        }

        public Task SetAsync(
            string key,
            byte[] value,
            Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);

            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
        }

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }

    /// <summary>
    /// A pass-through primary handler capturing what goes to the wire before forwarding to a real
    /// destination — used to observe the token backchannel's requests.
    /// </summary>
    internal sealed class CapturingPassThroughHandler : DelegatingHandler
    {
        public CapturingPassThroughHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public HttpRequestMessage? LastRequest
        {
            get; private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A consumer marker attached through the backchannel hook: stamps every request it sees, so the
    /// hook's reach is observable.
    /// </summary>
    internal sealed class BackchannelMarkerHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Headers.Add("X-Backchannel-Mark", "on");

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Captures every log entry the host writes, so warning assertions can count exact occurrences.
    /// </summary>
    internal sealed class CapturingLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> _entries = [];

        public IReadOnlyCollection<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries => _entries;

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<(Microsoft.Extensions.Logging.LogLevel, string)> _entries;

            public CapturingLogger(
                System.Collections.Concurrent.ConcurrentQueue<(Microsoft.Extensions.Logging.LogLevel, string)> entries)
            {
                _entries = entries;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

            public void Log<TState>(
                Microsoft.Extensions.Logging.LogLevel logLevel,
                Microsoft.Extensions.Logging.EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);
                _entries.Enqueue((logLevel, formatter(state, exception)));
            }
        }
    }
}
