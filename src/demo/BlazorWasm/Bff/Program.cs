using Cloudstrap.Authentication.ClientCredentials;
using Cloudstrap.Authentication.OpenIdConnect;
using Cloudstrap.Core;
using Cloudstrap.Extensions;
using Cloudstrap.Observability;
using Cloudstrap.Observability.AzureMonitor;
using Cloudstrap.WasmTestProject.Host.Bff.Services;
using Cloudstrap.WebApi;
using Microsoft.Extensions.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Fail fast: an invalid 'Cloudstrap' section aborts startup before the host is built
// (deliverable #1 demo — see the Startup_MissingSystemName E2E test).
CloudstrapOptions cloudstrapOptions = builder.Configuration.GetCloudstrapOptions();

// Bootstrap logging covers the window before the host's own pipeline exists (deliverable #2 demo).
using ILoggerFactory bootstrapLoggers = CloudstrapBootstrapLogger.Create(cloudstrapOptions);
ILogger startupLogger = bootstrapLoggers.CreateLogger("Cloudstrap.WasmTestProject.Startup");
string workloadName = cloudstrapOptions.Application.WorkloadName;
startupLogger.LogInformation("Configuration loaded for {WorkloadName}", workloadName);

// Owner-mode observability: Serilog host logging plus the OTel pipeline selected by
// Cloudstrap:OpenTelemetry (AzureMonitor mode here); correlation services and IBusinessTrace included.
// AddAzureMonitor is unconditional — the mode decides whether it contributes anything (deliverable #3 demo).
// Offline storage is off so a test run leaves no telemetry spool behind.
builder.UseCloudstrapObservability()
    .AddAzureMonitor(exporter => exporter.DisableOfflineStorage = true);

// One call replaces AddControllers and brings the whole Web API service side with it: versioning with
// per-version OpenAPI documents, the problem-details exception handler, HSTS/CORS registration and the
// health-check builder (deliverable #5 demo).
builder.AddCloudstrapWebApi();
builder.Services.AddSingleton<InMemoryDoctorStore>();

// #5's inbound JWT validation, demonstrated by #9: the Authority is the test identity provider the E2E
// fixture hosts on http://127.0.0.1:5310, and Cloudstrap:JwtBearer:RequireAuthenticatedEndpoints=false
// is the documented whole-application opt-out — the SUT stays anonymous except the endpoints that opt
// back in with [Authorize].
builder.AddCloudstrapJwtBearer();

// Machine-to-machine tokens (deliverable #9 demo): with Cloudstrap:HttpClients:SelfApi flagged
// AddClientAccessToken, this one call makes the SelfApi client transparently carry a cached, renewed
// bearer token acquired from the test identity provider — no other consumer change.
builder.Services.AddCloudstrapClientCredentials();

// Interactive user login (deliverable #10 demo): one call registers the hardened cookie session and
// the auth-code + PKCE challenge against the same test identity provider, coexisting with the JWT
// bearer scheme above (bearer callers still get 401s, browsers get a login page). The WASM
// auth-state UI arrives with deliverable #13.
// The configurator installs SUT-local challenge shaping (replaced by deliverable #13): callers not
// asking for text/html (API/XHR) get a bare 401 instead of a login redirect, while browser
// navigations keep redirecting — which is what auto-triggers login on /doctors. Only the one event
// property is assigned; the Events object stays Cloudstrap's (its sign-out wiring must survive).
builder.Services.AddCloudstrapOpenIdConnect(configure =>
    configure.OpenIdConnect = oidc =>
        oidc.Events.OnRedirectToIdentityProvider = context =>
        {
            if (!context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HandleResponse();
            }

            return Task.CompletedTask;
        });

// A typed client driven by Cloudstrap:HttpClients:SelfApi — config-bound base address and timeout, the
// correlation handler in its pipeline, and a readiness check probing the peer's /healthz. It calls back
// into this same app, so one process demonstrates a real outbound hop (deliverable #4 demo).
builder.Services.AddCloudstrapHttpServiceClient<ISelfApiClient, SelfApiClient>("SelfApi");

// The #10 user-token demo client: flagged both AddUserAccessToken and AddClientAccessToken — the
// signed-in user's token is the one that reaches the peer (AC-CC13 live in the running app).
builder.Services.AddCloudstrapHttpServiceClient<IUserApiClient, UserApiClient>("UserApi");
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: [CloudstrapHealthCheckTags.Liveness, CloudstrapHealthCheckTags.Readiness]);

WebApplication app = builder.Build();

// The entire request pipeline, in the order a hardened API needs it: problem-details error handling,
// security headers, routing, correlation (#2), controllers, the health probes (#4) and the OpenAPI
// documents plus the Scalar reference UI. The Blazor composition rides on the documented hook points, so
// static files still short-circuit before routing and the SPA fallback still catches unmatched paths.
app.UseCloudstrapWebApi(pipeline =>
{
    pipeline.BeforeRouting = branch => branch.UseBlazorFrameworkFiles().UseStaticFiles();
    pipeline.ConfigureEndpoints = endpoints =>
    {
        // The #10 opt-in login/logout endpoints (D-5), mapped before the SPA fallback so the
        // explicit routes win.
        endpoints.MapCloudstrapAuthenticationEndpoints();
        endpoints.MapFallbackToFile("index.html");
    };
});

await app.RunAsync();
