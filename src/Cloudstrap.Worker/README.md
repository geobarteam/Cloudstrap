# Cloudstrap.Worker

Worker-service bootstrap for the .NET **generic host**: one call gives a headless worker
validated Cloudstrap configuration (fail-fast at the call), correlation services, an additive
health-check builder, and container-probe HTTP endpoints — `/healthz` + `/ready` — that
**actually reflect the registered health checks**, served by a minimal internal Kestrel
side-host on a configurable port while the worker app itself gains no ASP.NET pipeline.

## Quick start

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Optional, explicit: KeyVault configuration — call it BEFORE AddCloudstrapWorker, because the
// worker's enabled decision reads configuration at the call (see “Configuration ordering”).
builder.AddCloudstrapKeyVault();

// Explicit sibling call: Serilog + the OTel pipeline — chain .AddAzureMonitor() if wanted, and
// pick owner/contribute mode here. Deliberately NOT bundled into AddCloudstrapWorker.
builder.UseCloudstrapObservability();

// This package: validated Cloudstrap options + correlation + the health listener.
builder.AddCloudstrapWorker();

builder.Services.AddHostedService<MyWorker>();
builder.Services.AddHealthChecks()
    .AddCheck("queue", () => /* your dependency check */ HealthCheckResult.Healthy(),
        tags: [CloudstrapHealthCheckTags.Readiness]);

await builder.Build().RunAsync();
```

For unattended services, keep fatal/exit logging alive after the host's own pipeline is gone —
the crash-flush pattern (guidance, deliberately not API):

```csharp
CloudstrapOptions cloudstrapOptions = builder.Configuration.GetCloudstrapOptions();
using ILoggerFactory bootstrapLoggers = CloudstrapBootstrapLogger.Create(cloudstrapOptions);
ILogger startupLogger = bootstrapLoggers.CreateLogger("Startup");
try
{
    await builder.Build().RunAsync();
    return 0;
}
catch (Exception exception)
{
    // The host's logging pipeline is disposed by RunAsync's failure path — the bootstrap
    // logger is what still flushes this line.
    startupLogger.LogCritical(exception, "Worker terminated unexpectedly");
    return 1;
}
```

## Settings

Owned by this package — `Cloudstrap:Worker`:

| Key | Default | Meaning |
|---|---|---|
| `HealthPort` | `9000` | The TCP port the health listener serves the probes on (1–65535). |
| `HealthListenAddress` | `"*"` | `"*"` binds all interfaces — the container reality (orchestrators reach the pod address, not loopback). Set `"localhost"` for local development. There is **no environment sniffing**: this option is the whole bind decision. |

Consumed, never redefined (owned elsewhere):

| Section | Owner | Used for |
|---|---|---|
| `Cloudstrap:HealthChecks` (`Enabled`, `LivenessPath` `/healthz`, `ReadinessPath` `/ready`) | Cloudstrap.Core | The kill switch and the probe paths — the worker's probes are the same probes every Cloudstrap web host serves. |
| `Cloudstrap:Logging` / `Cloudstrap:OpenTelemetry` / `Cloudstrap:Correlation` | Cloudstrap.Observability (the explicit `UseCloudstrapObservability()` sibling call) | Logging and telemetry. |
| `Cloudstrap:KeyVault` | Cloudstrap.Extensions (the explicit `AddCloudstrapKeyVault()` sibling call) | Configuration source. |

## Probe semantics

- Checks feed the probes through their **tags** (`CloudstrapHealthCheckTags.Liveness` = `live`,
  `.Readiness` = `ready`): a failing `ready`-tagged dependency check flips `/ready` to **503**
  while `/healthz` stays **200** — the instance leaves the load balancer without being
  restarted. **Untagged checks are served by neither probe.**
- Status mapping is the framework's: Healthy and **Degraded → 200** (orchestrators treat
  non-503 as passing; the body says `Degraded`), Unhealthy → 503. Zero matching checks → 200
  `Healthy`.
- The health port serves exactly the two probe endpoints; anything else is 404. Plain HTTP, no
  TLS, no authentication — the port is orchestrator-internal. A worker needing an authenticated
  HTTP surface is a web host: use `Cloudstrap.WebApi`.
- **A bind failure fails host startup naming the port** — a worker never runs silently unprobed.
- `Cloudstrap:HealthChecks:Enabled: false` registers no listener and never binds the port.

## Configuration ordering

`AddCloudstrapWorker` reads `Cloudstrap:HealthChecks:Enabled` **at the call**. Configuration
sources added afterwards (for example `AddCloudstrapKeyVault`) do not affect that decision —
add them first.

## Composability (Aspire and friends)

- Health checks are registered **additively on the stock `IHealthChecksBuilder`** — checks added
  before or after `AddCloudstrapWorker`, by you or by any other library, all feed the probes.
- This package adds **no telemetry exporter and no OTel pipeline** of its own. In a
  ServiceDefaults-style app keep your own pipeline and run `UseCloudstrapObservability` in
  **contribute** mode (`PipelineMode = ObservabilityPipelineMode.Contribute`).
- One probe owner: if your platform already hosts health endpoints for the worker, set
  `Cloudstrap:HealthChecks:Enabled: false` — Cloudstrap's probes or the platform's, not both.
- Zero `Aspire.*` references, zero external packages: three sibling Cloudstrap packages plus the
  ASP.NET Core shared framework (already required transitively by
  Cloudstrap.Observability/Extensions).

> **Not for web hosts.** A `WebApplication` already serves these probes on its real pipeline via
> `UseCloudstrapWebApi`/`UseCloudstrapMvc` (both use the same `MapCloudstrapHealthChecks`
> implementation). Running both would double-serve the probes on two ports.

## Migrating from the source library (`UseWorkerForNihdi`)

| Old | New |
|---|---|
| `UseWorkerForNihdi()` bundle (logging + KeyVault + health) | `UseCloudstrapObservability()` + `AddCloudstrapKeyVault()` + `AddCloudstrapWorker()` — explicit siblings |
| Health listener answered **200 unconditionally** | Probes evaluate the registered checks — readiness genuinely flips |
| Hard-coded port 9000, `HttpListener`, URL-ACL friction | Kestrel on `Cloudstrap:Worker:HealthPort` (default 9000), no ACL setup |
| `/health`, `/live`, `/ready` | The two standard paths from `Cloudstrap:HealthChecks` (`/healthz`, `/ready`), both configurable |
| `EnvironmentIsLocal()` loopback sniffing | Explicit `HealthListenAddress` option |
| Listener errors swallowed (worker ran unprobed) | Bind failure faults host startup naming the port |
| Config stash (`UseNihdiConfiguration`) | Dropped — resolve `IOptions<CloudstrapOptions>` |
