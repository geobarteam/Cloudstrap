namespace Cloudstrap.WebApi.Tests
{
    using System.Net;
    using System.Security.Claims;
    using System.Text.Encodings.Web;
    using Cloudstrap.WebApi.Tests.Infrastructure;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.AspNetCore.TestHost;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using NUnit.Framework;

    /// <summary>
    /// AC-W9 and AC-W10: registering the bearer secures every endpoint by default — minimal APIs included —
    /// while probes and documentation stay reachable and both documented opt-outs work; and an application
    /// that never registers it behaves exactly as it did before.
    /// </summary>
    [TestFixture]
    public sealed class AuthorizationPostureTests
    {
        [Test]
        public async Task WithBearerRegistered_UnauthenticatedRequestToAPlainEndpoint_Returns401()
        {
            // Arrange — the action carries no [Authorize]; the fallback policy is what challenges
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Get(app, "/api/guarded");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        [Test]
        public async Task WithBearerRegistered_UnauthenticatedRequestToAnAllowAnonymousEndpoint_Returns200()
        {
            // Arrange — this also pins middleware placement: authorization must run after routing, or the
            // [AllowAnonymous] metadata would be invisible and the fallback policy would challenge anyway
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Get(app, "/api/guarded/open");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task WithBearerRegistered_ValidToken_ReachesThePlainEndpoint()
        {
            // Arrange — the policy is satisfiable, not merely blocking
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage response = await Get(app, "/api/guarded", TestTokens.Issue());

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task WithRequireAuthenticatedEndpointsFalse_BothEndpointsAreAnonymous()
        {
            // Arrange — the global opt-out
            await using WebApplication app = await StartWithBearer(
                new Dictionary<string, string?>
                {
                    ["Cloudstrap:JwtBearer:RequireAuthenticatedEndpoints"] = "false",
                });

            // Act
            using HttpResponseMessage plain = await Get(app, "/api/guarded");
            using HttpResponseMessage anonymous = await Get(app, "/api/guarded/open");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(plain.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public async Task FallbackPolicy_AlsoCoversMinimalApiEndpoints()
        {
            // Arrange — the policy is not a controller-only convention
            await using WebApplication app = await StartWithBearer(
                pipeline: hooks => hooks.ConfigureEndpoints = endpoints =>
                    endpoints.MapGet("/minimal", () => "minimal"));

            // Act
            using HttpResponseMessage anonymous = await Get(app, "/minimal");
            using HttpResponseMessage authenticated = await Get(app, "/minimal", TestTokens.Issue());

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(authenticated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public async Task HealthProbes_StayAnonymous_UnderTheFallbackPolicy()
        {
            // Arrange
            await using WebApplication app = await StartWithBearer();

            // Act
            using HttpResponseMessage liveness = await Get(app, "/healthz");
            using HttpResponseMessage readiness = await Get(app, "/ready");

            // Assert — an orchestrator holds no token and must still reach them
            Assert.Multiple(() =>
            {
                Assert.That(liveness.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(readiness.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }

        [Test]
        public async Task OpenApiDocumentAndScalarUi_StayReachable_UnderTheFallbackPolicy()
        {
            // Arrange
            await using WebApplication app = await StartWithBearer(environment: "Development");

            // Act
            using HttpResponseMessage document = await Get(app, "/openapi/v1.json");
            using HttpResponseMessage ui = await Get(app, "/scalar");

            // Assert — the reference UI must not 401 before a reader can enter a token
            Assert.Multiple(() =>
            {
                Assert.That(document.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(ui.StatusCode, Is.EqualTo(HttpStatusCode.Found));
            });
        }

        [Test]
        public async Task WithoutBearerRegistered_EverythingIsAnonymousAndNothingFails()
        {
            // Arrange — the SUT's mode: no AddCloudstrapJwtBearer anywhere
            await using WebApplication app = await WebApiTestHost.StartAsync();

            // Act
            using HttpResponseMessage plain = await Get(app, "/api/guarded");
            using HttpResponseMessage anonymous = await Get(app, "/api/guarded/open");
            using HttpResponseMessage probe = await Get(app, "/healthz");

            // Assert
            Assert.Multiple(() =>
            {
                Assert.That(plain.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(probe.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(
                    app.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value.SchemeMap,
                    Is.Empty,
                    "No authentication scheme should be registered when the bearer was never added.");
            });
        }

        [Test]
        public async Task WithConsumerRegisteredAuthenticationScheme_ThePipelineStillWiresTheMiddleware()
        {
            // Arrange — a consumer bringing their own scheme instead of Cloudstrap's bearer
            await using WebApplication app = await WebApiTestHost.StartAsync(
                beforeBuild: builder => builder.Services
                    .AddAuthentication(StubAuthenticationHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, StubAuthenticationHandler>(
                        StubAuthenticationHandler.SchemeName,
                        configureOptions: null));

            // Act — an [Authorize] endpoint proves the middleware ran: without it the framework would fail
            // the request for carrying authorization metadata no middleware honoured
            using HttpResponseMessage response = await Get(app, "/api/guarded/attributed");

            // Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task WithBearerRegistered_HooksStillRunInTheDocumentedOrder()
        {
            // Arrange — BeforeAuthorization sits between the two auth middlewares
            await using WebApplication app = await StartWithBearer(
                pipeline: hooks =>
                {
                    hooks.BeforeAuthorization = branch => branch.Use(async (context, next) =>
                    {
                        context.Response.Headers["X-Before-Authorization"] =
                            context.User.Identity?.IsAuthenticated == true ? "authenticated" : "anonymous";
                        await next(context);
                    });
                });

            // Act
            using HttpResponseMessage response = await Get(app, "/api/guarded", TestTokens.Issue());

            // Assert — authentication has already run when the hook executes
            Assert.That(
                response.Headers.GetValues("X-Before-Authorization").Single(),
                Is.EqualTo("authenticated"));
        }

        private static async Task<WebApplication> StartWithBearer(
            IDictionary<string, string?>? configuration = null,
            string environment = "Production",
            Action<WebApiPipelineOptions>? pipeline = null)
        {
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Cloudstrap:JwtBearer:Authority"] = TestTokens.Issuer,
                ["Cloudstrap:JwtBearer:Audience"] = TestTokens.Audience,
            };

            if (configuration is not null)
            {
                foreach (KeyValuePair<string, string?> entry in configuration)
                {
                    values[entry.Key] = entry.Value;
                }
            }

            return await WebApiTestHost.StartAsync(
                values,
                pipeline: pipeline,
                environment: environment,
                beforeBuild: builder => builder.AddCloudstrapJwtBearer(TestTokens.Validation()));
        }

        private static async Task<HttpResponseMessage> Get(
            WebApplication app,
            string path,
            string? token = null)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(path, UriKind.Relative));

            if (token is not null)
            {
                request.Headers.Add("Authorization", TestTokens.BearerHeader(token));
            }

            return await app.GetTestClient().SendAsync(request, TestContext.CurrentContext.CancellationToken);
        }

        /// <summary>An always-succeeding scheme standing in for a consumer's own authentication.</summary>
        private sealed class StubAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
        {
            public const string SchemeName = "ContosoStub";

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                ClaimsPrincipal principal = new(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "contoso-user")],
                    SchemeName));

                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(principal, SchemeName)));
            }
        }
    }
}
