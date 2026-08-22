using Cloudstrap.Core;
using Cloudstrap.Demo.Worker;
using Cloudstrap.Observability;
using Cloudstrap.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Content root pinned to the binaries: a headless service is started with an arbitrary working
// directory (service manager, container, the E2E fixture), so appsettings.json is resolved next
// to the executable instead of wherever the process happened to start.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Fail fast: an invalid 'Cloudstrap' section aborts startup before the host is built (#1).
CloudstrapOptions cloudstrapOptions = builder.Configuration.GetCloudstrapOptions();

// The crash-flush pattern (guidance, deliberately not API): the bootstrap logger outlives the
// host's own logging pipeline, so fatal/exit paths still flush a line after RunAsync tears the
// OTel pipeline down.
using ILoggerFactory bootstrapLoggers = CloudstrapBootstrapLogger.Create(cloudstrapOptions);
ILogger startupLogger = bootstrapLoggers.CreateLogger("Cloudstrap.Demo.Startup");
startupLogger.LogInformation("Configuration loaded for {WorkloadName}", cloudstrapOptions.Application.WorkloadName);

// Console-mode observability (Cloudstrap:OpenTelemetry:Mode) — the E2E fixture captures this
// host's stdout, so its telemetry is assertable without any collector (#2).
builder.UseCloudstrapObservability();

// This package (#7): validated options + correlation + the health listener serving /healthz and
// /ready from the checks registered below, on Cloudstrap:Worker:HealthPort.
builder.AddCloudstrapWorker();

builder.Services.AddHostedService<PeriodicWorker>();
builder.Services.AddHealthChecks()
    .AddCheck(
        "self",
        () => HealthCheckResult.Healthy(),
        tags: [CloudstrapHealthCheckTags.Liveness])
    // The outage drill: a ready-tagged check flips /ready to 503 while the sentinel file exists
    // and /healthz stays 200 — the tag contract, drillable from outside the process.
    .AddCheck<DemoOutageHealthCheck>("demo-outage", tags: [CloudstrapHealthCheckTags.Readiness]);

try
{
    await builder.Build().RunAsync();
    return 0;
}
catch (Exception exception)
{
    // The host's logging pipeline is disposed by RunAsync's failure path — the bootstrap logger
    // is what still flushes this line (the D-5 crash-flush guidance, demonstrated).
    startupLogger.LogCritical(exception, "Worker terminated unexpectedly");
    return 1;
}
