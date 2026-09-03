using Cloudstrap.Core;
using Cloudstrap.Demo.Api.Data;
using Cloudstrap.Extensions;
using Cloudstrap.Messaging;
using Cloudstrap.Observability;
using Cloudstrap.WebApi;
using Microsoft.EntityFrameworkCore;
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

// The messaging node (#14): the SQL Server transport and durable store from Cloudstrap:Messaging
// (this host's workload queue, a workload-derived durability schema, the shared demo_transport
// queue schema), plus the transactional EF Core integration so OrdersController's row and its
// PlaceOrderCommand commit as one unit through IDbContextOutbox<DemoDbContext> (AC-MSG8 live).
// Wolverine's spans and metrics join the Console-mode pipeline above additively.
builder.AddCloudstrapMessaging()
    .UseSqlServer()
    .AddCloudstrapTransactionalMessaging<DemoDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: [CloudstrapHealthCheckTags.Liveness, CloudstrapHealthCheckTags.Readiness]);

WebApplication app = builder.Build();

// Demo-only: the LocalDB database and the demo.Orders table exist before the node starts and
// auto-provisions its own schemas (AutoProvision is on in Development). Production: IaC + migrations.
if (app.Environment.IsDevelopment())
{
    DemoDbContext.EnsureCreated(app.Services);
}

// The hardened pipeline with no hooks — a pure JSON API host: no static files, no SPA fallback.
app.UseCloudstrapWebApi();

await app.RunAsync();
