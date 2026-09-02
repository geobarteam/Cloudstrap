# Plan: 14-Messaging — A consumer calls `AddCloudstrapMessaging(...).UseSqlServer(...)` and gets a durable Wolverine messaging node — local/ASB/SQL transports, suffix conventions and workload routing, retries + dead-lettering, an EF Core transactional inbox/outbox that commits atomically with the handler's `DbContext`, correlation propagation and additive OTel — and the demo Api produces a command through the outbox that the demo Worker consumes over SQL Server, proven end to end by E2E

## Overview

Deliverable #14 of the extraction roadmap: the new **`Cloudstrap.Messaging`** package — the
extraction's biggest rewrite, a **rebuild against an observed contract** (NServiceBus source →
Wolverine target; almost no source line survives verbatim). **Binding spec: `_specs/14-Messaging.md`**
(APPROVED 2026-08-31, zero Open Questions, Decision Log D-1…D-5 final). Its Port Decision Table
(6 Port · 15 Redesign · 8 Replace · 27 Drop · 1 Routed), Public API Sketch, Behaviors & Conventions,
Dependencies table, Deliberate Behavior Changes 1–9 and Out-of-Scope list are authoritative and are
not re-litigated here. Nothing the spec marked **Drop** appears in this plan: no `EnableNServiceBus`
gate, no `IHostBuilder` overloads, no XML fallback serializer, no audit queue / ServicePlatform
monitoring, no `SendOnly` / `TransactionMode` / `PersistenceType` settings, no credential-bearing
configuration of any kind, no migration topology / `TypeLoader`, no `Bridge\` / `Encryption\`
material, no `TransactionalCommandExecutor` mediator or `RequiresTransactionalSessionAttribute`, no
`ICloudstrapBus` facade (finding 8), no message-contract base classes or marker interfaces, no
sagas, no blob claim-check (**routed to #15 — nothing here may pre-build it**), no PostgreSQL
durability (only the seam shape), no Dashboard tooling (#19/#20).

Reference patterns, all read before planning:

- **Plan-shape precedent: `_plans/13-BlazorWasmHelpers.md`** (and `_plans/12-BlazorServerHelpers.md`)
  — brand-new-package RED mechanics (the honest first failure is the test project failing to compile
  against missing types), `PackageSurfaceTests` permanent guards, packaging-step shape, demo + E2E
  demonstration slice, final-gate AC walk, gates at slice boundaries only, "spec-drift
  interpretation — confirm at gate" handling.
- **Shipped surfaces this package consumes (read on disk)**:
  `src/Cloudstrap.Core/ApplicationOptions.cs` (`WorkloadName` computed
  `{system}-{subsystem}-{type}`, `SystemName`), `src/Cloudstrap.Core/CorrelationOptions.cs` +
  **`src/Cloudstrap.Core/CorrelationMessageOptions.cs`** (⚠️ mechanic (i): Core **already ships**
  the `Cloudstrap:Correlation:Message` options type with `RequireForAllMessageHandlers` +
  `ExcludeMessageHandlers` — currently consumed by nothing; this plan consumes it instead of
  duplicating it), `src/Cloudstrap.Observability/Correlation/` (`ICorrelationContextAccessor`,
  `CorrelationRequiredAttribute`, `AllowNoCorrelationAttribute`, `AddCloudstrapCorrelation`),
  `src/Cloudstrap.Core/CloudstrapOptionsValidator.cs` + the `[OptionsValidator]` pattern
  (`ApplicationOptionsValidator`, `WorkerOptionsValidator`,
  `CloudstrapClientCredentialsOptionsValidator` — every failure names the offending key, never a
  value), `src/Cloudstrap.Worker/Cloudstrap.Worker.csproj` (package-csproj shape).
- **Demonstration vehicles (read on disk)**: `src/demo/Api/` (`Program.cs` two-call JWT host,
  `Controllers/DownstreamController.cs` controller shape, `appsettings.json` config shape),
  `src/demo/Worker/` (`Program.cs` generic-host bootstrap, `appsettings.json`, README feature
  matrix), `src/demo/Shared/Contracts/` (dependency-free DTO assembly — the suffix-convention
  story's proof), `src/demo/Shared/IdentityProvider/TestIdentityProviderSeed.cs` (seeded clients).
- **E2E harness (read on disk)**: `E2eFixture` (boots IdP 5310 → Api 5330 → Bff 5300; ⚠️ the Api
  boots for **every** E2E run — its new SQL messaging registration makes LocalDB an E2E-suite-wide
  prerequisite, the accepted D-3 posture, flagged at the final gate), `WorkerHostTests` (the
  self-booted-Worker fixture pattern + captured-stdout assertions the messaging E2E test reuses),
  `Infrastructure/SutProcess.cs` (`dotnet run --no-build`, `ASPNETCORE_ENVIRONMENT=Development` —
  so the demo's `AutoProvision` Development default is live in E2E).
- **Source contract**: adjudicated file-by-file in the spec's Port Decision Table (all 24 + 13
  source files were opened by the analyst); this plan builds the **targets** of that table only and
  does not re-open the source repo.

This is a library deliverable with **no database project** — every "DB changes" section below is
"none" (SQL Server artifacts — durability schemas, queue tables, the demo's `Orders` table — are
provisioned at runtime by Wolverine AutoProvision / EF `EnsureCreated`, per AC-MSG12/D-3).

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-M1 (real ASB, manual procedure only) | 8 (README procedure) — never automated |
| AC-M2 (outbox atomicity: throw → neither row nor message) | 6 (LocalDB) |
| AC-M3 (default suite: local transport, no network) | 1–4, 7 (local-transport tests); 5–6 are the D-3 LocalDB exception |
| AC-A3 / AC-ASP2 (zero `Nihdi.AspNetCore` / zero `Aspire.*`) | 8 (permanent guards) |
| AC-MSG1 (zero config → working local node) | 1 |
| AC-MSG2 (invalid config → startup fails naming the exact key, values never echoed) | 1 (unknown transport) · 4 (ASB keys) · 5 (`UseSqlServer` connection string) |
| AC-MSG3 (`DefaultAzureCredential`, no credential modeling) | 4 |
| AC-MSG4 (suffix conventions + D-1 routing, overridable, startup summary) | 2 |
| AC-MSG5 (immediate → delayed → dead-letter retry ladder, exactly one side effect) | 3 (local ladder) · 5 (dead-letter landing) |
| AC-MSG6 (poison message → durable store dead-letter table, type+id logged, never payload) | 5 · 9 (posture live in demo) |
| AC-MSG7 (transactional handler: entity + outbox in one transaction) | 6 |
| AC-MSG8 (HTTP-path outbox: no loss across a crash) | 6 (recovery test) · 9 (`IDbContextOutbox` live) |
| AC-MSG9 (correlation id end to end, configured header, `traceparent` independent) | 7 (unit) · 9 (live) |
| AC-MSG10 (enforcement: block + typed log; `AllowNoCorrelation` exempts; off → fresh scope) | 7 |
| AC-MSG11 (OTel additive: spans/metrics in the host's pipeline, no exporter, no pipeline → still boots) | 3 |
| AC-MSG12 (AutoProvision: Development default, explicit wins, one startup log line) | 3 |
| AC-MSG13 (workload-derived durability schema, sanitized, override) | 5 |
| AC-MSG14 (second `AddCloudstrapMessaging` fails fast, contractual) | 1 |
| AC-MSG15 (build/tests/format, XML docs, metadata, identifier + closure sweep, pinned OSI deps) | 8 |
| AC-MSG16 (Api → Worker over SQL Server on LocalDB, correlation observed, all pre-existing E2E green) | 9 |

### Dependency closure — ⚠️ new dependency family (dependency-update risk area, reviewed at Gate 1)

New CPM entries in `src/Directory.Packages.props`, each with the repo's license/justification
comment (CLAUDE.md rule 4):

- **Shipped closure (`Cloudstrap.Messaging` PackageReferences, version-less; all MIT, verified in
  the spec 2026-08-31, released 2026-08-30)**: `WolverineFx` 6.31.0 · `WolverineFx.AzureServiceBus`
  6.31.0 (disclosed transitive footprint: `Azure.Messaging.ServiceBus`, `Azure.Identity`,
  `WolverineFx.Newtonsoft` → `Newtonsoft.Json` — all MIT) · `WolverineFx.SqlServer` 6.31.0 (brings
  `Weasel.SqlServer`, JasperFx MIT) · `WolverineFx.EntityFrameworkCore` 6.31.0 (brings
  `Microsoft.EntityFrameworkCore` ≥ 10.0.4, MIT — **in-box per D-4**). Comment notes Wolverine's
  JasperFx runtime code generation (MIT) as new to the suite.
- **Family-floor pins (transitive pinning)**: `Microsoft.EntityFrameworkCore` 10.0.10 (raise
  Wolverine's ≥ 10.0.4 floor to the repo's 10.0.x family — the `System.Security.Cryptography.Xml`
  precedent). *Executor latitude: add further transitive floor pins only if restore/advisory checks
  demand them, each with a comment.*
- **Test/demo only — never referenced by a shipped package**: `Microsoft.EntityFrameworkCore.SqlServer`
  10.0.10 (MIT; the AC-M2-class tests' and demo hosts' EF provider) and, if the tests query
  durability tables directly, a `Microsoft.Data.SqlClient` pin at the version Weasel resolves.
  **No Testcontainers** — D-3: LocalDB by default, `CLOUDSTRAP_TEST_SQL` env-var override;
  `Testcontainers.MsSql` stays a noted follow-up only.

Project references: `Cloudstrap.Core` + `Cloudstrap.Observability` (naming, options/validator
pattern, correlation surface, OTel API for additive registration). Zero `NServiceBus.*`, zero
`Particular.*`, zero `Aspire.*`, zero `Nihdi.*` anywhere in the closure (AC-MSG15 — made permanent
by Step 8's guards).

### ⚠️ Risk areas (reviewed at the gates named)

- **New dependency family** — four `WolverineFx.*` packages + their JasperFx/Azure/EF/Newtonsoft
  closure and the runtime-codegen posture: **Gate 1** (CPM diff, licenses, expanded nupkg
  dependency list at Gate 3).
- **All-new public API surface** — the spec sketch's types, signed off verbatim at **Gate 1**
  (core node) and frozen by Step 8's guards at **Gate 3**.
- **`UseSqlServer` provider seam — one-way door**: `CloudstrapMessagingBuilder` is `public sealed`
  precisely so `UsePostgreSql(...)` can arrive later as an extension method on it, additively.
  Reviewed at **Gate 2**.
- **D-1 ASB topology conventions — one-way door**: `{WorkloadName}` queues, topic-per-event,
  `{WorkloadName}` subscriptions, `Destinations` map — these name **cloud resources** that outlive
  code. Reviewed at **Gate 1**.
- **Shared-contract touches**: mechanic (i)'s reuse of Core's shipped `CorrelationMessageOptions`
  (spec-drift interpretation) and D-5's **doc-only** XML amendment to #2's shipped
  `CorrelationRequired`/`AllowNoCorrelation` attributes — both proposed at **Gate 3**.
- **Demo/E2E SQL dependency** — the Api demo host (booted by `E2eFixture` for every E2E run) gains
  a hard LocalDB startup dependency, and the demo IdP seed gains a machine client: **final gate**
  (the accepted D-3 posture, stated explicitly).

### Planner mechanics decided here (no spec conflict unless flagged; each reviewed at the named gate)

**(a) New-package RED mechanics.** For each first step of a new area, the honest first failure is
the test project failing to compile against missing types (the #11/#12/#13 precedent), followed by
real red runs once the types exist.

**(b) The options pipeline (the #1/#7/#9 pattern).**
`services.AddOptions<CloudstrapMessagingOptions>().Bind(configuration.GetSection(CloudstrapMessagingOptions.SectionName))`
+ `.ValidateOnStart()`, validated by an **internal** `CloudstrapMessagingOptionsValidator` — a
source-generated `[OptionsValidator]` partial for data-annotation rules plus hand-written
conditional rules taking `IConfiguration` in its constructor (the `OpenTelemetryOptionsValidator`
precedent) for: `Transport = AzureServiceBus` requires `FullyQualifiedNamespace` **or** a
`ConnectionStrings:{AzureServiceBus:ConnectionStringName}` entry that resolves;
`UseSqlServer()`/`SqlServer` transport require their `ConnectionStringName` to resolve. Every
failure names the exact key (e.g. `'Cloudstrap:Messaging:AzureServiceBus:FullyQualifiedNamespace'`)
and **never echoes a value** (AC-MSG2). An unknown `Transport` string fails enum binding at startup
naming `'Cloudstrap:Messaging:Transport'`.

**(c) One node per process, fail fast (AC-MSG14).** `AddCloudstrapMessaging` registers an internal
`MessagingRegistrationState` marker (the #13 `BlazorWasmRegistrationState` precedent); a second call
finds it and throws `InvalidOperationException` naming `AddCloudstrapMessaging` **at the call site**
— earlier than startup, same contract.

**(d) The deferred-configuration seam.** `AddCloudstrapMessaging` calls Wolverine's
`IHostApplicationBuilder.UseWolverine(...)` exactly once with the package's base configuration;
everything the returned `CloudstrapMessagingBuilder` adds later (`UseSqlServer`,
`AddCloudstrapTransactionalMessaging<TDbContext>`) is applied through Wolverine's documented
modular-configuration seam (**`IWolverineExtension`** services applied to `WolverineOptions` at
bootstrap time), so builder calls compose regardless of order and the consumer's
`configurator.Wolverine` delegate still runs **last** with final say. ⚠️ *Wolverine-API caveat
(applies to every candidate engine API named in this plan)*: the behavioral contract is fixed by
the spec; exact Wolverine 6.31.0 API names (`UseWolverine`, `IWolverineExtension`,
`PersistMessagesWithSqlServer`, `UseSqlServerPersistenceAndTransport`,
`AddDbContextWithWolverineIntegration<T>`, `IDbContextOutbox<T>`, `Policies.AutoApplyTransactions`,
`OnException<...>().RetryTimes(...).Then.ScheduleRetry(...)`, `CustomizeOutgoingMessagesOfType`,
listener/publish endpoint APIs, AutoProvision flags) are **verified against the Wolverine 6.31.0
documentation/source at implementation time**; the executor reports any behavioral gap at the
covering gate instead of bending an AC.

**(e) Conventions + configurator order.** An internal conventions type applies, in order:
suffix classification (`*Command`/`*Event`/`*Message`) → endpoint identity (`EndpointName` ??
`ApplicationOptions.WorkloadName`) → transport-specific D-1 routing (ASB: commands to the
destination workload's queue via the `Destinations` map, events to a topic per event type with a
`{WorkloadName}` subscription per consumer; SQL transport: commands **and** events route to queues
via the `Destinations` map — topics are ASB-only, documented; Local: everything in-process) →
`Retries` failure policy → dead-letter posture (D-2) → then `configurator.Conventions` (replace/
extend classification + routing) → then `configurator.Wolverine` **last**. One startup log line
states the routing, durability, dead-letter and AutoProvision posture in force (AC-MSG4 +
AC-MSG12's "one startup log line" — emitted by an internal `IHostedService` summary logger).

**(f) OTel additive registration (AC-MSG11, AC-ASP1 posture).** The package calls the OpenTelemetry
API's `services.ConfigureOpenTelemetryTracerProvider(b => b.AddSource("Wolverine"))` /
`ConfigureOpenTelemetryMeterProvider(...)` (available via the `Cloudstrap.Observability` project
reference's OTel packages) — deferred configuration that enriches **whatever** pipeline the host
builds (Cloudstrap owner mode, contribute mode, Aspire ServiceDefaults) and is inert when no
pipeline exists. The package registers **no** exporter and **no** tracer/meter provider — Step 8's
guards make that permanent.

**(g) SQL test harness (D-3).** An internal test helper `SqlServerTestDatabase` in the test
project: connection string = `CLOUDSTRAP_TEST_SQL` env var when set, else
`Server=(localdb)\MSSQLLocalDB;Database=CloudstrapMessagingTests;Integrated Security=true;` —
creating the database on first use, isolating fixtures by unique schema names, cleaning up in
teardown. The SQL-backed tests (Steps 5–6) are ordinary tests in the same
`Cloudstrap.Messaging.Tests` project (the spec's single-project decision) and **run by default** —
D-3 explicitly rejected skip-when-absent; a fresh clone with VS or a `windows-latest` runner has
LocalDB. Short Wolverine durability polling intervals in test hosts keep recovery tests fast.

**(h) Handler discovery in tests.** The test exe is the entry assembly, so Wolverine's default
discovery finds the fixtures' handlers; where a test needs explicit control it uses
`configurator.Wolverine` (`opts.Discovery` …) — which doubles as living proof of the escape hatch.

**(i) ⚠️ Correlation options — spec-drift interpretation (Gate 3).** The spec sketches a
Messaging-owned public `MessageCorrelationOptions` bound to `Cloudstrap:Correlation:Message`
(D-5). **Discovered while planning**: `Cloudstrap.Core` *already ships*
`CorrelationMessageOptions` — bound at exactly that section path via `CorrelationOptions.Message`,
with exactly the two sketched members (`RequireForAllMessageHandlers`,
`ExcludeMessageHandlers`) — and nothing consumes it yet (it is the pre-provisioned hook). Shipping
the sketch's duplicate type would put **two public types on one section**. This plan therefore
ships **no new correlation options type**: the Messaging package binds Core's shipped
`CorrelationOptions` (`AddOptions<CorrelationOptions>().Bind(...)` — idempotent alongside any
host binding of the same section) and consumes `.HeaderName` + `.Message`. D-5's intent (one
attribute vocabulary, sibling section, zero shipped-code changes) is satisfied *more* literally —
zero new types, zero shipped-code changes. **Confirm at Gate 3**; if the user prefers the sketch's
letter, the executor adds the duplicate type in a follow-up step at that gate.

**(j) Full-suite check** (standing convention: `runTests` is not on the agent PATH — VERIFY invokes
each exe directly): `dotnet build src/Cloudstrap.sln`, then the **14** unit exes under
`src/Test/UnitTest/<Name>.Tests/bin/Debug/net10.0/<Name>.Tests.exe` (Core, Observability,
Observability.AzureMonitor, Extensions, WebApi, Mvc, Worker, TestIdentityProvider,
Authentication.ClientCredentials, Authentication.OpenIdConnect, BlazorCommon, BlazorServer,
BlazorWasm, **Messaging** — new in Step 1), then the E2E exe
`src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe`, then
`dotnet format src/Cloudstrap.sln --verify-no-changes`.

**(k) Demo topology (D-3 made concrete; final gate).** One shared LocalDB database
`CloudstrapDemo` (`ConnectionStrings:DefaultConnection` in both hosts; the E2E fixture forwards
`CLOUDSTRAP_TEST_SQL` as a `--ConnectionStrings:DefaultConnection=` override when set). Transport:
`SqlServer` in both hosts with an **explicitly configured shared** transport schema
(`Cloudstrap:Messaging:SqlTransport:SchemaName = "demo_transport"` — sender and listener must share
queue tables; the package itself keeps `SchemaName` null → Wolverine's default, no invented
opinion). Durability schemas stay workload-derived and distinct (`demo_application_api` /
`demo_application_worker` — AC-MSG13 visible in one database). The command contract
(`PlaceOrderCommand`) lives in `Cloudstrap.Demo.Contracts` with **zero package references** — the
suffix-conventions story live. Each host owns its own small `DbContext` over the shared
`demo.Orders` table (producer and consumer own their persistence models; no EF reference ever
touches Contracts). The demo IdP seed gains one machine client (`demo-machine`, audience
`demo-api`) so the E2E test can call the hardened Api.

**Target consumer composition** (the spec made concrete — also the demo `Program.cs` files, Step 9,
and the package README, Step 8):

```csharp
// Producer (HTTP host, e.g. the demo Api)
builder.AddCloudstrapMessaging()                                  // Cloudstrap:Messaging section
    .UseSqlServer()                                               // durable inbox/outbox on ConnectionStrings:DefaultConnection
    .AddCloudstrapTransactionalMessaging<DemoDbContext>();        // EF + outbox atomicity, IDbContextOutbox<T>

// Consumer (headless node, e.g. the demo Worker) — handlers are plain Wolverine handlers,
// IMessageBus / IDbContextOutbox are injected directly (finding 8: no facade).
builder.AddCloudstrapMessaging()
    .UseSqlServer()
    .AddCloudstrapTransactionalMessaging<WorkerDbContext>();
```

---

## Slice 1 — The node from configuration: one call boots a working local node; config flips it to Azure Service Bus; conventions, retries, provisioning and telemetry behave like a good citizen ⚠️ NEW-DEPENDENCY / PUBLIC-API / D-1 TOPOLOGY RISK AREA

---

## Step 1 — One call, zero config: `AddCloudstrapMessaging()` turns any host into a working in-process messaging node — a message published via `IMessageBus` reaches its handler with no network, no SQL, no Azure; the endpoint identity is the workload name; invalid transport config fails startup naming the exact key; a second call fails fast (AC-MSG1; AC-MSG14; AC-MSG2's unknown-transport clause; mechanics (a)–(d))

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Directory.Packages.props` *(modify)* — the ⚠️ dependency-closure section's new entries:
  `WolverineFx`, `WolverineFx.AzureServiceBus`, `WolverineFx.SqlServer`,
  `WolverineFx.EntityFrameworkCore` (all 6.31.0, MIT, license/justification comments incl. the
  Newtonsoft-via-ASB disclosure and the JasperFx codegen note) + the `Microsoft.EntityFrameworkCore`
  10.0.10 family-floor pin.
- `src/Cloudstrap.Messaging/Cloudstrap.Messaging.csproj` *(create)* — `Microsoft.NET.Sdk`,
  `net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`; version-less
  `PackageReference`s: the four `WolverineFx.*`; `ProjectReference`s: `Cloudstrap.Core`,
  `Cloudstrap.Observability`; `<InternalsVisibleTo Include="Cloudstrap.Messaging.Tests" />`.
  Description/tags/README metadata land in Step 8 (packable from day one — the #11/#12/#13
  precedent).
- `src/Cloudstrap.Messaging/MessagingTransport.cs` *(create)* — public enum
  `Local | AzureServiceBus | SqlServer` (spec sketch verbatim; default `Local`).
- `src/Cloudstrap.Messaging/CloudstrapMessagingOptions.cs` *(create)* — sealed; the sketch
  verbatim: `SectionName = "Cloudstrap:Messaging"`, `Transport = MessagingTransport.Local`,
  `EndpointName : string?`, `AutoProvision : bool?`, `AzureServiceBus`, `SqlTransport`,
  `Durability`, `Retries`, `DeadLetter`, `Destinations : IDictionary<string, string>`. Sub-option
  files *(create)*: `AzureServiceBusOptions.cs` (`FullyQualifiedNamespace : string?`,
  `ConnectionStringName : string?`), `SqlTransportOptions.cs`
  (`ConnectionStringName = "DefaultConnection"`, `SchemaName : string?`), `DurabilityOptions.cs`
  (`ConnectionStringName = "DefaultConnection"`, `SchemaName : string?`), `RetryOptions.cs`
  (`NumberOfImmediate = 5`, `NumberOfDelayed = 5`), `DeadLetterOptions.cs` (`QueueName : string?`).
  XML docs on every member carry the defaults, the no-secrets posture (connection strings resolve
  by **name** through `ConnectionStrings:`) and the `Destinations` binder-append caveat (#1's
  dictionary note).
- `src/Cloudstrap.Messaging/CloudstrapMessagingOptionsValidator.cs` *(create)* — internal,
  mechanic (b) (this step: the data-annotation half + unknown-transport binding failure; the
  transport-conditional rules grow in Steps 4–5).
- `src/Cloudstrap.Messaging/HostApplicationBuilderExtensions.cs` *(create)* — public static;
  `AddCloudstrapMessaging(this IHostApplicationBuilder builder, Action<CloudstrapMessagingConfigurator>? configure = null) : CloudstrapMessagingBuilder`
  — guard clauses; mechanic (c) duplicate-call fail-fast; options pipeline (b); boots Wolverine via
  mechanic (d): local transport default, endpoint identity `EndpointName ?? WorkloadName` (from
  Core's `ApplicationOptions`), consumer configurator delegates captured for later phases.
- `src/Cloudstrap.Messaging/CloudstrapMessagingBuilder.cs` *(create)* — `public sealed`, the ⚠️
  one-way-door seam type; this step: the type + internal state plumbing only (its two methods land
  in Steps 5–6); XML docs already state the PostgreSQL growth path (extension methods on this
  type).
- `src/Cloudstrap.Messaging/CloudstrapMessagingConfigurator.cs` *(create)* — sealed;
  `Conventions : Action<...>?` (typed against the internal conventions surface — executor names the
  delegate parameter type when the conventions type lands in Step 2; this step ships the property
  shape) and `Wolverine : Action<WolverineOptions>?` (runs last, final say — wired now).
- `src/Cloudstrap.Messaging/MessagingRegistrationState.cs` *(create)* — internal marker,
  mechanic (c).
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Cloudstrap.Messaging.Tests.csproj` *(create)* —
  `net10.0`, ProjectReference to the package, version-less already-pinned
  `Microsoft.Extensions.Hosting` / `Microsoft.Extensions.Configuration`; NUnit/MTP wiring inherited
  from `src/Test/Directory.Build.props`.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/LocalNodeTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/RegistrationTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under
  `Test\UnitTest`.

**RED** *(mechanic (a): first failure = the test project does not compile; then real red runs)*:
- Unit test file: `LocalNodeTests.cs` *(builds a real `HostApplicationBuilder` with in-memory
  configuration carrying a minimal valid `Cloudstrap:Application` section; test-fixture message
  types `PingCommand`/`PongEvent` + handlers recording invocations — mechanic (h))*
  - `AddCloudstrapMessaging_NoMessagingSection_HostStartsAndLocalMessageReachesItsHandler` — start
    the host, `IMessageBus.PublishAsync(new PingCommand(...))`, handler observed; no network/SQL/
    Azure touched (AC-MSG1 — the whole point of the default).
  - `AddCloudstrapMessaging_InvokeAsync_RunsTheHandlerInProcess` — Wolverine-as-mediator sanity
    (the finding-4 replacement is real).
  - `AddCloudstrapMessaging_DefaultEndpointIdentity_IsTheWorkloadName` — the node's
    endpoint/service identity equals `ApplicationOptions.WorkloadName`; an explicit
    `Cloudstrap:Messaging:EndpointName` wins (assert via `WolverineOptions` resolved from the
    container).
- Unit test file: `RegistrationTests.cs`
  - `AddCloudstrapMessaging_CalledTwice_ThrowsNamingTheDuplicateCall` (AC-MSG14; the exception
    message names `AddCloudstrapMessaging`).
  - `AddCloudstrapMessaging_UnknownTransportValue_StartupFailsNamingTheTransportKey` — config
    `Cloudstrap:Messaging:Transport = "RabbitMQ"` → host start fails; the failure names
    `'Cloudstrap:Messaging:Transport'` and does not echo other configured values (AC-MSG2).
  - `AddCloudstrapMessaging_ReturnsTheBuilder_AndConfiguratorWolverineDelegateRuns` — the returned
    type is `CloudstrapMessagingBuilder`; a `configurator.Wolverine` delegate observably mutates
    the effective `WolverineOptions` (the escape hatch is real from day one).
  - Guard clauses: null builder.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "LocalNodeTests|RegistrationTests"
  ```

**GREEN**: the Scope items — minimal implementations passing these tests; full XML docs on every
public member from the start. Wolverine API names per mechanic (d)'s caveat.

**DB changes**: none — this repository has no database project.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: a host now becomes a working messaging node with one call and zero
   configuration — behavior that did not exist in the suite before.
2. Full-suite check (mechanic (j)) — all green (the new exe joins the set); zero build warnings;
   `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Messaging.*.nupkg` appears under
   `src/Cloudstrap.Messaging/bin/Release/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Message contracts need zero package references: suffix conventions classify `*Command`/`*Event`/`*Message`, the `Destinations` map routes commands to destination workload queues, D-1's topology conventions are configured per transport, the consumer can override or replace everything, and one startup log line states the routing in force (AC-MSG4; D-1; mechanic (e))

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging/MessageConventions.cs` *(create — internal unless the
  `configurator.Conventions` delegate shape requires a public parameter type; if public, it is
  named at Gate 1 as part of the surface sign-off)* — suffix classification (source-compatible
  `*Command`/`*Event`/`*Message`), D-1 routing application per mechanic (e): the `Destinations`
  map (key = message namespace/type prefix, value = destination endpoint/workload queue name)
  routes commands on SQL/ASB transports; events → topic per event type + `{WorkloadName}`
  subscription (ASB only, applied in Step 4's transport mapping); Local → in-process.
- `src/Cloudstrap.Messaging/CloudstrapMessagingConfigurator.cs` *(modify)* — the `Conventions`
  delegate becomes functional (replace/extend classification + routing); ordering per mechanic (e)
  (defaults → `Conventions` → `Wolverine` last) enforced.
- `src/Cloudstrap.Messaging/MessagingStartupSummaryLogger.cs` *(create)* — internal
  `IHostedService` logging **one** summary line: transport, endpoint name, routing conventions in
  force (incl. each `Destinations` entry), durability + dead-letter posture, effective
  AutoProvision (the AutoProvision value itself is wired in Step 3 — this step logs the routing
  half).
- `src/Cloudstrap.Messaging/HostApplicationBuilderExtensions.cs` *(modify)* — wire conventions +
  summary logger into the bootstrap.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/ConventionTests.cs` *(create)* — fixture message
  types in a dedicated namespace (`Cloudstrap.Messaging.Tests.Fixtures.Contracts`) deliberately
  referencing **no** Wolverine/Cloudstrap types (the zero-package-reference contract, asserted).

**RED**:
- Unit test file: `ConventionTests.cs`
  - `Conventions_SuffixTypes_AreClassifiedAsCommandEventAndMessage` — classification observed via
    the conventions surface / effective `WolverineOptions` handler+routing state.
  - `Conventions_ContractAssemblyTypes_CarryNoPackageDependency` — reflection over the fixture
    contract types: no Wolverine/Cloudstrap attribute, interface or base class anywhere
    (AC-MSG4's dependency-free clause made a test).
  - `Conventions_DestinationsMap_RoutesCommandsToTheConfiguredWorkloadQueue` — with
    `Cloudstrap:Messaging:Transport = SqlServer` and a `Destinations` entry
    (`"Cloudstrap.Messaging.Tests.Fixtures.Contracts" → "contoso-orders-worker"`), the effective
    routing for `PlaceOrderCommand` targets the `contoso-orders-worker` queue endpoint (asserted on
    the configured `WolverineOptions` **without starting** the host — no SQL touched, AC-M3 intact).
  - `Conventions_ConfiguratorConventions_ReplacesTheDefaultRules` — a custom rule observably
    overrides the suffix default (AC-MSG4's override clause).
  - `Conventions_ConfiguratorWolverine_RunsLastOverConventions` — the `Wolverine` delegate wins
    over both defaults and `Conventions` (mechanic (e)'s ordering, observable).
  - `StartupSummary_LogsTheRoutingInForceInOneLine` — a captured `ILogger` (test logger provider)
    receives exactly one summary line naming the transport, endpoint name and each destination
    (AC-MSG4's summary clause).
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "ConventionTests"
  ```

**GREEN**: the Scope items; candidate Wolverine APIs for routing state inspection per
mechanic (d)'s caveat (executor latitude on the exact introspection surface — the assertions'
*behavioral* content is fixed).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: dependency-free contract types are now classified and routed by
   convention, overridably, with the routing stated at startup — none of which existed before.
2. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 3 — The node is reliable and observable by default: a transiently failing handler is retried immediately then rescheduled with increasing cooldown per the configured counts (exactly one side effect on eventual success), auto-provisioning defaults on in Development and off elsewhere with an explicit value winning and the effective value logged, and Wolverine spans/metrics land additively in whatever OTel pipeline the host has — never a second exporter (AC-MSG5's local half; AC-MSG12; AC-MSG11; mechanic (f))

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging/HostApplicationBuilderExtensions.cs` *(modify)* — retry failure policy
  from `RetryOptions` (`NumberOfImmediate` immediate retries, then `NumberOfDelayed` scheduled
  retries with increasing cooldown, then the D-2 dead-letter posture); AutoProvision resolution
  (`AutoProvision ?? (environment is Development)`) applied to Wolverine's storage/transport
  provisioning flags; mechanic (f)'s additive OTel source/meter registration.
- `src/Cloudstrap.Messaging/MessagingStartupSummaryLogger.cs` *(modify)* — the effective
  AutoProvision value joins the summary line (AC-MSG12).
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Cloudstrap.Messaging.Tests.csproj` *(modify)* —
  version-less `OpenTelemetry.Exporter.InMemory` + `OpenTelemetry.Extensions.Hosting` (both
  already pinned) for the AC-MSG11 tests.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/RetryPolicyTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/AutoProvisionTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/ObservabilityTests.cs` *(create)*

**RED**:
- Unit test file: `RetryPolicyTests.cs` *(local transport; a fixture handler with an invocation
  counter that throws until attempt N)*
  - `Retries_HandlerFailsFewerTimesThanImmediateCount_SucceedsWithExactlyOneSideEffect` —
    `NumberOfImmediate = 3`, handler throws twice then succeeds: invoked exactly 3 times, the side
    effect recorded exactly once (AC-MSG5's exactly-once clause).
  - `Retries_ConfiguredImmediateCount_IsHonored` — `NumberOfImmediate = 1`: a twice-failing
    message is not endlessly retried inline; it proceeds to the scheduled-retry stage (observable
    via Wolverine's envelope/attempt state or the failure-policy configuration — executor latitude
    per mechanic (d), behavioral content fixed).
  - `Retries_DefaultCounts_AreFiveAndFive` — the bound defaults (spec Part B port row).
- Unit test file: `AutoProvisionTests.cs`
  - `AutoProvision_NullInDevelopment_IsOn` / `AutoProvision_NullInProduction_IsOff` /
    `AutoProvision_ExplicitValue_WinsInEitherDirection` — asserted on the effective Wolverine
    provisioning flags (no SQL/ASB started) **and** on the summary log line stating the effective
    value (AC-MSG12 complete).
- Unit test file: `ObservabilityTests.cs`
  - `Messaging_HostWithOtelPipeline_WolverineSpansAppearInThatPipeline` — host registers its own
    OTel tracing with the InMemory exporter; a local publish/handle round trip produces
    Wolverine-sourced activities in the collected spans (AC-MSG11's owner/contribute posture).
  - `Messaging_RegistersNoExporterAndNoSecondTracerProvider` — service-collection descriptor
    assertions: `AddCloudstrapMessaging` alone adds no exporter registration and no
    `TracerProvider`/`MeterProvider` host registration (the AC-ASP1 posture as a tripwire).
  - `Messaging_NoOtelPipeline_HostStillStarts` (AC-MSG11's no-pipeline clause).
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "RetryPolicyTests|AutoProvisionTests|ObservabilityTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: transient failures now heal with bounded retries and one side effect,
   provisioning follows the environment contract, and messaging telemetry rides the host's
   pipeline additively — all new observable behavior.
2. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — Configuration flips the node to Azure Service Bus with no code change: `FullyQualifiedNamespace` + `DefaultAzureCredential` (no credential-bearing key exists to configure), connection-string-by-name as the emulator fallback, D-1's workload topology (command queues via `Destinations`, topic per event type, `{WorkloadName}` subscriptions) and the `{SystemName}-error` transport error-queue naming — and missing/ambiguous ASB config fails startup naming the exact key (AC-MSG2's ASB clauses; AC-MSG3; D-1; D-2's transport-queue half)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging/HostApplicationBuilderExtensions.cs` *(modify)* — the
  `Transport = AzureServiceBus` branch: Wolverine ASB transport with `FullyQualifiedNamespace` +
  `DefaultAzureCredential` when the namespace is set, else the named connection string; D-1
  topology applied (listener queue = endpoint name; command routing per `Destinations`; event
  topics per event type; subscription per consuming workload); the `{SystemName}-error` naming
  applied wherever a transport-level error queue materializes (D-2), `DeadLetter:QueueName`
  overriding.
- `src/Cloudstrap.Messaging/CloudstrapMessagingOptionsValidator.cs` *(modify)* — mechanic (b)'s
  ASB conditional rules.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/AzureServiceBusConfigTests.cs` *(create)* — ⚠️
  **no network** (AC-M3): every assertion is either a `ValidateOnStart` failure (fires before any
  connection) or an inspection of the configured, **unstarted** `WolverineOptions` transport state.

**RED**:
- Unit test file: `AzureServiceBusConfigTests.cs`
  - `AsbTransport_NoNamespaceAndNoConnectionString_StartupFailsNamingTheNamespaceKey` — the
    failure message contains `'Cloudstrap:Messaging:AzureServiceBus:FullyQualifiedNamespace'` and
    no configured **value** appears anywhere in it (AC-MSG2 verbatim).
  - `AsbTransport_ConnectionStringNameThatDoesNotResolve_StartupFailsNamingTheKey_NeverTheValue`.
  - `AsbTransport_NamespaceSet_UsesDefaultAzureCredential_AndNoSecretBearingSettingExists` — the
    configured transport's credential is a `DefaultAzureCredential`; plus a reflection assertion
    that `AzureServiceBusOptions` declares **no** property matching
    `(?i)tenant|clientid|secret|password|key$` (AC-MSG3's "no secret-bearing setting exists",
    permanent).
  - `AsbTransport_D1Topology_CommandQueueTopicPerEventAndWorkloadSubscription_AreConfigured` —
    with a `Destinations` entry and a fixture event type: the unstarted transport state shows the
    destination command queue, an event topic named for the event type, and this node's
    subscription named `{WorkloadName}` (the ⚠️ D-1 one-way door made assertable).
  - `AsbTransport_TransportErrorQueueName_DefaultsToSystemNameError_AndDeadLetterQueueNameOverrides`
    (D-2's naming half).
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "AzureServiceBusConfigTests"
  ```

**GREEN**: the Scope items. **AC-M1 itself (a real ASB namespace, linked spans) is deliberately not
automated** — its manual procedure is written in Step 8's README (the #4 AC-E5 precedent).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a pure configuration change now retargets the node at an ASB namespace
   with platform-standard credentials and the workload topology — with misconfiguration caught at
   startup by name.
2. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0; **no test touched the
   network** (AC-M3 posture).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the node core and its one-way doors *(covers Steps 1–4)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **the new dependency family** — the `Directory.Packages.props` diff
(four `WolverineFx.*` 6.31.0 pins + the EF family floor), the disclosed transitive closure
(Newtonsoft via ASB, JasperFx codegen) · **all-new public API** — every public type shipped so far
reviewed **verbatim against the spec's Public API Sketch** (`HostApplicationBuilderExtensions`,
`CloudstrapMessagingBuilder` (methodless so far), `CloudstrapMessagingConfigurator`,
`CloudstrapMessagingOptions` + five sub-option types, `MessagingTransport`; if Step 2 made a
conventions type public for the `Conventions` delegate, it is named and approved here) · **the ⚠️
D-1 topology one-way door** — queue/topic/subscription names are cloud resources; confirm the
`Destinations` semantics and the `{WorkloadName}`/topic-per-event/`{SystemName}-error` naming
before anything ships · mechanic (d)'s `IWolverineExtension` deferred-configuration seam and any
Wolverine-API deviations the executor logged under mechanic (d)'s caveat.

- [ ] Behavioral verification: test exe output shows — the zero-config local node round trip,
  endpoint identity = workload name, the duplicate-call and unknown-transport fail-fasts (Step 1);
  suffix classification with dependency-free contracts, `Destinations` routing, the
  override/replace hooks, the mechanic-(e) ordering proof and the one-line startup summary
  (Step 2); the retry ladder with exactly one side effect, the AutoProvision environment contract
  with its logged effective value, and additive-only OTel (Step 3); the ASB validation failures
  naming exact keys with no values echoed, `DefaultAzureCredential` with no secret-bearing
  setting, and the D-1 topology assertions (Step 4).
- [ ] Code review: options/validator code against the #1 pattern (keys named, values never
  echoed); `sealed`/static/internal-by-default; single namespace `Cloudstrap.Messaging`; full XML
  docs; the csproj → four PackageReferences + two ProjectReferences, nothing else; no Drop-listed
  concept resurrected.
- [ ] User approved — implementation may continue past this gate

---

## Slice 2 — Durable on SQL Server: `UseSqlServer()` gives the node a durable inbox/outbox with workload-schema isolation and store-based dead letters, and `AddCloudstrapTransactionalMessaging<TDbContext>` makes entity writes and messages atomic ⚠️ ONE-WAY-DOOR (provider seam) RISK AREA

---

## Step 5 — `UseSqlServer()` turns the node durable: Wolverine's inbox/outbox and dead-letter tables land in a workload-derived, sanitized schema (`contoso-orders-worker` → `contoso_orders_worker`; `Durability:SchemaName` overrides) so N workloads share one database; a poison message exhausts the retry ladder into the store's queryable dead-letter table with type+id logged and never the payload; the SqlServer transport moves real messages between two hosts on one database; an unresolvable connection string fails startup naming the key (AC-MSG13; AC-MSG6; AC-MSG5's dead-letter tail; AC-MSG2's `UseSqlServer` clause; D-2; D-3; mechanic (g)) ⚠️ first SQL-backed tests

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging/CloudstrapMessagingBuilder.cs` *(modify)* — the sketch's
  `UseSqlServer(string? connectionStringName = null) : CloudstrapMessagingBuilder`: durable
  message store (inbox/outbox + dead-letter tables) on the named connection string
  (`connectionStringName ?? Durability.ConnectionStringName`), schema
  `Durability.SchemaName ?? Sanitize(WorkloadName)` (lowercase, non-alphanumerics → `_`), applied
  through the mechanic-(d) `IWolverineExtension` seam; durable inbox/outbox policies on
  listeners/senders; store-based dead-lettering as the durable default (D-2); the
  `Transport = SqlServer` branch completed (queue tables in `SqlTransport.SchemaName` when set,
  else Wolverine's default — sender and listener must agree, documented).
- `src/Cloudstrap.Messaging/CloudstrapMessagingOptionsValidator.cs` *(modify)* — the
  SQL conditional rules (mechanic (b)): unresolvable durability/transport connection-string names
  fail startup naming `'Cloudstrap:Messaging:Durability:ConnectionStringName'` /
  `'Cloudstrap:Messaging:SqlTransport:ConnectionStringName'` (value never echoed).
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Cloudstrap.Messaging.Tests.csproj` *(modify)* —
  version-less `Microsoft.EntityFrameworkCore.SqlServer` (new test/demo CPM pin) + (if needed for
  direct table queries) `Microsoft.Data.SqlClient`.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/SqlServerTestDatabase.cs` *(create)* —
  mechanic (g)'s LocalDB/`CLOUDSTRAP_TEST_SQL` helper.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/SqlServerDurabilityTests.cs` *(create)*
- `src/Directory.Packages.props` *(modify)* — the test/demo-only pins above, commented
  "test/demo only — never referenced by a shipped package".

**RED** *(⚠️ these are the D-3 LocalDB tests — real SQL, no Docker, no cloud; AutoProvision on)*:
- Unit test file: `SqlServerDurabilityTests.cs`
  - `UseSqlServer_DefaultSchema_IsTheSanitizedWorkloadName` — application
    `contoso-orders-worker` → durability tables exist under schema `contoso_orders_worker`
    (queried via `INFORMATION_SCHEMA`), and a second workload's node provisions its own schema in
    the **same** database without collision (AC-MSG13 verbatim).
  - `UseSqlServer_DurabilitySchemaNameOverride_Wins`.
  - `UseSqlServer_UnresolvableConnectionStringName_StartupFailsNamingTheKey_NeverTheValue`
    (AC-MSG2's last clause — no SQL touched).
  - `UseSqlServer_PoisonMessage_LandsInTheDeadLetterTableWithTypeAndIdLogged_NeverThePayload` —
    an always-throwing handler with tiny retry counts: after the ladder exhausts, a row for the
    message exists in the store's dead-letter table (queryable — D-2's operational point); the
    captured log carries the message type and id and does **not** contain a sentinel payload
    string (AC-MSG6, AC-MSG5's tail, the secrets-and-telemetry rule).
  - `SqlTransport_TwoHostsOnOneDatabase_CommandCrossesFromSenderToListener` — two in-process hosts
    (different workload names, shared explicit `SqlTransport:SchemaName`), sender routes via
    `Destinations` to the listener's queue: the listener's handler observes the command — the
    cross-node exchange the demo (Step 9) then proves cross-**process**.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "SqlServerDurabilityTests"
  ```

**GREEN**: the Scope items (candidate APIs: `PersistMessagesWithSqlServer(connectionString, schema)`
/ `UseSqlServerPersistenceAndTransport(...)`, durable inbox/outbox policies — mechanic (d)'s
caveat applies).

**DB changes**: none — Wolverine AutoProvision creates its own schema objects at runtime; no
repo-owned SQL scripts exist or are added.

**VERIFY**:
1. Test exe → all pass: the node is now durable — schema-isolated storage, queryable dead letters
   with disciplined logging, and real cross-node SQL transport — none of which existed before.
2. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0. Record the LocalDB
   prerequisite in the step report (D-3).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — Entity writes and messages become atomic: `AddCloudstrapTransactionalMessaging<TDbContext>()` wires the handler's `DbContext` into Wolverine's shared-transaction EF integration — a handler that throws after staging an entity and a message commits neither (AC-M2), a succeeding handler commits row + outbox record in one transaction with dispatch only after commit (AC-MSG7), non-handler HTTP code gets the same guarantee via `IDbContextOutbox<TDbContext>` with crash-recovery delivery (AC-MSG8), and calling it without a durability provider fails fast naming `UseSqlServer` (AC-M2; AC-MSG7; AC-MSG8; D-4)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging/CloudstrapMessagingBuilder.cs` *(modify)* — the sketch's
  `AddCloudstrapTransactionalMessaging<TDbContext>(Action<DbContextOptionsBuilder>? optionsAction = null) : CloudstrapMessagingBuilder`:
  registers `TDbContext` through `WolverineFx.EntityFrameworkCore`'s shared-transaction
  integration (candidate: `AddDbContextWithWolverineIntegration<TDbContext>`), auto-applied
  transactional middleware on handlers, `IDbContextOutbox<TDbContext>` enabled; **fail fast at
  startup naming `UseSqlServer`** when no durability provider was chosen (the sketch's clause —
  implemented as a startup-time check so `UseSqlServer` may still be called after, order-free per
  mechanic (d)).
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Fixtures/OrdersDbContext.cs` *(create)* — a
  minimal test `DbContext` (one `Order` entity) on the EF SqlServer provider.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/TransactionalMessagingTests.cs` *(create)*

**RED** *(LocalDB per mechanic (g))*:
- Unit test file: `TransactionalMessagingTests.cs`
  - `TransactionalHandler_ThrowsAfterStagingEntityAndMessage_CommitsNeither` — **the AC-M2
    verbatim carry**: handler adds an `Order` and cascades a message, then throws; after retries
    are exhausted/short-circuited, the `Orders` table has no row and no outgoing envelope was
    delivered (subscriber handler never observed it, outbox drained empty).
  - `TransactionalHandler_Succeeds_EntityAndMessageCommitAtomically_DispatchAfterCommit` — the
    row exists, the cascaded message arrives at its handler, and the outbox record was written in
    the same transaction (AC-MSG7 — asserted via the store's outbox table before/after).
  - `DbContextOutbox_HttpPathPattern_StagesAndDeliversExactlyOnce` — the documented non-handler
    pattern: resolve `IDbContextOutbox<OrdersDbContext>`, stage entity + `SendAsync`, save/flush
    → row and delivery both observed (the AC-MSG8 pattern the README + demo teach).
  - `DbContextOutbox_CommittedButNotDispatched_IsRecoveredByANewNode` — **the AC-MSG8 crash
    half**: commit the entity + outbox row while suppressing dispatch (dispose the host before
    flushing outgoing), then start a fresh host on the same store with short durability polling:
    the message is delivered — exactly-once effective delivery, no loss.
  - `TransactionalMessaging_WithoutDurabilityProvider_FailsFastNamingUseSqlServer` (no SQL
    touched).
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "TransactionalMessagingTests"
  ```

**GREEN**: the Scope items — thin composition over `WolverineFx.EntityFrameworkCore` (the
finding-3 replacement: no reflection DbContext factory, no `isConfidential` flag; the consumer's
`optionsAction` configures the provider).

**DB changes**: none — EF `EnsureCreated`/Wolverine AutoProvision own the test artifacts.

**VERIFY**:
1. Test exe → all pass: the crown-jewel contract now holds — atomic entity+message commits from
   handlers **and** HTTP paths, with crash recovery — observable behavior new to the suite.
2. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: the durability contract and the provider seam *(covers Steps 5–6)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **the `UseSqlServer` one-way door** — `CloudstrapMessagingBuilder`'s
final method shapes reviewed against the sketch verbatim; confirm the seam leaves
`UsePostgreSql(...)` implementable later as an extension method with **no** signature break (the
reason the builder is public) · **AC-M2** — the founding spec's verbatim criterion, reviewed
against the test evidence line by line · the D-3 LocalDB test posture in practice (runtimes,
flakiness, `CLOUDSTRAP_TEST_SQL` override honored) · the new test/demo-only CPM pins
(`Microsoft.EntityFrameworkCore.SqlServer`, possibly `Microsoft.Data.SqlClient`).

- [ ] Behavioral verification: test exe output shows — sanitized workload-schema isolation with
  two workloads in one database, the override, the naming-the-key connection-string failure, the
  poison message in a queryable dead-letter table with type+id-only logging, and the two-host SQL
  transport exchange (Step 5); the AC-M2 both-halves atomicity, dispatch-after-commit, the
  `IDbContextOutbox` HTTP pattern and the crash-recovery delivery, and the fail-fast without a
  provider (Step 6).
- [ ] Code review: `UseSqlServer` + `AddCloudstrapTransactionalMessaging<TDbContext>` signatures
  === the spec sketch; the mechanic-(d) extension seam (order-free builder calls); no
  payload/connection-string ever logged; XML docs complete incl. the schema-vs-table-prefix
  deliberate-change note (Deliberate Behavior Change 2).
- [ ] User approved — implementation may continue past this gate

---

## Slice 3 — Correlated and shippable: the correlation id flows through envelopes on #2's vocabulary with enforcement, and the package is publishable and guarded forever ⚠️ SHARED-CONTRACT RISK AREA (Core options reuse + #2 doc amendment)

---

## Step 7 — The business correlation id flows across nodes and is enforceable: the configured header (`Cloudstrap:Correlation:HeaderName`) is stamped on every outgoing envelope from `ICorrelationContextAccessor` and populates the accessor on receive (so a remote handler sees the original inbound value, with W3C `traceparent` flowing independently); enforcement per Core's shipped `Cloudstrap:Correlation:Message` options + #2's `CorrelationRequired`/`AllowNoCorrelation` attribute walk blocks handling with a typed, logged error naming header and handler — and #2's attribute XML docs gain the message-handler wording (doc-only, D-5) (AC-MSG9; AC-MSG10; D-5; mechanic (i))

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging/Correlation/CorrelationEnvelopeMiddleware.cs` *(create — internal; one
  file or an in/out pair, executor's structural latitude)* — outgoing: stamp
  `Envelope.Headers[options.HeaderName]` from `ICorrelationContextAccessor.CorrelationId` when
  present (candidate: `CustomizeOutgoingMessagesOfType<object>` / send middleware); incoming: read
  the header into the accessor for the handler's logical flow; absent header + enforcement off →
  the handler runs with a fresh (null) correlation scope.
- `src/Cloudstrap.Messaging/Correlation/CorrelationEnforcementPolicy.cs` *(create — internal)* —
  the single enforcement rule (finding 5's five-to-one collapse): applied per handler chain
  (candidate: Wolverine handler policy/middleware), it blocks handling when
  `RequireForAllMessageHandlers` is on **or** the handler type hierarchy carries
  `CorrelationRequired`, unless `AllowNoCorrelation` is present or the handler's full type name is
  in `ExcludeMessageHandlers`; the block is a logged, typed error naming the configured header and
  the handler type — never the payload. The kept **outgoing** validation point: sending without a
  correlation id while enforcement is on is blocked the same way.
- `src/Cloudstrap.Messaging/HostApplicationBuilderExtensions.cs` *(modify)* — bind Core's shipped
  `CorrelationOptions` (mechanic (i)) + `AddCloudstrapCorrelation()` (idempotent `TryAdd`s from
  #2), wire the middleware + policy.
- `src/Cloudstrap.Observability/Correlation/CorrelationRequiredAttribute.cs` +
  `AllowNoCorrelationAttribute.cs` *(modify — ⚠️ doc-only, D-5's sanctioned amendment)* — XML
  `<summary>`/`<remarks>` extended to cover message handlers alongside endpoints (zero behavioral
  change; the #2 suite must stay green unchanged). **Proposed at Gate 3 per the standing
  pre-release amendment rule — presented as an explicit diff.**
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/CorrelationFlowTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/CorrelationEnforcementTests.cs` *(create)*

**RED** *(local transport — no SQL, no network)*:
- Unit test file: `CorrelationFlowTests.cs`
  - `Correlation_AccessorValueOnSend_ArrivesInTheRemoteHandlersAccessor` — set the accessor,
    publish; the handler-side accessor reads the same value; the envelope carried the default
    `X-Correlation-ID` header (AC-MSG9's core).
  - `Correlation_ConfiguredHeaderName_IsUsedOnTheEnvelope` — `Cloudstrap:Correlation:HeaderName =
    X-CUSTOM-ID`: the envelope header and the receive-side read both use it (one option, both
    sides — #2's setting, redefined nowhere).
  - `Correlation_TraceparentFlowsIndependentlyOfTheBusinessHeader` — with an OTel pipeline
    (InMemory exporter), the handler-side activity is parented/linked to the send-side activity
    regardless of the business header's presence (AC-MSG9's OTel clause).
  - `Correlation_NoInboundValue_EnforcementOff_HandlerRunsWithFreshScope` (AC-MSG10's off half).
- Unit test file: `CorrelationEnforcementTests.cs`
  - `Enforcement_RequireForAllOn_MessageWithoutCorrelation_IsBlockedWithTypedErrorNamingHeaderAndHandler`
    (AC-MSG10; assert the captured log names both, and the handler never ran).
  - `Enforcement_CorrelationRequiredOnHandlerHierarchy_BlocksWithoutTheFlag` (the attribute walk
    incl. a base-class declaration — the source's type-hierarchy behavior carried over).
  - `Enforcement_AllowNoCorrelationOnHandler_Exempts` / `Enforcement_ExcludeMessageHandlersList_Exempts`.
  - `Enforcement_OutgoingSendWithoutCorrelation_WhenRequired_IsBlocked` (the kept send-side
    point).
  - `Enforcement_BindsCoresShippedCorrelationMessageOptionsFromTheSiblingSection` — in-memory
    config `Cloudstrap:Correlation:Message:RequireForAllMessageHandlers = true` observably drives
    the behavior via `Cloudstrap.Core.CorrelationMessageOptions` — mechanic (i) made a test.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "CorrelationFlowTests|CorrelationEnforcementTests"
  ```

**GREEN**: the Scope items — reusing #2's accessor/attributes/header option wholesale; **no new
correlation types beyond the two internal middleware/policy classes** (mechanic (i): the sketched
`MessageCorrelationOptions` is deliberately not created — Gate 3 confirms).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a correlation id now survives the hop between nodes on the configured
   header and enforcement blocks uncorrelated handling on #2's vocabulary — new behavior.
2. Full-suite check (mechanic (j)) — all green **including the unchanged Observability suite**
   (the doc-only amendment changed no behavior); `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 8 — The package is publishable and guarded forever: metadata, README (quick start, options table, D-1 topology and D-2 dead-letter posture, both outbox patterns, the AC-M1 manual ASB verification procedure, the TLS/encryption-at-rest baseline, the `Destinations` binder caveat, migration notes on all nine Deliberate Behavior Changes), permanent tripwires on the closure and the dropped concepts, and the forbidden-identifier sweep (AC-MSG15; AC-A3; AC-ASP2; AC-M1's documented procedure)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging/Cloudstrap.Messaging.csproj` *(modify)* — `<Description>` (durable
  Wolverine messaging node in one call: local/Azure Service Bus/SQL Server transports, suffix
  conventions and workload routing, transactional EF Core inbox/outbox, retries, dead-lettering,
  correlation and OpenTelemetry — no hand-assembled bus, no lost or duplicated messages),
  `<PackageTags>$(PackageTags);messaging;wolverine;outbox;inbox;azureservicebus;sqlserver;
  transactional;correlation</PackageTags>`, `<PackageReadmeFile>README.md</PackageReadmeFile>` +
  pack item.
- `src/Cloudstrap.Messaging/README.md` *(create)* — quick start (the Overview's consumer
  composition, producer + consumer); the `Cloudstrap:Messaging` options table (defaults, overrides
  — every convention has one) + the `Cloudstrap:Correlation:Message` sibling block and #2's
  `HeaderName`; the D-1 topology and D-2 dead-letter posture (store table when durable,
  `{SystemName}-error` where a transport queue materializes — the user-approved divergence stated);
  both outbox patterns (handler-path automatic; HTTP-path `IDbContextOutbox<TDbContext>` — the
  explicit three-line pattern replacing the dropped attribute magic, AC-MSG8); **the AC-M1 manual
  procedure** (real ASB namespace: config, publish, subscribe, span-linked verification in App
  Insights — the #4 AC-E5 precedent; never automated); the no-secrets rules (key names only, no
  payload logging); the `Destinations` dictionary binder-append caveat; the encryption baseline
  (TLS + ASB encryption at rest — property-level encryption permanently dropped); AC-MSG14's
  one-node-per-process contract; migration notes vs the source (Deliberate Behavior Changes 1–9
  verbatim, incl. no wire compatibility with NServiceBus and schema-vs-prefix isolation).
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/PackageSurfaceTests.cs` *(create)* — permanent
  guards (the #11–#13 `PackageSurfaceTests` shape, adapted):
- `src/demo/README.md` or per-app READMEs — **not here**: demo docs land in Step 9.

**RED** *(the guard tests are tripwires against already-correct code and may pass immediately —
the honest failing state is in the artifacts: before GREEN the Release nupkg has no README/
description/tags; recorded per the #2…#13 precedent)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfMessagingAssembly_MatchTheApprovedClosure` — allowed name prefixes:
    `System`/`netstandard`, `Microsoft.Extensions.`, `Microsoft.EntityFrameworkCore`,
    `Microsoft.Data.SqlClient`, `Wolverine`, `JasperFx`, `Weasel`, `Azure.`, `Newtonsoft.Json`
    (the disclosed ASB transitive), `OpenTelemetry`, `Cloudstrap.Core`,
    `Cloudstrap.Observability`; explicitly **zero** names starting `NServiceBus`, `Particular`,
    `Aspire` (AC-ASP2), `Nihdi` (AC-A3), `Duende`, `MudBlazor`. *(Executor latitude: trim the
    allow-list to the actually observed set at GREEN time; the forbidden list is fixed.)*
  - `PublicSurface_IsExactlyTheApprovedTypes` — exported types are exactly the Gate-1-approved
    set (`HostApplicationBuilderExtensions`, `CloudstrapMessagingBuilder`,
    `CloudstrapMessagingConfigurator`, `CloudstrapMessagingOptions`, `AzureServiceBusOptions`,
    `SqlTransportOptions`, `DurabilityOptions`, `RetryOptions`, `DeadLetterOptions`,
    `MessagingTransport`, plus any conventions type Gate 1 approved), all in namespace
    `Cloudstrap.Messaging`, every class sealed or static (the mechanic-(i) posture made
    permanent: **no** `MessageCorrelationOptions`).
  - `PublicTypes_ContainNoForbiddenIdentifiers` — no public type/member matches
    `(?i)nihdi|riziv|cfe|nservicebus|particular|dynatrace`.
  - `MessagingAssembly_DeclaresNoDroppedConcepts` — no declared type/member name contains
    `SendOnly`, `TransactionMode`, `PersistenceType`, `Bridge`, `Encryption`, `DataBus`,
    `Databus`, `TypeLoader`, `CommandExecutor`, `TransactionalSession`, `UniformSession`,
    `Audit`, `ServiceControl`, `ServicePlatform`, `TenantId`, `ClientSecret` (the Drop rows stay
    dead forever).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata and write `README.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the four permanent guards.
2. `dotnet build src/Cloudstrap.sln -c Release` →
   `src/Cloudstrap.Messaging/bin/Release/Cloudstrap.Messaging.<version>.nupkg`; expand a `.zip`
   copy → `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.Messaging.dll` **and** `.xml`; nuspec
   shows the MIT license expression, description, tags, repository URL, and dependencies =
   exactly the four `WolverineFx.*` + `Cloudstrap.Core` + `Cloudstrap.Observability` — no
   `NServiceBus.*`, no `Aspire.*`, no test/demo pin leakage (AC-MSG15, AC-ASP2).
3. **AC-MSG15 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Messaging, src/Test/UnitTest/Cloudstrap.Messaging.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|cfe|nservicebus|particular)'
   ```
   → zero matches beyond the guard tests' self-referential patterns and the README's migration
   notes naming NServiceBus as the *source* engine (read the hits, as in plans 2–13).
4. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: correlation on shared vocabulary + the surface frozen *(covers Steps 7–8)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **shared-contract touches** — (1) mechanic (i)'s **spec-drift
resolution**: Core's shipped `CorrelationMessageOptions` is consumed instead of shipping the
sketch's duplicate `MessageCorrelationOptions` — **confirm or order the sketch's letter**;
(2) the **doc-only D-5 amendment** to #2's shipped `CorrelationRequired`/`AllowNoCorrelation`
attributes, presented as an explicit diff under the standing pre-release amendment rule (the
Observability suite green unchanged is the tripwire) · **public API frozen** — Step 8's
`PublicSurface_IsExactlyTheApprovedTypes` guard now pins the Gate-1 surface forever; the expanded
Release nupkg (metadata + dependency list) reviewed · the README's security-relevant consumer
instructions (outbox patterns, AC-M1 procedure, no-secrets rules) reviewed for correctness.

- [ ] Behavioral verification: test exe output shows — the cross-node correlation round trip on
  the default and configured header, `traceparent` independence, the fresh-scope off-path, the
  full enforcement matrix (require-all, attribute walk, both exemptions, the send-side block) and
  the Core-options binding proof (Step 7); the four permanent guards green, the expanded Release
  nupkg reviewed, the identifier sweep clean (Step 8).
- [ ] Code review: the middleware/policy internals against finding 5 (five types collapsed to
  one rule + one middleware pair); #2's surface consumed, never duplicated; README accuracy
  against as-built behavior (options table, topology, both outbox patterns, AC-M1 procedure).
- [ ] User approved — implementation may continue past this gate

---

## Slice 4 — Demonstrated live: the demo Api produces an order command through the transactional outbox, the demo Worker consumes it over the SQL Server transport on LocalDB, and E2E proves the flow — correlation id included — through the running processes

---

## Step 9 — The demo apps run the package (workflow rule 9; AC-MSG16; D-3's demo design verbatim): `Cloudstrap.Demo.Api` registers `AddCloudstrapMessaging().UseSqlServer().AddCloudstrapTransactionalMessaging<DemoDbContext>()` and its new `POST api/v1/orders` stages an `Order` row + sends `PlaceOrderCommand` via `IDbContextOutbox` (AC-MSG8 live); `Cloudstrap.Demo.Worker` becomes a real messaging node whose transactional handler marks the order processed and records the flowed correlation id (AC-MSG7/AC-MSG9 live); a new E2E test drives the whole thing through the running processes and every pre-existing E2E test stays green ⚠️ DEMO SQL/IDP-SEED RISK AREA

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/demo/Shared/Contracts/PlaceOrderCommand.cs` *(create)* — `public sealed record
  PlaceOrderCommand(Guid OrderId);` — **zero package references** in Contracts, untouched
  otherwise (the suffix-convention story live; teaching comment).
- `src/demo/Api/Cloudstrap.Demo.Api.csproj` *(modify)* — ProjectReference →
  `Cloudstrap.Messaging`; version-less `Microsoft.EntityFrameworkCore.SqlServer` (demo/test pin).
- `src/demo/Api/Data/DemoDbContext.cs` *(create)* — `Order` entity (`Id`, `Description`,
  `Status`, `ProcessedCorrelationId`) mapped to `demo.Orders`; `EnsureCreated` at startup in
  Development (teaching comment: demo-only, IaC in production).
- `src/demo/Api/Program.cs` *(modify)* — the headline three-call registration (mechanic (k),
  teaching comments); `Cloudstrap:Messaging` config via `appsettings.json` *(modify)*:
  `Transport = SqlServer`, `SqlTransport:SchemaName = demo_transport`, `Destinations`
  (`"Cloudstrap.Demo.Contracts" → "demo-application-worker"`), `Retries`, plus
  `ConnectionStrings:DefaultConnection` → LocalDB `CloudstrapDemo`.
- `src/demo/Api/Controllers/OrdersController.cs` *(create)* — versioned like
  `DownstreamController`, no `[Authorize]` (the fallback policy is the gate):
  `POST api/v1/orders` → stages an `Order` + `IDbContextOutbox<DemoDbContext>.SendAsync(new
  PlaceOrderCommand(id))`, save/flush, returns 202 + id (the AC-MSG8 README pattern, live);
  `GET api/v1/orders/{id}` → `{ status, processedCorrelationId }` (the D-3 "demo query endpoint").
- `src/demo/Worker/Cloudstrap.Demo.Worker.csproj` *(modify)* — ProjectReference →
  `Cloudstrap.Messaging`; version-less `Microsoft.EntityFrameworkCore.SqlServer`; ProjectReference
  → `..\Shared\Contracts\Cloudstrap.Demo.Contracts.csproj`.
- `src/demo/Worker/Data/WorkerDbContext.cs` *(create)* — the Worker's own model over the same
  `demo.Orders` table (mechanic (k): producer and consumer own their persistence models).
- `src/demo/Worker/PlaceOrderCommandHandler.cs` *(create)* — a plain Wolverine handler (finding 8
  live: no Cloudstrap base class): loads the `Order`, sets `Status = "Processed"` and
  `ProcessedCorrelationId = ICorrelationContextAccessor.CorrelationId`, saves via the
  transactional integration (AC-MSG7 live); logs the handled message **type + id, never the
  payload** (AC-MSG6 posture, teaching comment).
- `src/demo/Worker/Program.cs` *(modify)* — the same three-call registration + config
  *(`appsettings.json` modify)*: `Transport = SqlServer`, shared `demo_transport` schema, shared
  connection string; the node listens on its `demo-application-worker` workload queue by default —
  no listener config needed (teaching comment).
- `src/demo/Shared/IdentityProvider/TestIdentityProviderSeed.cs` *(modify)* — one new machine
  client `demo-machine` (`client_credentials`, audience `demo-api`) so the E2E test can call the
  hardened Api (mechanic (k); ⚠️ flagged below).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/MessagingTests.cs` *(create)* — `[TestFixture]` on the
  `WorkerHostTests` pattern: `OneTimeSetUp` boots the Worker via `SutProcess.Start` (health port
  **5351** to avoid the `WorkerHostTests` 5350 instance; `CLOUDSTRAP_TEST_SQL`, when set, is
  forwarded to **both** self-booted args and asserted paths); uses the fixture-owned Api at 5330
  and the IdP at 5310.
- `src/demo/Api/README.md` + `src/demo/Worker/README.md` *(modify)* — feature-matrix rows for #14
  (the three-call registration | the new E2E test names), harness notes (LocalDB `CloudstrapDemo`
  prerequisite, `CLOUDSTRAP_TEST_SQL` override, the shared `demo_transport` schema, the two
  distinct durability schemas visible in one database — AC-MSG13 as a teaching point).

**RED** *(write these tests first, run them, confirm they fail — today the Api has no orders
endpoint and the Worker handles nothing)*:
- E2E test file: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/MessagingTests.cs`
  - `Messaging_OrderPlacedThroughTheApiOutbox_IsProcessedByTheWorker_WithTheCorrelationIdObserved`
    — acquire a `client_credentials` token from the IdP (`demo-machine`); `POST api/v1/orders`
    with `Authorization: Bearer` and `X-Correlation-ID: e2e-<guid>` → 202 + id; poll
    `GET api/v1/orders/{id}` (30 s deadline, never a bare sleep) until `status == "Processed"`;
    assert `processedCorrelationId == "e2e-<guid>"` (AC-MSG16 + AC-MSG9 live: the command crossed
    processes over the SQL transport, the outbox dispatched after commit, the correlation id
    survived the hop).
  - `Messaging_WorkerLogsTheHandledCommandTypeAndId_NeverThePayload` — the Worker's captured
    stdout (the `SutProcess` capture) eventually names `PlaceOrderCommand`; a sentinel order
    description posted by the test never appears in the Worker's output (the AC-MSG6 logging
    posture proven live).
  - `Messaging_AnonymousOrdersPost_Returns401` — the hardened Api default still gates the new
    endpoint (the #27 regression posture; the error case).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "MessagingTests"
  ```

**GREEN**: the Scope items. **Every pre-existing E2E test must stay green unchanged** — in
particular the whole suite now boots an Api that requires LocalDB at startup (the D-3 posture);
if any existing test is disturbed, the executor reports it at the gate rather than weakening any
assertion.

**DB changes**: none in the repo — LocalDB `CloudstrapDemo` (tables, schemas, queues) is created
at runtime by EF `EnsureCreated` + Wolverine AutoProvision (Development).

**VERIFY**:
1. E2E exe → the three new tests pass **and every pre-existing E2E test passes unchanged** (build
   first; one-time `playwright.ps1 install chromium` if needed).
2. Manual smoke (optional but recorded): run IdP + Api + Worker per the READMEs; post an order
   with a correlation header (token via the seeded machine client), watch the Worker log the
   handled command, query the order → `Processed` + the correlation id; inspect LocalDB: two
   durability schemas + `demo_transport` + `demo.Orders` in one database.
3. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0; the demo projects still
   pack nothing.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #14 complete *(covers Step 9; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards
requires the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

⚠️ **Risk areas at this gate**: **the E2E-suite-wide LocalDB prerequisite** — the fixture-booted
Api now requires SQL at startup for **every** E2E run (the accepted D-3 posture: LocalDB ships
with VS and `windows-latest`; `CLOUDSTRAP_TEST_SQL` overrides; `Testcontainers.MsSql` stays the
recorded follow-up if CI ever leaves Windows) — **confirm** · **the demo IdP seed change** — one
new machine client (`demo-machine`, audience `demo-api`) reviewed; no shipped package touched ·
mechanic (k)'s demo topology (shared `demo_transport` schema by explicit config — a demo
decision, not a package opinion — and per-host DbContexts over one table) — **confirm**.

- [ ] Behavioral verification: the three new E2E tests pass
  (`Messaging_OrderPlacedThroughTheApiOutbox_IsProcessedByTheWorker_WithTheCorrelationIdObserved`,
  `Messaging_WorkerLogsTheHandledCommandTypeAndId_NeverThePayload`,
  `Messaging_AnonymousOrdersPost_Returns401`) and **all pre-existing E2E tests pass unchanged**;
  the full-suite check (build + 14 unit exes + E2E exe + `dotnet format --verify-no-changes`) is
  green end to end.
- [ ] Spec acceptance sign-off: walk **AC-M1 (procedure documented, not automated), AC-M2, AC-M3,
  AC-A3, AC-ASP2, AC-MSG1…AC-MSG16** against the step evidence using the Overview's AC coverage
  map — all met; confirm nothing from the spec's Drop / Out-of-Scope lists was resurrected (no
  blob claim-check pre-build for #15, no encryption, no Bridge/migration topology, no
  mediator/`ICloudstrapBus`, no `SendOnly`/`TransactionMode`, no credential settings, no sagas,
  no PostgreSQL beyond the seam shape) and every De-NIHDI row is closed
  (`UseNServiceBusForNihdi` → `AddCloudstrapMessaging`, `nihdi-default-bundle` gone, license
  paths/`PARTICULARSOFTWARE_LICENSE`/`\\riziv.*` UNC paths gone, `BusinessSystemName` →
  `SystemName`, neutral fixtures throughout).
- [ ] Docs review: `src/Cloudstrap.Messaging/README.md` matches as-built behavior (incl. the
  AC-M1 manual procedure and both outbox patterns); the Api/Worker demo READMEs cite the real E2E
  test names and the LocalDB prerequisite; the #2 attribute doc amendment shipped as approved at
  Gate 3. **Recorded follow-ups (not in this plan)**: the `configure-wolverine` skill
  (CLAUDE.md's pending-artefacts list — authored post-deliverable), deliverable #15 (blob
  claim-check) builds on this package, #19/#20 build against the D-2 dead-letter posture.
- [ ] User approved — deliverable #14 done; project-manager flips the ROADMAP row to ✅.
