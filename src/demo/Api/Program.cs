using Cloudstrap.Core;
using Cloudstrap.Extensions;
using Cloudstrap.Observability;
using Cloudstrap.WebApi;
using Microsoft.Extensions.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Fail fast: an invalid 'Cloudstrap' section aborts startup before the host is built (#1).
CloudstrapOptions cloudstrapOptions = builder.Configuration.GetCloudstrapOptions();

// Bootstrap logging covers the window before the host's own pipeline exists (#2).
using ILoggerFactory bootstrapLoggers = CloudstrapBootstrapLogger.Create(cloudstrapOptions);
ILogger startupLogger = bootstrapLoggers.CreateLogger("Cloudstrap.Demo.Startup");
startupLogger.LogInformation("Configuration loaded for {WorkloadName}", cloudstrapOptions.Application.WorkloadName);

// Console-mode observability (Cloudstrap:OpenTelemetry:Mode) — the E2E fixture captures this
// host's stdout, so its telemetry is assertable without any collector (#2).
builder.UseCloudstrapObservability();

// The whole Web API service side (#5): versioning, per-version OpenAPI + Scalar, problem details,
// HSTS/CORS registration and the health-check builder.
builder.AddCloudstrapWebApi();

// Inbound JWT validation (#5) with RequireAuthenticatedEndpoints left at its hardened `true`
// default — deliberately no config key and no [Authorize] attribute anywhere: every endpoint
// except the probe carve-out demands a validated token via the fallback policy.
builder.AddCloudstrapJwtBearer();

builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: [CloudstrapHealthCheckTags.Liveness, CloudstrapHealthCheckTags.Readiness]);

WebApplication app = builder.Build();

// The hardened pipeline with no hooks — a pure JSON API host: no static files, no SPA fallback.
app.UseCloudstrapWebApi();

await app.RunAsync();
