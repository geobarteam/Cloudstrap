namespace Cloudstrap.Extensions.Tests
{
    using System.Net;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Http;

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
    /// A second contract, used where two clients must be told apart.
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// Captures the additional handlers a named client's pipeline is built from, after every builder action
    /// has run. Registering it as an <see cref="IHttpMessageHandlerBuilderFilter"/> is the supported way to
    /// observe the materialized chain.
    /// </summary>
    internal sealed class HandlerChainCapture : IHttpMessageHandlerBuilderFilter
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

        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
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
    /// Builds a minimal host around an in-memory <c>Cloudstrap</c> configuration — the fixture idiom shared by
    /// every test in this project.
    /// </summary>
    internal static class TestHostBuilder
    {
        public static HostApplicationBuilder Create(Dictionary<string, string?>? configValues = null)
        {
            HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Configuration.AddInMemoryCollection(configValues ?? []);

            return builder;
        }

        public static Dictionary<string, string?> CatalogSection(
            string baseAddress = "https://catalog.contoso.example/",
            string timeout = "00:00:05",
            string sectionName = "Catalog") => new()
            {
                [$"Cloudstrap:HttpClients:{sectionName}:BaseAddress"] = baseAddress,
                [$"Cloudstrap:HttpClients:{sectionName}:Timeout"] = timeout,
            };
    }
}
