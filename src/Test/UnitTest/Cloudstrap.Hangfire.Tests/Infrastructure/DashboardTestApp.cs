namespace Cloudstrap.Hangfire.Tests.Infrastructure
{
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using Cloudstrap.Hangfire.Tests.Fakes;
    using Cloudstrap.Messaging.Tests.Infrastructure;
    using global::Hangfire;
    using global::Hangfire.Storage.Monitoring;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>The authentication the dashboard test app registers.</summary>
    internal enum DashboardTestScheme
    {
        /// <summary>No authentication scheme at all.</summary>
        None,

        /// <summary>The framework cookie scheme: an anonymous challenge redirects to its login path.</summary>
        Cookie,

        /// <summary>A bearer-style header scheme: challenge 401, forbid 403.</summary>
        Header,
    }

    /// <summary>
    /// A storage-only <see cref="WebApplication"/> on <see cref="TestServer"/> mapping the Cloudstrap dashboard over
    /// the fake storage (no SQL): <c>UseAuthentication</c>, <c>UseAuthorization</c>, then the mapping.
    /// </summary>
    internal sealed class DashboardTestApp : IAsyncDisposable
    {
        /// <summary>The header naming the authenticated user (<see cref="DashboardTestScheme.Header"/>).</summary>
        public const string UserHeader = "X-Test-User";

        /// <summary>The header listing the user's roles, comma-separated.</summary>
        public const string RolesHeader = "X-Test-Roles";

        /// <summary>The header carrying a <c>scope</c> claim value.</summary>
        public const string ScopeHeader = "X-Test-Scope";

        /// <summary>The consumer policy the tests reference: claim <c>scope=ops</c>.</summary>
        public const string OpsScopePolicy = "ops-scope";

        private DashboardTestApp(WebApplication app, CapturingLoggerProvider logs)
        {
            App = app;
            Logs = logs;
            Client = app.GetTestServer().CreateClient();
        }

        /// <summary>Gets the running application.</summary>
        public WebApplication App
        {
            get;
        }

        /// <summary>Gets a client that does not follow redirects.</summary>
        public HttpClient Client
        {
            get;
        }

        /// <summary>Gets the captured log entries.</summary>
        public CapturingLoggerProvider Logs
        {
            get;
        }

        /// <summary>Builds and starts the app.</summary>
        /// <param name="scheme">The authentication scheme to register.</param>
        /// <param name="settings">Extra configuration (for example <c>Cloudstrap:Hangfire:Dashboard:*</c>).</param>
        /// <param name="pathBase">A path base applied before routing, if any.</param>
        /// <param name="configure">The consumer's dashboard delegate, if any.</param>
        /// <param name="allowAnonymous">Whether the consumer bypasses endpoint authorization.</param>
        public static async Task<DashboardTestApp> StartAsync(
            DashboardTestScheme scheme = DashboardTestScheme.Header,
            IDictionary<string, string?>? settings = null,
            string? pathBase = null,
            Action<global::Hangfire.DashboardOptions>? configure = null,
            bool allowAnonymous = false)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseTestServer();
            Dictionary<string, string?> configuration = new()
            {
                ["Cloudstrap:Application:SystemName"] = "contoso",
                ["Cloudstrap:Application:SubsystemName"] = "ops",
                ["Cloudstrap:Application:SubsystemType"] = "web",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Cloudstrap.Hangfire"] = "Information",
            };
            foreach (KeyValuePair<string, string?> setting in settings ?? new Dictionary<string, string?>())
            {
                configuration[setting.Key] = setting.Value;
            }

            builder.Configuration.AddInMemoryCollection(configuration);
            CapturingLoggerProvider logs = new();
            builder.Logging.AddProvider(logs);

            FakeJobStorage storage = new();
            storage.Monitoring.Statistics = new StatisticsDto { Servers = 1, Recurring = 3 };
            builder.AddCloudstrapHangfire(hangfire =>
            {
                hangfire.RunServer = false;
                hangfire.Storage = global => global.UseStorage(storage);
            });

            switch (scheme)
            {
                case DashboardTestScheme.Cookie:
                    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
                    break;
                case DashboardTestScheme.Header:
                    builder.Services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
                    break;
                default:
                    break;
            }

            builder.Services.AddAuthorization(authorization =>
                authorization.AddPolicy(OpsScopePolicy, policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "ops")));

            WebApplication app = builder.Build();
            if (pathBase is not null)
            {
                app.UsePathBase(pathBase);
            }

            app.UseRouting();
            if (scheme != DashboardTestScheme.None)
            {
                app.UseAuthentication();
            }

            app.UseAuthorization();
            IEndpointConventionBuilder dashboard = app.MapCloudstrapHangfireDashboard(configure);
            if (allowAnonymous)
            {
                dashboard.AllowAnonymous();
            }

            await app.StartAsync();
            return new DashboardTestApp(app, logs);
        }

        /// <summary>Sends a GET as the given principal (header scheme), or anonymously.</summary>
        /// <param name="path">The request path.</param>
        /// <param name="user">The user name, or <see langword="null"/> for an anonymous request.</param>
        /// <param name="roles">The roles, comma-separated.</param>
        /// <param name="scope">A <c>scope</c> claim value.</param>
        /// <param name="headers">Extra request headers.</param>
        public Task<HttpResponseMessage> GetAsync(
            string path,
            string? user = null,
            string? roles = null,
            string? scope = null,
            IDictionary<string, string>? headers = null)
        {
            HttpRequestMessage request = new(HttpMethod.Get, path);
            if (user is not null)
            {
                request.Headers.Add(UserHeader, user);
            }

            if (roles is not null)
            {
                request.Headers.Add(RolesHeader, roles);
            }

            if (scope is not null)
            {
                request.Headers.Add(ScopeHeader, scope);
            }

            foreach (KeyValuePair<string, string> header in headers ?? new Dictionary<string, string>())
            {
                request.Headers.Add(header.Key, header.Value);
            }

            return Client.SendAsync(request);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            Logs.Dispose();
        }

        /// <summary>
        /// Authenticates from test headers: <see cref="UserHeader"/> names the user, <see cref="RolesHeader"/>
        /// lists <c>role</c> claims and <see cref="ScopeHeader"/> adds a <c>scope</c> claim. The base handler's
        /// challenge is 401 and its forbid is 403, like a bearer scheme.
        /// </summary>
        internal sealed class HeaderAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            public const string SchemeName = "TestHeader";

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                if (!Request.Headers.TryGetValue(UserHeader, out Microsoft.Extensions.Primitives.StringValues user))
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                }

                List<Claim> claims = [new Claim("name", user.ToString())];
                if (Request.Headers.TryGetValue(RolesHeader, out Microsoft.Extensions.Primitives.StringValues roles))
                {
                    claims.AddRange(roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(role => new Claim("role", role.Trim())));
                }

                if (Request.Headers.TryGetValue(ScopeHeader, out Microsoft.Extensions.Primitives.StringValues scope))
                {
                    claims.Add(new Claim("scope", scope.ToString()));
                }

                ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SchemeName, "name", "role"));
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
            }
        }
    }
}
