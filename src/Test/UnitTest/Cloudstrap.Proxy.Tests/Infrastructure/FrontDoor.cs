namespace Cloudstrap.Proxy.Tests.Infrastructure
{
    using System.Net.Http.Headers;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// The front-door host of mechanic (h): a <see cref="WebApplication"/> on <see cref="TestServer"/> with the
    /// <see cref="TestAuthenticationHandler"/> scheme, <c>UseAuthentication</c>, <c>UseAuthorization</c>, then
    /// <c>MapCloudstrapProxy</c>.
    /// </summary>
    internal sealed class FrontDoor : IAsyncDisposable
    {
        private FrontDoor(WebApplication app)
        {
            App = app;
            Client = app.GetTestServer().CreateClient();
        }

        /// <summary>Gets the running application.</summary>
        public WebApplication App
        {
            get;
        }

        /// <summary>Gets a client bound to the in-process server.</summary>
        public HttpClient Client
        {
            get;
        }

        /// <summary>
        /// Creates a front-door builder on <see cref="TestServer"/> with the test sign-in scheme and the given
        /// configuration. The caller registers the proxy (<c>AddCloudstrapProxy</c>) on it.
        /// </summary>
        /// <param name="settings">The in-memory configuration, for example from <see cref="ProxyConfig"/>.</param>
        /// <returns>The builder.</returns>
        public static WebApplicationBuilder CreateBuilder(IDictionary<string, string?> settings)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.Logging.ClearProviders();

            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            return builder;
        }

        /// <summary>Builds the app, maps the proxy behind authentication and authorization, and starts it.</summary>
        /// <param name="builder">A builder from <see cref="CreateBuilder"/> with the proxy registered.</param>
        /// <param name="beforeRouting">
        /// Optional middleware added before routing, for example <c>UsePathBase</c>.
        /// </param>
        /// <returns>The running front door.</returns>
        public static async Task<FrontDoor> StartAsync(WebApplicationBuilder builder, Action<WebApplication>? beforeRouting = null)
        {
            WebApplication app = builder.Build();
            beforeRouting?.Invoke(app);
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapCloudstrapProxy();

            try
            {
                await app.StartAsync();
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }

            return new FrontDoor(app);
        }

        /// <summary>Sends a GET as the given signed-in user, or anonymously.</summary>
        /// <param name="path">The request path.</param>
        /// <param name="user">The user name, or <see langword="null"/> for an anonymous request.</param>
        /// <returns>The front door's response.</returns>
        public Task<HttpResponseMessage> GetAsync(string path, string? user = "alice")
        {
            HttpRequestMessage request = new(HttpMethod.Get, path);
            if (user is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, user);
            }

            return Client.SendAsync(request);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}
