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
| A real messaging node: consumes `PlaceOrderCommand` from its workload queue over SQL Server, a plain Wolverine handler made transactional by taking `WorkerDbContext`, the flowed correlation id recorded (#14) | `AddCloudstrapMessaging().UseSqlServer().AddCloudstrapTransactionalMessaging<WorkerDbContext>()` + `PlaceOrderCommandHandler` | `Messaging_OrderPlacedThroughTheApiOutbox_IsProcessedByTheWorker_WithTheCorrelationIdObserved` |
| Handled messages are logged by **type and id, never payload** (#14) | `PlaceOrderCommandHandler`'s log lines | `Messaging_WorkerLogsTheHandledCommandTypeAndId_NeverThePayload` |

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
- Since #14 this host is a **SQL Server messaging node** and needs SQL Server **LocalDB** at
  startup (`ConnectionStrings:DefaultConnection` → the `CloudstrapDemo` database shared with the
  Api demo host, created on first run in Development together with `demo.Orders`).
  `CLOUDSTRAP_TEST_SQL` overrides the connection string; the E2E `MessagingTests` fixture forwards
  it and boots a second instance of this host on health port **5351** (5350 belongs to
  `WorkerHostTests`).
- One database, several schemas (AC-MSG13 as a teaching point): this host's durability tables
  land in `demo_application_worker`, the Api's in `demo_application_api`, the queue tables both
  share in the explicitly configured `demo_transport` schema — a demo decision, not a package
  opinion. The node listens on its own workload queue (`demo-application-worker`, sanitized to
  `demo_application_worker` by the transport) with no listener configuration at all.

## Running

```powershell
dotnet run --project src/demo/Worker      # probes: http://127.0.0.1:5350/healthz + /ready
```

Needs LocalDB (see the harness notes); no other process. To see a message flow, run the demo
IdP and the Api too and post an order (see the Api README). The port comes from
`Cloudstrap:Worker:HealthPort` — not `ASPNETCORE_URLS`.
