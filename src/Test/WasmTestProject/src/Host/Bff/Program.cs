using Cloudstrap.Core;
using Cloudstrap.Observability;
using Cloudstrap.Observability.Correlation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
// Cloudstrap:OpenTelemetry (Console mode here); correlation services and IBusinessTrace included.
builder.UseCloudstrapObservability();

builder.Services.AddControllers();
builder.Services.AddSingleton<Cloudstrap.WasmTestProject.Host.Bff.Services.InMemoryDoctorStore>();
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
app.MapHealthChecks(cloudstrapOptions.HealthChecks.LivenessPath, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(CloudstrapHealthCheckTags.Liveness),
});
app.MapHealthChecks(cloudstrapOptions.HealthChecks.ReadinessPath, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(CloudstrapHealthCheckTags.Readiness),
});
app.MapFallbackToFile("index.html");

await app.RunAsync();
