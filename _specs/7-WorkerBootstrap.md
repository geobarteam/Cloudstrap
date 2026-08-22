# Spec: Worker Bootstrap — `Cloudstrap.Worker` (Roadmap Deliverable #7)

> **Approved 2026-08-20 — zero Open Questions remain; spec is planner-ready.** All five gate questions were resolved by the user per this spec's recommendations (see the Decision Log at the end): the minimal Kestrel side-host reusing `MapCloudstrapHealthChecks` (D-1), the single unbundled `AddCloudstrapWorker` entry point (D-2), the two-knob `Cloudstrap:Worker` section (D-3), the `Cloudstrap.Demo.Worker` demo vehicle on health port 5350 (D-4), and crash-flush as guidance-only (D-5).
>
> Sources: `_plans/ROADMAP.md` §7 (hand-off brief, file inventory verified 2026-08-20, eleventh pass) · `_specs/Cloudstrap.md` (Package Map line 82 "Health listener port becomes configurable (default 9000)", De-NIHDI row 115 probe paths → `/healthz` + `/ready`, Aspire Coexistence AC-ASP1–AC-ASP3, hosting posture) · `_specs/5-WebApiBootstrap.md` / `_specs/6-MvcBootstrap.md` (composite-pattern precedent) · `_specs/2-ObservabilityBase.md` (the `DeferredLoggerFactory` Drop + `CloudstrapBootstrapLogger` replacement this spec leans on) · **shipped** code read in full: `src/Cloudstrap.Observability/HostApplicationBuilderExtensions.cs` (`UseCloudstrapObservability` — already `IHostApplicationBuilder`-based, incl. Serilog/OTel switch, correlation, `IBusinessTrace`, owner/contribute modes), `CloudstrapHealthCheckTags.cs`, `CloudstrapBootstrapLogger.cs`, `TraceNoiseFilter.cs`, `src/Cloudstrap.Extensions/HostApplicationBuilderExtensions.cs` (`AddCloudstrapKeyVault` — idempotent, `Enabled`-gated), `EndpointRouteBuilderExtensions.cs` (`MapCloudstrapHealthChecks`), `src/Cloudstrap.Core/HealthChecksOptions.cs`, `src/demo/Api/Program.cs` (the suite's composition convention), `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/` (fixture + host-test precedent) · source reference repo (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` — **every file of the source package was opened and read**: `Nihdi.Core.Configuration.Worker\HostApplicationBuilderExtensions.cs` (57 lines), `HealthCheckService.cs` (129 lines), `Nihdi.Core.Configuration.Worker.csproj` (17 lines), plus the observed-contract sites `Test\TestProject\src\Host\Worker\Program.cs` and `Common\Logging\DeferredLoggerFactory.cs`.
>
> **⚠️ Risk areas this deliverable touches** — **health-transport one-way door** (decided D-1: Kestrel side-host; the `Microsoft.AspNetCore.App` framework reference is **already transitively mandatory** — finding 5) · **public API surface** (a new package: `AddCloudstrapWorker` + `Cloudstrap:Worker` options shape are one-way doors, signed off at the spec gate D-2/D-3) · **Aspire overlap** (health endpoints + OTel wiring are ServiceDefaults territory — composability addressed below; AC-ASP2 carried) · **zero new NuGet dependencies** (any `AspNetCore.HealthChecks.*` addition was considered and rejected — see Dependencies).
>
> **Standing constraint**: nothing is published to nuget.org yet — breaking changes to shipped packages are allowed. This spec identifies **none needed**; if the plan finds friction bridging `MapCloudstrapHealthChecks` into the listener host, fixing `Cloudstrap.Extensions` at the source is permitted under the standing rule.

## Code-reading findings that shaped this spec

1. **The source health listener never consults a health check.** `HealthCheckService.ProcessHealthCheck` answers **200 "Healthy" to every GET unconditionally** (`HealthCheckService.cs` lines 111–112); no `IHealthCheck`, no `Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService`, no tag filtering is anywhere in the package. A worker with a dead SQL connection, a stuck queue consumer or a hung `BackgroundService` probes healthy forever — the probe is decorative. This is the defect-by-design the roadmap flags; it is **not ported** (AC-WK4).
2. **The listener also swallows its own death.** The whole accept loop sits in a `try { … } catch (Exception e) { LogError }` (lines 32–79): if `Start()` throws (port occupied, URL-ACL missing), the exception is logged once and `ExecuteAsync` returns — **the worker keeps running with no probe endpoint at all**. Combined with finding 1, health was doubly fictional. Cloudstrap fails the host fast instead (AC-WK9, Deliberate Change 5).
3. **Everything else `UseWorkerForNihdi` composed already ships public in Cloudstrap.** The OTel-vs-Serilog switch, unconditional `IBusinessTrace`, correlation and KeyVault map 1:1 to shipped `UseCloudstrapObservability` (#2 — which internalized the mode switch and dropped the `ClearProviders` ordering dance) and `AddCloudstrapKeyVault` (#4). The `InternalsVisibleTo("Nihdi.Core.Configuration.Worker")` grant resolves cleanly: no internal access is needed. **#7's genuinely new material is the health listener for headless hosts — nothing else.**
4. **Hard-coded transport everywhere**: port `9000` appears six times in string literals; prefixes `/health/`, `/live/`, `/ready/`; `localhost` vs `http://+` chosen by `EnvironmentIsLocal()` — the environment-sniffing pattern the hosting posture bans (deliverable-1 `IsRunningInAks` precedent). All become explicit options or Core's shipped `Cloudstrap:HealthChecks` values.
5. **The framework-reference "one-way door" is already open.** `Cloudstrap.Observability.csproj` and `Cloudstrap.Extensions.csproj` both carry `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (line 20 in each), and framework references flow transitively through project references — so every `Cloudstrap.Worker` consumer **already requires the ASP.NET Core shared framework regardless of the transport chosen here**. Choosing `HttpListener` to avoid the framework reference would avoid a cost that is already paid (decisive basis of D-1).
6. **The suite's composite convention does not bundle observability or KeyVault.** `src/demo/Api/Program.cs` shows the shipped #5 shape: `AddCloudstrapKeyVault()` (optional) → `UseCloudstrapObservability()` → `AddCloudstrapWebApi()` as explicit sibling calls. Bundling `UseCloudstrapObservability` inside `AddCloudstrapWorker` (as the source bundled it) would hide the `CloudstrapObservabilityBuilder` return that carries `.AddAzureMonitor()` (#3) and the owner/contribute mode — breaking the established exporter-chaining pattern (basis of D-2).
7. **Probe-noise filtering already exists and is config-driven.** #2's `TraceNoiseFilter` drops `HealthChecksOptions.LivenessPath`/`ReadinessPath` requests from traces (never hard-coded paths) — the worker listener inherits probe-trace suppression for free as long as it uses the same Core paths (AC-WK8).
8. **The crash-flush/bootstrap-logger pattern in the source's worker demo host is already adjudicated.** `Test\TestProject\src\Host\Worker\Program.cs` uses `BootstrapLoggerFactory` + `DeferredLoggerFactory` + an ordered dispose chain; #2 **Dropped** `DeferredLoggerFactory` (replaced by Serilog's `CreateBootstrapLogger()` inside `CloudstrapBootstrapLogger`) and `src/demo/Api/Program.cs` already demonstrates the surviving pattern with zero package API. Nothing here earns new API (decided D-5).
9. **The vestigial `System.Security.Cryptography.Xml` 10.0.9 pin is confirmed dead**: zero crypto usage in either source file; it was a transitive-advisory pin, a concern #4's suite-wide `CentralPackageTransitivePinningEnabled` owns. Dropped.

---

## User Story

**As a** .NET developer running a headless worker service (queue consumer, scheduled processor, background job host) in an Azure container or AKS,
**I want to** bootstrap the worker on the generic host with Cloudstrap's validated configuration, correlation and additive health-check registration in one call (`AddCloudstrapWorker`), with observability and KeyVault as the same explicit one-liners every other Cloudstrap host uses — and get container-probe HTTP endpoints (`/healthz`, `/ready`) on a configurable port that **actually reflect my registered health checks**,
**So that** my orchestrator restarts a genuinely dead worker and drains a genuinely unready one, my `Program.cs` stays under ten lines, and every convention (port, paths, bind address) is overridable without environment sniffing.

---

## Acceptance Criteria

> AC-ASP2 and AC-A3 are carried **verbatim** from the founding spec. The founding spec defines no dedicated worker AC block (Package Map row only); AC-WK1…AC-WK11 are new, spec-specific criteria (prefix `AC-WK` — `AC-W…` already names #5's WebApi criteria and AC numbers are never overloaded).

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried verbatim — this package references no auth packages at all)* |
| AC-WK1 | A generic host (`Host.CreateApplicationBuilder`) — no `WebApplicationBuilder` | `AddCloudstrapWorker()` and the host builds and runs | The `Cloudstrap` section is bound and validated eagerly (fail-fast at the call, #1 posture); stock `AddHealthChecks()` is registered additively; correlation services (#2) are registered idempotently; the internal health-listener hosted service is registered; the app itself gets **no** ASP.NET request pipeline. |
| AC-WK2 | Default configuration; all registered checks healthy (or none registered) | GET `http://<host>:9000/healthz` and `http://<host>:9000/ready` | Both answer 200 with the framework writer's one-word body — semantics identical to #4's `MapCloudstrapHealthChecks` (tag contract `CloudstrapHealthCheckTags.Liveness`/`Readiness`); this package re-implements **no** probe-evaluation logic. |
| AC-WK3 | `Cloudstrap:Worker:HealthPort = 5350` and overridden `Cloudstrap:HealthChecks:LivenessPath`/`ReadinessPath` | The worker starts | The listener binds the configured port only (nothing on 9000) and the probes answer on the configured paths. |
| AC-WK4 | A check tagged `ready` that reports Unhealthy, alongside a healthy `live`-tagged check | Both probes are queried; then the check recovers | `/ready` → **503** while `/healthz` stays **200** (probes reflect registered checks and honor the tag contract — the source's unconditional 200 is not ported); after recovery `/ready` returns to 200 (flip proven both directions). |
| AC-WK5 | `Cloudstrap:HealthChecks:Enabled = false` | The worker starts | No listener is started and the port is never bound; the worker runs otherwise unaffected (parity with #4's map-nothing behavior). |
| AC-WK6 | Default configuration; then `HealthListenAddress = "localhost"` | Bind behavior inspected | Default binds all interfaces (orchestrator probes reach the pod/container IP, not loopback); the loopback override is honored; the shipped assembly contains **no** environment/host sniffing (no `EnvironmentIsLocal`-style branching — hosting-posture ruling). |
| AC-WK7 | The listener is running | GET to an unknown path on the health port | 404 — the health port exposes exactly the two probe endpoints and nothing else. |
| AC-WK8 | `Cloudstrap:OpenTelemetry` active; `UseCloudstrapObservability` in contribute mode beside an existing (Aspire ServiceDefaults-style) pipeline | Telemetry is inspected while probes are polled | No duplicate exporters (AC-ASP1 contract, owned by #2 and composed — not re-implemented — here); probe requests produce **no trace spans** (#2's config-driven noise filter covers the shared `Cloudstrap:HealthChecks` paths). |
| AC-WK9 | The configured health port is already occupied | The worker starts | Host startup **fails fast** with a clear exception naming the port — the worker never runs silently unprobed (the source logged once and kept running; finding 2). |
| AC-WK10 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv` | All green; XML docs on all public API; package metadata complete; zero forbidden identifiers; closure contains zero `Aspire.*`, zero `Nihdi.*`, **zero new external NuGet packages**. |
| AC-WK11 | The demo suite extended with the worker demo host *(the D-4 vehicle: `src/demo/Worker/Cloudstrap.Demo.Worker`, health port 5350)* | The E2E suite runs | All pre-existing E2E tests stay green and ≥ 1 new E2E test proves, against the running process: probes answer (AC-WK2) and the readiness flip with a failing registered check (AC-WK4) — standing demo rule / workflow rule 9, incl. the new vehicle-table row. |

---

## Port Decision Table

One row per source public type/feature (the whole package — three files — was read in full; bundled sub-features are rowed individually). "Superseded" = adjudicated and shipped by an earlier deliverable — this deliverable consumes the shipped seam and must not rebuild it.

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `HostApplicationBuilderExtensions.UseWorkerForNihdi` | **Redesign** | `AddCloudstrapWorker(this IHostApplicationBuilder, Action<WorkerOptions>?)` | The one-call worker bootstrap earns its place (founding Package Map row). The shape does not: `NihdiConfiguration` + `ILogger` parameters (Cloudstrap binds from `builder.Configuration`), and it bundles observability/KeyVault against the shipped suite convention (finding 6 — decided D-2; Deliberate Change 2). |
| ├─ OTel-vs-Serilog logging switch (`ConfigureForNihdiOpenTelemetry` / `AddSerilogNihdi` + the `ClearProviders` ordering comment) | **Superseded** | `UseCloudstrapObservability` (#2, shipped — explicit sibling call) | #2 internalized the mode switch behind one call and removed the `ClearProviders` foot-gun entirely (Serilog is an *added* provider). Already `IHostApplicationBuilder`-based — worker-compatible today. |
| ├─ `AddOpenTelemetry(...)` always-on (registers `IBusinessTrace` regardless of telemetry mode) | **Superseded** | `AddCloudstrapBusinessTrace` inside `UseCloudstrapObservability` (#2, shipped) | Identical contract already ships: `IBusinessTrace` registered unconditionally, pipeline built only when `OpenTelemetry.IsActive`. |
| ├─ `AddAzureKeyVaultForNihdi(...)` | **Superseded** | #4's explicit `AddCloudstrapKeyVault()` — **not re-bundled** | Shipped, idempotent, `Enabled`-gated. Kept an explicit sibling call per the suite convention and the founding "Cloudstrap's KeyVault config or Aspire's, not both" doc rule — a silent bundle hides the one-owner decision. *(Deliberate Change 2; D-2)* |
| ├─ `AddCorrelation()` | **Superseded** | `AddCloudstrapCorrelation` (#2, shipped) — called idempotently by `AddCloudstrapWorker` | Same registration #5/#6 composites make; gives workers the outbound correlation delegating handler for typed clients. |
| ├─ `UseNihdiConfiguration(...)` (stash `NihdiConfiguration` in `builder.Properties` + remove/re-add the DI singleton) | **Drop** | — | The config-object-stash pattern died with #1: Cloudstrap binds validated options via `AddCloudstrapCore` and `builder.Configuration` is the single source (#4 posture). No consumer-visible capability is lost. |
| └─ `AddHostedService<HealthCheckService>()` | **Redesign** | internal health-listener hosted service registered by `AddCloudstrapWorker` (skipped when `Cloudstrap:HealthChecks:Enabled = false`) | Registration survives; the service it registers is rebuilt (rows below). |
| `HealthCheckService` (public `BackgroundService`) | **Redesign** | **internal** `WorkerHealthListener` — a hosted service exposing the two Cloudstrap probes for headless hosts | The capability (HTTP probes without an ASP.NET pipeline) is the deliverable's reason to exist — the gap `MapCloudstrapHealthChecks` (#4, `IEndpointRouteBuilder`-only) leaves. The type goes `internal`: consumers interact via configuration, not the service (internal-by-default rule). |
| ├─ raw `System.Net.HttpListener` transport (`http://+` prefixes) | **Replace** | minimal Kestrel endpoint host inside the hosted service *(decided D-1)* | `HttpListener` is legacy (Microsoft steers to Kestrel), wildcard binding needs an admin URL ACL on Windows (the friction the source dodged with env sniffing), and the framework reference Kestrel needs is **already transitively mandatory** (finding 5) — `HttpListener` would be a worse transport purchased at no saving. |
| ├─ unconditional 200 "Healthy" to every GET | **Redesign** | probes evaluate the **host's registered** `Microsoft.Extensions.Diagnostics.HealthChecks` checks, filtered by `CloudstrapHealthCheckTags` | Finding 1 — the defect-by-design. Target design: the listener bridges the host's `HealthCheckService` + `IConfiguration` into the endpoint host and runs #4's shipped `MapCloudstrapHealthChecks`, so worker probes and web-host probes are one implementation with one contract (AC-WK2/AC-WK4). |
| ├─ hard-coded port 9000 (six literals) | **Redesign** | `Cloudstrap:Worker:HealthPort`, default **9000** | Founding Package Map line 82 verbatim: configurable, default 9000. |
| ├─ probe prefixes `/health/` + `/live/` + `/ready/` | **Redesign** | Core's shipped `Cloudstrap:HealthChecks:LivenessPath` (`/healthz`) + `ReadinessPath` (`/ready`); the third `/health` path **dropped** | De-NIHDI row 115: standard paths, configurable. Two probes answer both orchestrator questions; a third aggregate path is a knob nobody sets (a consumer wanting one maps… nothing — they set both paths equal or query `/ready`). *(Deliberate Change 3)* |
| ├─ `EnvironmentIsLocal()` → `localhost` vs `http://+` prefix branching | **Drop** | explicit `Cloudstrap:Worker:HealthListenAddress` option, default all-interfaces | Hosting-posture ruling (deliverable-1 `IsRunningInAks` precedent): no environment sniffing; behavior identical across supported targets, loopback-only is an explicit dev-time override (AC-WK6). |
| ├─ GET-only handling (404 for other verbs) | **Redesign** | #4's probe-endpoint semantics as shipped | Consistency beats parity: worker probes must behave exactly like the same consumer's web-host probes (`MapCloudstrapHealthChecks`). Verb handling follows the shipped #4 contract; unknown paths 404 (AC-WK7). *(Deliberate Change 6)* |
| ├─ manual `Task.WhenAny` accept loop + `ThreadPool.QueueUserWorkItem` dispatch + swallow-all `catch` + `Dispose` listener close | **Drop** | — (Kestrel + hosted-service lifetime own all of it) | Transport internals with no contract value; the swallow-all catch is finding 2's defect — startup failure now fails the host (AC-WK9). |
| `Nihdi.Core.Configuration.Worker.csproj` (`System.Security.Cryptography.Xml` 10.0.9 pin; Common + Core references) | **Drop** | new `Cloudstrap.Worker.csproj` per #0 scaffolding | Crypto pin vestigial (finding 9 — zero crypto usage; #4's transitive pinning owns the concern). NRT + SDK analyzers + GitVersion + MIT metadata per suite standard. |
| *(observed-contract site)* worker demo `Program.cs` bootstrap pattern (`BootstrapLoggerFactory` + `DeferredLoggerFactory` swap + ordered crash-flush dispose chain) | **Drop** *(routed → demo host + README guidance)* | — | Finding 8: #2 already Dropped `DeferredLoggerFactory` and ships `CloudstrapBootstrapLogger`; the surviving pattern is consumer `Program.cs` guidance, demonstrated by the demo worker host — not package API. *(D-5)* |

**Tally**: 6 Redesign · 1 Replace · 5 Drop · 4 Superseded-reuse. *(Nothing qualified as a straight Port — every carried behavior needed reshaping; every capability the old package composed from Common/Core ships already.)*

---

## Public API Sketch

Namespace **`Cloudstrap.Worker`** (single namespace — suite precedent). Everything `public sealed`/`static`; the health-listener hosted service and the options validator are `internal`.

```text
Cloudstrap.Worker
├── HostApplicationBuilderExtensions (static)
│     AddCloudstrapWorker(this IHostApplicationBuilder builder,
│                         Action<WorkerOptions>? configure = null)
│         : IHostApplicationBuilder                      (shape decided D-2)
│       — binds + validates the Cloudstrap section eagerly (fail-fast, #1) and
│         WorkerOptions from Cloudstrap:Worker ([OptionsValidator], ValidateOnStart;
│         the configure callback runs after binding and wins);
│         AddCloudstrapCore + AddCloudstrapCorrelation (idempotent, #1/#2);
│         AddHealthChecks() (stock IHealthChecksBuilder — additive, Aspire posture);
│         registers internal WorkerHealthListener hosted service
│         (not registered when Cloudstrap:HealthChecks:Enabled = false).
│       Deliberately does NOT bundle UseCloudstrapObservability or AddCloudstrapKeyVault —
│       explicit sibling calls, suite convention (finding 6): keeps the
│       CloudstrapObservabilityBuilder return reachable for .AddAzureMonitor() (#3)
│       and owner/contribute mode (#2).                   (D-2)
│
└── WorkerOptions — section Cloudstrap:Worker (owned HERE)   (D-3)
      const SectionName = "Cloudstrap:Worker"
      HealthPort          : int    = 9000    — 1–65535 (founding default kept)
      HealthListenAddress : string = "*"     — "*" = all interfaces (container-probe
                                               reality); "localhost" = loopback-only
                                               (explicit dev override — replaces the
                                               EnvironmentIsLocal() sniff)

internal: WorkerHealthListener — hosted service running a minimal Kestrel endpoint host on
HealthPort/HealthListenAddress whose probe endpoints are #4's MapCloudstrapHealthChecks,
evaluated against the HOST's registered health checks (the host's HealthCheckService and
IConfiguration are bridged into the endpoint host — mechanism is plan detail); paths and
Enabled come from Core's shipped Cloudstrap:HealthChecks; tags from CloudstrapHealthCheckTags.
Bind/start failure faults host startup (AC-WK9). Source-generated [OptionsValidator]
validator (inherited fact — no Microsoft.Extensions.Options.DataAnnotations).
```

**Target consumer `Program.cs`** (also the demo host and README example):

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddCloudstrapKeyVault();          // optional, explicit (#4)
builder.UseCloudstrapObservability();     // explicit (#2) — chain .AddAzureMonitor() (#3) if wanted
builder.AddCloudstrapWorker();            // this package: core + correlation + health listener
builder.Services.AddHostedService<MyWorker>();
builder.Services.AddHealthChecks().AddCheck("queue", …, tags: [CloudstrapHealthCheckTags.Readiness]);
await builder.Build().RunAsync();
```

**Configuration** — this package owns one new section: `Cloudstrap:Worker` (`HealthPort`, `HealthListenAddress`). It **consumes** Core's shipped `Cloudstrap:HealthChecks` (`Enabled`, `LivenessPath`, `ReadinessPath`) and, via composition, `Cloudstrap:Logging`/`Cloudstrap:OpenTelemetry`/`Cloudstrap:Correlation`/`Cloudstrap:KeyVault` — never redefining any of them.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Health listener | On; Kestrel-based (D-1), all interfaces, port 9000; exactly two endpoints — `/healthz` (checks tagged `live`) and `/ready` (checks tagged `ready`); framework status mapping (Healthy/Degraded → 200, Unhealthy → 503); zero matching checks → 200 (framework semantics, parity with #4). | `Cloudstrap:Worker:HealthPort`, `Cloudstrap:Worker:HealthListenAddress`; paths via `Cloudstrap:HealthChecks:LivenessPath`/`ReadinessPath`; `Cloudstrap:HealthChecks:Enabled = false` removes the listener entirely. |
| Health-check registration | Stock `AddHealthChecks()` — additive `IHealthChecksBuilder` (Aspire posture); consumers tag checks with `CloudstrapHealthCheckTags`. #4's typed-client dependency checks (`HealthCheckPrefix` → readiness-tagged URL check) work unchanged in a worker. | Stock builder — register anything; untagged checks are simply served by neither probe (documented). |
| Observability | Not bundled — explicit `UseCloudstrapObservability()` sibling call (#2 contract: Serilog/OTel switch, `IBusinessTrace`, owner/contribute modes, `.AddAzureMonitor()` chaining). | #2's own options/hooks; skip the call entirely for consumers with their own logging. |
| KeyVault configuration | Not bundled — explicit `AddCloudstrapKeyVault()` (#4), documented "Cloudstrap's or Aspire's, not both". | #4's `Cloudstrap:KeyVault` section. |
| Correlation | Services registered idempotently (accessor + outbound delegating handler for typed clients). No inbound middleware — a worker has no inbound HTTP pipeline; correlation ids originate per operation (#2's `ICorrelationSource`). | #2's `Cloudstrap:Correlation` contract. |
| Probe telemetry noise | Probe requests are not traced — #2's `TraceNoiseFilter` drops the configured `Cloudstrap:HealthChecks` paths (finding 7). | `Cloudstrap:OpenTelemetry` ignore-segment options (#2). |
| Startup failure posture | Invalid `Cloudstrap` section → fail at `AddCloudstrapWorker` (#1); health port unbindable → host startup faults (AC-WK9). | Fix configuration; `Enabled = false` if no listener is wanted. |
| Bootstrap logging / crash flush | Guidance, not API: `CloudstrapBootstrapLogger.Create` before the host + dispose after `Build()` + the return-code `try/catch/finally` pattern — shown in the demo worker `Program.cs` and README (D-5). | Any `ILoggerFactory`; nothing in the package requires it. |
| Aspire coexistence | OTel: composition through #2's owner/contribute modes — this package adds no exporter, no pipeline (AC-ASP1 stays #2's contract). Health: checks registered via the stock builder (inherently additive, AC-ASP3 posture); Aspire ServiceDefaults maps health endpoints only on web hosts, so a generic-host worker has no competing endpoint — the README states the one-owner rule anyway (if the consumer hosts their own health endpoint by other means, set `Cloudstrap:HealthChecks:Enabled = false`). Zero `Aspire.*` (AC-ASP2). | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in `src/Test/UnitTest/Cloudstrap.Worker.Tests`; tests boot a real generic host with the listener on a free loopback port and assert with `HttpClient`: probes answer + framework body (AC-WK2), port/path overrides (AC-WK3), the Unhealthy flip + tag separation (AC-WK4), disabled → connection refused (AC-WK5), unknown path 404 (AC-WK7), occupied port → startup fault (AC-WK9); a reflection sweep asserts no environment-sniffing helpers (AC-WK6) and no probe-evaluation logic owned by this package (AC-WK2 rider). The demonstration slice adds the demo worker host + ≥ 1 E2E test (AC-WK11, D-4).

---

## Dependencies

| Package | License | Evidence & justification |
|---|---|---|
| `Cloudstrap.Core` *(project reference)* | MIT | `CloudstrapOptions` fail-fast binding, `HealthChecksOptions` (paths + `Enabled`), `AddCloudstrapCore`. |
| `Cloudstrap.Observability` *(project reference)* | MIT | `AddCloudstrapCorrelation`, `CloudstrapHealthCheckTags` (the tag contract the probes filter on); `UseCloudstrapObservability`/`CloudstrapBootstrapLogger` reachable for the sibling calls with one package family. |
| `Cloudstrap.Extensions` *(project reference)* | MIT | `MapCloudstrapHealthChecks` — the single probe implementation this package reuses instead of re-implementing; `AddCloudstrapKeyVault` reachable for the documented sibling call. |
| `Microsoft.AspNetCore.App` *(framework reference)* | MIT | Kestrel + endpoint routing + `Microsoft.Extensions.Diagnostics.HealthChecks` for the listener (D-1). **Already transitively mandatory** via the Observability/Extensions references (finding 5) — declaring it adds no consumer cost; fifth explicit declaration in the suite (#2, #4, #5, #6 precedent). |

**Zero new external NuGet packages** — no new CPM pins.

Considered and **rejected**: `AspNetCore.HealthChecks.*` helpers (nothing here needs one — probe evaluation is the framework's `HealthCheckService`; #4's existing `AspNetCore.HealthChecks.Uris` pin, Apache-2.0, keeps serving the typed-client dependency checks unchanged) · a `Microsoft.Extensions.Diagnostics.HealthChecks` package pin (ships inside the shared framework — a NuGet pin would be redundant) · `System.Security.Cryptography.Xml` (finding 9 — vestigial) · any `Nihdi.*` (AC-A3) · any `Aspire.*` (AC-ASP2).

---

## Deliberate Behavior Changes (vs. the source library)

1. **Probes tell the truth.** `/healthz` and `/ready` evaluate the registered health checks with the `live`/`ready` tag contract — Healthy/Degraded → 200, Unhealthy → 503. The source answered 200 unconditionally (finding 1).
2. **The one-call bundle is unbundled.** `AddCloudstrapWorker` no longer wires observability or KeyVault; they are the same explicit sibling calls every Cloudstrap host makes (finding 6). Cost: two extra lines in `Program.cs`. Gain: the observability builder return stays usable (`.AddAzureMonitor()`, contribute mode), KeyVault ownership stays visible, and worker hosts read identically to Api/Mvc hosts. *(Decided D-2.)*
3. **Probe surface is two standard paths, not three**: `/healthz` + `/ready` from Core's `Cloudstrap:HealthChecks` (configurable); the aggregate `/health` prefix is gone (De-NIHDI row 115).
4. **No environment sniffing**: all-interfaces binding by default, explicit `HealthListenAddress` override — `EnvironmentIsLocal()` branching removed (hosting posture).
5. **Listener failure fails the host** instead of being logged and swallowed (finding 2) — a worker never runs silently unprobed.
6. **Verb handling follows #4's shipped probe semantics** rather than the source's GET-only-404 — worker and web-host probes behave identically for the same consumer.
7. **No configuration stash**: `UseNihdiConfiguration`'s host-property/DI-singleton juggling is gone — validated options via #1.
8. **Transport is Kestrel, not `HttpListener`** — no Windows URL-ACL requirement for non-loopback binding. *(Decided D-1.)*

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| No health checks registered at all | Both probes 200 (framework semantics: zero matching checks = Healthy) — parity with #4's web-host probes. |
| A check registered without tags | Served by neither probe (tag predicate) — documented; the tags are the contract. |
| Degraded check | 200 with body `Degraded` (framework default writer) — orchestrators treat non-503 as passing. |
| Health port occupied at startup | Host startup faults with a clear exception (AC-WK9) — no silent unprobed worker. |
| `Cloudstrap:HealthChecks:Enabled = false` | No hosted service registered, port never bound; connection refused on probe attempts. |
| GET unknown path / other verbs on the health port | Unknown path → 404; verb handling per #4's shipped endpoint semantics (Deliberate Change 6). |
| TLS on the probe port | Not offered — probes are plain HTTP on an orchestrator-internal port (kubelet/App Service probe reality); a worker needing an authenticated or TLS surface is a web host and should use #4/#5 instead. Documented. |
| The consumer's host is actually a `WebApplication` | Not this package's scenario — use `MapCloudstrapHealthChecks` (#4) on the real pipeline; running both would double-serve probes on two ports (README warns). |
| `AddCloudstrapWorker` called twice | Second call is a no-op (marker/idempotence — #4 `AddCloudstrapKeyVault` precedent); exactly one listener. |
| Slow health check | Framework check timeouts/`HealthCheckRegistration` settings apply; the listener adds no timeout layer of its own. |
| Host shutdown | Listener stops with the hosted-service lifecycle; port released; in-flight probe requests complete per Kestrel graceful shutdown. |
| Probe traffic under active OTel | No spans (finding 7 — #2's noise filter on the shared config-driven paths). |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace, ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- Re-implementation of anything Superseded: observability incl. the Serilog/OTel switch and bootstrap logging (#2), Azure Monitor export (#3), KeyVault/BlobStorage/DataProtection/typed clients/probe mapping (#4). Any change to their surfaces needs the standing cross-package justification.
- Hangfire scheduling (#16) and messaging transports (#14) — the source's worker demo host wired both; the Cloudstrap demo worker uses a plain `BackgroundService`.
- Everything Dropped above: `UseNihdiConfiguration`'s config stash, `EnvironmentIsLocal()` branching, the `/health` aggregate path, the `HttpListener` accept-loop internals, the crypto pin, `DeferredLoggerFactory` (already Dropped by #2).
- New crash-flush/bootstrap-logger package API (D-5 — guidance and demo material only).
- A public standalone health-listener entry point (e.g. `AddCloudstrapWorkerHealthListener`) — the composite covers the scenario; exposing the granular piece is a compatible post-v1 addition if demand appears (internal-by-default rule).
- TLS/auth on the probe port; `IHealthCheckPublisher`-based push health (file/log publishers) — possible post-v1 complement, not a v1 requirement.
- Windows-service / systemd lifetime helpers (`UseWindowsService` etc.) — the source never had them (no gold-plating); consumers call the stock extensions.
- Test helpers (#8), Blazor (#11–#13), auth packages (#5/#9/#10 — a worker attaching client-credentials tokens to typed clients just references #9; nothing worker-specific to add).

---

## Decision Log (gate answers, 2026-08-20 — zero Open Questions remain; spec is planner-ready)

All five gate questions were answered by the user on 2026-08-20; each accepted this spec's recommendation as-is. The full findings/options/rationale for each question live in this repo's git history of this file (the pre-gate draft); the decided outcomes are:

| # | Question | Answer (user, 2026-08-20) |
|---|----------|---------------------------|
| **D-1** | Health-endpoint transport (⚠️ one-way door): port the check-aware `HttpListener`, a minimal Kestrel side-host, or an `IHealthCheckPublisher` push model? | **Minimal Kestrel endpoint host inside the internal hosted service, reusing #4's shipped `MapCloudstrapHealthChecks`** — one probe implementation for web and worker hosts (identical paths, tags, status mapping and `Enabled` gate). Basis: the `Microsoft.AspNetCore.App` framework reference is already transitively mandatory via the Observability/Extensions project references (finding 5), so `HttpListener` would buy Windows URL-ACL friction to avoid a cost already paid; the publisher model has no HTTP endpoint and fails the founding "health listener port" requirement. The framework reference is declared explicitly (fifth in the suite — #2/#4/#5/#6 precedent). |
| **D-2** | Entry-point shape and composite contents (⚠️ public API one-way door): unbundled single call, source-parity bundle, or an Add/Use pair? | **Single `AddCloudstrapWorker(this IHostApplicationBuilder, Action<WorkerOptions>?)`, unbundled**: core + correlation + stock `AddHealthChecks()` + the health listener. `UseCloudstrapObservability` (#2) and `AddCloudstrapKeyVault` (#4) stay **explicit sibling calls** — the shipped suite convention (finding 6), keeping the `CloudstrapObservabilityBuilder` return reachable for `.AddAzureMonitor()` (#3) and owner/contribute mode, and keeping KeyVault ownership visible ("Cloudstrap's or Aspire's, not both"). The roadmap's "one call" is satisfied per concern; the deviation from the source's bundle is recorded as Deliberate Change 2. No Use side — a generic host has no middleware phase. |
| **D-3** | `Cloudstrap:Worker` configuration contents | **Two knobs only**: `HealthPort` (int, default **9000** — founding Package Map line 82) and `HealthListenAddress` (default all-interfaces; `localhost` = the explicit dev-time override replacing the `EnvironmentIsLocal()` sniff). `Enabled` and the probe paths are **consumed from Core's shipped `Cloudstrap:HealthChecks`** — one kill switch, no duplicated sources of truth, and #2's trace-noise filter stays aligned with the probe paths for free. |
| **D-4** | Demonstration vehicle + headline E2E behavior (standing rule; no worker row existed) | **New demo app `Cloudstrap.Demo.Worker` at `src/demo/Worker/`** (`IsPackable=false`), health port **5350**: a trivial periodic `BackgroundService`, the D-2 five-line bootstrap (doubling as the README example incl. the D-5 crash-flush pattern), one always-healthy `live` check and one `ready`-tagged **`DemoOutageHealthCheck`** reporting Unhealthy while a configured sentinel file exists (the E2E suite's process-external toggle). New **`WorkerHostTests`** in `Cloudstrap.Demo.E2E.Tests`: probes answer → sentinel created → `/ready` 503 while `/healthz` stays 200 (the AC-WK4 flip + tag contract, live) → sentinel removed → recovery. **New CLAUDE.md rule-9 vehicle-table row**: "Worker / headless hosting features → `Cloudstrap.Demo.Worker` (health port 5350)" — the vehicle #14 (Messaging) and #16 (Hangfire) later extend. |
| **D-5** | Crash-flush / bootstrap-logger guidance: package API, demo pattern, or README-only? | **Guidance only — no new API**: the demo worker `Program.cs` + a README section demonstrate the surviving pattern (`CloudstrapBootstrapLogger.Create` before the host, dispose after `Build()`, return-code `try/catch/finally`). Grounded in #2's delivered story: `DeferredLoggerFactory` was Dropped there (replaced by Serilog's `CreateBootstrapLogger()`), and a `RunCloudstrapWorkerAsync`-style wrapper would be API the source never shipped as API — gold-plating with lifecycle-ownership implications. |
