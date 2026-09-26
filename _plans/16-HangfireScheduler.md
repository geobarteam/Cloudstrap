# Plan: 16-HangfireScheduler — A developer calls `AddCloudstrapHangfire()` and implements `IBackgroundRecurringTask`. The processing host then schedules and runs those jobs on SQL Server storage. Operators can retune or disable a job from configuration. Misconfiguration or a storage failure stops host startup. A storage-only web host shows the worker's jobs on a dashboard that always requires authentication. The demo Worker and demo BlazorServer prove this end to end.

## Overview

Deliverable #16 of the extraction roadmap: the new **`Cloudstrap.Hangfire`** package. **Binding spec:
`_specs/16-HangfireScheduler.md`** (APPROVED 2026-09-26, zero Open Questions; Decision Log D-1…D-7
user-approved, DD-1…DD-8 accepted). The spec's Port Decision Table (11 Port · 14 Redesign · 2 Replace ·
8 Drop · 1 Superseded · 4 Routed), Public API Sketch, Behaviors & Conventions, Dependencies, Deliberate
Behavior Changes 1–13, Edge Cases and Out-of-Scope list are authoritative and are not re-litigated here.
This plan does **not** include anything the spec marked Drop, Routed or Out-of-Scope. That means no
`INihdiBackgroundJobScheduler`/job-client facade, no `Use…` scheduling call on the generic host, no
localhost-only dashboard path, no `ForwardedPrefixPathBase`/`X-Forwarded-Prefix` handling (that is #18),
no bespoke authorization evaluator/filters/config reader, no token-less `ExecuteAsync()`/`RunAsync(string)`
overloads, no `IHostBuilder`/`IServiceCollection` overloads, no `Hangfire.InMemory` (not even for tests),
no `ServerName`/`QueuePollInterval` configuration keys, no `RunServer` configuration key (D-6), and no
`InternalsVisibleTo` grant to a Proxy package.

This is a **port with targeted redesigns**. Three things from the source survive intact:
- **The persisted dispatcher.** Every recurring job is stored against `RecurringTaskRunner.RunAsync(jobId, ct)`,
  never against the implementation type.
- **Configuration over code.** `Cloudstrap:Hangfire:Jobs:{id}` can override a job's schedule, time zone or
  enabled state, and invalid values fail fast.
- **Orphan reconciliation.** Recurring jobs whose task was deleted from code are removed on start.

What changes is the shape around them. Scheduling moves into a hosted service whose failure stops host
startup (D-2). The dashboard is mapped as an endpoint that requires authentication (D-1). `PrepareSchema`
becomes an explicit flag. The health check is tagged `ready`.

Reference patterns, all read before planning:

- **Plan-shape precedents**: `_plans/15-BlobClaimCheck.md` (new-package RED mechanics, the
  `DependencyPinTests` repo-root props reader, `PackageSurfaceTests` permanent guards, LocalDB D-3 tests,
  the Pin record, and the demonstration slice with a self-booted Worker fixture) and `_plans/14-Messaging.md`
  (single `Add` call, marker-based duplicate-call fail-fast, `AutoProvision ?? IsDevelopment()`, the
  startup summary logger, additive OTel). `_plans/7-WorkerBootstrap.md` is the precedent for the Worker demo
  host and probe contract. `_plans/25-WasmTestProjectSut.md` / `_plans/27-DemoAppsRestructure.md` set the
  demonstration-slice convention (rule 15).
- **Shipped code read on disk (the patterns mirrored)**:
  - `src/Cloudstrap.Messaging/HostApplicationBuilderExtensions.cs`: marker-service duplicate-call
    fail-fast; eager bind + validate throwing `ConfigurationValidationException`;
    `AddOptions<>().Bind().ValidateOnStart()` + `TryAddEnumerable` validator; explicit use of
    `Assembly.GetEntryAssembly()`; `ConfigureOpenTelemetryTracerProvider(t => t.AddSource(...))` with no
    exporter.
  - `Cloudstrap.Messaging.csproj`: packaging metadata and `InternalsVisibleTo` for its own tests only.
  - `src/Cloudstrap.Worker/HostApplicationBuilderExtensions.cs` + `Cloudstrap.Worker.csproj`: the explicit
    `<FrameworkReference Include="Microsoft.AspNetCore.App" />` precedent and the additive `AddHealthChecks()`.
  - `src/Cloudstrap.BlazorServer/WebApplicationExtensions.cs`: `ConfigureEndpoints` is invoked after
    `UseAuthentication`/`UseAuthorization`/`UseAntiforgery` and after `MapCloudstrapHealthChecks`.
  - `src/Cloudstrap.BlazorServer/WebApplicationBuilderExtensions.cs`: hardened antiforgery
    (`SecurePolicy = Always`, `SameSite = Strict`) with the `configurator.Antiforgery` hook, which has the
    final say.
  - `src/Cloudstrap.Authentication.OpenIdConnect/ServiceCollectionExtensions.cs:193`:
    `RoleClaimType = "role"`.
  - `src/Directory.Packages.props`: central pins, `CentralPackageTransitivePinningEnabled = true`, the
    security-floor precedent, Scrutor 7.0.0, `Microsoft.AspNetCore.TestHost` / `OpenTelemetry.Exporter.InMemory`
    as test-only pins.
  - Tests: `src/Test/UnitTest/Cloudstrap.Messaging.Tests/{PackageSurfaceTests, DependencyPinTests,
    Infrastructure/SqlServerTestDatabase, Infrastructure/CapturingLoggerProvider}.cs`.
- **Demo + E2E harness read on disk**:
  - Demo hosts: `src/demo/Worker/{Program.cs, appsettings.json, Cloudstrap.Demo.Worker.csproj,
    Data/WorkerDbContext.cs, README.md}` and `src/demo/BlazorServer/{Program.cs, appsettings.json,
    Cloudstrap.Demo.BlazorServer.csproj, README.md}`.
  - IdP seed: `src/demo/Shared/IdentityProvider/TestIdentityProviderSeed.cs`. The user `geobarteam`
    carries `role: tester`, and user claims reach **both** tokens
    (`TestIdentityProvider/EndpointRouteBuilderExtensions.cs:297–313`), so the OIDC cookie principal is
    in role `tester`.
  - E2E: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/{E2eFixture, WorkerHostTests, ClaimCheckTests,
    BlazorServerTests, AssemblyInfo (NonParallelizable), Infrastructure/SutProcess, Cloudstrap.Demo.E2E.Tests.csproj}.cs`
    and `src/demo/README.md` (port map).
  - CI: `.github/workflows/ci.yml` (sequential exe loop; `CLOUDSTRAP_TEST_SQL` →
    `Database=CloudstrapCi`).
- **Source reference (read-only)**: `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\Nihdi.Core.Configuration.Hangfire\`
  and `Test\UnitTest\Nihdi.Core.Configuration.Hangfire.Tests\`. The executor reads
  `RecurringJobsSchedulerTests.cs` / `RecurringTaskRunnerTests.cs` for the mocked-`IStorageConnection`
  technique (how recurring-job hashes and sets are stubbed). It ports the technique, never the code
  verbatim, and applies the De-NIHDI checklist.

This is a library deliverable with **no database project**. Every "DB changes" section is "none" apart from
Hangfire's own schema, which `PrepareSchemaIfNecessary` installs at runtime. That happens only in
Development / tests; production uses Hangfire's `Install.sql` via IaC.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-ASP2 (zero `Aspire.*`) · AC-A3 (zero `Nihdi.AspNetCore`) *(verbatim carry)* | 7 (permanent closure guard) · 1 (pin tripwire) |
| AC-HF1 (SQL Server storage on `ConnectionStrings:{name}`, recommended options + simple serializer, server options applied, key-named failure without the value) | 1 (registration, options) · 4 (live server options) |
| AC-HF2 (discovery: concrete implementations, interface-only transient, manual registration honored, empty scan OK) | 1 |
| AC-HF3 (processing host schedules every enabled task against the dispatcher before processing; summary lines; storage failure faults startup) | 2 (mocked) · 4 (LocalDB round trip) |
| AC-HF4 (duplicate `JobId` → startup fails naming id + both types; nothing written/removed) | 2 |
| AC-HF5 (config-over-code `Cron`/`TimeZone`/`Enabled`, case-insensitive; disabled → removed) | 2 |
| AC-HF6 (bad cron / bad time zone fail naming job + value; tz never misreported; validation for all tasks before first write) | 2 |
| AC-HF7 (overlap lock `recurring-task:{id}`, zero wait, skip + warning; no lock when disabled) | 3 (mocked) · 4 (real SQL lock) |
| AC-HF8 (orphan reconciliation; foreign/unloadable untouched; zero-task guard) | 2 (mocked) · 4 (real storage) |
| AC-HF9 (storage-only host: no server, no scheduling, no hosted service from this package; sees the worker's jobs) | 1 (descriptors) · 4 (two-host on one schema) · 9 (live, cross-process) |
| AC-HF10 (dashboard: challenge / 403 / 200; policy override; defense-in-depth filter always present) ⚠️ auth | 5 · 9 (live: IdP redirect, `tester` → 200) |
| AC-HF11 (no authentication scheme → mapping throws) | 5 |
| AC-HF12 (`Dashboard:ReadOnly`) | 5 |
| AC-HF13 (`/hangfire` default, `Dashboard:Path`, under `PathBase`, no forwarded-prefix handling) | 5 |
| AC-HF14 (health check `hangfire`/`ready`, data, failure status, no connection string, additive) | 6 · 8/9 (demo registration) |
| AC-HF15 (span `RecurringTask {jobId}` in the host's pipeline; no exporter/provider; inert without one) | 3 |
| AC-HF16 (structured logs: job id + elapsed + exception; never arguments/payloads/connection strings) | 3 |
| AC-HF17 (`PrepareSchema` default Development, explicit wins, summary, `false` + missing schema surfaces at startup) | 1 (effective value) · 4 (LocalDB) |
| AC-HF18 (`IBackgroundRecurringTask` references no `Hangfire.*` type) | 1 · 7 (permanent) |
| AC-HF19 (second `AddCloudstrapHangfire()` fails fast) | 1 |
| AC-HF20 (build/tests/format, closure, identifier sweep, XML docs, README with LGPL notice + golden rules + migration note, no-SQL unit suite, LocalDB storage tests with linked helper) | 7 (+ 1–6 evidence) |
| AC-HF21 (demo Worker = processing host, demo BlazorServer = dashboard-only `tester` host, both on `CloudstrapDemo`, both health-checked; E2E: anonymous → IdP, `tester` sees the Worker's job, *Trigger now* → Worker completion line; pre-existing E2E green) | 8 · 9 |

### Dependency closure: ⚠️ new dependency family (risk area, reviewed at Gate 1)

`src/Directory.Packages.props` changes in Step 1. Each pin gets the repo's license/justification comment in
a new `<!-- Cloudstrap.Hangfire (deliverable #16) -->` ItemGroup:

- **`Hangfire.Core`, `Hangfire.SqlServer`, `Hangfire.AspNetCore`: `1.8.25` (lockstep, DD-7).** Comment:
  **LGPL-3.0** (Hangfire OÜ, multi-licensed, LGPL by default; OSI-approved; the first non-permissive family
  in the suite, disclosed in the package README per the founding Package Map). Also note: free tier only;
  `Hangfire.AspNetCore` brings `Hangfire.NetCore` (LGPL-3.0); `Hangfire.Core` brings `Newtonsoft.Json`
  (MIT); `Hangfire.InMemory` was verified LGPL-3.0 and deliberately **not** referenced (spec Dependencies).
- **`Microsoft.Data.SqlClient` (MIT, D-4)**: see mechanic (a). The version is decided at pin time, not here.
- **`Scrutor` 7.0.0**: existing pin, comment updated to name `Cloudstrap.Hangfire` as the second consumer.
- **Possible security floor `Newtonsoft.Json`**: see mechanic (a) item 4.
- **No other new pins.** The test project uses the existing test-only pins `Microsoft.AspNetCore.TestHost`,
  `OpenTelemetry.Exporter.InMemory`, `OpenTelemetry.Extensions.Hosting` and `Microsoft.Extensions.Hosting`.
  Never `Hangfire.InMemory`, `Hangfire.Pro.*`, `Aspire.*`, `Nihdi.*` or `Testcontainers.*`.

Package references: `Cloudstrap.Core` + `Cloudstrap.Observability` (projects), `<FrameworkReference
Include="Microsoft.AspNetCore.App" />` (the `Cloudstrap.Worker.csproj` precedent; already transitive via
Observability), `Hangfire.Core`, `Hangfire.SqlServer`, `Hangfire.AspNetCore`, `Microsoft.Data.SqlClient`,
`Scrutor`. `InternalsVisibleTo` goes to `Cloudstrap.Hangfire.Tests` **only** (DD-1).

### ⚠️ Risk areas (reviewed at the gates named)

- **New LGPL-3.0 dependency family + `Microsoft.Data.SqlClient` pin (D-4, DD-7)**: the props diff, the
  filled Pin record, and any transitive effect on EF Core / Wolverine consumers. Reviewed at **Gate 1**.
- **Public API one-way doors**, all reviewed verbatim against the spec's Public API Sketch:
  - At **Gate 1**: `AddCloudstrapHangfire`, `CloudstrapHangfireConfigurator`, the `Cloudstrap:Hangfire`
    section shape (`HangfireOptions` + four sub-option types), and `IBackgroundRecurringTask` (D-3).
  - At **Gate 2**: the **persisted wire contract** `Cloudstrap.Hangfire.RecurringTaskRunner.RunAsync(string,
    CancellationToken)`, `CloudstrapHangfireActivitySources`, and the `[DisplayName]` choice (mechanic (h)).
  - At **Gate 3**: `MapCloudstrapHangfireDashboard` and `AddCloudstrapHangfireHealthCheck`, with the whole
    surface frozen by Step 7's guards.
- **Auth risk area (D-1)**: the dashboard's endpoint policy, the defense-in-depth filter, the no-scheme
  throw, the `configure` "tighten, never loosen" rule, and the antiforgery interplay (mechanic (i)).
  Reviewed at **Gate 3** (library) and the **final gate** (live, through the demo IdP).
- **Two-host topology posture (D-2)**: scheduling tied to `RunServer`, the zero-task guard, and the
  schema-per-processing-host golden rule. Reviewed at **Gate 2** (LocalDB two-host proof) and **Gate 3**
  (README wording).
- **E2E/demo infrastructure**: the BlazorServer demo now needs LocalDB for its dashboard/health check; every
  Worker-booting E2E fixture becomes a Hangfire processing host; a new fixture on port 5353. Reviewed at the
  **final gate**.

### Planner mechanics decided here (each reviewed at the named gate)

**(a) Pin-time procedure: an explicit executor action inside Step 1 (Gate 1).** *The planning session had
no network access.* Before editing `src/Directory.Packages.props` the executor:

1. Confirms `Hangfire.Core`/`.SqlServer`/`.AspNetCore` **1.8.25** on nuget.org (spec evidence 2026-08-28). If
   a newer 1.8.x patch exists, it keeps 1.8.25 unless the user says otherwise at Gate 1; DD-7 fixed the
   version.
2. Reads **`Microsoft.EntityFrameworkCore.SqlServer` 10.0.10's `Microsoft.Data.SqlClient` dependency range**
   from its nuspec. Transitive pinning is on, so a central `Microsoft.Data.SqlClient` pin **also moves the
   version EF Core and Wolverine's SQL packages resolve** in every test and demo project.
3. Checks whether **`Microsoft.Data.SqlClient` 7.x moved Entra ID (`Authentication=Active Directory
   Default`) support out of the core package** into a separate extension package. The spec's Behaviors
   table promises managed identity "through the connection string".
4. **Decision rule**:
   - Pin **7.1.0** (the spec's evidence) only if it (i) satisfies EF Core 10.0.10's range and (ii) keeps
     `Active Directory Default` working with no extra package.
   - Otherwise pin the **highest 6.x release that satisfies EF Core 10.0.10's range**, which is the
     alignment the spec calls "a plan-time check".
   - If restore raises a NuGet audit warning (NU1901–NU1904, fatal under `TreatWarningsAsErrors`) for
     `Newtonsoft.Json` via `Hangfire.Core`, add a **transitive security floor** pin at the current
     `13.0.x`. This is the `System.Security.Cryptography.Xml` precedent: same comment style, placed in the
     transitive-pinning `ItemGroup`.
5. Fills the **Pin record** below before GREEN.

The full existing unit + E2E suites are the regression gate for the SqlClient pin, since
Messaging/AzureBlob/E2E all use SqlClient through EF Core and Wolverine.

#### Pin record *(filled by the executor in Step 1, not a planning-time value)*

| Item | Value |
|---|---|
| `Hangfire.*` family version (three packages, lockstep) | **1.8.25**: the latest stable, no newer 1.8.x exists. `Hangfire.SqlServer`/`.AspNetCore` require `Hangfire.Core [1.8.25]` exactly. |
| `Microsoft.Data.SqlClient` version pinned + reason (EF Core 10.0.10 range; Entra ID support in the core package?) | **6.1.7**, the highest 6.x, not 7.1.0. EF Core SqlServer 10.0.10 requires `>= 6.1.1` with no upper bound, so 7.1.0 would satisfy the range. But 7.x no longer depends on `Azure.Identity`: Entra ID authentication moved to the separate `Microsoft.Data.SqlClient.Extensions.Azure` package, which breaks the "managed identity through the connection string alone" promise. Also, `Weasel.SqlServer` 9.32.0 (Wolverine) is compiled against 6.1.3. Transitive effect: EF Core and Wolverine move from 6.1.3 to 6.1.7 in every test and demo project, a patch-level move on the same line. |
| `Newtonsoft.Json` security floor needed? version | **Yes, 13.0.3.** `Hangfire.Core` floors it at 11.0.1, and restore raised NU1903 (GHSA-5crp-9r3c-p9vr, fixed in 13.0.1). 13.0.3 is what the Wolverine family already resolves, so the floor moves no existing consumer. It is placed in the transitive-pinning ItemGroup next to `System.Security.Cryptography.Xml`. |
| Verified on / source | 2026-09-26, from the nuget.org v3 flat container (version indexes and nuspecs of `Hangfire.*` 1.8.25, `Microsoft.Data.SqlClient` 6.1.7 / 7.1.0, `Microsoft.EntityFrameworkCore.SqlServer` 10.0.10 and `Weasel.SqlServer` 9.32.0). |
| Regression evidence (all unit exes + E2E green on the new SqlClient) | All 16 unit executables are green (855 tests, including the LocalDB Messaging and AzureBlob suites, which resolved 6.1.7). E2E: 63 of 63 green. The Release build produces `Cloudstrap.Hangfire.1.0.0.nupkg`. |

**(b) Options pipeline (the #1/#14 pattern; Gate 1).**
- `HangfireOptions` (`SectionName = "Cloudstrap:Hangfire"`) is bound **eagerly at the call**
  (`configuration.GetSection(...).Get<HangfireOptions>() ?? new()`, then validated, throwing
  `ConfigurationValidationException`). It is **also** bound via `AddOptions<HangfireOptions>().Bind(...)
  .ValidateOnStart()` with `TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<HangfireOptions>,
  HangfireOptionsValidator>())`.
- The validator combines a source-generated internal `[OptionsValidator]` partial
  (`HangfireOptionsAnnotationsValidator`; `[Range(1, int.MaxValue)]` on `Server:WorkerCount`) with hand-written
  rules:
  - `Server:Queues` entries must be non-empty.
  - `Dashboard:Path` must be rooted with no trailing slash.
  - `Storage:ConnectionStringName` must be non-empty.
  - `Storage:SchemaName`, when set, must be non-empty.
- Every failure names the exact key (`'Cloudstrap:Hangfire:Server:WorkerCount'` …) and never echoes a value.
- `Jobs` is a `Dictionary<string, HangfireJobOptions>(StringComparer.OrdinalIgnoreCase)`. The scheduler
  still looks ids up case-insensitively on its own (the spec: "the binder's comparer is not relied on").
- Connection-string resolution: `builder.Configuration.GetConnectionString(name)`. When it is null or empty,
  the host throws `InvalidOperationException` naming `'ConnectionStrings:{name}'` and
  `'Cloudstrap:Hangfire:Storage:ConnectionStringName'`, never a value (AC-HF1). **Skipped entirely when
  `configurator.Storage` is set.**
- Effective `PrepareSchema = options.Storage.PrepareSchema ?? builder.Environment.IsDevelopment()`.

**(c) Registration state + fail-fast duplicate (AC-HF19; Gate 1).** An internal sealed
`HangfireRegistrationState` singleton carries:
- the bound options;
- the configurator flags (`RunServer`, whether `Storage` is custom);
- the effective `PrepareSchema`;
- the effective schema name;
- the resolved task-assembly list;
- a mutable `DashboardPath : string?` (set by `MapCloudstrapHangfireDashboard`).

The first call registers it. A second call finds the descriptor and throws `InvalidOperationException`
naming `AddCloudstrapHangfire` at the call site. This is the #14 mechanic.

**(d) Registration order inside `AddCloudstrapHangfire` (AC-HF1/AC-HF3/AC-HF9; Gate 1).**
1. Guard.
2. Duplicate check.
3. Configurator.
4. Eager bind + validate.
5. Connection string.
6. `services.AddHangfire((sp, config) => { config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
   .UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings(); if (configurator.Storage is
   { } custom) custom(config); else config.UseSqlServerStorage(connectionString, storageOptions); })`, where
   `storageOptions` = the internal `SqlServerStorageOptionsFactory.Create(HangfireStorageOptions,
   bool prepareSchema, Action<SqlServerStorageOptions>? hatch)`:
   - `CommandBatchMaxTimeout = 5 min`, `SlidingInvisibilityTimeout = 5 min`, `QueuePollInterval = Zero`,
     `UseRecommendedIsolationLevel = true`, `DisableGlobalLocks = true`;
   - `PrepareSchemaIfNecessary = prepareSchema`;
   - `SchemaName` set **only** when `Storage:SchemaName` is set (D-7);
   - `SqlClientFactory = Microsoft.Data.SqlClient.SqlClientFactory.Instance`, which makes the D-4 provider
     explicit instead of Hangfire's reflection probe (executor verifies the property exists on 1.8.25; if
     not, it relies on Hangfire's default probe and records that);
   - the `SqlServer` hatch runs **last**.
7. Scrutor scan: `services.Scan(s => s.FromAssemblies(assemblies).AddClasses(c =>
   c.AssignableTo<IBackgroundRecurringTask>(), publicOnly: false).As<IBackgroundRecurringTask>()
   .WithTransientLifetime())`. Assemblies = `configurator.TaskAssemblies` when non-empty, else
   `Assembly.GetEntryAssembly()` when non-null, else none.
8. `services.TryAddScoped<RecurringTaskRunner>()` and `services.TryAddScoped<RecurringJobsScheduler>()`.
9. When `RunServer`: `services.AddHostedService<RecurringTaskSchedulingService>()` **then**
   `services.AddHangfireServer((sp, server) => ServerOptionsComposer.Apply(server, options.Server,
   configurator.Server))`. Hosted services start in registration order, so scheduling finishes before the
   server dequeues. The composer sets `WorkerCount`/`Queues` when configured, then runs the `Server` hatch last.
10. `services.ConfigureOpenTelemetryTracerProvider(t => t.AddSource(CloudstrapHangfireActivitySources.RecurringTask))`.
11. Return `builder`.

**Discovery includes non-public classes (`publicOnly: false`).** This is a planner decision the spec leaves
open. The source used Scrutor's public-only default. The suite's own rule is "internal by default", so a
consumer writing `internal sealed class NightlyCleanupTask` would otherwise be **silently unscheduled**. The
decision is reviewed at Gate 1. If the user prefers source parity, the flag flips and the demo task (Step 8)
becomes `public`.

**(e) Startup summary: where it is logged (Gate 1 / Gate 3).** The spec asks for "one startup summary"
(storage provider, schema, PrepareSchema in force, RunServer, task count, dashboard path when mapped) **and**
for "no `IHostedService` from this package" on a storage-only host (AC-HF9). These are reconciled as follows:
- **Processing host**: `RecurringTaskSchedulingService.StartAsync` logs the posture line, then one line per
  job (id, cron, time zone, or "disabled"). The dashboard path is included when
  `MapCloudstrapHangfireDashboard` ran first. It always does on a `WebApplication`, because mapping happens
  before `RunAsync`.
- **Storage-only host**: `MapCloudstrapHangfireDashboard` logs the posture line once at mapping time.
- **A storage-only host that never maps a dashboard** (enqueue-only) logs no summary. This is recorded in
  the README.
- Messages come from an internal static partial `HangfireStartupSummaryLogger` (`[LoggerMessage]`,
  category `Cloudstrap.Hangfire`). Posture line: `Cloudstrap Hangfire: storage {StorageProvider}, schema
  '{Schema}', prepare schema {PrepareSchema}, run server {RunServer}, {TaskCount} recurring task(s),
  dashboard {DashboardPath}`. `StorageProvider` is `SqlServer` or `custom`. The connection string, server
  or database name never appear.

**(f) Test harness (Steps 1–6).** The new project `src/Test/UnitTest/Cloudstrap.Hangfire.Tests`:
- **`[assembly: NonParallelizable]`** in `AssemblyInfo.cs`. Hangfire mutates process-global statics
  (`GlobalConfiguration`, `JobStorage.Current`), the E2E `AssemblyInfo.cs` precedent.
- **Linked, not copied (DD-8)**: `<Compile Include="..\Cloudstrap.Messaging.Tests\Infrastructure\SqlServerTestDatabase.cs"
  Link="Infrastructure\SqlServerTestDatabase.cs" />`. **`CapturingLoggerProvider.cs` is linked the same way**
  (same reasoning; #8 hoists both). Linked files keep their `Cloudstrap.Messaging.Tests.Infrastructure`
  namespace, and the tests add a `using`.
  - ⚠️ Consequence: the linked helper hard-codes the LocalDB catalog `CloudstrapMessagingTests`, so the
    Hangfire storage tests **share that catalog** with the Messaging tests. Test exes run sequentially
    (CI loop; local per-exe runs). Each Hangfire fixture calls `ResetAsync()` and uses its own
    `Cloudstrap:Hangfire:Storage:SchemaName = "hangfire_tests"`. Under `CLOUDSTRAP_TEST_SQL` all suites
    already share `CloudstrapCi`. #8's hoist parameterizes the catalog. Recorded, reported at Gate 2.
- **No SQL in Steps 1–3, 5 and the fake-backed half of 6**. Storage comes from the `configurator.Storage`
  hatch (`config => config.UseStorage(fakeStorage)`) with **hand-written fakes** in `Fakes/`:
  - `FakeJobStorage : JobStorage`;
  - `FakeStorageConnection : JobStorageConnection` (recurring-job set/hash stubs, a scriptable
    `AcquireDistributedLock`, a disposal counter);
  - `FakeMonitoringApi : IMonitoringApi` (scriptable `GetStatistics`, empty lists elsewhere);
  - `RecordingRecurringJobManager : IRecurringJobManager` (records `AddOrUpdate`/`RemoveIfExists`/`Trigger`,
    can throw on demand).

  **No mocking library.** The repo pins none (`Directory.Packages.props` has no Moq/NSubstitute) and CLAUDE.md
  rule 4 makes a new test dependency a reviewed change. The source's *technique* (stubbing the storage
  abstractions) is ported with fakes. Registration-shape tests use
  `Storage:PrepareSchema = false` and an unreachable connection string (`Server=unreachable.invalid;…;Connect
  Timeout=1`) and assert on descriptors / composed options only, so no connection is opened.
- **Fixture-task discipline.** Under Microsoft.Testing.Platform the test exe **is** the entry assembly, so
  the default scan discovers the test project's own tasks:
  - Discoverable fixtures (`Fixtures/Tasks/`) are all well-behaved:
    - `NightlyCleanupTask` (public, `IDisposable` too);
    - `InternalReportTask` (internal);
    - `DisabledInCodeTask` (`IsEnabled = false`);
    - `NoOverlapGuardTask` (`PreventOverlappingRuns = false`);
    - `GatedTask` (blocks on a static `SemaphoreSlim` for lock tests);
    - `ThrowingTask` (throws when run);
    - `RecordingTask` (appends to a static `InvocationRecorder`);
    - `AbstractBaseTask` (abstract, never registered).
  - **Misbehaving cases are never discoverable.**
    - Duplicate ids use closed generics of the open-generic fixture `SameIdTask<TMarker>` (`JobId =>
      "duplicate-id"`), registered manually (`SameIdTask<First>`, `SameIdTask<Second>`). Scrutor skips
      open generic definitions.
    - Bad cron / bad time zone come from **configuration overrides** on a well-behaved task (AC-HF6 is
      about per-job config values).
    - `RecurringJobsScheduler` unit tests construct the scheduler directly with explicit task lists.

**(g) Full-suite check (standing convention: `runTests` is not on the agent PATH; VERIFY invokes each exe
directly).** Run `dotnet build src/Cloudstrap.sln`, then **every** unit exe
`src/Test/UnitTest/<Name>.Tests/bin/Debug/net10.0/<Name>.Tests.exe` (the existing set plus
**`Cloudstrap.Hangfire.Tests`**, new in Step 1). Then run the E2E exe
`src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe`, then
`dotnet format src/Cloudstrap.sln --verify-no-changes`.

**(h) `[DisplayName("{0}")]` on `RecurringTaskRunner.RunAsync`: INCLUDED (planner's call, as delegated).**
- **Rationale:**
  - Hangfire's `DisplayNameAttribute` is read at **render time** from the method metadata. It is not
    persisted into storage, so it adds nothing to the one-way door and can be removed later without
    orphaning a job.
  - Without it, every job in the dashboard's Succeeded/Failed/Processing lists reads
    `RecurringTaskRunner.RunAsync`. That is useless when several tasks exist.
  - With it, the list shows the job id (`{0}` = the first argument, `jobId`; the `CancellationToken`
    argument is never rendered).
  - Cost: one attribute from a namespace the package already references. No behavior change.
- Proven by a reflection test (Step 3) and visible in the demo (Step 9).
- Reviewed at Gate 2.

**(i) Hardened antiforgery vs the dashboard over plain HTTP (final gate).** `Hangfire.AspNetCore` uses the
host's `IAntiforgery` (when registered) to issue and validate tokens for dashboard POST actions such as
*Trigger now*. The #12 composite hardens the antiforgery cookie to `SecurePolicy = Always`. ASP.NET Core's
`DefaultAntiforgery` refuses to issue tokens on a non-HTTPS request under that policy. The demo and E2E run
on `http://127.0.0.1:5340`, so the demo dashboard is expected to fail.
- **Library posture: unchanged.** The package never sets `DashboardOptions.IgnoreAntiforgeryToken` (that
  would be CSRF weakening on a cookie-authenticated UI). The README documents "serve the dashboard over
  HTTPS on hosts with hardened antiforgery".
- **Demo resolution** (Step 9, only if the executor observes the failure): the BlazorServer demo relaxes the
  antiforgery cookie to `CookieSecurePolicy.SameAsRequest` **in Development only**, through the shipped hook
  (`builder.AddCloudstrapBlazorServer(c => c.Antiforgery = a => a.Cookie.SecurePolicy = …)`), with a
  teaching comment and a README harness note.
- If the dashboard works over HTTP without it, nothing is changed and that is recorded.

**(j) Demo topology (AC-HF21, D-5; final gate).**
- Both demo hosts use `ConnectionStrings:DefaultConnection` → the `CloudstrapDemo` LocalDB database.
  Hangfire's default schema is `HangFire` (D-7, zero configuration), next to `demo`, `demo_transport` and
  `demo_application_*`.
- `Cloudstrap.Demo.Worker` is the processing host with `DemoOrderCountRecurringTask` (`CronExpression =
  "* * * * *"`, logs the `demo.Orders` row count via `WorkerDbContext`).
- `Cloudstrap.Demo.BlazorServer` is storage-only (`RunServer = false`), maps the dashboard in its
  `ConfigureEndpoints` hook next to `MapCloudstrapAuthenticationEndpoints()`, and uses
  `Cloudstrap:Hangfire:Dashboard:RequiredRole = "tester"`.
- Health checks: the Worker uses `AddCloudstrapHangfireHealthCheck()` (Unhealthy, `ready`); the
  BlazorServer uses `failureStatus: HealthStatus.Degraded`, so the app still answers `/ready` 200 without
  LocalDB.
- **Every** Worker-booting E2E fixture (`WorkerHostTests` 5350, `MessagingTests` 5351, `ClaimCheckTests`
  5352, new `HangfireTests` 5353) is a processing host with the **same** task set. `AddOrUpdate` is
  idempotent and reconciliation removes nothing, so they are safe sequentially (assembly-level
  `NonParallelizable`). Killed processes leave dead-server records that Hangfire times out.
- The new `HangfireTests : PageTestBase` fixture boots its own Worker on health port **5353** and its own
  BlazorServer on **5340**. `BlazorServerTests` boots 5340 separately; fixtures never overlap.
- `BlazorServerTests` and `HangfireTests` forward `CLOUDSTRAP_TEST_SQL` to the BlazorServer, as
  `WorkerHostTests`/`MessagingTests`/`ClaimCheckTests` already do for the Worker.
- **D-5 fallback**: waiting for the every-minute cron is used **only** if the dashboard's *Trigger now*
  DOM proves brittle under Playwright. The executor reports it at the final gate if used.

**Target consumer composition** (the spec made concrete: also the README quick start in Step 7 and the demo
`Program.cs` files in Steps 8–9):

```csharp
// Processing host (generic host) — demo Worker
builder.UseCloudstrapObservability();
builder.AddCloudstrapWorker();
builder.AddCloudstrapHangfire();                                          // storage + server + scheduling + discovery
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck();    // ready-tagged

// Storage-only dashboard host (web host) — demo BlazorServer
builder.AddCloudstrapHangfire(hangfire => hangfire.RunServer = false);
builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck(failureStatus: HealthStatus.Degraded);
app.UseCloudstrapBlazorServer<App>(pipeline => pipeline.ConfigureEndpoints = endpoints =>
{
    endpoints.MapCloudstrapAuthenticationEndpoints();
    endpoints.MapCloudstrapHangfireDashboard();                           // after the auth middleware by construction
});
```

---

## Slice 1: A processing host registers Hangfire in one call and schedules its declared tasks at startup; configuration can retune any job, and misconfiguration stops startup ⚠️ NEW DEPENDENCY FAMILY (LGPL) + PUBLIC-API ONE-WAY DOORS

---

## Step 1: `AddCloudstrapHangfire()` sets up Hangfire without touching storage (AC-HF1; AC-HF2; AC-HF9 descriptors; AC-HF17 effective value; AC-HF18; AC-HF19; mechanics (a)–(d), (f)) ⚠️ dependency add + public API

After this step:
- `Cloudstrap:Hangfire` binds and validates, naming the exact key on failure.
- The connection string is resolved by name, failing on the key and never echoing the value.
- SQL Server storage is registered with the recommended options, the simple type serializer and the
  explicit `PrepareSchema` default.
- Tasks are discovered and registered only as `IBackgroundRecurringTask`.
- A processing host registers scheduling before the Hangfire server; a storage-only host registers no
  hosted service.
- The span source is contributed.
- A second call fails fast.

- [x] Done *(checked by the executor when VERIFY passes; user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Directory.Packages.props` *(modify)*: mechanic (a) and the Dependency closure section.
- `_plans/16-HangfireScheduler.md` *(modify)*: the **Pin record** filled.
- `src/Cloudstrap.Hangfire/Cloudstrap.Hangfire.csproj` *(create)*:
  - `Microsoft.NET.Sdk`, `net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`;
  - `FrameworkReference Microsoft.AspNetCore.App`;
  - ProjectReferences `Cloudstrap.Core` + `Cloudstrap.Observability`;
  - version-less PackageReferences `Hangfire.Core`, `Hangfire.SqlServer`, `Hangfire.AspNetCore`,
    `Microsoft.Data.SqlClient`, `Scrutor`;
  - `<InternalsVisibleTo Include="Cloudstrap.Hangfire.Tests" />`.
  - Description/tags/README metadata land in Step 7 (packable from day one, the #14/#15 precedent).
- `src/Cloudstrap.Hangfire/HostApplicationBuilderExtensions.cs` *(create, public static)*:
  `AddCloudstrapHangfire(this IHostApplicationBuilder builder, Action<CloudstrapHangfireConfigurator>?
  configure = null) : IHostApplicationBuilder`, mechanics (b)–(d). Full XML docs cover:
  - the golden rules (one processing host per storage/schema; storage-only hosts set `RunServer = false`);
  - "inject Hangfire's `IBackgroundJobClient` for fire-and-forget work (no facade)";
  - the `<exception>` list.
- `src/Cloudstrap.Hangfire/CloudstrapHangfireConfigurator.cs` *(create, public sealed)*: `RunServer = true`,
  `TaskAssemblies : IList<Assembly>` (empty list), `SqlServer : Action<SqlServerStorageOptions>?`,
  `Storage : Action<IGlobalConfiguration>?`, `Server : Action<BackgroundJobServerOptions>?`, all per the
  sketch.
- `src/Cloudstrap.Hangfire/HangfireOptions.cs`, `HangfireStorageOptions.cs`, `HangfireServerOptions.cs`,
  `HangfireDashboardOptions.cs`, `HangfireJobOptions.cs` *(create, public sealed)*: the sketch verbatim,
  with defaults `ConnectionStringName = "DefaultConnection"`, `Path = "/hangfire"`, `ReadOnly = false`.
- `src/Cloudstrap.Hangfire/HangfireOptionsAnnotationsValidator.cs` *(create, internal `[OptionsValidator]`
  partial)* + `HangfireOptionsValidator.cs` *(create, internal)*: mechanic (b).
- `src/Cloudstrap.Hangfire/IBackgroundRecurringTask.cs` *(create, public interface)*: the D-3 shape with
  default interface members. XML docs cover:
  - the Hangfire/Cronos cron syntax;
  - `JobId` rename sensitivity ("override `JobId` for anything operators reference in `Jobs:`");
  - the IANA time-zone guidance;
  - "resolved per run in its own DI scope; honor the token".
- `src/Cloudstrap.Hangfire/HangfireRegistrationState.cs` *(create, internal sealed)*: mechanic (c).
- `src/Cloudstrap.Hangfire/SqlServerStorageOptionsFactory.cs` + `ServerOptionsComposer.cs` *(create,
  internal static)*: mechanic (d).
- `src/Cloudstrap.Hangfire/CloudstrapHangfireActivitySources.cs` *(create, public static)*:
  `public const string RecurringTask = "Cloudstrap.Hangfire";`.
- `src/Cloudstrap.Hangfire/RecurringTaskRunner.cs` + `RecurringJobsScheduler.cs` +
  `RecurringTaskSchedulingService.cs` *(create; **registration shells only**)*. They exist so the
  descriptors can be registered. The scheduler/service constructors take their final dependencies, but
  their bodies land in Steps 2–3. `RunAsync` resolves and runs the task by id, nothing more; this minimal
  body is completed in Step 3.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Cloudstrap.Hangfire.Tests.csproj` *(create)*:
  - `net10.0`, `FrameworkReference Microsoft.AspNetCore.App`, ProjectReference → the package;
  - version-less `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Configuration` (existing pins; **no
    mocking library**, see mechanic (f));
  - the two linked files (mechanic (f)).
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Fakes/{FakeJobStorage, FakeStorageConnection,
  FakeMonitoringApi, RecordingRecurringJobManager}.cs` *(create)*: mechanic (f).
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/AssemblyInfo.cs` *(create)*: `[assembly: NonParallelizable]`.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Fixtures/Tasks/*.cs` *(create)*: mechanic (f) fixtures.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Infrastructure/HangfireTestHost.cs` *(create)*: builds a
  `HostApplicationBuilder` with in-memory configuration (valid `Cloudstrap:Application` block,
  `ConnectionStrings:DefaultConnection` unreachable, `PrepareSchema = false`) and an optional fake
  storage via the `Storage` hatch.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/{RegistrationTests, OptionsValidationTests, DiscoveryTests,
  TaskContractTests, DependencyPinTests}.cs` *(create)*.
- `src/Cloudstrap.sln` *(modify)*: the package at the solution root, the test project under `Test\UnitTest`.

**RED** *(mechanic from #15 (a): the first failure is the new test project not compiling against missing
types; after that, real red runs)*:
- `RegistrationTests.cs`:
  - `AddCloudstrapHangfire_OnNullBuilder_ThrowsArgumentNullException`
  - `AddCloudstrapHangfire_CalledTwice_ThrowsNamingTheDuplicateRegistration` (AC-HF19)
  - `AddCloudstrapHangfire_UnresolvableConnectionStringName_FailsAtTheCallNamingTheKey_NeverTheValue`: the
    message contains `'ConnectionStrings:DefaultConnection'` and does not contain the configured value of a
    *different* existing connection string.
  - `AddCloudstrapHangfire_ConfiguredConnectionStringName_IsTheOneResolved`
  - `AddCloudstrapHangfire_CustomStorageHatch_SkipsSqlServerAndTheConnectionStringRequirement`
  - `AddCloudstrapHangfire_Default_RegistersStorageClientManagerRunnerAndScheduler_WithoutOpeningAConnection`:
    descriptors for `JobStorage`, `IBackgroundJobClient`, `IRecurringJobManager`, `RecurringTaskRunner`
    (scoped) and `RecurringJobsScheduler` (scoped); `IServiceProvider` built, nothing resolved that opens
    SQL.
  - `AddCloudstrapHangfire_RunServerTrue_RegistersTheSchedulingServiceBeforeTheHangfireServer`: the
    `IHostedService` descriptor order.
  - `AddCloudstrapHangfire_RunServerFalse_RegistersNoHostedServiceFromThisPackage` (AC-HF9 part):
    `IHostedService` descriptors equal the set present without the call.
  - `StorageOptions_AreHangfiresRecommendedSqlServerSettings`: the five fixed values,
    `PrepareSchemaIfNecessary`, and a `SchemaName` left at Hangfire's default when unset (D-7).
  - `StorageOptions_SchemaNameOverride_AndTheSqlServerHatch_RunLast`
  - `PrepareSchema_UnsetInDevelopment_IsTrue` / `_UnsetInProduction_IsFalse` /
    `_ExplicitValue_WinsInEitherEnvironment` (AC-HF17, asserted on the registration state)
  - `ServerOptions_WorkerCountAndQueues_AppliedWhenSet_ServerHatchRunsLast` (composer unit test; AC-HF1)
  - `AddCloudstrapHangfire_ContributesTheSpanSource_RegistersNoTracerProviderAndNoExporter`: the #14
    `ObservabilityTests` descriptor shape.
- `OptionsValidationTests.cs`:
  - `Options_Defaults_MatchTheSketch`
  - `Options_WorkerCountZero_FailsNamingTheKey`
  - `Options_EmptyQueueEntry_FailsNamingTheKey`
  - `Options_DashboardPathNotRootedOrTrailingSlash_FailsNamingTheKey`
  - `Options_JobsLookup_IsCaseInsensitive`
  - `Options_Failures_NeverEchoTheValue`
- `DiscoveryTests.cs` (AC-HF2):
  - `Scan_DefaultEntryAssembly_RegistersPublicAndInternalConcreteTasks_AsTheInterfaceOnly`: resolving
    `IEnumerable<IBackgroundRecurringTask>` includes `NightlyCleanupTask` and `InternalReportTask`;
    `IEnumerable<IDisposable>` does not contain `NightlyCleanupTask`; no `AbstractBaseTask`; no
    `SameIdTask<>`.
  - `Scan_TasksAreTransient`
  - `Scan_TaskAssembliesReplaceTheDefault`: an assembly with no tasks yields none, and that is not an error.
  - `ManualRegistration_IsHonoredAlongsideTheScan`
- `TaskContractTests.cs`:
  - `IBackgroundRecurringTask_Members_ReferenceNoHangfireTypes` (AC-HF18): reflection over every member
    signature.
  - `IBackgroundRecurringTask_Defaults_AreTypeNameUtcDefaultQueueEnabledAndOverlapGuarded`
  - `IBackgroundRecurringTask_HasExactlyOneExecuteAsync_TakingACancellationToken`
- `DependencyPinTests.cs` (props read from the repo root, the #15 idiom):
  - `HangfireFamily_AllThreePackagesPinnedInLockstep_At1_8_25`
  - `MicrosoftDataSqlClient_IsPinned`
  - `NoInMemoryProCommercialOrAspireHangfirePackages_ArePinned`: no `Include` starting with
    `Hangfire.InMemory`, `Hangfire.Pro`, `Aspire.`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project does not compile against the missing types
  src\Test\UnitTest\Cloudstrap.Hangfire.Tests\bin\Debug\net10.0\Cloudstrap.Hangfire.Tests.exe --filter "RegistrationTests|OptionsValidationTests|DiscoveryTests|TaskContractTests|DependencyPinTests"
  ```

**GREEN**: perform mechanic (a) and record the Pin record, add the pins, then `dotnet restore
src/Cloudstrap.sln`. Implement the Scope items with full XML docs on every public member from the start.
Hangfire API names (`SqlServerStorageOptions.SqlClientFactory`, `AddHangfireServer((sp, o) => …)`
overloads, `UseStorage`) are verified against the pinned 1.8.25 assemblies. Any deviation is recorded in
the step report, not bent around an AC.

**DB changes**: none.

**VERIFY** *(when all green, mark `Done` and continue straight to Step 2)*:
1. Test exe → all pass. One call now yields a validated, storage-registered, task-discovering Hangfire
   setup with the documented host roles, which is new behavior in the suite.
2. Full-suite check (mechanic (g)): every pre-existing unit exe **and the E2E suite** are green on the
   pinned `Microsoft.Data.SqlClient` (the transitive-pin regression gate); zero warnings; `dotnet format`
   exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Hangfire.*.nupkg` appears under
   `src/Cloudstrap.Hangfire/bin/Release/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2: The processing host schedules its tasks before processing starts (AC-HF3 mocked; AC-HF4; AC-HF5; AC-HF6; AC-HF8 mocked; mechanic (e))

After this step:
- Every enabled task is `AddOrUpdate`-d against `RecurringTaskRunner.RunAsync(jobId, CancellationToken)`
  on its queue and in its resolved time zone.
- `Jobs:{id}` overrides win over code (case-insensitive), and disabled jobs are removed.
- Duplicate ids and invalid time zones stop startup before anything is written; invalid cron values stop
  it too.
- Undeclared tasks owned by the dispatcher are reconciled away. Foreign or unloadable jobs are left alone,
  and nothing is reconciled when this host declares zero tasks.
- An override for an unknown id logs one warning.
- The summary lists every job.

- [x] Done

**Scope**:
- `src/Cloudstrap.Hangfire/RecurringJobsScheduler.cs` *(implement, internal sealed)*:
  - ctor `(IRecurringJobManager, IEnumerable<IBackgroundRecurringTask>, JobStorage, IOptions<HangfireOptions>,
    ILogger<RecurringJobsScheduler>)`;
  - `ScheduleAll()`, run as three phases:
    1. **Validate everything**: duplicates by `JobId` (`OrdinalIgnoreCase`); the effective time zone per task
       (config `TimeZone` → `TimeZoneInfo.FindSystemTimeZoneById`, wrapped into
       `ConfigurationValidationException` naming the job id, the value, the key
       `'Cloudstrap:Hangfire:Jobs:{id}:TimeZone'` and the IANA hint `use an IANA id such as
       'Europe/Brussels'`).
    2. **Write**: `RemoveIfExists` for disabled jobs; `AddOrUpdate(jobId, Job.FromExpression<RecurringTaskRunner>(r =>
       r.RunAsync(jobId, CancellationToken.None)), cron, new RecurringJobOptions { TimeZone = tz }, queue)`
       (executor uses the 1.8.25 overload that carries the queue). A Hangfire rejection of the cron is
       wrapped naming the job id, the cron value and its key, never reported as a time-zone problem.
    3. **Reconcile**: skipped with one info log when zero tasks are declared; otherwise
       `storage.GetConnection().GetRecurringJobs()`, and every job whose `Job.Type == typeof(RecurringTaskRunner)`
       and whose id is not declared is `RemoveIfExists` with one info log each. Jobs whose `Job` is null or
       whose `LoadException` is set are treated as foreign.
  - Also logs a warning for each `Jobs:` key matching no declared id.
- `src/Cloudstrap.Hangfire/RecurringTaskSchedulingService.cs` *(implement, internal sealed
  `IHostedService`)*: `StartAsync` opens a scope, resolves the scheduler, calls `ScheduleAll()`, and logs the
  posture + per-job summary. Exceptions propagate, so the host fails to start (finding 1). `StopAsync` is a
  no-op.
- `src/Cloudstrap.Hangfire/HangfireStartupSummaryLogger.cs` *(create, internal static partial)*: mechanic (e)
  messages. The job line reads `Recurring job '{JobId}': cron '{Cron}', time zone '{TimeZone}', queue
  '{Queue}'` or `Recurring job '{JobId}' disabled`.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/{RecurringJobsSchedulerTests, SchedulingServiceTests}.cs`
  *(create)*.

**RED** *(no SQL: the mechanic (f) fakes `RecordingRecurringJobManager`, `FakeJobStorage`,
`FakeStorageConnection` with the recurring-job set/hash stubs from the source test technique; linked
`CapturingLoggerProvider`)*:
- `RecurringJobsSchedulerTests.cs`:
  - `ScheduleAll_EnabledTasks_AreAddedAgainstTheDispatcher_NeverTheImplementationType` (AC-HF3): the captured
    `Job.Type == typeof(RecurringTaskRunner)`, `Method.Name == "RunAsync"`, `Args[0] == jobId`; queue and
    time zone as declared.
  - `ScheduleAll_ConfigCronTimeZoneEnabled_WinOverCode_ForAnyCaseOfTheId` (AC-HF5)
  - `ScheduleAll_EmptyConfigValues_FallBackToTheTasksOwn`
  - `ScheduleAll_DisabledByConfigOrByCode_RemovesIfExists_AndSchedulesNothing`
  - `ScheduleAll_DuplicateJobIds_FailsNamingTheIdAndBothTypes_WritesAndRemovesNothing` (AC-HF4): uses
    `SameIdTask<First>` and `SameIdTask<Second>`; the recording manager holds zero calls.
  - `ScheduleAll_UnknownTimeZone_FailsNamingJobAndValueWithTheIanaHint_NotAsACronError` (AC-HF6)
  - `ScheduleAll_TimeZoneValidation_CoversEveryTaskBeforeTheFirstWrite`: second task invalid, first never
    written.
  - `ScheduleAll_InvalidConfiguredCron_FailsNamingJobAndValue`
  - `ScheduleAll_UndeclaredDispatcherOwnedJobs_AreRemoved_OneInfoLogEach` (AC-HF8)
  - `ScheduleAll_ForeignAndUnloadableJobs_AreNeverTouched`
  - `ScheduleAll_ZeroDeclaredTasks_SkipsReconciliationEntirely_OneInfoLog` (the D-2 zero-task guard)
  - `ScheduleAll_JobsOverrideForAnUndeclaredId_LogsOneWarning`
- `SchedulingServiceTests.cs`:
  - `StartAsync_LogsThePostureLine_AndOneLinePerJob_WithIdCronAndTimeZone_NeverTheConnectionString` (AC-HF3)
  - `StartAsync_WhenSchedulingThrows_TheExceptionPropagates` (the service in isolation)
  - `HostStart_WhenSchedulingFails_FaultsStartup_AndTheHangfireServerNeverStarts`: a full host with the
    `Storage` hatch → fake storage, the recording manager set to throw (registered after the call,
    replacing Hangfire's); `host.StartAsync()` throws and the server's
    hosted service was never started (ordering from Step 1, observed).
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Hangfire.Tests\bin\Debug\net10.0\Cloudstrap.Hangfire.Tests.exe --filter "RecurringJobsSchedulerTests|SchedulingServiceTests"
  ```

**GREEN**: the Scope items, porting the source scheduler's logic with the three spec changes (validate all
before the first write, zero-task guard, `ConfigurationValidationException`).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass. Startup now schedules, overrides, validates and reconciles with host-faulting
   failures, which is new behavior.
2. Full-suite check (mechanic (g)): all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE: end of Slice 1, registration and scheduling *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval. Do not start the next step.*

⚠️ **Risk areas at this gate**:
- **New dependency family.** Review the `Directory.Packages.props` diff: three LGPL-3.0 `Hangfire.*` pins
  with the license comment; the `Microsoft.Data.SqlClient` pin and its **filled Pin record** (EF Core range,
  Entra ID support, transitive effect); any `Newtonsoft.Json` security floor. Also check the evidence that
  every pre-existing suite (incl. LocalDB Messaging tests and the E2E suite) passed on the resolved SqlClient.
- **Public API one-way doors.** Review verbatim against the spec's sketch: `AddCloudstrapHangfire(this
  IHostApplicationBuilder, Action<CloudstrapHangfireConfigurator>?)`, the configurator's five members,
  `HangfireOptions` and its four sub-types (keys, defaults), `IBackgroundRecurringTask` (D-3 shape),
  `CloudstrapHangfireActivitySources.RecurringTask`.
- **Planner decisions to confirm**: discovery includes **non-public** classes (mechanic (d)); the startup
  summary's placement (mechanic (e), including "enqueue-only storage-only host logs none"); cron failures
  are wrapped at write time (the spec requires only duplicate + time-zone pre-validation).

- [ ] Behavioral verification (Step 1): test exe output shows the duplicate-call and key-naming fail-fasts,
  the recommended storage options, the `PrepareSchema` matrix, interface-only transient discovery (public +
  internal), the scheduling-before-server descriptor order, zero hosted services on a storage-only host, and
  the pin tripwires.
- [ ] Behavioral verification (Step 2): dispatcher-bound scheduling, config-over-code, disabled removal, the
  duplicate / time-zone / cron failures that write nothing, reconciliation with foreign/unloadable jobs left
  alone and the zero-task guard, the unknown-override warning, the summary lines, and the host-faulting
  startup.
- [ ] Code review: options/validator follow the #1/#14 pattern (keys named, values never echoed);
  `sealed`/static/internal by default; single namespace `Cloudstrap.Hangfire`; XML docs complete; csproj
  references exactly as listed; no Drop-listed concept (no job-client facade, no `Use…` scheduling call,
  no environment-string checks, no `ServerName`/`QueuePollInterval` keys).
- [ ] User approved: implementation may continue past this gate.

---

## Slice 2: A triggered job runs exactly once at a time with spans and disciplined logs, proven on real SQL Server and across two hosts sharing one schema ⚠️ PERSISTED WIRE CONTRACT

---

## Step 3: A triggered recurring task runs once at a time and can be observed (AC-HF7 mocked; AC-HF15; AC-HF16; mechanic (h))

After this step, `RecurringTaskRunner.RunAsync(jobId, ct)`:
- resolves the task by id, and throws `InvalidOperationException` naming the id when none is registered;
- takes `recurring-task:{jobId}` with zero wait when overlap prevention is on, skipping with one warning
  when the lock is busy and releasing it at the end;
- runs the task inside a `Cloudstrap.Hangfire` span that ends with Ok or Error status;
- logs start / completed-in-ms / failed-after-ms (with the exception), rethrows so Hangfire retries, and
  never logs arguments or payloads;
- shows the job id in the dashboard through `[DisplayName("{0}")]`.

The span also reaches a consumer-owned OTel pipeline, and nothing happens without one.

- [ ] Done

**Scope**:
- `src/Cloudstrap.Hangfire/RecurringTaskRunner.cs` *(implement, `public sealed`, `[EditorBrowsable(Never)]`)*:
  - ctor `(IEnumerable<IBackgroundRecurringTask>, JobStorage, ILogger<RecurringTaskRunner>)`;
  - `[DisplayName("{0}")] public async Task RunAsync(string jobId, CancellationToken cancellationToken)`
    with guard clauses;
  - `private static readonly ActivitySource` named `CloudstrapHangfireActivitySources.RecurringTask`;
  - activity name `$"RecurringTask {jobId}"`, tag `cloudstrap.hangfire.job_id`;
  - `DistributedLockTimeoutException` → skip;
  - XML docs state the **wire-contract warning** ("Hangfire persists this type and method by name for every
    recurring job; never rename or move").
- `src/Cloudstrap.Hangfire/RecurringTaskLog.cs` *(create, internal static partial `[LoggerMessage]`)*:
  - `Recurring task {JobId} started`;
  - `Recurring task {JobId} completed in {ElapsedMilliseconds} ms`;
  - `Recurring task {JobId} failed after {ElapsedMilliseconds} ms` (Error, with the exception);
  - `Recurring task {JobId} skipped: the previous run still holds the lock` (Warning).
  - The E2E in Step 9 asserts on the *completed* line.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Cloudstrap.Hangfire.Tests.csproj` *(modify)*: version-less
  `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.InMemory` (existing test pins).
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/{RecurringTaskRunnerTests, TelemetryTests}.cs` *(create)*.

**RED**:
- `RecurringTaskRunnerTests.cs` (`FakeJobStorage.GetConnection()` → `FakeStorageConnection.AcquireDistributedLock`):
  - `RunAsync_ResolvesTheTaskByJobId_AndPassesTheCancellationToken`
  - `RunAsync_UnknownJobId_ThrowsInvalidOperationExceptionNamingTheId`: the message states "No registered
    recurring task with id 'X'".
  - `RunAsync_OverlapGuarded_AcquiresRecurringTaskLockWithZeroWait_AndReleasesItAfterTheRun`: resource
    `recurring-task:{id}`, timeout `TimeSpan.Zero`, the returned `IDisposable` disposed.
  - `RunAsync_LockBusy_SkipsTheRun_WithOneWarningNamingTheJob` (AC-HF7): the task was never executed.
  - `RunAsync_PreventOverlappingRunsFalse_TakesNoLock`
  - `RunAsync_TaskThrows_LogsFailedWithElapsedAndException_AndRethrows` (AC-HF16)
  - `RunAsync_Completes_LogsStartedAndCompletedWithElapsed_NeverArgumentsOrPayloads`: `ThrowingTask`
    carries a sentinel in its exception message, which is allowed only inside the exception object, never
    in a formatted message template. `RecordingTask` carries a sentinel field that never appears in any
    captured log.
  - `RunAsync_CarriesDisplayNameShowingTheJobId`: reflection shows `Hangfire.DisplayNameAttribute` with
    `"{0}"` on `RunAsync` (mechanic (h)).
- `TelemetryTests.cs` (AC-HF15):
  - `RunAsync_EmitsOneSpanNamedRecurringTaskJobId_WithTheJobIdTag_StatusOk` (`ActivityListener` on
    `Cloudstrap.Hangfire`)
  - `RunAsync_TaskThrows_SpanStatusIsError`
  - `ConsumerOwnedPipeline_ReceivesTheSpan_ThroughTheContributedSource`: a host with the consumer's own
    `AddOpenTelemetry().WithTracing(t => t.AddInMemoryExporter(list))` (no Cloudstrap observability) +
    `AddCloudstrapHangfire` (fake storage hatch, `RunServer = false`). `RecurringTaskRunner` is resolved
    in a scope and `RunAsync` called; `list` contains the span.
  - `NoPipeline_RunAsyncStillRuns_NoOp`
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Hangfire.Tests\bin\Debug\net10.0\Cloudstrap.Hangfire.Tests.exe --filter "RecurringTaskRunnerTests|TelemetryTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass. Runs are now overlap-guarded, traced and logged per the spec.
2. Full-suite check (mechanic (g)): all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4: Proven on real SQL Server LocalDB (AC-HF1 live; AC-HF3/AC-HF7/AC-HF8/AC-HF17 on real storage; AC-HF9 storage half; mechanic (f)) ⚠️ first LocalDB tests of the package

After this step:
- `PrepareSchema = true` creates Hangfire's schema; `false` against a missing schema stops processing-host
  startup.
- A processing host schedules its tasks, and a job triggered through `IRecurringJobManager` runs through
  the real `BackgroundJobServer`.
- The configured `WorkerCount`/`Queues` show on the live server.
- An orphaned dispatcher job is reconciled away, and a foreign job survives.
- A second trigger while the first run holds the real SQL distributed lock is skipped.
- A storage-only host on the same schema lists the processing host's recurring jobs and never registers a
  server.

- [ ] Done

**Scope**:
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Infrastructure/HangfireSqlHost.cs` *(create)*: builds real
  hosts on `SqlServerTestDatabase.ConnectionString` with `Storage:SchemaName = "hangfire_tests"`, optional
  `RunServer`, `Server:WorkerCount = 1`, and a short `SchedulePollingInterval` through the `Server` hatch so
  triggers are picked up in seconds.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/SqlServerStorageTests.cs` *(create)*.
- `.github/workflows/ci.yml` *(modify, comment only)*: the `CLOUDSTRAP_TEST_SQL` comment also names the
  Hangfire storage fixtures.

**RED** *(⚠️ D-3 LocalDB, no Docker, no cloud; `[OneTimeSetUp]` → `SqlServerTestDatabase.ResetAsync()`;
every test disposes its hosts; the helper is **linked**, mechanic (f))*:
- `SqlServerStorageTests.cs`:
  - `PrepareSchemaTrue_InstallsTheHangfireTables_InTheConfiguredSchema` (AC-HF17): the linked
    `TableExistsAsync("hangfire_tests", "Job")` returns true.
  - `PrepareSchemaFalse_AgainstAMissingSchema_FaultsProcessingHostStartup`: a fresh schema name, so
    `host.StartAsync()` throws and nothing is silently empty.
  - `ProcessingHost_SchedulesItsTasks_AndATriggeredJobRunsThroughTheRealServer` (AC-HF3): after start, the
    storage lists `RecordingTask` with its cron; `IRecurringJobManager.Trigger("RecordingTask")` leads to the
    recorder seeing one execution within a 30 s deadline (polled, never slept).
  - `ProcessingHost_ServerOptions_AreVisibleOnTheLiveServer` (AC-HF1): the monitoring API's `Servers()`
    shows the configured `WorkerCount` and `Queues`.
  - `Reconciliation_OnRealStorage_RemovesTheOrphan_KeepsTheForeignJob` (AC-HF8): host A declares an extra
    manually registered task and stops. A foreign job is added with `RecurringJob.AddOrUpdate` against a
    test static method. Host B declares fewer tasks and starts: the orphan is gone and the foreign job
    remains.
  - `OverlapLock_OnRealStorage_SecondTriggerIsSkippedWhileTheFirstRunHoldsTheLock` (AC-HF7): `GatedTask`
    blocks, a second trigger follows, the skip warning is captured, and the gate is released. Exactly one
    execution, and after release a third trigger runs (lock released).
  - `StorageOnlyHost_OnTheSameSchema_ListsTheProcessingHostsJobs_AndRegistersNoServer` (AC-HF9): a
    `RunServer = false` host on the same schema reads `connection.GetRecurringJobs()` → the ids of the
    processing host. Every job's `Job.Type` is `RecurringTaskRunner` (the property that lets a host without
    the worker's assembly render it; the cross-process proof is Step 9). The monitoring API's server count
    equals the processing host's alone.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Hangfire.Tests\bin\Debug\net10.0\Cloudstrap.Hangfire.Tests.exe --filter "SqlServerStorageTests"
  ```

**GREEN**: no new production code is expected. Steps 1–3 wired the behavior and this step proves it on real
storage. Any gap (for example Hangfire's lock exception surface on SQL Server, or the queue overload) is
fixed minimally in the package and reported at Gate 2.

**DB changes**: none. Hangfire installs its schema in the reset test catalog.

**VERIFY**:
1. Test exe → all pass on LocalDB. The two-host topology, real locks, reconciliation and schema posture are
   observed on real SQL Server.
2. Full-suite check (mechanic (g)): all green; `dotnet format` exit 0. Record the shared-catalog
   observation (mechanic (f)) in the step report.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE: end of Slice 2, running jobs and the wire contract *(covers Steps 3–4)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval. Do not start the next step.*

⚠️ **Risk areas at this gate**:
- **The persisted wire contract.** `Cloudstrap.Hangfire.RecurringTaskRunner.RunAsync(string,
  CancellationToken)`: type name, namespace, assembly name, method name and parameter list are chosen
  **once** (renaming later orphans every persisted job; DD-6). Also `[EditorBrowsable(Never)]`.
- **The `[DisplayName("{0}")]` decision** (mechanic (h): included; render-time only, not persisted).
- **The two-host posture (D-2)** as observed on LocalDB.
- **The linked-helper shared catalog** (mechanic (f)).
- **Any Hangfire-API deviations** found in Step 4.

- [ ] Behavioral verification (Step 3): resolve-by-id plus the unknown-id failure; zero-wait lock with skip
  warning and release; the no-lock path; failure logging plus rethrow; no payloads in logs; the Ok/Error span
  reaching a consumer-owned pipeline; the no-pipeline no-op; the `DisplayName` tripwire.
- [ ] Behavioral verification (Step 4): schema install and the missing-schema startup fault; the real
  schedule → trigger → run round trip; live server options; real-storage reconciliation; the real
  distributed-lock skip; the storage-only host listing the processing host's jobs with no server.
- [ ] Code review: the runner's XML docs carry the wire-contract warning; logs are structured (ids +
  elapsed); `using`/disposal of the lock and connection; tests poll with deadlines (no bare sleeps).
- [ ] User approved: implementation may continue past this gate.

---

## Slice 3: Operators see and operate the worker's jobs from any web host behind authentication; probes know about storage; the package is publishable and guarded ⚠️ AUTH RISK AREA + PUBLIC API FROZEN + LGPL NOTICE

---

## Step 5: `MapCloudstrapHangfireDashboard()` serves the dashboard only to authenticated users (AC-HF10; AC-HF11; AC-HF12; AC-HF13; mechanic (e) storage-only summary; D-1) ⚠️ auth

After this step, on any web host with an authentication scheme:
- An anonymous request gets the scheme's challenge: a cookie scheme redirects to login and a bearer scheme
  returns 401. The dashboard HTML is never served.
- An authenticated user outside `Dashboard:RequiredRole` gets 403. An authorized user gets 200.
- `Dashboard:AuthorizationPolicy` replaces the inline policy.
- Cloudstrap's authenticated-only Hangfire filter is always first in Hangfire's own list. It still denies
  an anonymous request when a consumer bypasses endpoint authorization with `.AllowAnonymous()`, and a
  consumer's `configure` cannot remove it.
- `Dashboard:ReadOnly` disables actions.
- The dashboard is served at `/hangfire`, at `Dashboard:Path`, and correctly under a `PathBase`.
- Mapping on a host without an authentication scheme throws.
- A storage-only host logs its startup summary at mapping time.

- [ ] Done

**Scope**:
- `src/Cloudstrap.Hangfire/EndpointRouteBuilderExtensions.cs` *(create, public static)*:
  `MapCloudstrapHangfireDashboard(this IEndpointRouteBuilder endpoints, Action<DashboardOptions>? configure
  = null) : IEndpointConventionBuilder`.
  - Guard.
  - **Throw** `InvalidOperationException` when `IOptions<AuthenticationOptions>.Value.SchemeMap.Count == 0`
    (the `WebApplicationExtensions.cs:196` test idiom). The message names the requirement: "register an
    authentication scheme (for example AddCloudstrapOpenIdConnect or AddCloudstrapWebApi's JWT bearer)
    before mapping the dashboard".
  - Read `IOptions<HangfireOptions>`.
  - `DashboardOptionsComposer.Create(dashboardOptions, configure)`.
  - `endpoints.MapHangfireDashboard(path, composed)`.
  - `.RequireAuthorization(policyName)` when `AuthorizationPolicy` is set, else
    `.RequireAuthorization(new AuthorizationPolicyBuilder().RequireAuthenticatedUser()[.RequireRole(role)].Build())`.
  - Record `state.DashboardPath`; when `!state.RunServer`, log the posture summary (mechanic (e)).
  - XML docs cover:
    - the call-site rule: call it from a composite's `ConfigureEndpoints` hook or after
      `UseAuthentication`/`UseAuthorization`;
    - "tighten, never loosen: loosening means Hangfire's own `MapHangfireDashboard`";
    - the HTTPS/antiforgery note (mechanic (i));
    - "no `X-Forwarded-Prefix` handling (proxy concerns: #18)".
- `src/Cloudstrap.Hangfire/DashboardOptionsComposer.cs` *(create, internal static)*: builds
  `DashboardOptions`. It sets `IsReadOnlyFunc = _ => readOnly`, runs `configure` **last**, then normalizes
  `Authorization = [new AuthenticatedDashboardAuthorizationFilter(), ..consumer filters except any
  AuthenticatedDashboardAuthorizationFilter]`, so the floor is always first and cannot be removed.
- `src/Cloudstrap.Hangfire/AuthenticatedDashboardAuthorizationFilter.cs` *(create, internal sealed
  `IDashboardAuthorizationFilter`)*: `context.GetHttpContext().User.Identity?.IsAuthenticated == true`.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Cloudstrap.Hangfire.Tests.csproj` *(modify)*: version-less
  `Microsoft.AspNetCore.TestHost` (existing test-only pin).
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Infrastructure/DashboardTestApp.cs` *(create)*: a
  `WebApplication` on `TestServer` with the fake-storage hatch (`FakeMonitoringApi` returns stubbed
  statistics and empty lists, so dashboard pages render without SQL).
  - Schemes: the framework **cookie** scheme (anonymous challenge → 302 to its login path) or a test
    **bearer-style** handler (header `X-Test-User` / `X-Test-Roles` → principal with `role` claims;
    challenge 401, forbid 403).
  - `UseAuthentication()` → `UseAuthorization()` → map.
  - Optional `UsePathBase`.
  - Executor latitude: if rendering the home page against fakes proves brittle, the 200 assertions target a
    storage-free dashboard resource route under the same endpoint. The authorization semantics are
    identical, because Hangfire applies its filters to every dashboard request.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/{DashboardAuthorizationTests, DashboardOptionsTests}.cs` *(create)*.

**RED**:
- `DashboardAuthorizationTests.cs`:
  - `Anonymous_CookieScheme_IsRedirectedToLogin_DashboardHtmlNeverServed`
  - `Anonymous_BearerScheme_Returns401`
  - `Authenticated_NotInRequiredRole_Returns403`
  - `Authenticated_InRequiredRole_Returns200`
  - `NoRequiredRole_AnyAuthenticatedPrincipal_Returns200` (the trusted-subsystem posture)
  - `AuthorizationPolicySet_ConsumerPolicyReplacesTheInlineOne`: the consumer policy requires claim
    `scope=ops`. A principal in `RequiredRole` without the claim gets 403; with it, 200.
  - `NoAuthenticationSchemeRegistered_MapThrowsNamingTheRequirement` (AC-HF11)
  - `EndpointAuthorizationBypassedWithAllowAnonymous_HangfireFloorStillDeniesTheAnonymousRequest`: Hangfire
    answers 401 and no dashboard HTML is served (defense in depth).
  - `DefaultPath_IsHangfire` · `ConfiguredPath_IsServed_DefaultPathIs404` ·
    `UnderPathBase_IsServedAtPathBasePlusPath` (AC-HF13)
  - `XForwardedPrefixHeader_HasNoEffect` (tripwire: #18's concern stays out)
  - `StorageOnlyHost_Mapping_LogsTheSummaryOnce_WithTheDashboardPath` (mechanic (e))
- `DashboardOptionsTests.cs`:
  - `Composer_CloudstrapFilterIsFirst_EvenWhenConfigureClearsOrReplacesAuthorization`
  - `Composer_ConsumerFilters_AreKeptAfterTheFloor_AllMustPass`
  - `Composer_ReadOnlyTrue_IsReadOnlyFuncReturnsTrue_DefaultFalse` (AC-HF12)
  - `Composer_ConfigureRunsLast_ForOtherOptions`
  - `Composer_NeverSetsIgnoreAntiforgeryToken`
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Hangfire.Tests\bin\Debug\net10.0\Cloudstrap.Hangfire.Tests.exe --filter "DashboardAuthorizationTests|DashboardOptionsTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass. No code path serves the dashboard below "authenticated", which is new behavior.
2. Full-suite check (mechanic (g)): all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6: Probes know whether Hangfire storage answers (AC-HF14; DD-3)

After this step, `AddHealthChecks().AddCloudstrapHangfireHealthCheck()` registers the check additively on
the stock builder:
- name `hangfire`, tag `ready`, both overridable, as is the failure status;
- Healthy with `servers`/`recurring`/`enqueued`/`failed` data when storage answers;
- the registration's failure status (default Unhealthy) when it does not;
- never a connection string, server name or exception message in the result.

This holds with fake storage and on real LocalDB, including an unreachable server.

- [ ] Done

**Scope**:
- `src/Cloudstrap.Hangfire/HealthChecksBuilderExtensions.cs` *(create, public static)*:
  `AddCloudstrapHangfireHealthCheck(this IHealthChecksBuilder builder, string name = "hangfire", HealthStatus?
  failureStatus = null, IEnumerable<string>? tags = null) : IHealthChecksBuilder`. Default tags are
  `[CloudstrapHealthCheckTags.Readiness]`, added via `builder.Add(new HealthCheckRegistration(...))`.
  - XML docs note that it is opt-in, and that storage-only hosts typically pass `HealthStatus.Degraded`.
- `src/Cloudstrap.Hangfire/HangfireStorageHealthCheck.cs` *(create, internal sealed `IHealthCheck`)*: resolves
  `JobStorage`, calls `GetMonitoringApi().GetStatistics()`, and returns `Healthy` with the four data entries.
  - On an exception it returns `new HealthCheckResult(context.Registration.FailureStatus, "Hangfire storage
    is unreachable ({ExceptionTypeName})")`.
  - **The exception object is not attached** (planner decision, so provider messages that may echo server or
    database names never reach a probe writer). The failure is logged at Warning with the exception so
    operators keep the detail in logs. Reviewed at Gate 3.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/HealthCheckTests.cs` *(create)*.

**RED**:
- `HealthCheckTests.cs`:
  - `Defaults_NameHangfire_TagReady_FailureUnhealthy`
  - `Overrides_NameFailureStatusAndTags_AreHonored`
  - `StorageAnswers_ReportsHealthy_WithServersRecurringEnqueuedFailedData` (`FakeMonitoringApi`)
  - `StorageThrows_ReportsTheRegistrationsFailureStatus_DegradedWhenConfigured`
  - `StorageThrows_ResultNeverContainsTheConnectionStringOrTheExceptionMessage`: the fake throws with
    `Server=secret-host;Password=p4ss`, and neither string appears in `Description`, `Data`, or as
    `Exception`.
  - `Registration_IsAdditive_ExistingChecksBeforeAndAfterAreKept`
  - `RealStorage_OnLocalDb_ReportsHealthy` (LocalDB, linked helper)
  - `UnreachableServer_ReportsTheFailureStatus`: `Server=unreachable.invalid;Connect Timeout=1`,
    `PrepareSchema = false`.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Hangfire.Tests\bin\Debug\net10.0\Cloudstrap.Hangfire.Tests.exe --filter "HealthCheckTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass. Storage reachability now reaches `/ready` through the tag contract.
2. Full-suite check (mechanic (g)): all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 7: The package is publishable and its surface is locked (AC-HF20; AC-ASP2; AC-A3; AC-HF18 permanent; finding 13)

After this step:
- Package metadata is complete.
- The README carries the **LGPL-3.0 notice** (plus the `Newtonsoft.Json` disclosure), the topology golden
  rules, the configuration reference, the host-role guide, the `IBackgroundJobClient` guidance,
  troubleshooting, the HTTPS/antiforgery note and the source-library migration note.
- Permanent tripwires guard the closure, the public surface, the persisted wire contract, the dropped
  concepts and the README notice.
- The stale `configure-hangfire` skill is rewritten against the as-built package.

- [ ] Done

**Scope**:
- `src/Cloudstrap.Hangfire/Cloudstrap.Hangfire.csproj` *(modify)*:
  - `<Description>`: Hangfire in one call. SQL Server storage with Hangfire's recommended settings,
    interface-based recurring tasks scheduled at startup, configuration-driven per-job overrides, overlap
    prevention, orphan reconciliation, an always-authenticated endpoint-routed dashboard, a readiness health
    check and additive OpenTelemetry; Hangfire itself is LGPL-3.0.
  - `<PackageTags>$(PackageTags);hangfire;background-jobs;recurring-jobs;scheduler;cron;dashboard;sqlserver</PackageTags>`
  - `<PackageReadmeFile>README.md</PackageReadmeFile>` + the pack item.
- `src/Cloudstrap.Hangfire/README.md` *(create)*, with sections in this order:
  1. **License notice**: the `Hangfire.*` family is **LGPL-3.0** (Hangfire OÜ; free tier only; commercial
     licenses exist); `Hangfire.NetCore` is LGPL-3.0; `Newtonsoft.Json` (MIT) arrives transitively;
     `Cloudstrap.Hangfire` itself is MIT.
  2. Quick start (the Overview's two compositions).
  3. Mental model: the dispatcher, and why the dashboard host needs no worker reference.
  4. **Topology golden rules**, in neutral vocabulary (processing host / storage-only host / proxy host):
     - exactly one processing host (task set) per storage/schema;
     - independent processing hosts sharing a database set their own `Storage:SchemaName`;
     - storage-only hosts set `RunServer = false`;
     - all hosts share storage;
     - tasks live with the processing host.
  5. `IBackgroundRecurringTask` reference, including `JobId` rename sensitivity and "internal classes are
     discovered too".
  6. The configuration reference table (every key, default, override).
  7. The code hatches.
  8. Schema preparation (`PrepareSchema`, Hangfire's `Install.sql` for IaC, managed identity via
     `Authentication=Active Directory Default` as allowed by the Step 1 pin record).
  9. Dashboard: placement, the policy/role/override rules, "tighten, never loosen", **serve over HTTPS on
     hosts with hardened antiforgery**, and "proxying is `Cloudstrap.Hangfire.Proxy` (#18)".
  10. Health check.
  11. Telemetry (source name, span, no exporter).
  12. Logging (what is and is never logged).
  13. Fire-and-forget: inject `IBackgroundJobClient`; the public-method rule; runs on a processing host.
  14. Troubleshooting: "No registered recurring task with id 'X'", disappearing jobs, 401/403, startup
      faults, a missing schema.
  15. Aspire coexistence (`ConnectionStrings:` convention, additive health check, contributed source, zero
      `Aspire.*`).
  16. **Migration notes**: Deliberate Behavior Changes 1–13 verbatim, including "delete the source library's
      persisted recurring jobs from the dashboard once".
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/PackageSurfaceTests.cs` *(create; the #14/#15 shape)*.
- `.claude/skills/configure-hangfire/SKILL.md` *(rewrite in full)*. It must be written against the as-built
  package, keeping the skill's frontmatter shape. It covers:
  - the host-role decision table (`RunServer`, dashboard mapping, health failure status);
  - `AddCloudstrapHangfire` + configurator;
  - `IBackgroundRecurringTask`;
  - `Cloudstrap:Hangfire` keys;
  - `MapCloudstrapHangfireDashboard` placement in the composites' `ConfigureEndpoints`;
  - `AddCloudstrapHangfireHealthCheck`;
  - `IBackgroundJobClient`;
  - the validation-rules and troubleshooting tables;
  - "the proxy host is #18 (not yet shipped)".

  It must contain **no** `AddHangfireForCloudstrap`, `runServer:` parameter, `ICloudstrapBackgroundJobScheduler`,
  `UseHangfire…`, TST/VAL/PRD, `AccessRole`, `HttpClientServiceRegistry`, `DACPAC`, `configure-sts` or
  `database-changes` references.

**RED** *(the guard tests are tripwires against already-correct code and may pass immediately; the honest
failing state is in the artifacts: before GREEN there is no README, the nupkg has no
description/tags/readme, and the README-notice test fails because the file is missing)*:
- `PackageSurfaceTests.cs`:
  - `ReferencedAssemblies_MatchTheApprovedClosure`:
    - allowed prefixes: `System`, `netstandard`, `Microsoft.Extensions.`, `Microsoft.AspNetCore`,
      `Microsoft.Data.SqlClient`, `Hangfire`, `Scrutor`, `OpenTelemetry`, `Cloudstrap.Core`,
      `Cloudstrap.Observability` (executor latitude: trim to the observed set);
    - **forbidden**: `Aspire`, `Nihdi`, `NServiceBus`, `Particular`, `Duende`, `MudBlazor`, `Wolverine`,
      `Hangfire.InMemory`, `Hangfire.Pro`.
  - `PublicSurface_IsExactlyTheApprovedTypes`: exactly `HostApplicationBuilderExtensions`,
    `CloudstrapHangfireConfigurator`, `EndpointRouteBuilderExtensions`, `HealthChecksBuilderExtensions`,
    `IBackgroundRecurringTask`, `RecurringTaskRunner`, `CloudstrapHangfireActivitySources`,
    `HangfireOptions`, `HangfireStorageOptions`, `HangfireServerOptions`, `HangfireDashboardOptions`,
    `HangfireJobOptions`. All are in namespace `Cloudstrap.Hangfire`, every class is sealed or static, and
    the only public interface is `IBackgroundRecurringTask`.
  - `RecurringTaskRunner_WireContract_IsFrozen`:
    - `typeof(RecurringTaskRunner).FullName == "Cloudstrap.Hangfire.RecurringTaskRunner"`;
    - assembly name `Cloudstrap.Hangfire`;
    - exactly one public instance method `RunAsync(string, CancellationToken)` returning `Task`;
    - `[EditorBrowsable(Never)]`;
    - `[DisplayName("{0}")]`.
  - `IBackgroundRecurringTask_ReferencesNoHangfireType` (AC-HF18, permanent)
  - `PublicTypes_ContainNoForbiddenIdentifiers`: `(?i)nihdi|riziv|cfe|bff|wfe|nservicebus|dynatrace`.
  - `Assembly_DeclaresNoDroppedConcepts`: no declared type or member name containing
    `BackgroundJobScheduler`, `ForwardedPrefix`, `AccessRole`, `LocalRequestsOnly`, `TrustedSubsystem`,
    `ConfigurationReader`, `AccessEvaluator`, `ServerName`, `QueuePollInterval`, `WithoutDashboard`,
    `ForNihdi`, `Environment` + `Tier` (the TST/VAL/PRD taxonomy), `InMemory`.
  - `PackageReadme_CarriesTheLgplNotice_AndTheGoldenRules`: reads `src/Cloudstrap.Hangfire/README.md` from
    the repo root and asserts `LGPL-3.0`, `Newtonsoft.Json`, `RunServer = false`, `Storage:SchemaName`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Hangfire.Tests\bin\Debug\net10.0\Cloudstrap.Hangfire.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: the csproj metadata, the README, and the skill rewrite, per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → every test passes, including the seven permanent guards.
2. `dotnet build src/Cloudstrap.sln -c Release`, then expand a `.zip` copy of
   `src/Cloudstrap.Hangfire/bin/Release/Cloudstrap.Hangfire.<version>.nupkg`:
   - contents: `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.Hangfire.dll` **and** `.xml`;
   - nuspec: MIT license expression, description, tags, repository URL;
   - dependencies exactly `Cloudstrap.Core`, `Cloudstrap.Observability`, `Hangfire.Core`,
     `Hangfire.SqlServer`, `Hangfire.AspNetCore`, `Microsoft.Data.SqlClient`, `Scrutor`, and the framework
     reference;
   - no `Aspire.*`, `Hangfire.InMemory`, `Wolverine*` or test/demo pin leakage (AC-HF20, AC-ASP2).
3. **AC-HF20 identifier sweep** (package + tests + skill):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Hangfire, src/Test/UnitTest/Cloudstrap.Hangfire.Tests, .claude/skills/configure-hangfire |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|\bcfe\b|\bbff\b|\bwfe\b|TST/VAL/PRD|AddHangfireForCloudstrap|ICloudstrapBackgroundJobScheduler)'
   ```
   The only matches allowed are the guard tests' self-referential patterns and the README's migration notes
   naming *source* concepts. The executor reads every hit (the plans 2–15 practice).
4. Full-suite check (mechanic (g)): all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE: end of Slice 3, the dashboard, the probe, and the frozen surface *(covers Steps 5–7)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval. Do not start the next step.*

⚠️ **Risk areas at this gate**:
- **Auth (D-1)**, review in detail:
  - `MapCloudstrapHangfireDashboard`'s exact signature and XML docs;
  - the inline policy (`RequireAuthenticatedUser` + `RequireRole`) versus `AuthorizationPolicy`
    replacement;
  - the no-scheme throw;
  - the composer's "floor first, cannot be removed" normalization and the `.AllowAnonymous()` defense-in-depth
    evidence;
  - no `IgnoreAntiforgeryToken` anywhere;
  - the README's HTTPS/antiforgery guidance (mechanic (i)).
- **Health check**: the decision not to attach the exception object (log-only detail).
- **Public API frozen**: `PublicSurface_IsExactlyTheApprovedTypes` and `RecurringTaskRunner_WireContract_IsFrozen`
  pin the surface for good; the expanded Release nupkg (metadata + dependency list) is reviewed.
- **LGPL notice**: review the README license section's wording.
- **Skill rewrite**: review `.claude/skills/configure-hangfire/SKILL.md` against the as-built package.

- [ ] Behavioral verification (Step 5): cookie redirect / bearer 401 / wrong-role 403 / right-role 200; the
  policy override; the no-scheme throw; the `.AllowAnonymous()` bypass still denied by the Hangfire floor;
  read-only; default path, configured path and `PathBase`; the forwarded-prefix header ignored; the
  storage-only summary.
- [ ] Behavioral verification (Step 6): the default and overridden name/tags/failure status, healthy with
  data, failure without leaks, additive registration, and LocalDB healthy/unreachable.
- [ ] Behavioral verification (Step 7): the seven guards green, the expanded nupkg reviewed, the identifier
  sweep clean.
- [ ] Code review: README accuracy against as-built behavior (keys, defaults, golden rules, troubleshooting,
  migration notes 1–13); skill accuracy; no Drop/Routed concept resurrected.
- [ ] User approved: implementation may continue past this gate.

---

## Slice 4: Demonstrated live. The demo Worker is the processing host running a neutral every-minute task, the demo BlazorServer is a storage-only host showing the Worker's job to the signed-in `tester`, and E2E triggers the job from the dashboard and observes the Worker run it (workflow rule 9; AC-HF21; D-5)

---

## Step 8: The demo Worker schedules and runs `DemoOrderCountRecurringTask` (AC-HF21 processing half; AC-HF14 demo registration; mechanic (j)) ⚠️ E2E infrastructure

After this step:
- The demo Worker is a Hangfire processing host on `CloudstrapDemo` and schedules `DemoOrderCountRecurringTask`
  (`* * * * *`, UTC) at startup, with the summary visible on stdout.
- The task logs the `demo.Orders` row count.
- The Worker's `/ready` includes the ready-tagged `hangfire` storage check.
- Every pre-existing Worker-booting E2E fixture stays green now that each Worker instance is also a
  processing host.

- [ ] Done

**Scope**:
- `src/demo/Worker/Cloudstrap.Demo.Worker.csproj` *(modify)*: ProjectReference →
  `..\..\Cloudstrap.Hangfire\Cloudstrap.Hangfire.csproj`.
- `src/demo/Worker/DemoOrderCountRecurringTask.cs` *(create)*: `internal sealed class
  DemoOrderCountRecurringTask(WorkerDbContext db, ILogger<DemoOrderCountRecurringTask> logger) :
  IBackgroundRecurringTask`. It has `CronExpression => "* * * * *"`, and `ExecuteAsync(ct)` logs `Demo order
  count: {OrderCount} row(s) in demo.Orders` from `await db.Orders.CountAsync(ct)`.
  - Teaching comments: discovered by the entry-assembly scan (internal is fine, mechanic (d)); resolved per
    run in its own scope, so the `DbContext` is per run; never logs row contents.
  - If Gate 1 reverted the non-public decision, the class is `public sealed`.
- `src/demo/Worker/Program.cs` *(modify)*: `builder.AddCloudstrapHangfire();` after the messaging block,
  with a teaching comment (the processing host: storage + server + scheduling; one processing host per
  schema; Hangfire's default `HangFire` schema in `CloudstrapDemo`). `.AddCloudstrapHangfireHealthCheck()`
  is appended to the existing `AddHealthChecks()` chain (ready-tagged, Unhealthy).
  `WorkerDbContext.EnsureCreated` already runs before `RunAsync`, so the database exists before the
  scheduling service touches storage.
- `src/demo/Worker/appsettings.json` *(modify)*: a `Cloudstrap:Hangfire` block of `//` comments only. It
  states the defaults in force (`Storage:ConnectionStringName = DefaultConnection`, schema `HangFire`,
  `PrepareSchema` unset → Development) and an example `Jobs:DemoOrderCountRecurringTask` override
  (commented), which is the operator-override teaching point. **No values set.**
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/HangfireTests.cs` *(create)*: `[TestFixture] public sealed class
  HangfireTests : PageTestBase`. It boots a Worker on `--Cloudstrap:Worker:HealthPort=5353` with the
  `CLOUDSTRAP_TEST_SQL` and `CLOUDSTRAP_TEST_BLOB` forwards (the `ClaimCheckTests` pattern: the Worker is
  also a claim-check node) and its own outage-sentinel temp path.
- `src/demo/Worker/README.md` *(modify)*: feature-matrix rows for #16 (call | E2E test names) and harness
  notes:
  - schema `HangFire` in `CloudstrapDemo`;
  - every E2E Worker instance is a processing host with the same task set (idempotent);
  - port 5353 is owned by `HangfireTests`;
  - the every-minute cron;
  - the `Jobs:` override example.
- `src/demo/README.md` *(modify)*: port-map row `5353 | Worker demo, fourth instance | HangfireTests`.

**RED** *(today the Worker has no Hangfire; the summary line is absent)*:
- E2E test file: `HangfireTests.cs`
  - `Hangfire_WorkerStartup_SchedulesTheDemoTaskOnTheProcessingHost_AndLogsTheSummary`: poll the Worker's
    captured output (30 s deadline) for the posture line (`run server True`, `1 recurring task(s)`) and the
    job line (`DemoOrderCountRecurringTask`, `* * * * *`, `UTC`). Assert the output never contains the
    connection string's `Server=` fragment.
  - `Hangfire_WorkerReady_IncludesTheStorageCheck_AndIsHealthy`: `GET :5353/ready` → 200. Together with
    `WorkerHostTests`' outage drill this proves the ready contract still holds with the added check.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "HangfireTests"
  ```

**GREEN**: the Scope items. **Every pre-existing E2E test must stay green unchanged**, in particular
`WorkerHostTests` (probes, heartbeat, outage drill, no probe spans), `MessagingTests` and `ClaimCheckTests`,
all of whose Workers now also run Hangfire. The executor reports any disturbance rather than weakening an
assertion.

**DB changes**: none in the repo. Hangfire installs `HangFire.*` tables in `CloudstrapDemo` at first
Development start (`PrepareSchema` default), alongside the existing schemas.

**VERIFY**:
1. E2E exe → the two new tests pass and **every pre-existing E2E test passes unchanged**.
2. Manual smoke (optional, recorded): `dotnet run --project src/demo/Worker`, then observe the summary and,
   within a minute, `Recurring task DemoOrderCountRecurringTask completed in … ms` plus the row-count line.
3. Full-suite check (mechanic (g)): all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 9: The demo BlazorServer is a storage-only host whose dashboard shows the Worker's job to the `tester` and can trigger it (AC-HF21 dashboard half; AC-HF9 live cross-process; AC-HF10 live; mechanics (i)–(j)) ⚠️ auth (live) + E2E infrastructure

After this step:
- `Cloudstrap.Demo.BlazorServer` registers Hangfire storage only, with no server and no scheduling, and
  maps `MapCloudstrapHangfireDashboard()` in its `ConfigureEndpoints` hook.
- An anonymous `/hangfire` is redirected to the demo IdP.
- The signed-in `tester` sees `DemoOrderCountRecurringTask` on the recurring-jobs page. The app does not
  reference the Worker's assembly.
- Clicking *Trigger now* makes the separate Worker process run the job within seconds.
- The BlazorServer `/ready` stays 200 through a `Degraded`-on-failure storage check.

- [ ] Done

**Scope**:
- `src/demo/BlazorServer/Cloudstrap.Demo.BlazorServer.csproj` *(modify)*: ProjectReference →
  `..\..\Cloudstrap.Hangfire\Cloudstrap.Hangfire.csproj`. It must **not** reference `Cloudstrap.Demo.Worker`
  (the teaching point).
- `src/demo/BlazorServer/Program.cs` *(modify)*:
  - `builder.AddCloudstrapHangfire(hangfire => hangfire.RunServer = false);` (teaching comment: storage
    only; never dequeues, never schedules, never reconciles; renders the Worker's jobs through the shared
    dispatcher type);
  - `builder.Services.AddHealthChecks().AddCloudstrapHangfireHealthCheck(failureStatus: HealthStatus.Degraded);`
  - `ConfigureEndpoints = endpoints => { endpoints.MapCloudstrapAuthenticationEndpoints();
    endpoints.MapCloudstrapHangfireDashboard(); }`;
  - **mechanic (i) only if observed**: `AddCloudstrapBlazorServer(c => c.Antiforgery = a => { if
    (builder.Environment.IsDevelopment()) a.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; })`,
    with a teaching comment (demo over plain HTTP; production serves HTTPS and keeps `Always`).
- `src/demo/BlazorServer/appsettings.json` *(modify)*:
  - `ConnectionStrings:DefaultConnection` → the same `CloudstrapDemo` LocalDB string as the Worker, with a
    `//` comment (shared with the Worker; `CLOUDSTRAP_TEST_SQL` overrides in E2E);
  - `Cloudstrap:Hangfire:Dashboard:RequiredRole = "tester"` with a `//` comment (the seeded demo user's
    role; the OIDC package maps roles from `role`).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/BlazorServerTests.cs` *(modify)*: forward `CLOUDSTRAP_TEST_SQL` as
  `--ConnectionStrings:DefaultConnection=` when set (today it passes `null` arguments).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/HangfireTests.cs` *(modify)*: after the Worker, boot the
  BlazorServer on `http://127.0.0.1:5340` (project path + SQL forward), wait for `/`, and dispose both in
  `[OneTimeTearDown]` (BlazorServer first).
- `src/demo/BlazorServer/README.md` *(modify)*:
  - feature-matrix rows for #16 (storage-only host + dashboard | the E2E names);
  - harness notes:
    - now needs LocalDB for the dashboard and a healthy storage check (the app still boots and `/ready`
      stays 200 via `Degraded`);
    - `RequiredRole: tester`;
    - no reference to the Worker;
    - the antiforgery note if mechanic (i) applied;
    - `HangfireTests` also boots this host on 5340.
- `src/demo/README.md` *(modify)*: the port-map row for 5340 lists `BlazorServerTests` **and**
  `HangfireTests`, plus a harness bullet naming the two-host Hangfire topology in `CloudstrapDemo`.
- `src/demo/Worker/README.md` *(modify)*: a cross-reference row ("the dashboard lives in the BlazorServer
  demo").

**RED** *(today `/hangfire` on 5340 falls through to the Blazor router and nothing is triggerable)*:
- E2E test file: `HangfireTests.cs`
  - `Hangfire_AnonymousDashboardRequest_IsChallengedToTheIdentityProvider_NeverServesTheDashboard`: a
    `HttpClient` with `AllowAutoRedirect = false`. `GET :5340/hangfire` → 302 with `Location` starting
    `http://127.0.0.1:5310/`; the body does not contain `Hangfire Dashboard` (AC-HF10 live).
  - `Hangfire_SignedInTester_SeesTheWorkersRecurringJob_OnAHostThatDoesNotReferenceTheWorker`: Playwright
    goes to `:5340/hangfire/recurring`, waits for the IdP URL, calls `BrowserSignIn.FillLoginFormAsync`, and
    returns. The row for `DemoOrderCountRecurringTask` is visible. A static assertion checks that the
    BlazorServer build output directory contains no `Cloudstrap.Demo.Worker.dll` (AC-HF9 cross-process
    proof).
  - `Hangfire_TesterTriggersTheJobFromTheDashboard_TheWorkerRunsItWithinSeconds`:
    - count `Recurring task DemoOrderCountRecurringTask completed` occurrences in the Worker's captured
      output;
    - on the signed-in recurring page, tick the row's checkbox (value = the job id) and click the *Trigger
      now* button;
    - poll the Worker's captured output (30 s deadline) until the count increases and a `Demo order count:`
      line is present;
    - assert `ConsoleErrors` is empty.
    - An overlap skip against a coincident cron run still yields a new completion line from the cron run,
      so the assertion is robust.
    - D-5 fallback (mechanic (j)) is used only if the DOM proves brittle, and is reported at the gate.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "HangfireTests|BlazorServerTests"
  ```

**GREEN**: the Scope items. **Every pre-existing E2E test must stay green unchanged**. `BlazorServerTests`
matters most: probes, headers, the WhoAmI span and the sign-in flow, now on a host with Hangfire storage and
the `Degraded` check. Mechanic (i) is applied only on observed need and reported.

**DB changes**: none. Both hosts share the `HangFire` schema that the Worker (or whichever host touches
storage first in Development) prepares.

**VERIFY**:
1. E2E exe → the three new tests plus Step 8's two pass, and **every pre-existing E2E test passes
   unchanged**. Do this after a build; LocalDB is required; run the one-time `playwright.ps1 install
   chromium` if needed.
2. Manual smoke (optional, recorded): run the IdP, Api, Worker and BlazorServer per the READMEs; open
   `http://127.0.0.1:5340/hangfire`; sign in as the demo user; see the job; trigger it; watch the Worker log.
3. Full-suite check (mechanic (g)): all green; `dotnet format` exit 0; the demo projects still pack nothing.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE: final, deliverable #16 complete *(covers Steps 8–9; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the
user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

⚠️ **Risk areas at this gate**:
- **Auth, live**: the anonymous → IdP redirect and the `tester`-role dashboard through the real OIDC cookie
  principal.
- **Mechanic (i)**: whether the Development-only antiforgery relaxation was needed, and its exact scope.
  **Confirm.**
- **Demo topology (mechanic (j))**: every E2E Worker is now a processing host on `CloudstrapDemo`; the
  BlazorServer demo now needs LocalDB for its dashboard; the new port 5353. **Confirm.**
- **Whether the D-5 fallback was used.**

- [ ] Behavioral verification (Step 8): `Hangfire_WorkerStartup_SchedulesTheDemoTaskOnTheProcessingHost_AndLogsTheSummary`
  and `Hangfire_WorkerReady_IncludesTheStorageCheck_AndIsHealthy` pass.
- [ ] Behavioral verification (Step 9):
  `Hangfire_AnonymousDashboardRequest_IsChallengedToTheIdentityProvider_NeverServesTheDashboard`,
  `Hangfire_SignedInTester_SeesTheWorkersRecurringJob_OnAHostThatDoesNotReferenceTheWorker` and
  `Hangfire_TesterTriggersTheJobFromTheDashboard_TheWorkerRunsItWithinSeconds` pass.
- [ ] Regression: **all pre-existing E2E tests pass unchanged**, and the full-suite check (build + every
  unit exe + E2E exe + `dotnet format --verify-no-changes`) is green end to end.
- [ ] Spec acceptance sign-off:
  - Walk **AC-ASP2, AC-A3, AC-HF1…AC-HF21** against the step evidence using the Overview's AC coverage map;
    all must be met.
  - Confirm nothing from the Drop / Routed / Out-of-Scope lists was resurrected:
    - no job-client facade;
    - no `Use…` scheduling call;
    - no localhost-only path;
    - no forwarded-prefix handling;
    - no bespoke auth evaluator/reader;
    - no token-less overloads;
    - no `Hangfire.InMemory`;
    - no `RunServer`/`ServerName`/`QueuePollInterval` config keys;
    - no Proxy `InternalsVisibleTo`.
  - Confirm every De-NIHDI row is closed: `Cloudstrap:Hangfire` section, neutral host vocabulary, no
    TST/VAL/PRD, `Cloudstrap.Hangfire` span source, neutral fixtures and demo task.
- [ ] Docs review:
  - `src/Cloudstrap.Hangfire/README.md` matches as-built behavior, including the LGPL-3.0 notice.
  - `.claude/skills/configure-hangfire/SKILL.md` matches as-built behavior.
  - The Worker/BlazorServer/demo READMEs cite the real E2E test names, port 5353, the LocalDB prerequisite
    for the dashboard, and `RequiredRole: tester`.
  - **Recorded follow-ups (not in this plan)**:
    - #8 hoists `SqlServerTestDatabase` + `CapturingLoggerProvider` into `Cloudstrap.Testing`, parameterizes
      the catalog, and repoints the Hangfire link;
    - #18 dashboard proxy through the public seam (`Dashboard:Path`, `HangfireDashboardOptions.RequiredRole`);
    - D-2(b) owner-scoped reconciliation as the post-v1 improvement if a consumer reports the residual
      hazard;
    - an optional `Cloudstrap.Hangfire.PostgreSql` leaf post-v1.
- [ ] User approved: deliverable #16 is done; the project-manager flips the ROADMAP row to ✅.
