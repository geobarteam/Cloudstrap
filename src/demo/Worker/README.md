# Cloudstrap.Demo.Worker

A **headless worker** on the .NET generic host — the `Cloudstrap.Worker` README's consumer
example, live: a five-call bootstrap (fail-fast options, the crash-flush bootstrap logger,
Console-mode observability, `AddCloudstrapWorker()`, one `BackgroundService`) whose container
probes on **http://127.0.0.1:5350** genuinely reflect the registered health checks. No IdP, no
peer host — it runs alone.

## Feature matrix

| Feature | The Cloudstrap call | Proven by (E2E) |
|---|---|---|
| Container probes on a generic host — anonymous, framework bodies, exactly two endpoints (#7) | `AddCloudstrapWorker()` (port from `Cloudstrap:Worker:HealthPort`) | `WorkerHost_Probes_AnswerAnonymouslyWithFrameworkBodies` |
| A real `BackgroundService` runs while probing; Console-mode telemetry is live and capturable (#2/#7) | `UseCloudstrapObservability()` + `AddHostedService<PeriodicWorker>()` | `WorkerHost_Heartbeat_AndStartupLog_AreCapturedFromStdout` |
| The outage drill: a ready-tagged check flips `/ready` to 503 while `/healthz` stays 200, and recovery follows (#7) | consumer `AddCheck<DemoOutageHealthCheck>` tagged `Readiness` (sentinel file at `Demo:OutageSentinelPath`) | `WorkerHost_ReadyFlipsTo503WhileTheOutageSentinelExists_AndRecovers` |
| Probe polling produces no trace spans — #2's noise filter covers the worker listener for free (#2/#7) | shared `Cloudstrap:HealthChecks` paths | `WorkerHost_ProbePolling_ProducesNoTraceSpans` |

## Harness notes

- `appsettings.json` sets `HealthListenAddress: "localhost"` — the **documented dev-time
  override** (no firewall prompts on dev machines and CI agents). The shipped default `"*"` is
  the all-interfaces container posture.
- The content root is pinned to `AppContext.BaseDirectory` in `Program.cs` — a headless service
  is started with an arbitrary working directory (service manager, container, the E2E fixture),
  so settings resolve next to the executable.
- The E2E fixture boots this host by project path with `--Cloudstrap:Worker:HealthPort=5350`
  (a generic host ignores `ASPNETCORE_URLS`).
- `Program.cs` demonstrates the **crash-flush pattern** (D-5 guidance, not API): the bootstrap
  logger outlives the host pipeline so fatal/exit paths still flush.

## Running

```powershell
dotnet run --project src/demo/Worker      # probes: http://127.0.0.1:5350/healthz + /ready
```

No other process needed. The port comes from `Cloudstrap:Worker:HealthPort` — not
`ASPNETCORE_URLS`.
