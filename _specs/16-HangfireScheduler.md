# Spec: Hangfire Scheduler — `Cloudstrap.Hangfire` (Roadmap Deliverable #16)

> Status: **APPROVED 2026-09-26 — zero Open Questions (all seven resolved by the user 2026-09-26, in
> each case accepting the analyst's recommendation; see Decision Log D-1–D-7); planner-ready.**
>
> This is a **port with targeted redesigns** of a self-contained, well-tested source package — not a rebuild.
> The three ideas that make the source valuable survive intact: (1) recurring jobs are persisted against one
> shared dispatcher type, never the implementation type, so a dashboard-only host renders a worker's jobs
> without referencing its assembly; (2) operators retune or disable any job from configuration without a
> redeploy, with unknown time zones and bad cron expressions failing fast; (3) the scheduling host reconciles
> away recurring jobs whose task was deleted from code. What changes is the shape around them.
>
> Sources: `_plans/ROADMAP.md` §16 (hand-off brief, inventory verified 2026-09-24) · `_specs/Cloudstrap.md`
> (Package Map line 89 "free Hangfire tier only (LGPL noted in docs)", De-NIHDI row 111 environment taxonomy,
> Decisions Made "Hosting targets", Aspire Coexistence AC-ASP1–AC-ASP3, AC-A3) · shape precedents
> `_specs/7-WorkerBootstrap.md` (verdict table, D-1/D-2 single-`Add` worker posture) and `_specs/14-Messaging.md`
> (Decision Log, package-level AC set, "no facade over the bus", D-3 LocalDB posture) · **shipped code read**:
> `src/Cloudstrap.Worker/HostApplicationBuilderExtensions.cs` (marker idempotence, options binding,
> additive `AddHealthChecks`), `src/Cloudstrap.Extensions/DependencyHealthCheckSetup.cs` +
> `CloudstrapHealthProbeMarker.cs`, `src/Cloudstrap.Observability/CloudstrapHealthCheckTags.cs`,
> `CloudstrapActivitySources.cs`, `OpenTelemetryPipeline.cs` (owner/contribute), `CloudstrapObservabilityBuilder.cs`,
> `src/Cloudstrap.BlazorServer/BlazorServerPipelineOptions.cs` + `WebApplicationBuilderExtensions.cs:120`
> (the #12 `tracing.AddSource` contribution precedent), `src/Cloudstrap.WebApi/WebApiPipelineOptions.cs`,
> `src/Cloudstrap.Messaging/CloudstrapMessagingBuilder.cs` + the `AutoProvision ?? IsDevelopment()` pattern,
> `src/Cloudstrap.Core/ApplicationOptions.cs`, `src/Cloudstrap.Authentication.OpenIdConnect/ServiceCollectionExtensions.cs:193`
> (`RoleClaimType = "role"`), `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Infrastructure/SqlServerTestDatabase.cs`,
> `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/{E2eFixture,WorkerHostTests,BlazorServerTests}.cs`,
> `src/demo/{Worker,Api,BlazorServer}/Program.cs` + `appsettings.json`, `src/demo/Shared/IdentityProvider/TestIdentityProviderSeed.cs:101`
> (test user carries `role: tester`), `src/Directory.Packages.props`, `.claude/skills/configure-hangfire/SKILL.md`
> · **source reference repo (read-only)** `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` —
> **every file of `Nihdi.Core.Configuration.Hangfire\` was opened**: `IServiceCollectionExtension.cs`,
> `HangfireForNihdiOptions.cs`, `IBackgroundRecurringTask.cs`, `RecurringJobsScheduler.cs`, `RecurringTaskRunner.cs`,
> `ScheduleTasksUtility.cs`, `IHostExtensions.cs`, `WebApplicationExtensions.cs`, `ForwardedPrefixPathBase.cs`,
> `INihdiBackgroundJobScheduler.cs`, `NihdiBackgroundJobScheduler.cs`, `Authorization\*` (4), `HealthChecks\*` (2),
> the csproj, `README.md`, `stylecop.json`; old Core `Settings\Hangfire\*` (3); `Nihdi.Core.Configuration.Hangfire.Proxy\*`
> (2 + csproj, read-and-route); all 15 test files of `Test\UnitTest\Nihdi.Core.Configuration.Hangfire.Tests\`; the
> call sites `Test\TestProject\src\Host\Worker\Program.cs`, `…\Worker\BackgroundRecurringTask\DoctorCheckerBackGroundRecurringTask.cs`,
> `…\Host\Bff\Program.cs:110–169`.
>
> External evidence gathered 2026-09-26 (nuget.org / GitHub):
> [Hangfire.Core 1.8.25](https://www.nuget.org/packages/Hangfire.Core) — **LGPL-3.0** (Hangfire OÜ multi-licenses:
> LGPL-3.0 by default, commercial Standard/Royalty-free as paid alternatives), released **2026-08-28**, 415 M
> downloads, dependency `Newtonsoft.Json ≥ 9.0.1` (MIT) ·
> [Hangfire.SqlServer 1.8.25](https://www.nuget.org/packages/Hangfire.SqlServer) — LGPL-3.0, 2026-08-28, depends on
> `Hangfire.Core` only — **no SQL client dependency** (the consumer must supply one; confirmed by the source README) ·
> [Hangfire.AspNetCore 1.8.25](https://www.nuget.org/packages/Hangfire.AspNetCore) — LGPL-3.0, 2026-08-28, brings
> `Hangfire.NetCore` + framework-provided `Microsoft.AspNetCore.Antiforgery`/`Http.Abstractions` ·
> [Hangfire.InMemory 1.0.0](https://www.nuget.org/packages/Hangfire.InMemory) — LGPL-3.0 (same multi-license), released
> 2024-09-18, Hangfire OÜ — **verified but not adopted** (see Dependencies) ·
> [Microsoft.Data.SqlClient 7.1.0](https://www.nuget.org/packages/Microsoft.Data.SqlClient) — **MIT**, released 2026-09-18.
> The source pinned 1.8.23; the current family is 1.8.25 (three patch releases in 2026 — actively maintained).
>
> **Risk areas this deliverable touches** — **LGPL-3.0 dependency family** (first non-permissive family in the suite;
> README notice is a hard definition-of-done item; `Hangfire.InMemory` license-verified and deliberately not
> referenced) · **auth risk area** (dashboard authorization — human review; D-1) · **public API one-way doors**:
> `IBackgroundRecurringTask` (D-3), the `Cloudstrap:Hangfire` section shape, and above all
> **`Cloudstrap.Hangfire.RecurringTaskRunner.RunAsync(string, CancellationToken)` — Hangfire persists this type +
> method by name in storage for every recurring job; renaming after ship orphans persisted jobs** ·
> **new dependencies**: `Hangfire.Core`/`.SqlServer`/`.AspNetCore` 1.8.25 + `Microsoft.Data.SqlClient` (MIT; D-4) ·
> **environment sniffing removed** (`PrepareSchema`) · **two-host topology** preserved consciously (dashboard host
> `RunServer=false` sharing storage with a processing worker) · **Aspire overlap**: health check additive, `ActivitySource`
> contributed, `ConnectionStrings:` convention; AC-ASP2 carried · **#18 seam**: `ForwardedPrefixPathBase` routed to
> #18 through a public seam; no `InternalsVisibleTo` grant re-established.
>
> **Standing constraint**: nothing is published to nuget.org yet — breaking changes to shipped packages are allowed.
> This spec identifies **no shipped-package amendment** needed (`AddCloudstrapHangfire` registers hosted services on
> any `IHostApplicationBuilder`; the #7 Worker surface needs no hook). Demo-app and E2E changes are demo material.

## Code-reading findings that shaped this spec

1. **The source's "fail fast" never failed the host.** `ScheduleTasksUtility` runs `ScheduleAllBackgroundRecurringTasks`
   inside an `IHostApplicationLifetime.ApplicationStarted` callback (`ScheduleTasksUtility.cs:17–27`). The generic host
   catches and logs exceptions thrown by started-callbacks; a duplicate job id, an invalid cron or an unknown IANA zone
   therefore produced one error log line and a host that **kept running with no recurring jobs scheduled**. The
   duplicate-id pre-check keeps the store consistent (nothing half-applied), but the process itself stays up, silently
   unscheduled. Cloudstrap schedules from a hosted service's `StartAsync`, where an exception faults host startup
   (AC-HF3/AC-HF4/AC-HF6; Deliberate Change 1).
2. **The roadmap's "no authorization filter applied" is imprecise — and the truth is worse.** `UseHangfireForNihdi`
   passes a fresh `DashboardOptions` to `UseHangfireDashboard` (`WebApplicationExtensions.cs:48–51`), whose
   `Authorization` defaults to Hangfire's `LocalRequestsOnlyAuthorizationFilter` (the source README §9.1 confirms
   "localhost only"). On the supported hosting matrix that filter is either always-deny (behind an App Service
   front-end or an ingress, every request is remote) or, worse, always-allow (a same-pod sidecar/proxy arrives on
   loopback). Neither is an acceptable posture; the path is dropped (Deliberate Change 2).
3. **The two "host-type" dashboard filters are one rule.** `TrustedSubsystemHangfireDashboardAuthorizationFilter`
   = `IsAuthenticated`; `RoleHangfireDashboardAuthorizationFilter` = `HangfireDashboardAccessEvaluator.IsAuthorized`
   = `IsAuthenticated && (role empty || IsInRole(role))`. The role filter with an empty role **is** the
   trusted-subsystem filter. The Cfe/Bff distinction was vocabulary, not behavior — and vocabulary the De-NIHDI
   checklist removes. One authenticated-plus-optional-role rule replaces both (D-1).
4. **The dashboard is a set of endpoints, and ASP.NET Core already has an authorization system for endpoints.**
   `Hangfire.AspNetCore` ships `IEndpointRouteBuilder.MapHangfireDashboard(...)` returning an
   `IEndpointConventionBuilder`, so `RequireAuthorization(...)` applies the stock authorization middleware: cookie
   schemes challenge with a login redirect, JWT schemes with 401, roles come from the scheme's `RoleClaimType`
   (the #10 OIDC package sets `"role"`). This dissolves the brief's "hook point relative to `UseAuthentication`/
   `UseAuthorization`" risk: both shipped composites expose `ConfigureEndpoints : Action<IEndpointRouteBuilder>`,
   mapped **after** the auth middleware (`WebApiPipelineOptions.cs:57`, `BlazorServerPipelineOptions.cs:71`). The
   source's four authorization files become one internal defense-in-depth filter plus a stock policy (D-1).
5. **`INihdiBackgroundJobScheduler` hides nothing.** Its two members are `IBackgroundJobClient.Enqueue`/`Schedule`
   with identical `Expression<Func<Task>>` signatures; the expression is still a Hangfire concept (public method,
   serializable arguments), so a consumer "without a Hangfire dependency" still obeys every Hangfire rule and cannot
   reach the other eight client operations (continuations, `Delete`, `Requeue`, typed `Enqueue<T>`). Exactly the
   #14 "no facade over the bus" case; dropped.
6. **The environment taxonomy shows up once**: `CanPrepareSchema = !(TST|VAL|PRD)` (`IServiceCollectionExtension.cs:85–88`).
   It becomes the explicit `Storage:PrepareSchema : bool?` with a `Development` default — the #14 `AutoProvision`
   pattern verbatim, the #1 `IsRunningInAks` drop precedent.
7. **The two-host topology rests on two deliberate choices that must survive together**: `UseSimpleAssemblyNameTypeSerializer`
   (type names persisted without version/culture/token, so the dashboard host's `Cloudstrap.Hangfire` build can
   differ from the worker's) and the `RecurringTaskRunner` dispatcher (`RecurringJobsScheduler.cs:128–139`).
   The dashboard host registers storage only (`runServer:false`, `Bff\Program.cs:128–132`) and **never schedules**
   — the source README's golden rule 1, enforced there by documentation only. Cloudstrap enforces it structurally:
   scheduling is a consequence of running the processing server, so a storage-only host cannot schedule (D-2).
8. **Orphan reconciliation assumes exactly one scheduling host per storage.** `RemoveOrphanedRecurringJobs` deletes
   every dispatcher-owned recurring job not declared *by this host* (`RecurringJobsScheduler.cs:153–170`). Two
   different processing hosts sharing one storage/schema would delete each other's jobs on every start — the
   source README §12 documents "recurring jobs disappear" as a known failure mode. Resolved by D-2 (scheduling tied to `RunServer`, zero-task guard, one
   processing host per storage/schema as the documented golden rule).
9. **Scrutor's `AsImplementedInterfaces()` over-registers.** A task that also implements `IDisposable` or a domain
   interface is registered under *all* of them (`IServiceCollectionExtension.cs:58–62`), leaking into unrelated
   `IEnumerable<T>` resolutions. Cloudstrap registers `As<IBackgroundRecurringTask>()` only. The
   `Assembly.GetCallingAssembly()` default is also unreliable under JIT inlining (documented by Microsoft) — the
   entry assembly is the deterministic default.
10. **The health check registers with no tags by default** (`HangfireHealthCheckExtensions.cs:36`, `tags ?? []`).
    Under the shipped #2/#4/#7 probe contract an untagged check is served by **neither** `/healthz` nor `/ready`
    (the #7 spec's documented edge case). Storage reachability is a readiness concern (a dead SQL must drain the
    pod, not restart it): default tag `ready` (Deliberate Change 6).
11. **`Hangfire.SqlServer` needs an ADO.NET SQL client the consumer must reference** (source README §4 note; nuget
    dependency list confirms). The demo hosts get `Microsoft.Data.SqlClient` transitively through EF Core, but a
    consumer with no EF reference hits a runtime provider-resolution failure on the first storage call — the
    "one call works on a fresh clone" promise (the #14 `WolverineFx.RuntimeCompilation` precedent) argues for
    shipping it (D-4).
12. **Nothing #16 ships pre-empts #18.** `Hangfire.Proxy` used two internals: `HangfireDashboardAccessEvaluator`
    (for its front-door policy) and `ForwardedPrefixPathBase.DashboardPath`. Under this spec the evaluator becomes a
    stock `RequireAuthenticatedUser().RequireRole(...)` policy that #18 can build inline from the **public**
    `HangfireDashboardOptions.RequiredRole`, and the path becomes the public `Dashboard:Path` option. No
    `InternalsVisibleTo` grant is re-established; `ForwardedPrefixPathBase` (a reverse-proxy concern with no
    behavior in the absence of a proxy) is routed to #18 whole.
13. **The mechanically renamed `configure-hangfire` skill is entirely stale**: it documents the source shape
    (`AddHangfireForCloudstrap(cloudstrapConfiguration, loggerFactory, …)`, `runServer:`, `ICloudstrapBackgroundJobScheduler`,
    TST/VAL/PRD, `Cloudstrap:HttpClientServiceRegistry`). It must be rewritten against the as-built package
    (definition-of-done item for the plan; not an analyst deliverable).

---

## User Story

**As an** ASP.NET Core developer deploying to Azure who needs scheduled background work (nightly clean-ups,
periodic reconciliations, reminder batches) and a way for operators to see and retune it,
**I want to** register Hangfire with SQL Server storage in one call (`AddCloudstrapHangfire`), declare each
recurring job by implementing one interface with a cron expression, let operators change a schedule or disable a
job from configuration without a redeploy, and expose the Hangfire dashboard from any web host with an
authorization posture that is never weaker than "authenticated" —
**So that** my worker runs the jobs, my web host shows them (without referencing the worker's code), a job never
runs twice concurrently, a deleted job never fires against missing code, and my probes and traces know about
Hangfire without a second telemetry pipeline.

---

## Acceptance Criteria

> AC-ASP2 and AC-A3 are carried **verbatim** from the founding spec. The founding spec defines no dedicated
> Hangfire AC block (Package Map row + environment-taxonomy row only); AC-HF1…AC-HF21 are new, spec-specific
> criteria (prefix `AC-HF`; precedent: `AC-MSG` in `_specs/14-Messaging.md`).

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried verbatim — this package references no auth packages)* |
| AC-HF1 | A host (generic or web) with `ConnectionStrings:DefaultConnection` set | `AddCloudstrapHangfire()` and the host starts | Hangfire is registered with SQL Server storage on that connection string (name from `Cloudstrap:Hangfire:Storage:ConnectionStringName`, default `DefaultConnection`), the recommended `SqlServerStorageOptions` and `SimpleAssemblyNameTypeSerializer`; `Server:WorkerCount`/`Server:Queues` are applied to the processing server when set. An unresolvable connection-string **name** fails at the call naming the key (`'ConnectionStrings:DefaultConnection'`); the value never appears in any message. |
| AC-HF2 | Classes implementing `IBackgroundRecurringTask` in the entry assembly (default) or in `TaskAssemblies` | The host starts | Each concrete, non-abstract implementation is registered **transient as `IBackgroundRecurringTask` only** (never under its other interfaces); an implementation registered manually in DI is honored identically; a scan finding nothing is not an error. |
| AC-HF3 | A processing host (`RunServer = true`) declaring N enabled tasks | The host starts | Before the processing server dequeues anything, every task is registered as a recurring job under its `JobId`, on its `Queue`, in its resolved time zone, stored against `RecurringTaskRunner.RunAsync(jobId, CancellationToken)` — never the implementation type; the startup summary log lists job id, cron and time zone per job. A storage failure during scheduling **faults host startup** (finding 1). |
| AC-HF4 | Two tasks resolving to the same `JobId` | The host starts | Startup fails with a message naming the id and both implementation types; **no** recurring job is written or removed. |
| AC-HF5 | `Cloudstrap:Hangfire:Jobs:{jobId}` with `Cron`, `TimeZone` and/or `Enabled` set (any case of the id) | Scheduling runs | Configured values win over the task's own `CronExpression`/`TimeZone`/`IsEnabled`; an absent or empty value falls back to the task's; `Enabled = false` (config) or `IsEnabled = false` (code) removes the job if present and schedules nothing. |
| AC-HF6 | A per-job `Cron` Hangfire rejects, or a `TimeZone` id `TimeZoneInfo.FindSystemTimeZoneById` rejects | The host starts | Startup fails with a message naming the job id and the offending value (`"… invalid time zone 'Not/AZone' … use an IANA id such as 'Europe/Brussels'"`); a bad time zone is never misreported as a cron problem; duplicate-id and time-zone validation complete for **all** tasks before the first write. |
| AC-HF7 | A task with `PreventOverlappingRuns = true` (default) whose previous run still holds the lock | Hangfire triggers it again | The trigger is **skipped** (not queued) with one warning log naming the job id; the distributed lock is `recurring-task:{jobId}` acquired with zero wait and released when the run ends. With `PreventOverlappingRuns = false` no lock is taken. |
| AC-HF8 | Storage holds recurring jobs stored against `RecurringTaskRunner` whose ids this host no longer declares, plus a recurring job created by other code | Scheduling runs on a host that declares ≥ 1 task | The undeclared dispatcher-owned jobs are removed (one info log each); the foreign job is untouched; a job whose type Hangfire cannot load is treated as foreign. On a processing host that declares **zero** tasks, reconciliation is skipped entirely (one info log; nothing removed) — the zero-task guard (D-2). |
| AC-HF9 | `AddCloudstrapHangfire(h => h.RunServer = false)` on a web host sharing storage with a processing worker | The host starts and the dashboard is opened | No `BackgroundJobServer` and no scheduling service is registered (no `IHostedService` from this package); the host never dequeues; the dashboard lists the worker's recurring jobs by id and last/next execution **without referencing the worker's assembly**. |
| AC-HF10 | A web host with an authentication scheme, `MapCloudstrapHangfireDashboard()` mapped after the auth middleware | Requests reach the dashboard path | Anonymous → the scheme's challenge (login redirect for cookies, 401 for JWT) — the dashboard HTML is never served; authenticated but not in `Dashboard:RequiredRole` (when set) → 403; authenticated (and in the role when set) → 200. `Dashboard:AuthorizationPolicy` names a consumer policy that replaces the built-in one. Independently of endpoint authorization, Hangfire's own filter list always contains Cloudstrap's authenticated-only filter (defense in depth) — **no code path serves the dashboard with Hangfire's localhost-only default**. *(D-1)* |
| AC-HF11 | A host with **no** authentication scheme registered | `MapCloudstrapHangfireDashboard()` is called | The call throws an `InvalidOperationException` naming the requirement — the dashboard is never mapped unauthenticated. |
| AC-HF12 | `Cloudstrap:Hangfire:Dashboard:ReadOnly = true` | An authorized user opens the dashboard | Trigger/delete/requeue actions are unavailable (Hangfire `IsReadOnlyFunc`); with `false` (default) they are available. |
| AC-HF13 | Default configuration; then `Dashboard:Path = "/ops/jobs"`; then `Cloudstrap:Application:PathBase = "/myapp"` | The dashboard is requested | Served at `/hangfire` by default, at the configured path when set, and correctly under the application path base (endpoint routing) — no `X-Forwarded-Prefix` handling in this package (routed to #18). |
| AC-HF14 | `services.AddHealthChecks().AddCloudstrapHangfireHealthCheck()` on a host with storage registered | Probes run | A check named `hangfire` tagged `ready` (defaults; name, `failureStatus`, tags overridable) reports Healthy with `servers`/`recurring`/`enqueued`/`failed` data when storage answers, and the registration's failure status (default Unhealthy) when it does not; the connection string never appears in the result. Registered additively on the stock `IHealthChecksBuilder` (Aspire posture). |
| AC-HF15 | A host with an OTel pipeline (Cloudstrap owner mode, contribute mode, or an Aspire ServiceDefaults-style pipeline) | A recurring task runs | One span from `ActivitySource` `Cloudstrap.Hangfire` named `RecurringTask {jobId}` with tag `cloudstrap.hangfire.job_id`, status Ok/Error, appears in **that** pipeline; this package registers no exporter and no tracer provider; with no pipeline present it still runs (no-op). *(AC-ASP1 posture)* |
| AC-HF16 | A task starts, completes, fails, or is skipped for overlap | Logs are inspected | Structured lines carry the job id, elapsed milliseconds and (on failure) the exception; **no** task arguments, payloads or connection strings are ever logged. |
| AC-HF17 | `Storage:PrepareSchema` unset in `Development` vs `Production`; then set explicitly | Storage initializes | Schema is prepared in Development and expected to pre-exist otherwise; an explicit value wins in either direction; the effective value is in the startup summary. With `false` against a missing schema the storage error surfaces at startup (health check + faulted scheduling), never silently. |
| AC-HF18 | The public `IBackgroundRecurringTask` interface is inspected by reflection | — | No member signature references a `Hangfire.*` type; a task assembly needs only `Cloudstrap.Hangfire` for the interface (Hangfire types stay behind the code-level hatches on the configurator and the dashboard mapping). |
| AC-HF19 | `AddCloudstrapHangfire()` called twice on one host | The second call runs | It fails fast with a message naming the duplicate registration (Hangfire hosts one storage per process; the #14 AC-MSG14 precedent). |
| AC-HF20 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv`, `Cfe`, `Bff`, `WFE` in the package | All green; XML docs on all public API; package metadata complete; README carries the **LGPL-3.0 notice for the `Hangfire.*` family** (plus the transitive `Newtonsoft.Json` disclosure), the topology golden rules and the source-library migration note; zero forbidden identifiers; closure contains zero `Aspire.*`/`Nihdi.*`; every dependency OSI-licensed and CPM-pinned. Unit tests for discovery, scheduling, overrides, overlap prevention, reconciliation, the dashboard filter/policy and the health check run **with no SQL Server** (mocked `JobStorage`/`IStorageConnection`/`IRecurringJobManager`, the source's own technique); storage-backed integration tests (schema preparation, a real schedule/reconcile round trip, the distributed-lock skip) run on SQL Server LocalDB with the `CLOUDSTRAP_TEST_SQL` override (D-3 template; the LocalDB helper is **linked**, not copied a third time — #8, sequenced directly after #16, hoists it; DD-8). |
| AC-HF21 | The demo apps extended per D-5 — **`Cloudstrap.Demo.Worker`** as the processing host (server + scheduling + one neutral demo recurring task) and **`Cloudstrap.Demo.BlazorServer`** as the dashboard-only host (`RunServer = false`, `Dashboard:RequiredRole = "tester"`), both on the D-3 `CloudstrapDemo` LocalDB database; both register the health check | The E2E suite runs | All pre-existing E2E tests stay green and new tests in `Cloudstrap.Demo.E2E.Tests` prove through the running apps: anonymous `/hangfire` on the BlazorServer host redirects to the IdP (the auth posture observed); the signed-in `tester` sees the dashboard **listing the Worker's job** (the two-host topology live); the signed-in `tester` triggers the Worker's job through the dashboard's *Trigger now* action and the Worker is observed to run it (its completion log line appears in the Worker's captured output — seconds, not a cron-boundary wait; D-5). The demo task is a neutral `DemoOrderCountRecurringTask` (logs the `demo.Orders` row count, `CronExpression = "* * * * *"`). *(workflow rule 9)* |

---

## Port Decision Table

One row per source public type/feature (bundled sub-features rowed individually). "Superseded" = adjudicated and
shipped by an earlier deliverable; "Routed" = belongs to a later deliverable and must not be built here.

### Part A — `Nihdi.Core.Configuration.Hangfire\` (18 files + csproj + README)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `IServiceCollectionExtension.AddHangfireForNihdi(services, NihdiConfiguration, ILoggerFactory, connectionString?, Assembly[]?, runServer, Action<HangfireForNihdiOptions>?)` | **Redesign** | `AddCloudstrapHangfire(this IHostApplicationBuilder, Action<CloudstrapHangfireConfigurator>?)` | The one-call bootstrap is the deliverable's point; a seven-parameter `IServiceCollection` method taking a settings object and a logger factory the host already owns is not. `IHostApplicationBuilder` gives access to `Configuration` (connection strings, `Cloudstrap:Hangfire`) and `Environment` (the `PrepareSchema` default) — the #7/#14 shape. Second call fails fast (AC-HF19). |
| ├─ `NihdiConfiguration` + `ILoggerFactory` parameters, `logger.LogInformation("Adding Hangfire configuration for Nihdi.")` | **Superseded** | #1 options binding + DI `ILogger<T>`; #2's bootstrap logger for pre-host logging | The config-object-and-logger-factory passing pattern died with #1/#2 (the #7 precedent). |
| ├─ `connectionString ??= nihdiConfiguration.GetDefaultConnectionString()` | **Redesign** | `Cloudstrap:Hangfire:Storage:ConnectionStringName` (default `DefaultConnection`), resolved from `ConnectionStrings:` | Platform convention (founding Aspire posture item 3; the #14 `UseSqlServer(connectionStringName)` idiom): an Aspire-provisioned SQL resource lands under `ConnectionStrings:` and plugs in with no Cloudstrap key. A name, never a value. |
| ├─ Scrutor scan `AssignableTo<IBackgroundRecurringTask>().AsImplementedInterfaces().WithTransientLifetime()` over `[Assembly.GetCallingAssembly()]` | **Redesign** | Scrutor scan `As<IBackgroundRecurringTask>()`, transient, over `Assembly.GetEntryAssembly()` by default + `configurator.TaskAssemblies` | Finding 9: `AsImplementedInterfaces` over-registers; `GetCallingAssembly` is unreliable under inlining. Scrutor stays (already pinned, MIT; the #11 precedent) — a hand-rolled reflection loop would save one already-paid dependency and re-implement its abstract/generic filtering. |
| ├─ `AddHangfire(...)`: `SetDataCompatibilityLevel(Version_180)` + `UseSimpleAssemblyNameTypeSerializer()` + `UseRecommendedSerializerSettings()` | **Port** | same, inside `AddCloudstrapHangfire` | Hangfire's own recommended settings; the simple-assembly-name serializer is one half of the two-host topology (finding 7). |
| ├─ `UseSqlServerStorage(connectionString, BuildStorageOptions(...))` with fixed tuning (`CommandBatchMaxTimeout` 5 min, `SlidingInvisibilityTimeout` 5 min, `QueuePollInterval` 0, `UseRecommendedIsolationLevel`, `DisableGlobalLocks`) | **Port** | same fixed defaults + `configurator.SqlServer : Action<SqlServerStorageOptions>?` as the escape hatch; `Storage : Action<IGlobalConfiguration>?` to bring another storage entirely | These are the Hangfire 1.8 documented recommended SQL Server settings, not magic numbers. One engine-typed hatch (the #14 `configurator.Wolverine` precedent) replaces per-knob configuration. |
| ├─ `CanPrepareSchema(environment)` = `!(TST\|VAL\|PRD)` → `PrepareSchemaIfNecessary` | **Redesign** | `Cloudstrap:Hangfire:Storage:PrepareSchema : bool?` — `null` → `true` iff `Development` | Finding 6; founding De-NIHDI row 111 + hosting-posture ruling: explicit flag with an environment-based default, identical across Web Apps and AKS; the #14 `AutoProvision` shape verbatim (AC-HF17). |
| ├─ `runServer` parameter → `AddHangfireServer(...)` | **Port** | `configurator.RunServer : bool = true` (code-level; D-6) | The storage-only dashboard host is the other half of the two-host topology (finding 7). Scheduling is tied to it (D-2). |
| ├─ `ConfigureServerOptions` (`WorkerCount`, `Queues`, `ServerName`) | **Redesign** | `Cloudstrap:Hangfire:Server:{WorkerCount?, Queues?}` + `configurator.Server : Action<BackgroundJobServerOptions>?` | Worker count and queue list are ops-tunable per environment — configuration earns them. `ServerName` is a display detail whose Hangfire default (machine name = pod/instance name) is right on the supported hosts; reachable through the hatch, not a config knob nobody sets. |
| ├─ `services.AddSingleton(hangfireOptions)` + `AddSingleton(nihdiConfiguration.Hangfire)` | **Drop** | `IOptions<HangfireOptions>` (`ValidateOnStart`, source-generated validator — #1 pattern) | Raw singleton settings objects are the pre-#1 pattern. |
| ├─ `AddScoped<RecurringJobsScheduler>()` + `AddScoped<RecurringTaskRunner>()` | **Port** | same lifetimes (internal scheduler; public dispatcher) | Hangfire's `AspNetCoreJobActivator` opens a DI scope per job execution, so a scoped dispatcher resolving transient tasks gives each run its own scope (a task's `DbContext` is per run). Correct as sourced. |
| └─ `AddScoped<INihdiBackgroundJobScheduler, NihdiBackgroundJobScheduler>()` | **Drop** | — (consumers inject Hangfire's `IBackgroundJobClient`, registered by `AddHangfire`) | Finding 5. |
| `HangfireForNihdiOptions` (`WorkerCount`, `Queues`, `ServerName`, `QueuePollInterval`, `DashboardReadOnly`) | **Redesign** | split: config `Server:WorkerCount`/`Server:Queues`/`Dashboard:ReadOnly` + the `SqlServer`/`Server` hatches (`ServerName`, `QueuePollInterval` live there) | A code-only options bag that duplicates what configuration should own; `QueuePollInterval` defaults to the Hangfire-recommended zero and is a tuning detail for the hatch. |
| `IBackgroundRecurringTask` (`CronExpression`; DIMs `JobId` = type name, `TimeZone` = UTC, `Queue` = "default", `IsEnabled` = true, `PreventOverlappingRuns` = true; `ExecuteAsync()` + `ExecuteAsync(CancellationToken)`) | **Port** (shape per D-3) | `Cloudstrap.Hangfire.IBackgroundRecurringTask` — same members, **one** `ExecuteAsync(CancellationToken)` | The consumer contract and the deliverable's reason to exist; every member maps to a Hangfire scheduling fact a consumer actually needs. The token-less overload existed only for pre-existing consumers (CLAUDE.md: async public methods take a `CancellationToken`). |
| `RecurringJobsScheduler` (internal): duplicate-id fail-fast, config-over-code `Cron`/`TimeZone`/`Enabled`, IANA fail-fast, schedule via dispatcher, orphan reconciliation | **Port** | internal `RecurringJobsScheduler` (ctor: `IRecurringJobManager`, `IEnumerable<IBackgroundRecurringTask>`, `JobStorage`, `IOptions<HangfireOptions>`, `ILogger`) | Well-designed, fully tested (17 tests) — the crown jewel. Changes: `HangfireConfiguration? = null` optional ctor parameter → options injection; `ConfigurationException` → Core's `ConfigurationValidationException`; time-zone validation runs for all tasks before the first write (AC-HF6); zero declared tasks → reconciliation skipped (D-2 zero-task guard; AC-HF8). |
| `RecurringTaskRunner` (public): resolve task by `JobId`, distributed lock `recurring-task:{id}` zero-timeout skip, `ActivitySource("Nihdi.Core.Configuration.Hangfire")`, start/complete/fail logs | **Port** (wire contract — one-way door) | `public sealed class Cloudstrap.Hangfire.RecurringTaskRunner { Task RunAsync(string jobId, CancellationToken) }`, `[EditorBrowsable(Never)]`; source `Cloudstrap.Hangfire`; tag `cloudstrap.hangfire.job_id` | Hangfire persists `Type` + `Method` by name for every recurring job; the name is chosen once. Public because Hangfire's `Job` requires a public method and the dashboard renders it; hidden from IntelliSense because no consumer calls it. |
| ├─ `RunAsync(string)` token-less overload ("retained for jobs persisted before the token-aware overload existed") | **Drop** | — | Back-compat for persisted NIHDI jobs; Cloudstrap has none. Fewer persisted signatures = a smaller one-way door. |
| `ScheduleTasksUtility.ScheduleBackgroundRecurringTasks` (`ApplicationStarted` callback) | **Redesign** | internal `RecurringTaskSchedulingService : IHostedService` registered by `AddCloudstrapHangfire` when `RunServer`, **before** the Hangfire server so it starts first | Finding 1 — exceptions in `StartAsync` fault the host; scheduling completes before processing starts (AC-HF3). |
| `IHostExtensions.UseHangfireForNihdiWithoutDashboard(IHost, ILoggerFactory)` | **Drop** | — (collapsed into `AddCloudstrapHangfire`) | #7 D-1/D-2: single `Add`, no `Use` phase on a generic host. The separate call was the footgun that let a dashboard host schedule (finding 7). |
| `WebApplicationExtensions.UseHangfireForNihdi` (schedule + dashboard with Hangfire's default localhost-only filter) | **Drop** | — | Finding 2: the fail-open/fail-closed localhost filter is wrong on every supported host; the scheduling half is the hosted service above. The `BackwardCompatibilityTests` signature lock exists only for NIHDI consumers (founding Non-Goal: no `*ForNihdi` compatibility). |
| `WebApplicationExtensions.UseHangfireDashboardForNihdiCfe` / `UseHangfireDashboardForNihdiBff` (+ `BuildCfe/BffDashboardOptions`) | **Redesign** | one `MapCloudstrapHangfireDashboard(this IEndpointRouteBuilder, Action<DashboardOptions>? configure = null) : IEndpointConventionBuilder` (D-1) | Findings 3 + 4: two host-typed methods encoding one rule become one endpoint-routed mapping with a stock authorization policy; host-type vocabulary (Cfe/Bff) disappears per the De-NIHDI checklist. Fits both shipped composites' `ConfigureEndpoints` hook. |
| ├─ `ApplyNihdiAuthorization` (always overwrite `DashboardOptions.Authorization`) | **Redesign** | Cloudstrap's authenticated-only filter is always **prepended**; filters a consumer adds through `configure` are kept (Hangfire requires all filters to pass) | Fail-safe survives (a consumer cannot remove the floor); the override is "tighten", never "loosen"; loosening means calling Hangfire's own `MapHangfireDashboard` — documented. |
| ├─ `ApplyDashboardReadOnly` (`IsReadOnlyFunc = _ => true`) | **Port** | `Cloudstrap:Hangfire:Dashboard:ReadOnly` (default `false`) | Least-privilege monitoring is a real ops need; one bool. |
| ├─ `ForwardedPrefixPathBase.UseForwardedPrefix(app)` call inside both dashboard methods | **Routed** | #18 | Finding 12. |
| `ForwardedPrefixPathBase` (internal; `X-Forwarded-Prefix` → `PathBase` for `/hangfire`, validated) | **Routed** | #18 `Cloudstrap.Hangfire.Proxy` (or the #17 proxy's transform) — public seam = `Dashboard:Path` + endpoint routing | Finding 12: no behavior without a proxy; the constants it exposed to the Proxy package become public options here. The strict header validation is good code #18 should carry over. |
| `INihdiBackgroundJobScheduler` + `NihdiBackgroundJobScheduler` | **Drop** | — (README: inject `IBackgroundJobClient`) | Finding 5; #14 "no facade over the bus", #9 "no facade over `IClientCredentialsTokenManager`". |
| `Authorization\HangfireDashboardAccessEvaluator` (`IsAuthenticated && (role empty \|\| IsInRole)`) | **Replace** | stock `AuthorizationPolicyBuilder.RequireAuthenticatedUser()` + `.RequireRole(role)` when `RequiredRole` is set | Finding 4: the framework's policy system does exactly this, with challenge/forbid semantics per scheme and role mapping from the scheme's `RoleClaimType`. Bespoke evaluator code eliminated. |
| `Authorization\RoleHangfireDashboardAuthorizationFilter` | **Replace** | the policy above | Finding 3 — the role filter *is* the evaluator. |
| `Authorization\TrustedSubsystemHangfireDashboardAuthorizationFilter` (`IsAuthenticated`) | **Redesign** | internal `AuthenticatedDashboardAuthorizationFilter : IDashboardAuthorizationFilter` — the defense-in-depth floor inside Hangfire's own filter list | Finding 3: the "trusted subsystem" posture is "authenticated under a JWT scheme"; the filter survives as the layer that holds even if endpoint authorization were misconfigured. Neutral name. |
| `Authorization\HangfireDashboardConfigurationReader` (`configuration["Nihdi:Hangfire:Dashboard:AccessRole"]`) | **Drop** | `IOptions<HangfireOptions>` binding | Hand-rolled configuration reading; #1's binding pattern. |
| `HealthChecks\HangfireHealthCheck` (`GetMonitoringApi().GetStatistics()`; data servers/recurring/enqueued/failed) | **Port** | internal `HangfireStorageHealthCheck` | Small, correct, tested; the data payload is useful in probe output. |
| `HealthChecks\AddHangfireHealthCheckForNihdi(builder, name = "hangfire", failureStatus?, tags?)` | **Port** | `AddCloudstrapHangfireHealthCheck(this IHealthChecksBuilder, name = "hangfire", HealthStatus? failureStatus = null, IEnumerable<string>? tags = null)` — default tags `[CloudstrapHealthCheckTags.Readiness]` | Additive on the stock builder (Aspire posture, kept as sourced); finding 10 changes the default tag (Deliberate Change 6). Stays opt-in: the storage-only host wants `Degraded`, the worker wants `Unhealthy` — a per-host decision, not a package default. |
| `Nihdi.Core.Configuration.Hangfire.csproj` — `Hangfire.*` 1.8.23, `Scrutor` 7.0.0, `InternalsVisibleTo` (tests, Proxy, Proxy.Tests), StyleCop | **Redesign** | `Cloudstrap.Hangfire.csproj` per suite standard: `Hangfire.*` **1.8.25**, `Scrutor`, `Microsoft.Data.SqlClient` (D-4), `InternalsVisibleTo("Cloudstrap.Hangfire.Tests")` only | Finding 12: the Proxy grant is not re-established. StyleCop is gone suite-wide. |
| `README.md` (NIHDI topologies WFE/BFF/CFE, `Nihdi:` keys, TST/VAL/PRD, DACPAC) | **Redesign** | package README: mental model, topology golden rules in neutral vocabulary (processing host / storage-only host / proxy host), configuration reference, LGPL notice, migration note | The source README's structure is excellent; its vocabulary and every code sample are NIHDI-shaped. |
| `stylecop.json` | **Drop** | — | No StyleCop in Cloudstrap. |
| *(observed-contract sites)* `Worker\Program.cs:62–74` + `DoctorCheckerBackGroundRecurringTask.cs`, `Bff\Program.cs:128–166` | **Routed** | demo slice (D-5): `Cloudstrap.Demo.Worker` (processing host) + `Cloudstrap.Demo.BlazorServer` (dashboard-only host) with the neutral `DemoOrderCountRecurringTask` | Consumer shape only; `DoctorChecker…` data is demo-only and NIHDI-flavored. |

### Part B — old Core `Settings\Hangfire\` (3 files, bound at `Nihdi:Hangfire`)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `HangfireConfiguration` (`Dashboard`, case-insensitive `Jobs` dictionary) | **Redesign** | `HangfireOptions` — section `Cloudstrap:Hangfire`, **owned by this package** (roadmap refinement 1); adds `Storage` and `Server` sub-options | Options live with the package that uses them; the case-insensitive job-id lookup is preserved as a scheduler guarantee (the binder's comparer is not relied on). |
| `HangfireJobConfiguration` (`Cron = ""`, `TimeZone = ""`, `Enabled : bool?`) | **Port** | `HangfireJobOptions { Cron : string?, TimeZone : string?, Enabled : bool? }` | The operator-facing contract, verbatim; nullable strings instead of empty-string sentinels. |
| `HangfireDashboardConfiguration` (`AccessRole = ""`) | **Redesign** | `HangfireDashboardOptions { RequiredRole : string?, Path = "/hangfire", ReadOnly = false, AuthorizationPolicy : string? }` | `AccessRole` → `RequiredRole` (neutral, precise); `Path`, `ReadOnly` and the policy override are the "every convention has an override" rows for the dashboard. |

### Part C — `Nihdi.Core.Configuration.Hangfire.Proxy\` (read-and-route)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `HangfireDashboardProxyBinding`, `HangfireDashboardProxyExtensions` (`AddHangfireDashboardProxyForNihdiWfe` + `UseHangfireDashboardProxyForNihdiWfe`: trusted-subsystem proxy + front-door role policy + `/hangfire` forwarder) | **Routed** | #18 | Depends on #17. Its two internal imports resolve publicly under this spec (finding 12): the front-door policy is the same inline `RequireAuthenticatedUser().RequireRole(RequiredRole)` built from the public `HangfireDashboardOptions`; the path is `Dashboard:Path`. Nothing here pre-empts it. |

**Tally**: 11 Port · 14 Redesign · 2 Replace · 8 Drop · 1 Superseded · 4 Routed *(40 rows)*.

---

## Public API Sketch

Namespace **`Cloudstrap.Hangfire`** (single namespace, matching the package id). Everything `public sealed`/`static`;
the scheduler, the scheduling hosted service, the dashboard filter, the health check and the validator are
`internal`. Hangfire's own types appear in the public surface only behind the code-level hatches
(`SqlServerStorageOptions`, `BackgroundJobServerOptions`, `IGlobalConfiguration`, `DashboardOptions`) and in
`RecurringTaskRunner`'s existence — never in `IBackgroundRecurringTask` (AC-HF18).

```text
Cloudstrap.Hangfire
├── HostApplicationBuilderExtensions (static)
│     AddCloudstrapHangfire(this IHostApplicationBuilder builder,
│                           Action<CloudstrapHangfireConfigurator>? configure = null)
│         : IHostApplicationBuilder
│       — binds + validates HangfireOptions (Cloudstrap:Hangfire) with ValidateOnStart;
│         resolves ConnectionStrings:{Storage:ConnectionStringName} (fail fast on the key name);
│         AddHangfire: Version_180 + SimpleAssemblyNameTypeSerializer + recommended serializer
│           + UseSqlServerStorage(recommended SqlServerStorageOptions, PrepareSchemaIfNecessary =
│             Storage:PrepareSchema ?? IsDevelopment) — or configurator.Storage when set;
│         scans configurator.TaskAssemblies (default: entry assembly) → transient
│           IBackgroundRecurringTask registrations (As<IBackgroundRecurringTask>() only);
│         registers RecurringTaskRunner (scoped, the dispatcher) + RecurringJobsScheduler (scoped);
│         when configurator.RunServer (default true): the internal scheduling hosted service, then
│           AddHangfireServer (Server:WorkerCount / Server:Queues / configurator.Server applied);
│         contributes the Cloudstrap.Hangfire ActivitySource to any OTel tracer pipeline present
│           (ConfigureOpenTelemetryTracerProvider(t => t.AddSource(...)) — the #12 precedent; no exporter);
│         logs one startup summary (storage provider, schema, PrepareSchema in force, RunServer,
│           task count, dashboard path when mapped);
│         second call → InvalidOperationException (AC-HF19).
│
├── CloudstrapHangfireConfigurator (public sealed — code-level hooks; the #5/#14 configurator precedent)
│     RunServer       : bool = true                            — false = storage-only host (dashboard,
│                                                                enqueue-only); never dequeues, never
│                                                                schedules (D-2/D-6)
│     TaskAssemblies  : IList<Assembly>                        — assemblies scanned for tasks; empty →
│                                                                Assembly.GetEntryAssembly()
│     SqlServer       : Action<SqlServerStorageOptions>?        — tune the SQL Server storage (poll interval,
│                                                                schema name is also Storage:SchemaName)
│     Storage         : Action<IGlobalConfiguration>?           — bring your own storage (Hangfire.InMemory,
│                                                                another provider); when set, Storage:* config
│                                                                and the SqlServer hatch are ignored and the
│                                                                startup summary says so
│     Server          : Action<BackgroundJobServerOptions>?     — runs last over the server options
│
├── EndpointRouteBuilderExtensions (static)                     (D-1)
│     MapCloudstrapHangfireDashboard(this IEndpointRouteBuilder endpoints,
│                                    Action<DashboardOptions>? configure = null)
│         : IEndpointConventionBuilder
│       — throws InvalidOperationException when no authentication scheme is registered (AC-HF11);
│         maps Hangfire's dashboard at Dashboard:Path via MapHangfireDashboard;
│         RequireAuthorization: Dashboard:AuthorizationPolicy when set, else the inline policy
│           RequireAuthenticatedUser() [+ RequireRole(Dashboard:RequiredRole) when set];
│         DashboardOptions.Authorization = [AuthenticatedDashboardAuthorizationFilter, ..consumer filters];
│         IsReadOnlyFunc per Dashboard:ReadOnly; configure runs last (cannot remove the Cloudstrap filter).
│       Call it from a composite's ConfigureEndpoints hook (#5 WebApi, #12 BlazorServer, #6 Mvc) or on
│       a WebApplication after UseAuthentication/UseAuthorization.
│
├── HealthChecksBuilderExtensions (static)
│     AddCloudstrapHangfireHealthCheck(this IHealthChecksBuilder builder,
│                                      string name = "hangfire",
│                                      HealthStatus? failureStatus = null,
│                                      IEnumerable<string>? tags = null)   — default tags: ["ready"]
│         : IHealthChecksBuilder
│
├── IBackgroundRecurringTask (public interface — THE consumer contract; shape per D-3)
│     string       CronExpression         { get; }              — required (Hangfire/Cronos syntax)
│     string       JobId                  => GetType().Name     — stable id shown in the dashboard and used
│                                                                as the Cloudstrap:Hangfire:Jobs:{id} key
│     TimeZoneInfo TimeZone               => TimeZoneInfo.Utc
│     string       Queue                  => "default"
│     bool         IsEnabled              => true
│     bool         PreventOverlappingRuns => true
│     Task         ExecuteAsync(CancellationToken cancellationToken)
│
├── RecurringTaskRunner (public sealed, [EditorBrowsable(Never)] — the persisted dispatcher, one-way door)
│     Task RunAsync(string jobId, CancellationToken cancellationToken)
│       — resolves the registered task by JobId (InvalidOperationException naming the id when absent —
│         the "storage-only host running a server" misconfiguration surfaces here), takes the
│         recurring-task:{jobId} distributed lock with zero wait when PreventOverlappingRuns
│         (DistributedLockTimeoutException → warning + skip), runs the task inside a
│         Cloudstrap.Hangfire activity "RecurringTask {jobId}" with start/complete/fail logs.
│       Optional refinement the planner may include (one attribute, no behavior): Hangfire's
│       [DisplayName("{0}")] on RunAsync so the dashboard shows the job id instead of the method call.
│
├── CloudstrapHangfireActivitySources (static)
│     const RecurringTask = "Cloudstrap.Hangfire"                — published so a pipeline owner outside DI
│                                                                can AddSource it (the #2/#12 precedent)
│
├── HangfireOptions — section Cloudstrap:Hangfire (owned HERE)
│     const SectionName = "Cloudstrap:Hangfire"
│     Storage   : HangfireStorageOptions   { ConnectionStringName = "DefaultConnection",
│                                            SchemaName : string?   — null → Hangfire's default (D-7),
│                                            PrepareSchema : bool?  — null → true iff Development }
│     Server    : HangfireServerOptions    { WorkerCount : int?  (≥ 1 when set),
│                                            Queues : string[]?  (non-empty entries when set) }
│     Dashboard : HangfireDashboardOptions { Path = "/hangfire" (rooted, no trailing slash),
│                                            RequiredRole : string?,
│                                            AuthorizationPolicy : string?,
│                                            ReadOnly = false }
│     Jobs      : IDictionary<string, HangfireJobOptions>  — keyed by JobId, looked up case-insensitively
│
└── HangfireJobOptions { Cron : string?, TimeZone : string? (IANA id), Enabled : bool? }

internal: RecurringJobsScheduler · RecurringTaskSchedulingService (IHostedService) ·
          AuthenticatedDashboardAuthorizationFilter (IDashboardAuthorizationFilter) ·
          HangfireStorageHealthCheck (IHealthCheck) · HangfireOptionsValidator ([OptionsValidator]) ·
          HangfireStartupSummaryLogger (LoggerMessage source-generated).
```

**Target consumer `Program.cs`** — processing host (generic host; also the demo Worker and the README example):

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.UseCloudstrapObservability();                       // explicit (#2) — the Hangfire span source joins it
builder.AddCloudstrapWorker();                              // #7: probes on Cloudstrap:Worker:HealthPort
builder.AddCloudstrapHangfire();                            // this package: storage + server + scheduling + discovery
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck();   // ready-tagged storage check
await builder.Build().RunAsync();

public sealed class NightlyCleanupTask : IBackgroundRecurringTask
{
    public string CronExpression => "0 3 * * *";
    public Task ExecuteAsync(CancellationToken cancellationToken) => /* the work */;
}
```

Storage-only dashboard host (web host on a shipped composite; also the demo BlazorServer):

```csharp
builder.AddCloudstrapHangfire(hangfire => hangfire.RunServer = false);   // storage only — never dequeues, never schedules
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck(failureStatus: HealthStatus.Degraded);
…
app.UseCloudstrapBlazorServer<App>(pipeline => pipeline.ConfigureEndpoints = endpoints =>
{
    endpoints.MapCloudstrapAuthenticationEndpoints();
    endpoints.MapCloudstrapHangfireDashboard();              // after auth middleware by construction
});
```

**Configuration** — this package owns exactly one new section, `Cloudstrap:Hangfire` (above). It consumes
`ConnectionStrings:{name}` (platform convention), `Cloudstrap:Application:PathBase` (via endpoint routing — nothing
read directly) and the host environment. Per-job overrides:

```jsonc
"Cloudstrap": { "Hangfire": {
  "Storage":   { "ConnectionStringName": "DefaultConnection" },      // "PrepareSchema": null → Development only
  "Server":    { "WorkerCount": 4, "Queues": [ "critical", "default" ] },
  "Dashboard": { "RequiredRole": "ops", "ReadOnly": false },        // "Path": "/hangfire", "AuthorizationPolicy": null
  "Jobs": {
    "NightlyCleanupTask": { "Cron": "0 4 * * *", "TimeZone": "Europe/Brussels", "Enabled": true }
  }
} }
```

**Deliberately not shipped**: no `Use*` phase on a generic host (D-1/D-2 of #7) · no job-client facade
(finding 5) · no environment-string checks (finding 6) · no `runServer:true` + localhost-only combined dashboard call
(finding 2) · no `X-Forwarded-Prefix` handling (#18) · no in-memory storage default (Dependencies) · no
`IHostBuilder`/`IServiceCollection` overloads.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Activation | Nothing happens until `AddCloudstrapHangfire()` is called; no config flag toggles the package. | Call it, or don't. |
| Storage | SQL Server on `ConnectionStrings:DefaultConnection`, Hangfire's recommended options, Hangfire's default schema (`HangFire`; D-7) — the dashboard-only host and the processing host read the same schema with zero configuration. Azure SQL with managed identity works through the connection string's `Authentication=Active Directory Default` — no Cloudstrap credential type (the #1 credential ruling). | `Storage:ConnectionStringName`, `Storage:SchemaName`, `configurator.SqlServer`; `configurator.Storage` to replace the provider entirely. |
| Schema preparation | `Storage:PrepareSchema` unset → prepared in `Development`, expected to pre-exist elsewhere (IaC/migration script: Hangfire ships `Install.sql`); stated in the startup summary. | `Storage:PrepareSchema = true/false` (explicit wins). |
| Host role | `RunServer = true`: processing host — runs the server **and** schedules (the "processing host schedules" rule enforced structurally). `RunServer = false`: storage-only host — dashboard and/or `IBackgroundJobClient` enqueueing only; never dequeues, never schedules, never reconciles. Exactly one processing host (task set) per storage/schema is the documented golden rule; several independent processing hosts on one database each set their own `Storage:SchemaName` (D-2). | `configurator.RunServer` — code-level only, no configuration key (D-6). |
| Task discovery | Entry assembly scanned for concrete `IBackgroundRecurringTask` implementations, registered transient as the interface only. | `configurator.TaskAssemblies`; or skip scanning by registering tasks in DI yourself (the scheduler consumes `IEnumerable<IBackgroundRecurringTask>`). |
| Job identity | `JobId` = implementation type name (rename-sensitive: a renamed class is reconciled as a new job — history is lost, nothing fires twice). | Override `JobId` on the task (recommended for anything operators reference in `Jobs:`). |
| Scheduling | At host start, before the server processes: validate duplicates + time zones for all tasks → `AddOrUpdate` each enabled task via the dispatcher (`RemoveIfExists` for disabled) → reconcile orphans → one summary log line per job. Any failure faults startup. | `Jobs:{id}` config; `IsEnabled`; the scheduling service is internal — a consumer wanting different lifecycle timing calls `IRecurringJobManager` themselves and sets `RunServer = false`. |
| Configuration-over-code | `Jobs:{id}:Cron` / `:TimeZone` / `:Enabled` win over the task's declaration; empty/null falls back; id lookup case-insensitive. | It *is* the override. |
| Overlap prevention | Distributed lock `recurring-task:{id}` with zero wait; a busy lock **skips** the trigger (warning log). | `PreventOverlappingRuns => false` on the task. |
| Orphan reconciliation | Dispatcher-owned recurring jobs not declared by this host are removed on the processing host's start; jobs of other types (or unloadable types) are never touched. A processing host declaring zero tasks skips reconciliation (info log). | No switch (D-2): isolate independent processing hosts with `Storage:SchemaName`; a storage-only host (`RunServer = false`) never reconciles. |
| Dashboard | Not mapped unless `MapCloudstrapHangfireDashboard()` is called; at `/hangfire`; requires an authentication scheme (throws otherwise); policy = authenticated + `RequiredRole` when set; Cloudstrap's authenticated-only Hangfire filter always present; not read-only. | `Dashboard:Path`, `Dashboard:RequiredRole`, `Dashboard:AuthorizationPolicy` (a consumer policy replaces the inline one), `Dashboard:ReadOnly`, `configure` (extra Hangfire filters/options — additive). Loosening below "authenticated" is not offered: use Hangfire's own `MapHangfireDashboard`. |
| Dashboard on a JWT-only host | Same mapping: anonymous → 401 from the bearer scheme; a caller with a valid token (a #18 proxy host) → 200. The "trusted-subsystem" posture is this policy with no role. | `RequiredRole` / `AuthorizationPolicy` (e.g. a scope requirement). |
| Fire-and-forget / delayed jobs | Consumers inject Hangfire's `IBackgroundJobClient` (registered by `AddHangfire`); documented in the README with the public-method rule. Jobs run on a processing host that can resolve the target type. | — (no facade; finding 5). |
| Health | Opt-in `AddCloudstrapHangfireHealthCheck()`: name `hangfire`, tag `ready`, Unhealthy on failure, statistics as data. Additive on the stock builder — coexists with Aspire's health-check registrations. | `name`, `failureStatus` (the storage-only host typically wants `Degraded`), `tags`. |
| Telemetry | `Cloudstrap.Hangfire` `ActivitySource` contributed via `ConfigureOpenTelemetryTracerProvider` to whatever tracer pipeline the host has — Cloudstrap owner mode, contribute mode, or Aspire ServiceDefaults; no exporter, no provider of its own; inert without a pipeline. Span `RecurringTask {jobId}`, tag `cloudstrap.hangfire.job_id`, status Ok/Error. Hangfire's own job lifecycle emits no spans (no built-in OTel in Hangfire 1.8). | A pipeline owner outside DI adds `CloudstrapHangfireActivitySources.RecurringTask` with `AddSource`; suppress via the pipeline's sampler/filters. |
| Logging | Start / completed-in-ms / failed-after-ms (with exception) / skipped-for-overlap per run; startup summary; orphan removals. Never task arguments, payloads or connection strings. | Standard `Logging:LogLevel:Cloudstrap.Hangfire`. |
| Serialization | Hangfire's recommended settings + simple assembly-name type serializer (two-host topology). | `configurator.Storage` hatch (`IGlobalConfiguration`) for consumers who need different serializer settings — at their own two-host risk, documented. |
| Aspire coexistence | Storage from `ConnectionStrings:` (an Aspire-provisioned SQL resource plugs in); health checks additive (AC-ASP3 posture); `ActivitySource` contributed, never a second exporter (AC-ASP1 posture); zero `Aspire.*` (AC-ASP2). Hangfire is outside ServiceDefaults' remit otherwise. | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in `src/Test/UnitTest/Cloudstrap.Hangfire.Tests`.
The default suite runs **with no SQL Server**: `RecurringJobsScheduler`/`RecurringTaskRunner` against mocked
`IRecurringJobManager`, `JobStorage`, `IStorageConnection` (the source's technique, ported); options binding +
validation; discovery over a fixture assembly; the dashboard mapping with `WebApplication` + a test cookie/JWT scheme
(anonymous → challenge, wrong role → 403, right role → 200, no scheme → throws); the health check against a mocked
`IMonitoringApi`; the `ActivityListener` capture of the span; registration-shape tests with `PrepareSchema = false`
(no connection is opened at registration). Storage-backed integration tests (AC-HF3 round trip, AC-HF7 lock skip,
AC-HF8 reconciliation against real storage, AC-HF17 schema preparation) run on SQL Server **LocalDB** by default with
the `CLOUDSTRAP_TEST_SQL` override (D-3; helper linked from `Cloudstrap.Messaging.Tests`, hoisted by #8). Demo/E2E
per AC-HF21 and D-5.

---

## Dependencies

| Package | Version (pin at plan time) | License | Why it is justified |
|---|---|---|---|
| `Hangfire.Core` | 1.8.25 (2026-08-28) | **LGPL-3.0** (Hangfire OÜ; multi-licensed, LGPL by default) | The engine — founding Package Map decision ("free Hangfire tier only, LGPL noted in docs"). Brings `Newtonsoft.Json` (MIT; already transitively present via `WolverineFx.Newtonsoft`). OSI-approved; CLAUDE.md rule 4 explicitly anticipates it. README notice is a hard DoD item. |
| `Hangfire.SqlServer` | 1.8.25 | LGPL-3.0 | SQL Server storage (founding "SQL Server" posture). No SQL client dependency of its own (finding 11). |
| `Hangfire.AspNetCore` | 1.8.25 | LGPL-3.0 | `AddHangfire`/`AddHangfireServer` DI integration, `AspNetCoreJobActivator` (scope per job), `MapHangfireDashboard`. Brings `Hangfire.NetCore` (LGPL-3.0) and the framework's Antiforgery/Http abstractions. The `Microsoft.AspNetCore.App` framework reference it needs is **already transitively mandatory** through `Cloudstrap.Observability` (#7 finding 5) — no new consumer cost, including on worker hosts. |
| `Microsoft.Data.SqlClient` | 7.1.0 (2026-09-18) | MIT | **D-4 (user-approved).** The ADO.NET provider Hangfire.SqlServer resolves at runtime; shipping it makes "one call on a fresh clone" hold (the #14 `WolverineFx.RuntimeCompilation` precedent). Demo hosts already carry it transitively via EF Core SqlServer — pin alignment with EF Core 10's floor is a plan-time check. |
| `Scrutor` | 7.0.0 (pinned, #11) | MIT | Assembly scan for task implementations; already in the suite. |
| `Cloudstrap.Core` *(project)* | — | MIT | `ConfigurationValidationException`, options/validator patterns, `ApplicationOptions`. |
| `Cloudstrap.Observability` *(project)* | — | MIT | `CloudstrapHealthCheckTags.Readiness` (the tag contract), the OpenTelemetry hosting package for `ConfigureOpenTelemetryTracerProvider` (additive source registration), and the framework reference. The #7/#12 precedent. |
| `Microsoft.AspNetCore.App` *(framework reference)* | — | MIT | Endpoint routing + authorization for the dashboard mapping; already transitive (above). |

**Considered and rejected**: `Hangfire.InMemory` 1.0.0 (LGPL-3.0, verified) — not needed: unit tests mock the storage
abstractions (as the source does) and storage-backed tests run on LocalDB (D-3); an in-memory *default* in Development
would make dev and production run different storage engines and cannot exercise the two-host topology. Consumers who
want it reach it through `configurator.Storage` — nothing in the closure · `Quartz.NET` (Apache-2.0) / `TickerQ` (MIT)
as engine alternatives — the founding Package Map decided Hangfire with the LGPL cost known; Quartz has no bundled
dashboard (more code to own), TickerQ is young; not re-litigated · any `Hangfire.Pro.*` (commercial) · any `Aspire.*`
(AC-ASP2) · any `Nihdi.*` (AC-A3).

Closure summary: **one new dependency family (`Hangfire.*`, LGPL-3.0)** + one MIT provider package (D-4) + already-pinned
Scrutor/Microsoft packages. Zero `Aspire.*`, zero `Nihdi.*`.

---

## Deliberate Behavior Changes (vs the source library)

1. **Fail-fast now fails the host.** Scheduling runs in a hosted service's `StartAsync`; duplicate ids, bad cron, unknown
   time zones and unreachable storage abort startup instead of logging once and running unscheduled (finding 1).
2. **The localhost-only dashboard path is gone.** Every mapping is authenticated; a host with no authentication scheme
   cannot map the dashboard at all (finding 2; AC-HF10/AC-HF11).
3. **Two host-typed dashboard calls become one endpoint-routed mapping with a stock authorization policy**
   (`RequiredRole` empty = any authenticated principal); Cfe/Bff vocabulary removed (findings 3–4; D-1).
4. **Scheduling is no longer a separate `Use…` call** — it is a consequence of `RunServer = true`; a storage-only host
   structurally cannot schedule or reconcile; a processing host declaring zero tasks skips reconciliation
   (finding 7–8; D-2).
5. **`PrepareSchemaIfNecessary` by environment string → explicit `Storage:PrepareSchema` with a `Development` default.**
6. **The health check is `ready`-tagged by default** (source: untagged → served by no probe; finding 10).
7. **No job-client facade** — consumers inject `IBackgroundJobClient` (finding 5).
8. **One `ExecuteAsync(CancellationToken)`** on the task interface; the token-less overload and the token-less
   persisted `RunAsync(string)` are not carried (no persisted jobs to serve).
9. **Discovery defaults to the entry assembly and registers the task interface only** (finding 9).
10. **`ServerName` and `QueuePollInterval` are no longer configuration knobs** — reachable through the engine-typed hatches.
11. **Second registration fails fast** instead of double-registering storage and servers.
12. **The persisted dispatcher identity changes** to `Cloudstrap.Hangfire.RecurringTaskRunner, Cloudstrap.Hangfire` —
    recurring jobs persisted by the source library are neither executed nor reconciled by Cloudstrap (their type does
    not load → treated as foreign); the README's migration note tells source-library users to delete them from the
    dashboard once (founding Non-Goal: no back-compat; documented, not solved).
13. **(D-4)** `Microsoft.Data.SqlClient` ships in the closure instead of being a consumer prerequisite.

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| Processing host declares zero tasks | Server runs (fire-and-forget jobs still process); scheduling writes nothing; reconciliation skipped with an info log — nothing removed (D-2 zero-task guard). |
| Storage-only host receives a recurring job trigger (someone set `RunServer = true` by mistake on a host without the tasks) | `RecurringTaskRunner` throws `InvalidOperationException("No registered recurring task with id 'X'")` — the job fails visibly in the dashboard with an actionable message; README troubleshooting entry. |
| Two replicas of the same processing host | Both schedule the same declared set (`AddOrUpdate` is idempotent) and reconcile nothing of each other's — safe; Hangfire's server coordination handles execution. |
| A task class is renamed without overriding `JobId` | Old job removed as orphan, new job scheduled; dashboard history lost; nothing fires twice. Documented. |
| `Jobs:{id}` entry for an id no task declares | Ignored with one warning in the startup summary (a typo in an operator override is worth a line). |
| Task's `ExecuteAsync` honors the token; host shuts down mid-run | Hangfire signals the job cancellation token; the run ends; the lock is released with the connection. |
| Task throws | Logged with exception + elapsed; rethrown so Hangfire records the failure and applies its retry policy (`AutomaticRetryAttribute` default 10) — retries respect the overlap lock like any trigger. |
| `PreventOverlappingRuns` and a crashed run (lock holder died) | Hangfire's SQL distributed lock expires with the connection; the next trigger proceeds. |
| Dashboard path collides with an application route | Endpoint routing reports the ambiguity per framework rules; `Dashboard:Path` is the fix. |
| Consumer supplies `DashboardOptions.Authorization` in `configure` | Their filters are appended after Cloudstrap's; all must pass. |
| `AuthorizationPolicy` names a policy that does not exist | Framework behavior: startup/first-request failure naming the policy — surfaced, not swallowed. |
| Hangfire dashboard assets under `Cloudstrap:Application:PathBase` | Correct — endpoint routing + Hangfire's `PathBase`-aware URL generation; the forwarded-prefix case (proxy strips a prefix) is #18's. |
| `PrepareSchema = false` and the schema is absent | First storage call fails; the scheduling service faults startup on a processing host; on a storage-only host the health check reports the failure status and the dashboard errors — never a silent empty dashboard. |
| Persistent dev database (`CloudstrapDemo`) with `PrepareSchema = true` on every start | Hangfire's installer is versioned and idempotent; coexists with the Wolverine schemas (`demo_transport`, `demo_application_*`) and `demo` in the same database. |
| Host has an OTel pipeline in contribute mode (Aspire ServiceDefaults) | The span source joins that pipeline; no exporter added; AC-HF15. |
| `ConnectionStrings:` entry exists but is invalid | Hangfire/SqlClient error at first storage use — startup fault on a processing host; the message from the provider may echo server/database names (provider behavior); Cloudstrap's own messages never do. |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace,
  ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- **Dashboard proxying** (#18): `ForwardedPrefixPathBase`, the front-door proxy policy, `/hangfire` forwarding through
  the #17 YARP forwarder. This spec guarantees only the public seam (`Dashboard:Path`, `HangfireDashboardOptions.RequiredRole`,
  endpoint-routed mapping) — no `InternalsVisibleTo` to a Proxy package.
- Everything Dropped above: the localhost-only combined `UseHangfireForNihdi`, the separate scheduling `Use…` call, the
  job-client facade, the bespoke authorization evaluator/filters/config reader, the token-less overloads, raw settings
  singletons, StyleCop.
- Hangfire Pro features (batches, Redis), commercial licenses, `Hangfire.InMemory` as a shipped dependency or a
  Development default.
- Other storage providers (PostgreSQL, Redis) as Cloudstrap packages — the `configurator.Storage` hatch is the growth
  path; a `Cloudstrap.Hangfire.PostgreSql` leaf is post-v1 if demand appears.
- Fire-and-forget/continuation/batch job helpers, job filters, retry-policy configuration, a dashboard title/branding
  option — the source had none (no gold-plating); Hangfire's own API is the documented path.
- Serving the dashboard from a generic-host worker (the #7 side-host serves probes only) — a host wanting a dashboard
  is a web host.
- Hangfire-lifecycle OpenTelemetry spans for non-recurring jobs (Hangfire 1.8 has no built-in instrumentation).
- The `Cloudstrap.Testing` hoist of `SqlServerTestDatabase` (#8, sequenced directly after #16 — user-approved
  2026-09-26) — #16 links the existing file (DD-8).
- Rewriting `.claude/skills/configure-hangfire/SKILL.md` is a **plan definition-of-done item** (finding 13), not a spec artefact.

---

## Decision Log (gate answers, 2026-09-26 — zero Open Questions remain; spec is planner-ready)

All seven Open Questions (OQ-1–OQ-7) were answered by the user on 2026-09-26 ("accept all recommendations"),
in each case accepting the analyst's recommended option. D-*n* resolves OQ-*n*. The evidence behind each call
remains on record in the Code-reading findings, the Port Decision Table and the Deliberate Behavior Changes;
the rejected options are recorded here so the planner never resurrects them.

| # | Decision (user-approved 2026-09-26) | Rejected alternatives | Basis |
|---|---|---|---|
| **D-1** | **Dashboard authorization = endpoint-routed `MapCloudstrapHangfireDashboard()`** applying `RequireAuthorization` with a stock policy (inline `RequireAuthenticatedUser()` + `RequireRole(Dashboard:RequiredRole)` when set, or the consumer's `Dashboard:AuthorizationPolicy`), plus one **internal authenticated-only `IDashboardAuthorizationFilter`** always prepended to Hangfire's filter list as defense in depth; throws when no authentication scheme is registered. The source's four authorization files are eliminated. | (b) port the two filters behind `UseCloudstrapHangfireDashboard(app)`; (c) ship both. | Findings 2–4; AC-HF10–AC-HF13. Auth risk area — human-reviewed at this gate. |
| **D-2** | **Scheduling is tied to `RunServer`** (a storage-only host structurally cannot schedule or reconcile) + a **zero-task guard** (a processing host declaring zero tasks skips orphan reconciliation, one info log) + **schema-per-processing-host guidance** (independent processing hosts sharing one database each set `Storage:SchemaName`; documented as the golden rule in the README). | (b) owner-scoped reconciliation via a persisted per-workload id set — recorded as the post-v1 improvement if a consumer reports the residual hazard; (c) a `ReconcileOrphans` switch. | Findings 7–8; AC-HF3, AC-HF8, AC-HF9; no gold-plating. |
| **D-3** | **`IBackgroundRecurringTask` keeps the default-interface-member shape**, with a single `ExecuteAsync(CancellationToken)`; `JobId` defaults to the implementation type name (rename sensitivity documented; reconciliation guarantees no double firing). | (b) abstract base class `RecurringTask`; (c) mandatory explicit `JobId`. | Public-API one-way door; AC-HF2, AC-HF18. |
| **D-4** | **`Microsoft.Data.SqlClient` (MIT) ships in the `Cloudstrap.Hangfire` closure** — "one call on a fresh clone" holds; pin alignment with EF Core 10's floor is a plan-time check. | (b) document it as a consumer prerequisite (source parity). | Finding 11; #14 `WolverineFx.RuntimeCompilation` precedent; Dependencies. |
| **D-5** | **Demo vehicles**: `Cloudstrap.Demo.Worker` = processing host (server + scheduling + the neutral `DemoOrderCountRecurringTask`, `CronExpression = "* * * * *"`, logging the `demo.Orders` row count); `Cloudstrap.Demo.BlazorServer` = dashboard-only host (`RunServer = false`, `Dashboard:RequiredRole = "tester"`); both on the D-3 `CloudstrapDemo` LocalDB database. **E2E**: anonymous `/hangfire` → IdP redirect; signed-in `tester` sees the Worker's job listed; the test **triggers the job through the dashboard** (*Trigger now*) and observes the Worker's completion log line in its captured output. Waiting for the every-minute cron is the documented fallback only if the dashboard DOM proves brittle under Playwright. | (c) `Cloudstrap.Demo.Api` as the dashboard host (proves only a 401). | Workflow rule 9; AC-HF21. |
| **D-6** | **`RunServer` is a code-level configurator flag** (`CloudstrapHangfireConfigurator.RunServer`, default `true`) — no configuration key. A config override remains a compatible later addition if a real drain scenario appears. | (b) `Cloudstrap:Hangfire:Server:Enabled`; (c) both. | #5 D-2 precedent (role-defining switches live in `Program.cs`). |
| **D-7** | **Hangfire's default schema** (`HangFire`) with `Cloudstrap:Hangfire:Storage:SchemaName` as the override — the two-host topology works with zero configuration. | (b) `SystemName`-derived schema; (c) `WorkloadName`-derived schema (breaks the two-host default). | Finding 7; source parity; least surprise for Hangfire users. |

**Analyst decisions accepted with the spec** (not gated as Open Questions; approved together with the spec on 2026-09-26):

| # | Decision | Basis |
|---|---|---|
| DD-1 | `ForwardedPrefixPathBase` + the Proxy package are **routed to #18** through a public seam; no `InternalsVisibleTo` grant. | Finding 12. |
| DD-2 | `INihdiBackgroundJobScheduler` is **dropped**; consumers inject `IBackgroundJobClient`. | Finding 5; #14/#9 precedent. |
| DD-3 | The health check stays **opt-in** on the stock builder, default tag `ready`. | Finding 10; per-host failure status. |
| DD-4 | No in-memory storage in the closure or as a Development default. | Dependencies section. |
| DD-5 | Entry point names: `AddCloudstrapHangfire`, **`MapCloudstrapHangfireDashboard`** (the roadmap's `UseCloudstrapHangfireDashboard` challenged: endpoint-routing idiom, consistent with `MapCloudstrapHealthChecks`/`MapCloudstrapAuthenticationEndpoints`, and it is what makes the composites' `ConfigureEndpoints` hook the natural home), `AddCloudstrapHangfireHealthCheck`. | Finding 4. |
| DD-6 | `RecurringTaskRunner` keeps its name in namespace `Cloudstrap.Hangfire`, public sealed, `[EditorBrowsable(Never)]`, single `RunAsync(string, CancellationToken)`. | Wire contract; one-way door chosen consciously. |
| DD-7 | Hangfire family pinned at **1.8.25** (source: 1.8.23). | Current stable, 2026-08-28. |
| DD-8 | The storage-backed tests **link** (never copy) `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Infrastructure/SqlServerTestDatabase.cs`; the hoist into `Cloudstrap.Testing` belongs to #8, which the user approved (2026-09-26) to run **directly after #16** and which then repoints the link. | Roadmap sequencing (user-approved); avoids a third copy of the helper. |

---

## Open Questions

None. OQ-1–OQ-7 were resolved by the user on 2026-09-26 and folded in as Decision Log D-1–D-7. The spec is
**APPROVED** and planner-ready.

