namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using System.Collections.Concurrent;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Logging;

    /// <summary>One request as the downstream echo server received it.</summary>
    /// <param name="Path">The request path, query string excluded.</param>
    /// <param name="Headers">Every request header, comma-joined per name, case-insensitive keys.</param>
    internal sealed record RecordedRequest(string Path, IReadOnlyDictionary<string, string> Headers);

    /// <summary>
    /// The downstream API of mechanic (h): an in-process Kestrel app on <c>127.0.0.1</c> with an ephemeral port
    /// (loopback only, no external network). It records every request it receives and answers 200 with a JSON echo
    /// of the path.
    /// </summary>
    internal sealed class EchoServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly ConcurrentQueue<RecordedRequest> _requests = new();

        private EchoServer(WebApplication app)
        {
            _app = app;
        }

        /// <summary>Gets the server's base address, for example <c>http://127.0.0.1:54321/</c>.</summary>
        public Uri Address { get; private set; } = null!;

        /// <summary>Gets the requests received so far, in arrival order.</summary>
        public IReadOnlyList<RecordedRequest> Requests => [.. _requests];

        /// <summary>Gets or sets the status code the echo answers with. Defaults to 200.</summary>
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        /// <summary>Starts an echo server on a free loopback port.</summary>
        /// <returns>The running server.</returns>
        public static async Task<EchoServer> StartAsync()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            WebApplication app = builder.Build();
            EchoServer server = new(app);
            app.Run(server.HandleAsync);

            await app.StartAsync();
            server.Address = new Uri(app.Urls.Single().TrimEnd('/') + "/");
            return server;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private async Task HandleAsync(HttpContext context)
        {
            Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in context.Request.Headers)
            {
                headers[header.Key] = header.Value.ToString();
            }

            string path = context.Request.Path.Value ?? string.Empty;
            _requests.Enqueue(new RecordedRequest(path, headers));
            context.Response.StatusCode = StatusCode;
            await context.Response.WriteAsJsonAsync(new { echo = "downstream", path });
        }
    }
}
