# Cloudstrap.Observability

Serilog logging, a vendor-neutral OpenTelemetry pipeline (traces, metrics, logs), correlation and business
tracing for ASP.NET Core applications — one call, driven by the `Cloudstrap:` configuration section.

> **Runtime requirement**: this package carries a `Microsoft.AspNetCore.App` framework reference. Every
> consumer requires the ASP.NET Core shared framework at run time — `mcr.microsoft.com/dotnet/aspnet` base
> images work; `mcr.microsoft.com/dotnet/runtime`-only base images are **not** supported.

## Quick start

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.UseCloudstrapObservability();

var app = builder.Build();
app.UseRouting();
app.UseCloudstrapCorrelation();   // after routing, so endpoint metadata is visible
app.MapControllers();
app.Run();
```

```json
{
  "Cloudstrap": {
    "Application": {
      "SystemName": "Contoso",
      "SubsystemName": "Orders",
      "SubsystemType": "Api"
    },
    "OpenTelemetry": {
      "Mode": "Otlp",
      "Endpoint": "https://collector.example.com"
    },
    "Logging": {
      "Level": "Information"
    }
  }
}
```

Misconfiguration fails at the `UseCloudstrapObservability()` call with a `ConfigurationValidationException`
listing every violation — never at first use.

## Telemetry modes (`Cloudstrap:OpenTelemetry:Mode`)

| Mode | Behavior |
|------|----------|
| `Disabled` | No tracer/meter/log providers are registered. `IBusinessTrace` and correlation still resolve and work as safe no-ops. The default. |
| `Console` | Traces, metrics and logs export to the console — local development without a collector. |
| `Otlp` | Exports over OTLP. See endpoint resolution below. |
| `AzureMonitor` | Requires the `Cloudstrap.Observability.AzureMonitor` package to contribute the exporter. Without it, **host startup fails** with an actionable message — telemetry is never silently dropped. |

### OTLP endpoint resolution

- An explicit `Cloudstrap:OpenTelemetry:Endpoint` wins: Cloudstrap configures HTTP/protobuf with per-signal
  paths (`/v1/traces`, `/v1/metrics`, `/v1/logs`, preserving any base path) and formats
  `Cloudstrap:OpenTelemetry:Headers` into the exporter headers.
- No explicit endpoint but `OTEL_EXPORTER_OTLP_ENDPOINT` present: Cloudstrap sets nothing — endpoint,
  protocol and headers are the OpenTelemetry SDK's to resolve from the standard variables.
- Neither: startup fails validation, naming both `Endpoint` and `OTEL_EXPORTER_OTLP_ENDPOINT`.
- `ConfigureOtlpExporter` (code-level option) runs last per signal and has the final say.

## Owner vs. contribute (Aspire coexistence)

Cloudstrap coexists with platform defaults such as Aspire ServiceDefaults without depending on them:

- **Owner** (default): Cloudstrap stands up the whole pipeline — resource identity (`service.name` from the
  workload name, `deployment.environment.name`, `host.name`, plus `cloudstrap.system.name`,
  `cloudstrap.subsystem.name`, `cloudstrap.subsystem.type` and optional `cloudstrap.environment.tier`),
  ASP.NET Core / HTTP client / runtime / optional SQL client instrumentation, exporter selection per mode.
- **Contribute** (`options.PipelineMode = ObservabilityPipelineMode.Contribute`): the host owns the pipeline;
  Cloudstrap adds only its differentiated pieces — the `cloudstrap.*` resource attributes (no `service.name`
  takeover), the sampler chain and the trace noise filters. No instrumentation, no exporters, no duplicate
  spans, no Azure Monitor guard.
- **`ApplySampler` caveat**: OpenTelemetry's `SetSampler` is last-wins. When the host must own the sampler,
  set `ApplySampler = false` and Cloudstrap steps aside entirely.

### Trace noise defaults (every convention has an override)

Probe endpoints (from `Cloudstrap:HealthChecks`), `/_blazor`, `/_framework/`, `/_content/`, static-asset
extensions and configured `IgnoredPathSegments` are dropped from traces; Blazor Server component-hub spans
are sampled out unless `EnableBlazorHubTracing`. Filters compose with — never overwrite — a filter the host
already set. Switch the whole default filter off with `EnableDefaultTraceNoiseFilter = false`;
`AlwaysOnSampler` records every span for development diagnosis.

## Logging

- The Serilog provider is **added** to the host's logging — `ClearProviders` is never called, and providers
  you registered stay.
- The minimum level comes from `Cloudstrap:Logging:Level`; framework categories (`Microsoft.AspNetCore`,
  `Microsoft.AspNetCore.Hosting.Diagnostics`, `System.Net.Http.HttpClient`, `Microsoft.Hosting.Lifetime`)
  are seeded at `Warning`; `Cloudstrap:Logging:LevelOverrides` entries are applied last, so your overrides
  win — including over the seeds. `Level: None` writes nothing.
- File logging (`Cloudstrap:Logging:File`) writes daily-rolling `log-.log` files (10 MB size cap, 20 files
  retained, shared) **directly** under the configured `Path` — no subfolders, no machine-derived names.
- `ConfigureSerilog` (code-level option) runs last over the Serilog configuration and has the final say.

### Logging before the host exists

```csharp
CloudstrapOptions options = configuration.GetCloudstrapOptions();
using ILoggerFactory bootstrapLoggers = CloudstrapBootstrapLogger.Create(options);
bootstrapLoggers.CreateLogger("Contoso.Orders.Startup").LogInformation("Configuration loaded");
// dispose after Build() — the host's own logging takes over from there
```

The factory is independent of the host pipeline and never sets the global `Log.Logger`.

## Correlation

- Header convention: `X-Correlation-ID`, overridable via `Cloudstrap:Correlation:HeaderName`.
- `app.UseCloudstrapCorrelation()` (place it **after routing**) establishes the ambient id for every
  request: the inbound header value, or a generated one (the current trace id, else a GUID — override by
  registering your own `ICorrelationSource`).
- Read or set the id anywhere — with or without an `HttpContext` — through `ICorrelationContextAccessor`.
- Require correlation globally (`Cloudstrap:Correlation:Request:RequireForAllEndpoints`) or per endpoint
  (`[CorrelationRequired]`); a missing header then yields `400 application/problem+json` naming the header.
  Exemptions: configured `HealthEndpoints`/`ExcludeEndpoints` paths, health-check endpoint metadata, and
  `[AllowNoCorrelation]`. The 400 body is backed by the framework's problem-details services, which
  `AddCloudstrapCorrelation` registers additively.
- Outbound propagation: `.AddCloudstrapCorrelationHandler()` on any `IHttpClientBuilder` adds a set-if-absent
  delegating handler carrying the same header — safe under retries and double registration.

## Business tracing

```csharp
public sealed class OrderService(IBusinessTrace businessTrace)
{
    public void Submit(Order order)
    {
        using IBusinessTraceScope scope = businessTrace.StartSpan("SubmitOrder", nameof(OrderService));
        // ... domain work ...
        scope.SetOutcome("succeeded");
    }
}
```

Keep operation, component and outcome **low-cardinality** — kinds of work, never user or document
identifiers. Spans ride the pipeline in both owner and contribute modes (the `Cloudstrap.Business` source is
pre-wired; consumers owning their own pipeline can `AddSource(CloudstrapActivitySources.Business)`), and a
disabled pipeline makes every scope a safe no-op.

## Health-check tag vocabulary

`CloudstrapHealthCheckTags.Liveness` (`"live"`) and `CloudstrapHealthCheckTags.Readiness` (`"ready"`) are the
shared tags Cloudstrap hosting packages use to route checks to the liveness and readiness probes.
