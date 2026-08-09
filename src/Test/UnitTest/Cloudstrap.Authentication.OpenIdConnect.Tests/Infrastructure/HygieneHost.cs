namespace Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure
{
    using System.Diagnostics;
    using System.Security.Claims;
    using Cloudstrap.Observability;
    using Cloudstrap.TestIdentityProvider;
    using Cloudstrap.WebApi;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Hosting.Server;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using NUnit.Framework;
    using OpenTelemetry.Trace;

    /// <summary>
    /// The AC-OIDC7 fixture: the full stack an interactive sign-in crosses in production — #2's
    /// observability pipeline exporting to an in-memory list, #5's problem-details handler in the
    /// pipeline, and Debug-level log capture — so all four output channels (logs, activities, response
    /// bodies, exception text) are inspectable in one run.
    /// </summary>
    internal sealed class HygieneHost : IAsyncDisposable
    {
        private HygieneHost(
            TestIdentityProviderHost identityProvider,
            WebApplication app,
            TestServer appServer,
            CapturingLoggerProvider logs,
            List<Activity> activities)
        {
            IdentityProvider = identityProvider;
            App = app;
            AppServer = appServer;
            Logs = logs;
            Activities = activities;
        }

        /// <summary>
        /// Gets the in-process identity provider.
        /// </summary>
        public TestIdentityProviderHost IdentityProvider
        {
            get;
        }

        /// <summary>
        /// Gets the application under test.
        /// </summary>
        public WebApplication App
        {
            get;
        }

        /// <summary>
        /// Gets the application's TestServer.
        /// </summary>
        public TestServer AppServer
        {
            get;
        }

        /// <summary>
        /// Gets every log entry the host wrote, captured at Debug level.
        /// </summary>
        public CapturingLoggerProvider Logs
        {
            get;
        }

        /// <summary>
        /// Gets every activity the observability pipeline exported.
        /// </summary>
        public List<Activity> Activities
        {
            get;
        }

        /// <summary>
        /// Starts the fixture.
        /// </summary>
        /// <returns>The started fixture.</returns>
        public static async Task<HygieneHost> StartAsync()
        {
            CapturingLoggerProvider logs = new();
            List<Activity> activities = [];

            TestIdentityProviderHost identityProvider = TestIdentityProviderHost.StartInProcess(
                static options =>
                {
                    options.Clients.Add(new TestIdentityProviderClient
                    {
                        ClientId = OidcTestHost.ClientId,
                        ClientSecret = OidcTestHost.ClientSecret,
                        Scopes = { "catalog.read" },
                        Audiences = { "contoso-api" },
                        RedirectUris = { new Uri(OidcTestHost.AppBase, "signin-oidc") },
                        PostLogoutRedirectUris = { new Uri(OidcTestHost.AppBase, "signout-callback-oidc") },
                    });
                    options.Users.Add(new TestIdentityProviderUser
                    {
                        Username = OidcTestHost.Username,
                        Password = OidcTestHost.Password,
                        Claims = { ["name"] = [OidcTestHost.UserDisplayName] },
                    });
                },
                baseAddress: OidcTestHost.IdpBase);

            try
            {
                WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
                {
                    EnvironmentName = "Production",
                    ApplicationName = "Cloudstrap.Authentication.OpenIdConnect.Tests",
                });
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cloudstrap:Application:SystemName"] = "Contoso",
                    ["Cloudstrap:Application:SubsystemName"] = "Catalog",
                    ["Cloudstrap:Application:SubsystemType"] = "Api",
                    ["Cloudstrap:OpenTelemetry:Mode"] = "Console",
                    ["Cloudstrap:OpenIdConnect:Authority"] = OidcTestHost.IdpBase.AbsoluteUri,
                    ["Cloudstrap:OpenIdConnect:ClientId"] = OidcTestHost.ClientId,
                    ["Cloudstrap:OpenIdConnect:ClientSecret"] = OidcTestHost.ClientSecret,
                    // Everything at Debug for the capture; the console sink stays quiet.
                    ["Logging:LogLevel:Default"] = "Debug",
                    ["Logging:Console:LogLevel:Default"] = "Warning",
                });
                builder.WebHost.UseTestServer();

                builder.UseCloudstrapObservability(options =>
                    options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(activities));
                builder.AddCloudstrapWebApi();
                builder.Services.AddCloudstrapOpenIdConnect(configurator =>
                    configurator.OpenIdConnect = oidc =>
                        oidc.BackchannelHttpHandler = identityProvider.CreateHandler());
                builder.Logging.AddProvider(logs);

                WebApplication app = builder.Build();
                app.UseCloudstrapWebApi(static pipeline => pipeline.ConfigureEndpoints = static endpoints =>
                {
                    endpoints.MapGet(
                        "/protected/page",
                        static (ClaimsPrincipal user) => Results.Text("hello " + user.Identity!.Name));
                    endpoints.MapGet("/protected/tokens", async (HttpContext context) =>
                    {
                        string? accessToken = await context.GetTokenAsync("access_token");
                        string? refreshToken = await context.GetTokenAsync("refresh_token");
                        string? idToken = await context.GetTokenAsync("id_token");

                        return Results.Json(new { accessToken, refreshToken, idToken });
                    });
                    endpoints.MapGet("/", static () => Results.Text("home")).AllowAnonymous();
                });

                await app.StartAsync(TestContext.CurrentContext.CancellationToken);

                TestServer appServer = (TestServer)app.Services.GetRequiredService<IServer>();
                appServer.BaseAddress = OidcTestHost.AppBase;

                return new HygieneHost(identityProvider, app, appServer, logs, activities);
            }
            catch
            {
                identityProvider.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a fresh browserless user agent against this host.
        /// </summary>
        /// <returns>The agent. The caller owns and disposes it.</returns>
        public BrowserlessUserAgent CreateAgent() =>
            new(
                OidcTestHost.AppBase,
                AppServer.CreateHandler(),
                OidcTestHost.IdpBase,
                IdentityProvider.CreateHandler());

        /// <summary>
        /// Flushes the tracing pipeline so <see cref="Activities"/> is complete.
        /// </summary>
        public void FlushTelemetry() =>
            App.Services.GetRequiredService<TracerProvider>().ForceFlush();

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            IdentityProvider.Dispose();
        }
    }
}
