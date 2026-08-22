namespace Cloudstrap.Authentication.OpenIdConnect.Tests.Infrastructure
{
    using System.Security.Claims;
    using Cloudstrap.TestIdentityProvider;
    using Cloudstrap.WebApi;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Hosting.Server;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    /// <summary>
    /// The AC-OIDC9 fixture: one application registering <em>both</em> #5's
    /// <c>AddCloudstrapJwtBearer</c> (its metadata backchannel routed to the in-process provider) and
    /// <c>AddCloudstrapOpenIdConnect</c>, in either registration order, with three endpoints — one on
    /// the default scheme, one pinned to <c>Bearer</c>, one anonymous.
    /// </summary>
    internal sealed class CoexistenceHost : IAsyncDisposable
    {
        public static readonly Uri AppBase = new("https://app.example.com/");

        private CoexistenceHost(TestIdentityProviderHost identityProvider, WebApplication app, TestServer appServer)
        {
            IdentityProvider = identityProvider;
            App = app;
            AppServer = appServer;
        }

        /// <summary>
        /// Gets the in-process identity provider serving both the machine and interactive clients.
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
        /// Starts the fixture.
        /// </summary>
        /// <param name="jwtBearerFirst">
        /// The registration order: <see langword="true"/> registers <c>AddCloudstrapJwtBearer</c>
        /// before <c>AddCloudstrapOpenIdConnect</c>; <see langword="false"/> the other way around
        /// (plan-level pick 4 — the outcome must be identical).
        /// </param>
        /// <returns>The started fixture.</returns>
        public static async Task<CoexistenceHost> StartAsync(bool jwtBearerFirst)
        {
            TestIdentityProviderHost identityProvider = TestIdentityProviderHost.StartInProcess(
                static options =>
                {
                    options.Clients.Add(new TestIdentityProviderClient
                    {
                        ClientId = OidcTestHost.ClientId,
                        ClientSecret = OidcTestHost.ClientSecret,
                        Scopes = { "catalog.read" },
                        Audiences = { "contoso-api" },
                        RedirectUris = { new Uri(AppBase, "signin-oidc") },
                        PostLogoutRedirectUris = { new Uri(AppBase, "signout-callback-oidc") },
                    });
                    options.Clients.Add(new TestIdentityProviderClient
                    {
                        ClientId = "contoso-service",
                        ClientSecret = "placeholder-not-a-real-secret",
                        Scopes = { "catalog.read" },
                        Audiences = { "contoso-api" },
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
                    ["Cloudstrap:JwtBearer:Authority"] = OidcTestHost.IdpBase.AbsoluteUri,
                    ["Cloudstrap:JwtBearer:Audience"] = "contoso-api",
                    ["Cloudstrap:OpenIdConnect:Authority"] = OidcTestHost.IdpBase.AbsoluteUri,
                    ["Cloudstrap:OpenIdConnect:ClientId"] = OidcTestHost.ClientId,
                    ["Cloudstrap:OpenIdConnect:ClientSecret"] = OidcTestHost.ClientSecret,
                    ["Logging:LogLevel:Default"] = "Warning",
                });
                builder.WebHost.UseTestServer();

                builder.AddCloudstrapWebApi();

                void RegisterJwtBearer() => builder.AddCloudstrapJwtBearer(bearer =>
                    bearer.BackchannelHttpHandler = identityProvider.CreateHandler());
                void RegisterOpenIdConnect() => builder.Services.AddCloudstrapOpenIdConnect(configurator =>
                    configurator.OpenIdConnect = oidc =>
                        oidc.BackchannelHttpHandler = identityProvider.CreateHandler());

                if (jwtBearerFirst)
                {
                    RegisterJwtBearer();
                    RegisterOpenIdConnect();
                }
                else
                {
                    RegisterOpenIdConnect();
                    RegisterJwtBearer();
                }

                WebApplication app = builder.Build();
                app.UseCloudstrapWebApi(static pipeline => pipeline.ConfigureEndpoints = static endpoints =>
                {
                    endpoints.MapGet(
                            "/coexist/default",
                            static (ClaimsPrincipal user) => Results.Text("default:" + user.Identity!.Name))
                        .RequireAuthorization();
                    endpoints.MapGet("/coexist/bearer", static () => Results.Text("bearer"))
                        .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Bearer" });
                    endpoints.MapGet("/coexist/anonymous", static () => Results.Text("anonymous"))
                        .AllowAnonymous();
                });

                await app.StartAsync(TestContext.CurrentContext.CancellationToken);

                TestServer appServer = (TestServer)app.Services.GetRequiredService<IServer>();
                appServer.BaseAddress = AppBase;

                return new CoexistenceHost(identityProvider, app, appServer);
            }
            catch
            {
                identityProvider.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Creates a plain client against the application — no cookie jar; coexistence assertions are
        /// about single requests.
        /// </summary>
        /// <returns>The client. The caller owns and disposes it.</returns>
        public HttpClient CreateClient() =>
            new(AppServer.CreateHandler())
            {
                BaseAddress = AppBase,
            };

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            IdentityProvider.Dispose();
        }
    }
}
