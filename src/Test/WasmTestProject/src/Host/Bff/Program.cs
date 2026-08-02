using Cloudstrap.Core;
using Cloudstrap.Extensions;
using Cloudstrap.Observability;
using Cloudstrap.Observability.AzureMonitor;
using Cloudstrap.Observability.Correlation;
using Cloudstrap.WasmTestProject.Host.Bff.Services;
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

builder.Services.AddControllers();
builder.Services.AddSingleton<InMemoryDoctorStore>();

// A typed client driven by Cloudstrap:HttpClients:SelfApi — config-bound base address and timeout, the
// correlation handler in its pipeline, and a readiness check probing the peer's /healthz. It calls back
// into this same app, so one process demonstrates a real outbound hop (deliverable #4 demo).
builder.Services.AddCloudstrapHttpServiceClient<ISelfApiClient, SelfApiClient>("SelfApi");
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: [CloudstrapHealthCheckTags.Liveness, CloudstrapHealthCheckTags.Readiness]);

WebApplication app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();
// After routing, so endpoint metadata (health checks, [AllowNoCorrelation]) is visible to it.
app.UseCloudstrapCorrelation();

app.MapControllers();
// One call replaces the hand-mapped, tag-filtered probe endpoints (deliverable #4 demo).
app.MapCloudstrapHealthChecks();
app.MapFallbackToFile("index.html");

await app.RunAsync();
