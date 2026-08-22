# Plan: 7-WorkerBootstrap — A consumer bootstraps a headless worker on the generic host with `AddCloudstrapWorker` and gets container-probe HTTP endpoints on a configurable port that actually reflect the registered health checks — in under ten lines of `Program.cs`

## Overview

Deliverable #7 of the extraction roadmap: the new `Cloudstrap.Worker` package — the suite's **ninth**.
**Binding spec: `_specs/7-WorkerBootstrap.md`** (approved 2026-08-20, zero Open Questions). Its Port
Decision Table (**6 Redesign · 1 Replace · 5 Drop · 4 Superseded-reuse — zero straight Ports**), Public
API Sketch, Behaviors & Conventions table, Dependencies table, Deliberate Behavior Changes 1–8, Edge
Cases, Out of Scope list and Decision Log (**D-1** minimal Kestrel side-host reusing #4's shipped
`MapCloudstrapHealthChecks` · **D-2** single unbundled `AddCloudstrapWorker(this IHostApplicationBuilder,
Action<WorkerOptions>?)` · **D-3** two-knob `Cloudstrap:Worker` section, `Enabled`/paths consumed from
Core's `Cloudstrap:HealthChecks` · **D-4** `Cloudstrap.Demo.Worker` demo vehicle on health port 5350 with
the sentinel-file `DemoOutageHealthCheck` · **D-5** crash-flush as guidance only, no new API) are
authoritative and are not re-litigated here. Nothing the spec marked Drop appears in this plan: no
`UseNihdiConfiguration` config stash, no `EnvironmentIsLocal()` branching, no `/health` aggregate path,
no `HttpListener` accept-loop internals, no crypto pin, no `DeferredLoggerFactory`, no crash-flush
package API, no public standalone health-listener entry point, no TLS/auth on the probe port, no
Windows-service/systemd helpers, no Hangfire (#16) or Messaging (#14) wiring, no re-implementation of
anything Superseded (#2 observability incl. the logging switch and `IBusinessTrace`, #4 KeyVault, #2
correlation — composed, never rebuilt).

Reference patterns, all read in full before planning:

- **Primary reference: `_plans/6-MvcBootstrap.md` / shipped `Cloudstrap.Mvc` (#6)** — the newest
  composite-package precedent (slice/step/gate granularity, brand-new-project RED mechanics,
  `PackageSurfaceTests` permanent guards, packaging step shape, demo-host + E2E demonstration step,
  final-gate AC walk). #5's approved composite decisions carried through #6 apply where a generic host
  has an equivalent: eager configuration read at registration time, hook-less fail-fast posture, marker
  idempotence. A generic host has **no middleware phase**, so there is no `Use` side and no pipeline
  hooks (D-2) — the parts of #5/#6 about middleware ordering do not transfer, and nothing here invents
  a substitute.
- **Shipped seams this package consumes (read on disk, never rebuilt)**:
  `src/Cloudstrap.Core/ServiceCollectionExtensions.cs` (`AddCloudstrapCore` — idempotent option
  binding + `ValidateOnStart`), `ConfigurationExtensions.GetCloudstrapOptions` (the eager fail-fast
  read), `HealthChecksOptions` (`Enabled`, `LivenessPath` `/healthz`, `ReadinessPath` `/ready`);
  `src/Cloudstrap.Observability/Correlation/ServiceCollectionExtensions.cs`
  (`AddCloudstrapCorrelation(this IServiceCollection)` — idempotent), `CloudstrapHealthCheckTags`
  (`"live"`/`"ready"`), `HostApplicationBuilderExtensions.cs` (`UseCloudstrapObservability` — already
  `IHostApplicationBuilder`-based, the explicit sibling call), `CloudstrapBootstrapLogger` (the D-5
  guidance pattern), `TraceNoiseFilter` (config-driven probe-noise suppression, finding 7);
  `src/Cloudstrap.Extensions/EndpointRouteBuilderExtensions.cs` (`MapCloudstrapHealthChecks` —
  marker-idempotent, `Enabled`-gated, tag-predicate probes, `.AllowAnonymous().ShortCircuit()`, the
  framework response writer — **the single probe implementation the D-1 listener runs**),
  `HostApplicationBuilderExtensions.cs` (`AddCloudstrapKeyVault` — the `builder.Properties` marker
  idempotence precedent this package's run-once guard copies).
- **Demonstration harness (verified on disk)**: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/` —
  `SutProcess.Start(baseUrl, applicationArguments, projectRelativePath)` (`dotnet run --no-build
  --no-launch-profile`, forces `ASPNETCORE_ENVIRONMENT=Development`, captures stdout/stderr in
  `CapturedOutput`), `BlazorServerTests`/`MvcHostTests` (the boot-your-own-host-by-project-path +
  `WaitUntilReadyAsync` polling precedent Step 5 follows), `E2eFixture` (boots IdP 5310 → Api 5330 →
  Bff 5300 — the worker fixture needs none of them and touches none of them), `PageTestBase` (not
  needed here — a worker has no browser surface; `WorkerHostTests` is a plain `HttpClient` +
  captured-output fixture, the `ApiHostTests` style); `src/demo/README.md` (port map 5300–5340 +
  59999, layout, manual-run table), `src/demo/Api/README.md` (the feature-matrix shape with E2E test
  names per row), `src/demo/Api/Program.cs` + `appsettings.json` (the suite composition convention
  and the Console-mode stdout-capture posture the demo worker mirrors), `.vscode/launch.json` +
  `tasks.json` (per-app `build-demo-*` task + launch config + the "Demo apps (all hosts + IdP)"
  compound), `src/demo/Directory.Build.props` (`IsPackable=false` — demo apps pack nothing),
  `src/Test/Directory.Build.props` (`.Tests`-suffix MTP/NUnit wiring, `IsPackable=false`).

This is a library deliverable with no database and no UI of its own: the plan-template's
endpoint-integration block does not apply literally. Its equivalent here is that **every step's tests
boot a real generic host (`Host.CreateApplicationBuilder`) with the listener on a free loopback port and
assert over real HTTP with `HttpClient`** — status codes and the framework writer's one-word bodies —
plus the mandatory E2E demonstration slice (Steps 5–6, the D-4 demo worker driven end to end).

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-WK1 (one call: eager fail-fast, additive `AddHealthChecks`, idempotent correlation, listener registered, no ASP.NET pipeline on the app itself) | 1 |
| AC-WK2 (probes answer with framework semantics via #4's implementation; port 9000 default; no probe-evaluation logic of our own) | 1 (default as bound value) · 2 (behavior + sweep) · 5 (live) |
| AC-WK3 (port + path overrides honored; nothing on 9000 when overridden) | 2 |
| AC-WK4 (ready 503 while healthz 200 with a failing `ready` check; recovery — flip both directions) | 3 (+ live in 6) |
| AC-WK5 (`Cloudstrap:HealthChecks:Enabled=false` → no listener, port never bound) | 1 (no registration) · 3 (connection refused) |
| AC-WK6 (all-interfaces default, explicit loopback override, zero environment sniffing) | 3 (+ permanent guard in 4) |
| AC-WK7 (unknown path on the health port → 404; exactly two endpoints) | 2 |
| AC-WK8 (composes with #2 contribute mode, no duplicate exporters; probe requests produce no trace spans) | 4 (composition half) · 6 (no-span half, live via captured stdout) |
| AC-WK9 (occupied port → host startup fails fast naming the port) | 3 |
| AC-WK10 (build/tests/format, XML docs, metadata, closure + identifier sweeps, zero new external NuGet) | 4 |
| AC-WK11 (demo worker host on 5350; ≥ 1 E2E proving probes + the readiness flip; existing E2E green; vehicle-table row) | 5–6 |
| AC-ASP2 (zero `Aspire.*` in the closure) | 4 (permanent guard) |
| AC-A3 (zero `Nihdi.AspNetCore` references) | 4 (permanent guard; this package references no auth package at all) |

### New CPM entries: **none**

The spec's headline dependency fact, carried into the plan: **zero new external NuGet packages, zero new
CPM pins**. `Cloudstrap.Worker.csproj` has *no* `PackageReference` at all — three project references
(`Cloudstrap.Core`, `Cloudstrap.Observability`, `Cloudstrap.Extensions`) plus the suite's **fifth**
`Microsoft.AspNetCore.App` framework reference (after #2, #4, #5, #6 — already transitively mandatory
via the Observability/Extensions references, spec finding 5). The test project uses only already-pinned
packages (`Microsoft.Extensions.Configuration`(+`.Binder`), `Microsoft.Extensions.Hosting`) plus the
package project reference. The demo app adds only project references. The E2E project gains no reference.

### ⚠️ Risk areas (spec header; reviewed at the gates named)

- **Public API one-way doors (new package = all-new API)** — `AddCloudstrapWorker`, `WorkerOptions` and
  the `Cloudstrap:Worker` section (two knobs: `HealthPort`, `HealthListenAddress`) are permanent
  surface, signed off against the spec's Public API Sketch at **Gate 1**.
- **Health-transport one-way door** — decided at the spec gate (D-1, Kestrel side-host); the
  *implementation* (the internal `WorkerHealthListener` mechanics, mechanic (b)) gets explicit human
  review at **Gate 1**, including the fifth `Microsoft.AspNetCore.App` framework reference.
- **Aspire overlap** — health endpoints + OTel wiring are ServiceDefaults territory: this package adds
  no exporter and no pipeline (composition through #2's owner/contribute modes, AC-WK8), registers
  checks additively on the stock `IHealthChecksBuilder`, and the README states the one-owner rule
  (`Cloudstrap:HealthChecks:Enabled=false` when the consumer hosts probes by other means). Reviewed at
  **Gate 1** (composition) and the **final gate** (README + AC-ASP2 guard).
- **Zero new NuGet dependencies** — any `AspNetCore.HealthChecks.*` temptation was considered and
  rejected in the spec; the Step 4 closure guard makes it permanent. **Final gate** confirms the
  Release nupkg dependency list.

### Planner mechanics decided here (no spec conflict; each flagged for review at the named gate)

**(a) Source-generated options validator.** `WorkerOptionsValidator` (`internal sealed partial :
IValidateOptions<WorkerOptions>`, `[OptionsValidator]` — the `ApplicationOptionsValidator` /
`CloudstrapJwtBearerOptionsValidator` precedent) carries two pure attribute rules: `[Range(1, 65535)]`
on `HealthPort` and `[Required]` on `HealthListenAddress`. No conditional rules exist, so the
source-generated split applies (the spec sketch says exactly this — no
`Microsoft.Extensions.Options.DataAnnotations` package). *(Gate 1.)*

**(b) The listener mechanism — outcomes pinned, exact APIs with executor latitude, reported at Gate 1.**
`WorkerHealthListener` (`internal sealed`, `IHostedService`) builds a minimal endpoint host inside
`StartAsync`: `WebApplication.CreateEmptyBuilder(new WebApplicationOptions())` +
`builder.WebHost.UseKestrelCore()`, listening on `http://{HealthListenAddress}:{HealthPort}` (`"*"` →
all interfaces, `"localhost"` → loopback). The inner host's DI **bridges the parent host's services**:
the parent `IConfiguration` (so `MapCloudstrapHealthChecks` reads the real `Cloudstrap:HealthChecks`
section), the parent `Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService` (so the probes
evaluate the **host's** registered checks — the D-1 "one probe implementation" bridge), and the parent
`ILoggerFactory` (so Kestrel startup/shutdown logs flow through the host's logging); plus `AddRouting()`.
Pipeline: `UseRouting()` → `MapCloudstrapHealthChecks()` — nothing else, so unknown paths 404 by
construction (AC-WK7) and this package re-implements zero probe logic (AC-WK2 rider). `StartAsync`
awaits the inner host's start **without catching**: a bind failure propagates, faulting host startup
(AC-WK9 — the framework's default `IHostedService` failure posture does the fail-fast; the executor
confirms the surfaced exception type/message names the address/port and reports it at Gate 1, wrapping
with a clearer message only if the stock one does not name the port). `StopAsync`/`DisposeAsync` stop
and dispose the inner host with the hosted-service lifecycle. *(Gate 1 — this is the D-1 door's
implementation review.)*

**(c) Eager reads at registration time** (#5/#6 mechanic (l) carried): `AddCloudstrapWorker` calls
`builder.Configuration.GetCloudstrapOptions()` eagerly (the AC-WK1 fail-fast — invalid `Cloudstrap`
section throws **at the call**) and reads `Cloudstrap:HealthChecks:Enabled` eagerly to decide whether
the listener hosted service is registered at all (AC-WK5's registration half). `WorkerOptions` itself
is bound via `AddOptions<WorkerOptions>().BindConfiguration(WorkerOptions.SectionName).ValidateOnStart()`
+ `TryAddEnumerable` of the validator; the `configure` callback is applied with
`services.PostConfigure(configure)` so it **runs after binding and wins** (spec sketch verbatim).
README consequence (written in Step 4): configuration sources added after `AddCloudstrapWorker`
(e.g. `AddCloudstrapKeyVault`) do not affect the Enabled decision — call KeyVault first.

**(d) Run-once marker.** `AddCloudstrapWorker` guards with a `builder.Properties` marker
(`"Cloudstrap.Worker"` — the `AddCloudstrapKeyVault` precedent): the second call is a **no-op** (spec
edge case "called twice — exactly one listener"), not a throw (unlike `UseCloudstrapMvc`'s pipeline
guard — there is no pipeline to double-build here; the source of truth is the spec's edge-case table).
*(Gate 1.)*

**(e) Free-port test strategy — port 9000 is never live-bound by the unit suite.** Every behavioral
test acquires a free loopback port (bind a `TcpListener` on port 0, read the assigned port, release)
and configures `Cloudstrap:Worker:HealthPort` + `HealthListenAddress=localhost` — no firewall prompts,
no collision with a developer's real port 9000. The **default 9000** is asserted as the bound option
value (Step 1), not as a live bind; AC-WK3's "nothing on 9000" is asserted as the listener's composed
address carrying only the configured port (see (f)) rather than a connection attempt to 9000 that a
developer's unrelated service could poison.

**(f) Bind-address proof strategy (AC-WK6 without flaky LAN probing).** Loopback override: behavioral —
`HealthListenAddress=localhost` answers on `127.0.0.1`. All-interfaces default: asserted on the inner
host's bound server addresses (`IServer.Features.Get<IServerAddressesFeature>()` via an
`InternalsVisibleTo` seam on the listener, or the composed URL — executor latitude, outcome pinned:
the default composes an all-interfaces binding, the override composes loopback-only), **not** by
connecting from a LAN address (agents without a routable NIC would flake). The "no environment
sniffing" clause is a reflection sweep: no method named like `EnvironmentIsLocal`/`IsRunningIn*` and
no `IHostEnvironment`-conditional bind logic in the shipped assembly — made a permanent guard in Step 4.

**(g) The demo worker binds loopback explicitly and the fixture owns the port.** The demo app's
`appsettings.json` sets `Cloudstrap:Worker:HealthListenAddress: "localhost"` — the **documented
dev-time override** (AC-WK6's second half, live), which also avoids Windows Firewall prompts on dev
machines and CI agents; a comment in the file says exactly that (the all-interfaces default is the
container posture, proven per (f)). `WorkerHostTests` passes
`--Cloudstrap:Worker:HealthPort=5350` as an application argument (the task-level fact: a generic host
ignores `ASPNETCORE_URLS`, so the `SutProcess.Start` `baseUrl` parameter only feeds the readiness
poller — the port must arrive as configuration). `appsettings.json` also carries `HealthPort: 5350`
so `dotnet run --project src/demo/Worker` behaves identically without arguments.

**(h) Full-suite check** (the #27 Context-§9 convention, extended): `runTests` is not on the agent
PATH — VERIFY invokes each exe directly. The check means: `dotnet build src/Cloudstrap.sln`, then the
**10** unit exes under `src/Test/UnitTest/<Name>.Tests/bin/Debug/net10.0/<Name>.Tests.exe` (Core,
Observability, Observability.AzureMonitor, Extensions, WebApi, Mvc, TestIdentityProvider,
Authentication.ClientCredentials, Authentication.OpenIdConnect, **Worker** — new in Step 1), then the
E2E exe `src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe`, then
`dotnet format src/Cloudstrap.sln --verify-no-changes`.

**(i) Library-behavior confirmations the executor makes during RED and reports at the covering gate**:
  1. **The framework writer's bodies** — `Healthy` / `Degraded` / `Unhealthy` one-word bodies and the
     Healthy/Degraded→200, Unhealthy→503 mapping through the bridged `HealthCheckService` (Gate 1).
  2. **The occupied-port exception** — exact type (`IOException`/`AddressInUseException`) and whether
     the stock message names the port; wrap only if it does not (mechanic (b); Gate 1).
  3. **The console-exporter span shape** for the Step 6 no-span assertion — what an HTTP-server span
     for a probe path would look like in Console-mode stdout, so the negative assertion is precise
     (final gate).

**(j) `InternalsVisibleTo` to `Cloudstrap.Worker.Tests` only** (suite precedent) — the internal
listener and validator are directly testable; no cross-package IVT (the spec's resolved
`InternalsVisibleTo` note: none needed into #2/#4).

**Target consumer `Program.cs`** (the spec sketch — also the demo host, Step 5, and the README example,
Step 4):

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.AddCloudstrapKeyVault();          // optional, explicit (#4)
builder.UseCloudstrapObservability();     // explicit (#2) — chain .AddAzureMonitor() (#3) if wanted
builder.AddCloudstrapWorker();            // this package: core + correlation + health listener
builder.Services.AddHostedService<MyWorker>();
builder.Services.AddHealthChecks().AddCheck("queue", …, tags: [CloudstrapHealthCheckTags.Readiness]);
await builder.Build().RunAsync();
```

---

## Slice 1 — One `AddCloudstrapWorker` call gives a generic host truthful, configurable, fail-fast container probes ⚠️ PUBLIC-API / D-1-IMPLEMENTATION RISK AREA

---

## Step 1 — One call on a plain generic host wires the worker bootstrap: the `Cloudstrap` section validated eagerly and fail-fast, core + correlation registered idempotently, the stock health-check builder additive, the listener registered exactly once, `Cloudstrap:Worker` bound/validated with the callback winning — and the worker app itself carries no ASP.NET pipeline (AC-WK1; AC-WK2's default-port clause; AC-WK5's registration half)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Worker/Cloudstrap.Worker.csproj` *(create)* — Sdk project, `TargetFramework=net10.0`,
  `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`;
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (the suite's **fifth** — D-1/finding 5);
  `<ProjectReference>` to `..\Cloudstrap.Core\`, `..\Cloudstrap.Observability\`,
  `..\Cloudstrap.Extensions\`; `<InternalsVisibleTo Include="Cloudstrap.Worker.Tests" />` (mechanic (j));
  **zero `PackageReference`**. Description/tags/README metadata land in Step 4 (the #5/#6 precedent —
  packable from day one).
- `src/Cloudstrap.Worker/WorkerOptions.cs` *(create)* — `public sealed`, the D-3 shape verbatim:
  `const string SectionName = "Cloudstrap:Worker"`; `HealthPort : int = 9000` (`[Range(1, 65535)]`);
  `HealthListenAddress : string = "*"` (`[Required]`). XML docs state: `"*"` = all interfaces (the
  container-probe reality — orchestrators reach the pod IP, not loopback), `"localhost"` = the explicit
  dev-time loopback override replacing the source's `EnvironmentIsLocal()` sniff; paths and the kill
  switch live in Core's `Cloudstrap:HealthChecks`, never duplicated here (D-3).
- `src/Cloudstrap.Worker/WorkerOptionsValidator.cs` *(create)* — mechanic (a): `internal sealed
  partial`, `[OptionsValidator]`, `IValidateOptions<WorkerOptions>`.
- `src/Cloudstrap.Worker/WorkerHealthListener.cs` *(create — this step: the hosted-service shell only)* —
  `internal sealed : IHostedService` per mechanic (b); Step 1 registers it and proves registration
  semantics; its live HTTP behavior is Step 2's cycle (`StartAsync` may throw `NotImplementedException`
  or bind minimally — executor's choice, as long as Step 1's tests don't need it started; the Step 1
  host-boot tests that would start it use `Cloudstrap:HealthChecks:Enabled=false` or a free port).
- `src/Cloudstrap.Worker/HostApplicationBuilderExtensions.cs` *(create)* —
  `public static IHostApplicationBuilder AddCloudstrapWorker(this IHostApplicationBuilder builder, Action<WorkerOptions>? configure = null)`:
  guard clause; `builder.Properties` run-once marker (mechanic (d) — second call no-op);
  eager `builder.Configuration.GetCloudstrapOptions()` (mechanic (c) — the AC-WK1 fail-fast);
  `services.AddCloudstrapCore()` + `services.AddCloudstrapCorrelation()` (idempotent, #1/#2);
  `AddOptions<WorkerOptions>().BindConfiguration(...).ValidateOnStart()` + `TryAddEnumerable` validator
  + `PostConfigure(configure)` when non-null (callback runs after binding and wins); stock
  `services.AddHealthChecks()` (additive `IHealthChecksBuilder` — Aspire posture);
  `AddHostedService<WorkerHealthListener>()` **only when** the eagerly-read
  `Cloudstrap:HealthChecks:Enabled` is true (AC-WK5's registration half). Registers **no** observability,
  no KeyVault, no auth — the D-2 unbundling; the XML docs name the two explicit sibling calls and why
  (the `CloudstrapObservabilityBuilder` return stays reachable for `.AddAzureMonitor()` and
  owner/contribute mode; KeyVault ownership stays visible — "Cloudstrap's or Aspire's, not both").
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/Cloudstrap.Worker.Tests.csproj` *(create)* — `net10.0`,
  `<ProjectReference>` to the package, version-less `<PackageReference>`s
  `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Binder`,
  `Microsoft.Extensions.Hosting` (all already CPM-pinned; NUnit/MTP wiring inherited from
  `src/Test/Directory.Build.props` via the `.Tests` suffix).
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/Infrastructure/WorkerTestHost.cs` *(create)* — the fixture
  helper: builds a real generic host from `Host.CreateApplicationBuilder` + an in-memory `Cloudstrap:`
  dictionary (valid `Application` values — neutral fixtures: `SystemName=contoso`,
  `SubsystemName=widgets`, `SubsystemType=worker`, `EnvironmentTier=Local`), a free-loopback-port
  helper (mechanic (e)), optional configure callback, and start/stop lifecycle helpers returning an
  `HttpClient` targeting `http://127.0.0.1:{port}`.
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/AddCloudstrapWorkerTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under the
  `Test\UnitTest` solution folder (same nesting as the existing nine).

**RED** *(write these tests first; for a brand-new project the honest first failure is the test project
failing to compile against missing types — the plan-5/plan-6 precedent — followed by real red runs once
the types exist)*:
- Unit test file: `AddCloudstrapWorkerTests.cs`
  - `AddCloudstrapWorker_OnNullBuilder_ThrowsArgumentNullException` (guard clause).
  - `AddCloudstrapWorker_WithInvalidCloudstrapSection_ThrowsAtTheCall` — a missing/invalid
    `Cloudstrap:Application` section throws `ConfigurationValidationException` **at the
    `AddCloudstrapWorker` call**, before the host is built (AC-WK1's eager fail-fast, mechanic (c)).
  - `AddCloudstrapWorker_RegistersCoreOptionsCorrelationAndTheListener` — after the call:
    `IOptions<CloudstrapOptions>`/`IOptions<HealthChecksOptions>` resolve, `ICorrelationContextAccessor`
    resolves, and exactly one hosted-service descriptor is `WorkerHealthListener` (AC-WK1).
  - `AddCloudstrapWorker_ConsumerHealthCheckRegistrationIsAdditive` — a consumer
    `AddHealthChecks().AddCheck(...)` before **and** after the call both land in the same registry
    (stock builder semantics, Aspire posture).
  - `AddCloudstrapWorker_CalledTwice_RegistersExactlyOneListener` — mechanic (d): second call no-op,
    one hosted-service descriptor (spec edge case).
  - `AddCloudstrapWorker_BuiltHost_HasNoAspNetRequestPipeline` — the built host's root provider has no
    `IServer` registration: the app itself gets no ASP.NET pipeline (AC-WK1's last clause; the D-1
    Kestrel lives *inside* the listener, not in the app's DI).
  - `WorkerOptions_Defaults_Port9000AllInterfaces` — with no `Cloudstrap:Worker` section the bound
    options carry `HealthPort=9000` (AC-WK2's founding default, asserted as the bound value —
    mechanic (e)) and `HealthListenAddress="*"`.
  - `WorkerOptions_ConfigureCallback_RunsAfterBindingAndWins` — configuration sets one port, the
    callback sets another → the callback's value is resolved (spec sketch: "runs after binding and
    wins").
  - `AddCloudstrapWorker_HealthPortOutOfRange_FailsStartupNamingTheMember` — `HealthPort=0` →
    starting the host throws `OptionsValidationException` naming `HealthPort` (`ValidateOnStart`,
    mechanic (a)).
  - `AddCloudstrapWorker_WithHealthChecksDisabled_RegistersNoListener` — `Cloudstrap:HealthChecks:Enabled=false`:
    zero `WorkerHealthListener` descriptors; the host builds and runs otherwise unaffected (AC-WK5's
    registration half; the never-bound half is Step 3's).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.Worker.Tests\bin\Debug\net10.0\Cloudstrap.Worker.Tests.exe --filter "AddCloudstrapWorkerTests"
  ```

**GREEN**: the Scope items. Full XML docs on every public member — `AddCloudstrapWorker` names the
configuration sections it owns (`Cloudstrap:Worker`) and consumes (`Cloudstrap:HealthChecks`, and via
composition `Cloudstrap:Logging`/`OpenTelemetry`/`Correlation`/`KeyVault` — never redefined), the D-2
sibling-call convention, and the fail-fast posture.

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: a plain generic host now gains the whole worker bootstrap from one call —
   eagerly validated configuration, idempotent core/correlation, an additive health-check builder, the
   listener registered exactly once and only when enabled, bound-and-validated `Cloudstrap:Worker`
   options with a winning callback, and no ASP.NET pipeline on the app itself — none of which existed
   before.
2. Full-suite check (mechanic (h)) — all green (the new exe joins the set); zero build warnings;
   `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Worker.*.nupkg` appears under
   `src/Cloudstrap.Worker/bin/Release/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — The worker answers container probes over real HTTP: #4's probe implementation live on the configured port and paths with the framework's semantics, exactly two endpoints (unknown paths 404), and zero probe-evaluation code of our own (AC-WK2, AC-WK3, AC-WK7) ⚠️ *(Risk Area: the D-1 transport implementation — mechanic (b))*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Worker/WorkerHealthListener.cs` *(modify)* — the full mechanic (b) implementation:
  inner `WebApplication.CreateEmptyBuilder` + `UseKestrelCore` on
  `http://{HealthListenAddress}:{HealthPort}`; bridged parent `IConfiguration`,
  `HealthCheckService` and `ILoggerFactory`; `AddRouting()`; pipeline `UseRouting()` →
  `MapCloudstrapHealthChecks()`; graceful stop/dispose on the hosted-service lifecycle. No catch
  around start (the AC-WK9 posture — asserted in Step 3).
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/WorkerProbeEndpointTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/Infrastructure/WorkerTestHost.cs` *(modify)* — start the
  host for real and expose the probe `HttpClient`.

**RED** *(write these tests first, run them, confirm they fail — every assertion is real HTTP against
the running listener on a free loopback port, mechanic (e))*:
- Unit test file: `WorkerProbeEndpointTests.cs`
  - `Probes_WithNoChecksRegistered_BothAnswer200Healthy` — GET `/healthz` and `/ready` → 200, body
    `Healthy` (framework semantics: zero matching checks = Healthy; parity with #4's web-host probes —
    AC-WK2, mechanic (i.1); spec edge case).
  - `Probes_WithHealthyTaggedChecks_Answer200WithTheFrameworkBody` — one `live`-tagged + one
    `ready`-tagged healthy check registered by the "consumer" on the stock builder → both probes 200
    `Healthy` through the **host's** `HealthCheckService`, bridged (AC-WK2's "re-implements no
    probe-evaluation logic" made observable: the checks were registered on the parent host, never on
    the inner one).
  - `Probes_AnswerOnTheConfiguredPortAndPaths` — `Cloudstrap:Worker:HealthPort={free}` +
    `Cloudstrap:HealthChecks:LivenessPath=/alive` + `ReadinessPath=/accepting` → both overridden paths
    answer on the configured port; the default paths 404 (AC-WK3).
  - `Listener_BindsOnlyTheConfiguredPort` — the listener's bound address set carries exactly the one
    configured port (mechanic (f)'s assertion seam — AC-WK3's "nothing on 9000" without a live probe
    of a port this suite does not own, mechanic (e)).
  - `Probe_UnknownPathOnTheHealthPort_Returns404` — GET `/{random}` on the health port → 404: the
    health port exposes exactly the two probe endpoints and nothing else (AC-WK7).
  - `WorkerAssembly_OwnsNoProbeEvaluationLogic` — reflection over the package assembly: no type
    implements `IHealthCheck` or `IHealthCheckPublisher`, and no type name contains `Probe` or
    `HealthCheckService` (the AC-WK2 rider; the source's unconditional-200 `HealthCheckService`
    provably not ported — made a permanent guard in Step 4).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Worker.Tests\bin\Debug\net10.0\Cloudstrap.Worker.Tests.exe --filter "WorkerProbeEndpointTests"
  ```

**GREEN**: the Scope items — the whole listener is bridging + composition; the probes themselves are
`MapCloudstrapHealthChecks` exactly as shipped (D-1's "one probe implementation" — worker and web-host
probes share paths, tags, status mapping, `Enabled` gate and the anonymous/short-circuit posture).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a headless generic host now serves the standard Cloudstrap container probes
   over real HTTP on a configurable port with configurable paths, evaluated by the framework against
   the host's own check registry, with exactly two endpoints — the gap `MapCloudstrapHealthChecks`
   left for generic hosts is closed with zero probe code of our own.
2. Full-suite check (mechanic (h)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 3 — Probes tell the truth and failure is loud: the readiness flip both directions under the tag contract, untagged checks served by neither, degraded stays passing, disabled means never bound, the loopback override with zero environment sniffing, and an occupied port fails the host naming it (AC-WK4, AC-WK5, AC-WK6, AC-WK9)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Worker/WorkerHealthListener.cs` *(modify — only if the Step 2 implementation needs
  the AC-WK9 message wrapper per mechanic (i.2); otherwise no production change: this step's behaviors
  fall out of Steps 1–2 by construction and are pinned here)*
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/Infrastructure/ToggleHealthCheck.cs` *(create)* — a
  test-owned `IHealthCheck` whose result a test flips at runtime (`Healthy`/`Degraded`/`Unhealthy`).
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/WorkerProbeTruthTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `WorkerProbeTruthTests.cs`
  - `ReadyProbe_FlipsTo503WithAFailingReadyCheckWhileHealthzStays200_AndRecovers` — a healthy
    `live`-tagged check + a `ToggleHealthCheck` tagged `ready`: both 200 → toggle Unhealthy →
    `/ready` 503 while `/healthz` stays 200 → toggle back → `/ready` 200 (AC-WK4, the flip **both
    directions** and the `CloudstrapHealthCheckTags` separation — the source's unconditional 200
    provably dead).
  - `Probes_UntaggedCheck_IsServedByNeitherProbe` — an untagged Unhealthy check leaves both probes at
    200 (the tag predicate is the contract — spec edge case, documented in Step 4's README).
  - `ReadyProbe_DegradedCheck_Answers200WithDegradedBody` — Degraded → 200, body `Degraded`
    (framework writer; orchestrators treat non-503 as passing — spec edge case, mechanic (i.1)).
  - `Listener_WithHealthChecksDisabled_NeverBindsThePort` — `Cloudstrap:HealthChecks:Enabled=false`
    with a configured free port: the host runs, a GET to that port throws `HttpRequestException`
    (connection refused — the port this test owns; AC-WK5's never-bound half).
  - `Listener_LocalhostOverride_AnswersOnLoopback` — `HealthListenAddress=localhost` → probes answer
    on `127.0.0.1` and the bound addresses are loopback-only (mechanic (f); AC-WK6's override half).
  - `Listener_DefaultAddress_ComposesAllInterfacesBinding` — the default `"*"` composes an
    all-interfaces binding (mechanic (f)'s assertion seam; AC-WK6's default half — the live
    all-interfaces reality is the container's, documented; no LAN probing in unit tests).
  - `WorkerAssembly_ContainsNoEnvironmentSniffing` — reflection sweep: no method/type name matching
    `(?i)environmentislocal|isrunningin`, and the shipped assembly's bind decision consumes only
    `WorkerOptions` (AC-WK6's last clause — the hosting-posture ruling made structural; permanent
    guard in Step 4).
  - `Listener_PortAlreadyOccupied_FailsHostStartupNamingThePort` — a test-held `TcpListener` occupies
    the configured port: `host.StartAsync()` throws, the exception (or its inner chain) names the
    port, and the host is **not** left running (AC-WK9 — finding 2's silent-unprobed-worker defect
    dead; mechanic (i.2): the executor pins the confirmed exception type in the assertion and reports
    it at Gate 1).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Worker.Tests\bin\Debug\net10.0\Cloudstrap.Worker.Tests.exe --filter "WorkerProbeTruthTests"
  ```

**GREEN**: minimal — these behaviors are the Steps 1–2 design's consequences, pinned red-first; the
only permitted production change is the mechanic (i.2) message wrapper if the stock bind exception
does not name the port.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: worker probes now provably reflect the registered checks in both directions
   under the tag contract, degraded stays passing, disabling health checks removes the listener and
   the port entirely, the bind address is an explicit option with no environment sniffing anywhere in
   the assembly, and a worker can no longer run silently unprobed — startup fails naming the port.
2. Full-suite check (mechanic (h)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the entry point, the options shape and the D-1 listener are frozen *(covers Steps 1–3)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **public API one-way door** — `AddCloudstrapWorker(this
IHostApplicationBuilder, Action<WorkerOptions>?)` returning `IHostApplicationBuilder`, `WorkerOptions`
(`HealthPort`/`HealthListenAddress`, `SectionName`) and the two-knob `Cloudstrap:Worker` section
against the spec's Public API Sketch **verbatim** (D-2/D-3 conformance — any deviation needs naming);
the D-2 unbundling honored (no observability, no KeyVault, no auth registered by this call) · the
**D-1 implementation** — mechanic (b)'s inner-host bridging (`IConfiguration`, `HealthCheckService`,
`ILoggerFactory`), reviewed as the health-transport door's as-built shape; the **fifth
`Microsoft.AspNetCore.App` framework reference** (csproj created in Step 1; runtime-image consequence
documented at Step 4) · **zero `PackageReference`** in the package csproj (the spec's headline
dependency fact) · mechanic (d)'s no-op-second-call semantics (vs #6's throwing pipeline guard —
confirm the spec edge case's reading) · mechanics (e)/(f) — the free-port strategy and the
composed-address proof of the all-interfaces default (confirm, or direct a live-bind assertion) ·
mechanics (i.1)/(i.2) executor reports — the framework writer bodies and the confirmed occupied-port
exception type/message.

- [x] Behavioral verification: test exe output shows — the eager fail-fast at the call, the
  registration set with idempotence and the no-`IServer` proof, the bound defaults (9000/`"*"`) and
  the winning callback, the out-of-range fail-fast, the disabled no-registration (Step 1); both probes
  live over real HTTP with framework bodies through the bridged host registry, the port+path
  overrides, the single-port binding, the unknown-path 404 and the no-probe-logic sweep (Step 2); the
  503 flip both directions with tag separation, untagged-neither, degraded-200, disabled-never-bound,
  the loopback override + composed-default assertions, the no-sniffing sweep and the occupied-port
  startup fault naming the port (Step 3). *(Executor reports: mechanic (i.1) — framework writer bodies
  `Healthy`/`Degraded` confirmed with Healthy/Degraded→200, Unhealthy→503; mechanic (i.2) — Kestrel's
  stock bind exception names the address+port, no wrapper added.)*
- [x] Code review: entry-point/options signatures vs the spec sketch, verbatim; `internal` by default
  (`WorkerHealthListener`, validator) + sealed + full XML docs;
  `dotnet list src/Cloudstrap.Worker/Cloudstrap.Worker.csproj package` → **zero package references**,
  three project references; the listener composes `MapCloudstrapHealthChecks` and owns no probe
  evaluation, no paths, no tags of its own.
- [x] User approved — implementation may continue past this gate (2026-08-22)

---

## Slice 2 — Publishable, permanently guarded, composable beside Aspire-style pipelines, and demonstrated live by a running demo worker

---

## Step 4 — The package is publishable and guarded forever: metadata, README (incl. the D-5 crash-flush guidance and the one-owner Aspire rule), contribute-mode composition proven, and tripwires on the surface, the closure and the forbidden identifiers (AC-WK8 composition half, AC-WK10, AC-ASP2, AC-A3)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Worker/Cloudstrap.Worker.csproj` *(modify)* — `<Description>` (worker-service
  bootstrap for the generic host: validated configuration, correlation, additive health-check
  registration and a Kestrel-based health listener serving `/healthz` + `/ready` from the registered
  checks on a configurable port — one call and one `Cloudstrap:Worker` section),
  `<PackageTags>$(PackageTags);worker;backgroundservice;healthchecks;probes;generichost</PackageTags>`,
  `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.Worker/README.md` *(create)* — the sub-ten-line `Program.cs` quick start (mirrors
  the Step 5 demo host — the spec's "doubles as the README example", incl. the **D-5 crash-flush
  pattern**: `CloudstrapBootstrapLogger.Create` before the host, dispose after `Build()`, the
  return-code `try/catch/finally` — guidance, deliberately not API); settings table for
  `Cloudstrap:Worker` (`HealthPort` 9000, `HealthListenAddress` `"*"`) plus the consumed sections
  (`Cloudstrap:HealthChecks` `Enabled`/`LivenessPath`/`ReadinessPath`, and via the sibling calls
  `Cloudstrap:Logging`/`OpenTelemetry`/`Correlation`/`KeyVault`) marked "owned elsewhere, never
  redefined" (D-3); the tag contract (`CloudstrapHealthCheckTags`, untagged checks served by neither
  probe — documented); the status mapping incl. Degraded→200; the **one-owner Aspire note** (checks
  additive on the stock builder; a ServiceDefaults worker keeps its own OTel via #2's contribute mode;
  if the consumer hosts probes by other means set `Cloudstrap:HealthChecks:Enabled=false`; zero
  `Aspire.*`); the **"not for web hosts"** warning (a `WebApplication` uses
  `MapCloudstrapHealthChecks` on its real pipeline — running both double-serves probes on two ports;
  spec edge case); the **no-TLS/no-auth posture** of the probe port (orchestrator-internal plain HTTP;
  a worker needing an authenticated surface is a web host — use #4/#5); the loopback override for
  local dev; the configuration-ordering note (mechanic (c): KeyVault first); the framework-reference
  consequence (ASP.NET Core shared framework required — already transitively true via
  Observability/Extensions, finding 5); migration notes (Deliberate Behavior Changes 1–8 — probes
  tell the truth, the bundle unbundled, two paths not three, no env sniffing, bind failure fails the
  host, #4 verb semantics, no config stash, Kestrel not HttpListener).
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/WorkerObservabilityCompositionTests.cs` *(create)* —
  the AC-WK8 composition half.
- `src/Test/UnitTest/Cloudstrap.Worker.Tests/PackageSurfaceTests.cs` *(create)* — permanent guards
  mirroring `Cloudstrap.Mvc.Tests/PackageSurfaceTests.cs`.

**RED** *(composition tests are honest red; the guard tests are tripwires against already-correct code
and may pass immediately — the honest failing state is in the artifacts: before GREEN the Release
nupkg has no README/description/tags; recorded per the plan-2…6 precedent)*:
- Unit test file: `WorkerObservabilityCompositionTests.cs`
  - `Worker_WithUseCloudstrapObservabilityContributeMode_BootsAndServesProbes` — a host calling
    `UseCloudstrapObservability` in **contribute** mode (the Aspire-ServiceDefaults-beside-us shape)
    + `AddCloudstrapWorker`: the host starts and both probes answer — this package adds no exporter
    and no pipeline of its own; exporter-duplication prevention remains #2's own tested contract,
    composed not re-implemented (AC-WK8's composition half; the no-span half lands live in Step 6).
  - `Worker_WithObservabilityOwnerMode_BootsAndServesProbes` — the default composition (the demo
    host's shape) boots identically.
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfWorkerAssembly_MatchTheApprovedClosure` — every referenced assembly
    starts with `System` or `Microsoft.` or equals
    `Cloudstrap.Core`/`Cloudstrap.Observability`/`Cloudstrap.Extensions`; explicitly **zero** names
    starting `Aspire` (AC-ASP2), `Nihdi` (AC-A3), `Serilog`, `OpenTelemetry`, `Duende`, `Hangfire`,
    `Wolverine` (this package composes — it must never grow direct telemetry/messaging references).
  - `PublicTypes_OfWorkerAssembly_ContainNoForbiddenIdentifiers` — no public type/member matches
    `(?i)nihdi|riziv|dynatrace|nservicebus`.
  - `PublicTypes_OfWorkerAssembly_AreSealedOrStaticAndInTheSingleApprovedNamespace` — namespace
    `Cloudstrap.Worker` only; every public class sealed or static; no public interfaces; exactly the
    two public types of the spec sketch (`HostApplicationBuilderExtensions`, `WorkerOptions`).
  - `WorkerAssembly_DeclaresNoProbeEvaluationOrEnvironmentSniffingTypes` — the Step 2/3 sweeps made
    permanent: no `IHealthCheck`/`IHealthCheckPublisher` implementors, no type name containing
    `Probe`/`HealthCheckService`, no member matching `(?i)environmentislocal|isrunningin` (findings
    1/4 guarded forever).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Worker.Tests\bin\Debug\net10.0\Cloudstrap.Worker.Tests.exe --filter "WorkerObservabilityCompositionTests|PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata and write `README.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the composition pair and the four new guards.
2. `dotnet build src/Cloudstrap.sln -c Release` →
   `src/Cloudstrap.Worker/bin/Release/Cloudstrap.Worker.<version>.nupkg`; expand a `.zip` copy →
   contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.Worker.dll` **and** `.xml`; the nuspec
   shows the MIT license expression, description, tags, repository URL, and a dependency list whose
   direct `Cloudstrap.*` entries are exactly Core/Observability/Extensions — no `Aspire.*`, no
   `Nihdi.*`, **zero new external packages** (AC-WK10, AC-ASP2).
3. **AC-WK10 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Worker, src/Test/UnitTest/Cloudstrap.Worker.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the
   hits, as in plans 2–6).
4. **Closure check**: `dotnet list src/Cloudstrap.Worker/Cloudstrap.Worker.csproj package` reviewed
   against the spec's Dependencies table — three project references, the framework reference,
   nothing else.
5. Full-suite check (mechanic (h)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 5 — The demo suite gains a running headless worker: `Cloudstrap.Demo.Worker` on health port 5350 with the five-line bootstrap, the D-5 crash-flush pattern and Console-mode observability — probes proven anonymously over real HTTP by a new E2E fixture while every existing E2E test stays green (AC-WK11 first half; demonstration slice, D-4)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/demo/Worker/Cloudstrap.Demo.Worker.csproj` *(create)* — `Microsoft.NET.Sdk` (a **generic host**,
  not Sdk.Web — the point of the deliverable), `net10.0`, `<ProjectReference>` to `Cloudstrap.Worker`
  and `Cloudstrap.Observability` (for `CloudstrapBootstrapLogger` — already in the closure); no
  `.Tests` suffix so it stays a plain app; `IsPackable=false` inherited from
  `src/demo/Directory.Build.props`.
- `src/demo/Worker/Program.cs` *(create)* — the README's consumer example, live (D-4/D-5): fail-fast
  `GetCloudstrapOptions()`, `CloudstrapBootstrapLogger.Create` + the return-code `try/catch/finally`
  crash-flush pattern (the D-5 guidance, demonstrated — with the explanatory comment),
  `UseCloudstrapObservability()` (Console mode via config — stdout capturable, the Api-host
  precedent), `AddCloudstrapWorker()`, `AddHostedService<PeriodicWorker>()`, one always-healthy
  `self` check tagged `Liveness` — deliberately under ten lines of composition.
- `src/demo/Worker/PeriodicWorker.cs` *(create)* — a trivial periodic `BackgroundService` logging a
  neutral heartbeat (`Demo worker heartbeat {n}`) every few seconds — the "plain `BackgroundService`"
  the spec's Out of Scope mandates (no Hangfire, no messaging) and the stdout signal the E2E asserts.
- `src/demo/Worker/appsettings.json` *(create)* — `Cloudstrap:Application` (`SystemName: "demo"`,
  `SubsystemName: "worker"`, `SubsystemType: "worker"`, `EnvironmentTier: "Local"`),
  `Cloudstrap:OpenTelemetry: { "Mode": "Console" }`, `Cloudstrap:Worker: { "HealthPort": 5350,
  "HealthListenAddress": "localhost" }` with the mechanic (g) comment (loopback = the documented
  dev-time override; the shipped default is all-interfaces for container probes).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/WorkerHostTests.cs` *(create)* — plain `HttpClient` fixture
  (no Playwright page — a worker has no browser surface): `[OneTimeSetUp]` boots the host per
  mechanic (g) —
  `SutProcess.Start("http://127.0.0.1:5350", ["--Cloudstrap:Worker:HealthPort=5350"], "src/demo/Worker/Cloudstrap.Demo.Worker.csproj")`
  (the `BlazorServerTests` boot-by-project-path precedent; the explicit port argument because a
  generic host ignores `ASPNETCORE_URLS` — the `baseUrl` parameter only feeds the poller), polling
  `/healthz` to readiness with the `WaitUntilReadyAsync` idiom; `[OneTimeTearDown]` disposes it.
  This fixture needs no IdP and touches no fixture-owned host.
- `src/demo/Worker/README.md` *(create)* — the feature-matrix shape (`src/demo/Api/README.md`
  precedent): what the host is (the five-line worker bootstrap incl. the D-5 crash-flush pattern —
  the teaching point), the matrix rows citing the real E2E test names (Step 5's probe/telemetry rows
  now; Step 6 adds the outage row), the loopback-override note, and the running instructions
  (`dotnet run --project src/demo/Worker` — port from `Cloudstrap:Worker:HealthPort`, no
  `ASPNETCORE_URLS`).
- `src/demo/README.md` *(modify)* — port map row (`5350 | Worker demo | WorkerHostTests (or dotnet
  run)`), layout row (`Worker/  Cloudstrap.Demo.Worker  headless worker + health listener (README
  inside)`), architecture note (standalone box — needs no peers), manual-run command line.
- `.vscode/tasks.json` *(modify)* — `build-demo-worker` task (the existing per-app pattern).
- `.vscode/launch.json` *(modify)* — "Demo Worker" configuration (`coreclr`, `preLaunchTask:
  build-demo-worker`, program `src/demo/Worker/bin/Debug/net10.0/Cloudstrap.Demo.Worker.dll`, cwd,
  `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT: Development`, **no** `ASPNETCORE_URLS` — irrelevant to
  a generic host, port comes from appsettings) + added to the "Demo apps (all hosts + IdP)" compound.
- `src/Cloudstrap.sln` *(modify)* — the demo project under the same solution folder as the existing
  demo apps.

**RED** *(write these tests first, run them, confirm they fail — before GREEN the demo project does
not exist, so `SutProcess.Start` throws its project-path `FileNotFoundException`)*:
- E2E test file: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/WorkerHostTests.cs`
  - `WorkerHost_Probes_AnswerAnonymouslyWithFrameworkBodies` — GET `http://127.0.0.1:5350/healthz`
    and `/ready` → 200 with the framework's one-word body, no auth of any kind (AC-WK2 live, D-4's
    first headline behavior); GET an unknown path on 5350 → 404 (AC-WK7 live).
  - `WorkerHost_Heartbeat_AndStartupLog_AreCapturedFromStdout` — the process's `CapturedOutput`
    contains the bootstrap-logger startup line and at least one `PeriodicWorker` heartbeat: the
    generic host runs a real `BackgroundService` while probing, and Console-mode observability is
    live and capturable (the fixture posture Step 6's no-span assertion builds on).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "WorkerHostTests"
  ```

**GREEN**: the Scope items. **Every pre-existing E2E test must stay green unchanged** — the new host is
additive on its own port; the IdP, Api, Bff, Mvc and BlazorServer fixtures are untouched. *(If any
existing test is disturbed, the executor reports it at the gate rather than weakening the assertion.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the two new `WorkerHostTests` pass **and all pre-existing E2E tests pass unchanged**
   (build first; one-time `playwright.ps1 install chromium` if needed — the suite still boots its
   browser fixtures).
2. Manual smoke (optional but recorded): `dotnet run --project src/demo/Worker` then
   `curl http://127.0.0.1:5350/healthz` → 200 `Healthy`; Ctrl+C stops cleanly (graceful listener
   shutdown).
3. Full-suite check (mechanic (h)) — all green; `dotnet format` exit 0; Release build of the demo
   project packs **nothing** (`IsPackable=false` inherited).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — The outage drill, live: a sentinel file flips `/ready` to 503 while `/healthz` stays 200 and recovery follows its removal, probe polling leaves no trace spans in the captured telemetry — and the demo-vehicle table gains its worker row (AC-WK4 live, AC-WK8 live, AC-WK11; the rule-9 doc addition)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/demo/Worker/DemoOutageHealthCheck.cs` *(create)* — the D-4 vehicle detail: `internal sealed :
  IHealthCheck` reporting Unhealthy while the sentinel file at `Demo:OutageSentinelPath` exists
  (Healthy otherwise) — the E2E suite's process-external toggle; registered in `Program.cs` tagged
  `CloudstrapHealthCheckTags.Readiness`. Lives in the **demo app** (consumer code), never in the
  package — the Step 4 no-`IHealthCheck` guard keeps that true forever.
- `src/demo/Worker/Program.cs` *(modify)* — register the outage check
  (`.AddCheck<DemoOutageHealthCheck>("demo-outage", tags: [CloudstrapHealthCheckTags.Readiness])`).
- `src/demo/Worker/appsettings.json` *(modify)* — a default `Demo:OutageSentinelPath` (overridden by
  the E2E via argument).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/WorkerHostTests.cs` *(modify)* — the `[OneTimeSetUp]` boot
  gains `--Demo:OutageSentinelPath=<per-run temp file path>` in its application arguments; two new
  tests.
- `src/demo/Worker/README.md` *(modify)* — the outage-drill matrix row + the no-probe-spans row,
  citing the new E2E test names.
- `CLAUDE.md` *(modify — docs, human-reviewed at the final gate)* — workflow rule 9's vehicle table:
  the "worker/messaging features → the demo app their deliverable designates" clause becomes
  "**worker / headless-hosting features → `Cloudstrap.Demo.Worker`** · messaging features → the demo
  app their deliverable designates" (the D-4 row #14/#16 later extend); the Project Structure demo
  tree gains the `Worker/` line (5350).
- `.claude/agents/planner.md` *(modify — docs)* — rule 15's vehicle clause gains the same worker row.
- `.claude/templates/plan-template.md` *(modify — docs)* — the demonstration-slice comment's vehicle
  list gains the same worker row.

**RED** *(write these tests first, run them, confirm they fail — the outage check does not exist yet,
so `/ready` stays 200 with the sentinel present)*:
- E2E test file: `WorkerHostTests.cs`
  - `WorkerHost_ReadyFlipsTo503WhileTheOutageSentinelExists_AndRecovers` — the D-4 headline drill,
    live against the running process: probes 200 → the test **creates the sentinel file** → poll
    until `/ready` → **503** while `/healthz` stays **200** (the AC-WK4 flip + tag contract through a
    real orchestrator-style HTTP surface) → the test **deletes the sentinel** → poll until `/ready`
    → 200 (recovery, both directions; `try/finally` deletes the sentinel so a failed run cannot
    poison later fixtures).
  - `WorkerHost_ProbePolling_ProducesNoTraceSpans` — after the suite's accumulated probe polling,
    the process's `CapturedOutput` contains **no** console-exporter span for the probe paths
    (`/healthz`/`/ready`) while it does contain other telemetry (the Step 5 heartbeat/startup lines —
    proving capture works, so the negative is meaningful): #2's config-driven `TraceNoiseFilter`
    covers the worker listener for free because it uses the shared `Cloudstrap:HealthChecks` paths
    (AC-WK8's no-span half, live; mechanic (i.3): the executor pins the confirmed console-span shape
    in the assertion and reports it at the gate).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "WorkerHostTests"
  ```

**GREEN**: the Scope items — the demo check, the registration, the doc rows. The CLAUDE.md /
planner.md / plan-template vehicle-row edits are surgical (one clause + one tree line each), reviewed
verbatim at the final gate; no other instruction content changes.

**DB changes**: none.

**VERIFY**:
1. E2E exe → all four `WorkerHostTests` pass **and every pre-existing E2E test passes unchanged**.
2. Manual smoke (optional but recorded): `dotnet run --project src/demo/Worker`, create the sentinel
   file → `/ready` 503, `/healthz` 200; delete it → `/ready` 200.
3. Full-suite check (mechanic (h)) — all green; `dotnet format` exit 0.
4. Doc sweep: the CLAUDE.md rule-9 clause, planner rule-15 clause and plan-template comment all name
   `Cloudstrap.Demo.Worker`; `src/demo/README.md` port map and the demo README matrix are consistent
   with the as-built tests.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #7 complete *(covers Steps 4–6; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires
the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

⚠️ **Risk areas at this gate**: the **packaging check** — the expanded Release
`Cloudstrap.Worker.<version>.nupkg` (README/icon/dll/xml, MIT, exactly the three `Cloudstrap.*` direct
dependencies, zero new external packages) and the confirmation that nothing under `src/demo` packs ·
the **Aspire posture as documented** — the README's one-owner rule, contribute-mode composition and
the AC-ASP2 permanent guard · the **rule-9 vehicle-table edits** to CLAUDE.md / planner.md /
plan-template — instruction files, reviewed verbatim · mechanic (i.3)'s executor report (the console
span shape behind the no-span assertion).

- [ ] Behavioral verification: the four `WorkerHostTests` pass (probes anonymous + framework bodies,
  captured heartbeat telemetry, the sentinel 503/200 flip with recovery, the no-probe-spans proof);
  **all pre-existing E2E tests pass unchanged**; the composition pair and the four
  `PackageSurfaceTests` guards are green; the expanded Release `.nupkg` contents were reviewed; the
  identifier sweep is empty (self-referential hits only); the full-suite check (build + 10 unit exes
  + E2E exe + `dotnet format --verify-no-changes`) is green end to end.
- [ ] Spec acceptance sign-off: walk **AC-WK1…AC-WK11 + AC-ASP2 + AC-A3** against the step evidence
  using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop /
  Out-of-Scope lists was resurrected (no config stash, no env sniffing, no `/health` aggregate path,
  no `HttpListener`, no crypto pin, no crash-flush API, no standalone listener entry point, no
  TLS/auth on the probe port, no Hangfire/Messaging wiring, no re-implementation of #2/#3/#4
  surfaces, zero `Aspire.*`, zero `Nihdi.*`) and that every De-NIHDI row is closed
  (`UseWorkerForNihdi` → `AddCloudstrapWorker`, three probe prefixes → the two standard configurable
  paths, `EnvironmentIsLocal()` → the explicit option, no company headers, neutral fixtures).
- [ ] Docs review: `src/Cloudstrap.Worker/README.md` matches as-built behavior (quick start mirrors
  the demo `Program.cs` incl. the D-5 crash-flush pattern, settings tables, tag contract with the
  untagged-check note, one-owner Aspire rule, not-for-web-hosts and no-TLS warnings, migration
  notes); `src/demo/Worker/README.md` matrix rows cite the real E2E test names; `src/demo/README.md`
  port map (5350), layout and manual-run rows are accurate; the VS Code launch config + compound and
  the `build-demo-worker` task work (one F5 boots the worker with the rest); the CLAUDE.md rule-9 /
  planner rule-15 / plan-template worker rows read exactly as decided in D-4.
- [ ] User approved — deliverable #7 done; project-manager flips the ROADMAP row to ✅.
