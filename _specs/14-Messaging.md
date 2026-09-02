# Spec: Messaging — `Cloudstrap.Messaging` (Roadmap Deliverable #14)

> Status: **APPROVED 2026-08-31 — zero Open Questions (all five resolved by the user 2026-08-31, in
> each case accepting the analyst's recommendation; see Decision Log D-1–D-5); planner-ready.**
>
> This is a **rebuild against an observed contract**, not a port: the source runs on NServiceBus
> (commercial, license-file handling included), the target is **Wolverine** (MIT) per the founding
> spec's Messaging Migration table. Almost no source line survives verbatim; what carries over is the
> *behavioral contract* — one-call node bootstrap, suffix conventions, `{WorkloadName}` endpoint
> naming, durable inbox/outbox, EF transactional messaging, retries, correlation propagation.
>
> Sources: `_plans/ROADMAP.md` §14 (hand-off brief, inventory verified 2026-08-31) ·
> `_specs/Cloudstrap.md` (Decisions Made "Messaging" + "Messaging durability store", Messaging
> Migration table, AC-M1–AC-M4, De-NIHDI-fication Checklist, Aspire Coexistence, Hosting targets) ·
> shape precedents `_specs/9-ClientCredentialsAuth.md` / `_specs/5-WebApiBootstrap.md` · **shipped**
> code read: `src/Cloudstrap.Core/ApplicationOptions.cs` (WorkloadName/SystemName conventions),
> `src/Cloudstrap.Observability/Correlation/*` (all 9 files — `ICorrelationContextAccessor`,
> `CorrelationRequiredAttribute`, `AllowNoCorrelationAttribute`, `HttpContextExtensions`,
> middleware/delegating handler, `Cloudstrap:Correlation` section shape) · source reference repo
> (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` — **every row of the
> Port Decision Table was opened**: all 24 non-`obj` files of `Nihdi.Core.Configuration.NServiceBus\`
> (composition layer, `Behaviors\` ×7, `Bridge\` ×2, `Encryption\` ×3, `Persistence\`,
> `TransactionalSession\` ×2, `Transport\` ×8, `TransportConfiguration\` ×3) and all 13 files of old
> Core `Settings\NServiceBus\`.
>
> External evidence gathered 2026-08-31 (nuget.org):
> [WolverineFx 6.31.0](https://www.nuget.org/packages/WolverineFx) — **MIT**, published 2026-08-30,
> `net9.0`/**`net10.0`**, 7.8M total downloads, owner jeremydmiller (JasperFx) ·
> [WolverineFx.AzureServiceBus 6.31.0](https://www.nuget.org/packages/WolverineFx.AzureServiceBus) —
> **MIT**, 2026-08-30, deps `Azure.Messaging.ServiceBus ≥ 7.20.1` (MIT), `Azure.Identity ≥ 1.17.0`
> (MIT), `WolverineFx.Newtonsoft` (→ `Newtonsoft.Json`, MIT) ·
> [WolverineFx.SqlServer 6.31.0](https://www.nuget.org/packages/WolverineFx.SqlServer) — **MIT**,
> 2026-08-30, deps `Weasel.SqlServer` (JasperFx, MIT), `WolverineFx.RDBMS` ·
> [WolverineFx.EntityFrameworkCore 6.31.0](https://www.nuget.org/packages/WolverineFx.EntityFrameworkCore)
> — **MIT**, 2026-08-30, deps `Microsoft.EntityFrameworkCore ≥ 10.0.4` (MIT). The whole family
> released in lockstep **the day before this spec** — an actively maintained ecosystem.
>
> **⚠️ Risk areas this deliverable touches**: the extraction's **biggest rewrite** (design-from-
> contract on a framework the suite has never used) · **new dependency family** — four `WolverineFx.*`
> packages, all license-verified MIT above, plus their JasperFx/Azure SDK/EF Core closure ·
> **public-API one-way doors**: the `UseSqlServer` storage-provider seam (PostgreSQL must slot in
> later without a break) and the ASB topology conventions (they name *cloud resources* — queues,
> topics, subscriptions — that outlive any code change; D-1/D-2) · **shared-contract touch**: the
> correlation integration reuses #2's shipped surface (D-5) · SQL Server availability in tests/demo
> (D-3) · gates #15 (blob claim-check) and the #19→#20 Dashboard chain.

## Code-reading findings that shaped this spec

1. **The source package is mostly NServiceBus-operating code, not domain value.** Of the 24 source
   files, three are license-path handling for a commercial product Cloudstrap does not use, five are
   dropped-by-founding-spec features (`Bridge\`, `Encryption\`), three are a stale duplicate folder
   (`TransportConfiguration\` — its namespace has zero imports; `EndpointConfigurationBuilder`
   imports only `…NServiceBus.Transport`), and one (`TypeLoader`, 247 lines) exists solely to
   support the ASB **migration topology** — a NIHDI-specific SQL→ASB transition aid that loads event
   types by name with a four-strategy `Assembly.LoadFrom` shotgun (directory-scanning included).
   The durable behavioral contract is perhaps six files' worth.
2. **The environment sniffing shows up three times and mislabels itself every time.**
   `EnvironmentBasedInstallerEnabler` keys installers off the literal string `"LOC"` (the enterprise
   environment taxonomy); `EndpointConfigurationBuilder` gates the diagnostics *file path* on
   `!IsRunningInAks()` (the deliverable-1 drop precedent, plus the dropped `D:\logsint` convention);
   `AzureServiceBusTransportBuilder.CreateTokenCredential` picks `WorkloadIdentityCredential` iff
   AKS, else `ClientSecretCredential` from config-borne secrets, else connection string. All three
   are cloud-vs-on-prem discriminators the founding spec already ruled out: standard ASP.NET Core
   environments, no file diagnostics, and `DefaultAzureCredential` (which covers workload identity,
   managed identity and env-var service principals uniformly via `AZURE_*` variables — #1's OQ-2
   ruling: no Cloudstrap type models credentials).
3. **The outbox contract is real and is the crown jewel.** `SqlServerPersistenceBuilder` +
   `DbcontextExtensions` implement exactly the pattern AC-M2 demands: the handler's `DbContext` is
   constructed over the *transport transaction's* connection (`ISqlStorageSession.Connection`),
   enlisted in its transaction, and `SaveChangesAsync` is hooked to run before the messaging commit.
   Wolverine's EF Core integration (`WolverineFx.EntityFrameworkCore` — shared-transaction DbContext
   registration + `IDbContextOutbox`) is this same design as a maintained library feature; the
   bespoke reflection-based DbContext factory (constructor probing via `Activator.CreateInstance`,
   plus an enterprise `isConfidential` constructor flag) does not need to survive.
4. **`TransactionalCommandExecutor` is a hand-rolled mediator, and Wolverine *is* a mediator.** It
   resolves `ICommandHandler<TCommand, Result<TResult>>` (coupled to `Nihdi.Core.Functional.Result`),
   opens an NServiceBus `ITransactionalSession`, and commits on success. Wolverine's
   `IMessageBus.InvokeAsync<T>()` with auto-applied transactional middleware is the same capability
   with none of the ownership; porting the executor would mean shipping a second mediator on top of
   the one the package installs. Its `RequiresTransactionalSessionAttribute` MVC filter reduces to
   the documented `IDbContextOutbox` usage pattern. (The root-namespace
   `TransactionalCommandExecutor.cs` is already `[Obsolete]` in the source — a duplicate.)
5. **The correlation behaviors are five files wrapping one rule, and half the rule already
   shipped.** `CorrelationIncoming/OutgoingMessageBehavior` copy the correlation id between the
   configured header and `ICorrelationContextAccessor`; the three validation behaviors funnel into
   one `CorrelationValidatorBehavior` that walks the type hierarchy for
   `CorrelationRequired`/`AllowNoCorrelation` attributes and honors `RequireForAllMessageHandlers` +
   an `ExcludeMessageHandlers` list. Deliverable #2 already shipped the accessor, both attributes,
   the configurable header name (`Cloudstrap:Correlation:HeaderName`, default `X-Correlation-ID`)
   and the `Request:` enforcement block. The messaging side is two Wolverine middleware (in/out) plus
   one enforcement check reusing #2's vocabulary — not six new types (D-5).
6. **The config surface validates strings that should be enums, and defaults that should not
   exist.** `ValidTransportTypes`/`ValidPersistenceTypes`/`ValidTransactionMode` are hand-rolled
   string validators over magic strings; `TransactionMode` itself is an NServiceBus-specific concept
   with a cross-rule ("outbox forces ReceiveOnly") that Wolverine's durable-endpoint model makes
   unrepresentable — there is nothing to map it *to*. `TopicsBundleNamePrefix` defaults to
   `nihdi-default-bundle` (a checklist item), and the `Destinations` dictionary + `RouteConfigurator`
   delegate exist because NServiceBus has no routing without them.
7. **Wolverine ≥ 3 removed the historical objections.** No Lamar container replacement (stock
   `ServiceProvider`), System.Text.Json is the default serializer, `IMessageBus` is uniform (the
   founding spec's UniformSession row confirmed), native OpenTelemetry via a `Wolverine`
   `ActivitySource`/`Meter` — which means observability integration is *additive registration into
   whatever OTel pipeline exists*, never a second exporter (the AC-ASP1 posture). Wolverine is
   registered through the host builder exactly once — a second `AddCloudstrapMessaging` call cannot
   be made idempotent the way #9's registration was, so it must fail loudly instead.
8. **One deliberate non-abstraction**: consumers write Wolverine handlers and inject Wolverine's
   `IMessageBus`/`IDbContextOutbox` directly. Cloudstrap.Messaging is *bootstrap + conventions*, not
   a facade over the bus — wrapping a mediator API behind `ICloudstrapBus` would be surface without
   behavior (the #9 "no facade over `IClientCredentialsTokenManager`" precedent) and would leak on
   the first advanced feature a consumer needs.

---

## User Story

**As an** ASP.NET Core developer deploying to Azure whose services exchange commands and events,
**I want to** stand up a durable messaging node with one registration call —
`AddCloudstrapMessaging(...).UseSqlServer(...)` — and get transports (Azure Service Bus, SQL Server,
local), a transactional inbox/outbox that commits atomically with my EF Core `DbContext`, sensible
retries, dead-lettering, correlation propagation and OpenTelemetry spans out of the box,
**So that** I never hand-assemble a message bus, never write outbox plumbing, and my messages are
neither lost nor duplicated when a handler and a database write must succeed or fail together.

---

## Acceptance Criteria

> AC-M1, AC-M2, AC-M3 are carried **verbatim** from the founding spec (AC-M4 is deliverable #15's).
> AC-A3 and AC-ASP2 are carried as standing tripwires. AC-MSG1…AC-MSG15 are new, spec-specific
> criteria (precedent: AC-CC1…16 in `_specs/9-ClientCredentialsAuth.md`).

| # | Given | When | Then |
|---|-------|------|------|
| AC-M1 | ASB transport configured | Handler publishes an event | Subscriber endpoint receives it; span linked across endpoints in App Insights. *(carried verbatim — verified against a real ASB namespace via the documented manual procedure, the #4 AC-E5 precedent; not part of the automated suite)* |
| AC-M2 | Transactional messaging with EF DbContext | Handler throws after staging entity + outgoing message | Neither the row nor the message is committed (outbox atomicity). *(carried verbatim)* |
| AC-M3 | In-memory transport, no Azure resources | Full test suite runs | All messaging tests pass locally with no network. *(carried verbatim)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried — must stay green)* |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-MSG1 | A host calling `AddCloudstrapMessaging()` with **no** `Cloudstrap:Messaging` section at all | The host starts and a handler-owning assembly is present | A local (in-process) node runs: a message published via `IMessageBus` reaches its handler; no network, no SQL, no Azure dependency touched. |
| AC-MSG2 | `Transport = AzureServiceBus` with neither `FullyQualifiedNamespace` nor a resolvable connection string; or an unknown `Transport` value; or `UseSqlServer()` with an unresolvable connection string | The host starts | Startup fails naming the exact offending key (e.g. `'Cloudstrap:Messaging:AzureServiceBus:FullyQualifiedNamespace'`), via the source-generated `[OptionsValidator]` + `ValidateOnStart` pattern. Connection-string and namespace **values** never appear in the message. |
| AC-MSG3 | `Transport = AzureServiceBus` with `FullyQualifiedNamespace` set and no connection string | The node connects | Authentication uses `DefaultAzureCredential` (honoring `AZURE_*` env vars / workload identity / managed identity uniformly); no Cloudstrap type models tenant/client/secret; no secret-bearing setting exists in the section. |
| AC-MSG4 | Types named `*Command`, `*Event`, `*Message` in a handler assembly, with no per-type configuration | Messages are sent/published | The suffix conventions classify and route them per the routing conventions (D-1); a consumer can override or replace the conventions via the configurator, and a startup-logged summary states the routing in force. |
| AC-MSG5 | A handler that fails transiently N times, with configured retry counts | The message is processed | It is retried immediately up to `Retries:NumberOfImmediate` times, then rescheduled up to `Retries:NumberOfDelayed` times with increasing cooldown, then dead-lettered per D-2; a handler that succeeds on retry produces exactly one observed side effect. |
| AC-MSG6 | The demo Worker with durable SQL messaging and a poison message | Retries exhaust | The message lands in the durable message store's dead-letter table in the durability schema — queryable and replayable (D-2) — and the failure is logged with the message type and id — never the payload. |
| AC-MSG7 | `AddCloudstrapTransactionalMessaging<TDbContext>()` on a host with `UseSqlServer()` | A handler stages an entity and cascades a message, then succeeds | Entity row, inbox/outbox record and outgoing message commit in **one** database transaction (the AC-M2 success half); the message is dispatched only after commit. |
| AC-MSG8 | An HTTP host (non-handler code path) using the transactional outbox pattern documented in the README | The endpoint saves changes and sends a message; the process is killed between commit and dispatch | After restart, the durable outbox delivers the message — exactly-once effective delivery, no loss. |
| AC-MSG9 | An inbound HTTP request carrying `X-Correlation-ID` (or the configured header) whose handler sends a command to another node | The remote handler runs | `ICorrelationContextAccessor.CorrelationId` in the remote handler equals the original inbound value; the envelope carries the configured header name; W3C `traceparent` flows independently via OTel. |
| AC-MSG10 | Correlation enforcement on (`Cloudstrap:Correlation:Message:RequireForAllMessageHandlers` or `CorrelationRequired` on the handler, D-5) and a message arriving without a correlation id | The handler would run | Handling is blocked with a logged, typed error naming the header and handler; `AllowNoCorrelation` on the handler exempts it; with enforcement off the message handles normally and a fresh scope applies. |
| AC-MSG11 | A host with an existing OTel pipeline (Cloudstrap Observability owner mode **or** an Aspire ServiceDefaults-style pipeline) | Messages flow | Wolverine send/receive spans and metrics appear in that pipeline (source/meter registered additively); the package registers **no** exporter and **no** second tracer/meter provider; with no OTel pipeline present, startup still succeeds. *(AC-ASP1 posture)* |
| AC-MSG12 | Default settings in `Development` vs `Production` environments | The node starts | Auto-provisioning of queues/topics/durability tables is **on** in Development and **off** otherwise; an explicit `Cloudstrap:Messaging:AutoProvision` value wins in either direction; the effective value is stated in one startup log line. |
| AC-MSG13 | `UseSqlServer()` with defaults for an app whose `WorkloadName` is `contoso-orders-worker` | Durability storage is provisioned | Wolverine's durability tables land in schema `contoso_orders_worker` (workload-derived, sanitized) — two workloads can share one database without collision; `Cloudstrap:Messaging:Durability:SchemaName` overrides. |
| AC-MSG14 | `AddCloudstrapMessaging()` called twice | The host starts | Startup fails fast with a message naming the duplicate call (Wolverine hosts exactly one node per process — finding 7); the failure mode is a test, not an accident. |
| AC-MSG15 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv`, `NServiceBus`, `Particular` | All green; XML docs on all public API; package metadata + README complete (including the AC-M1 manual ASB procedure and the outbox patterns); zero forbidden identifiers; zero `NServiceBus.*`/`Aspire.*` in any closure; every dependency OSI-licensed and pinned. |
| AC-MSG16 | The demo apps (designated vehicle: **`Cloudstrap.Demo.Worker`** as the messaging node, **`Cloudstrap.Demo.Api`** as the producer via transactional messaging) and the E2E suite, with no cloud ASB available | The E2E suite runs | All pre-existing E2E tests stay green and ≥ 1 new E2E test proves through the running apps that a command produced by the Api is handled by the Worker (SqlServer transport on LocalDB, D-3); correlation id observed end to end. *(workflow rule 9)* |

---

## Port Decision Table

One row per source public type/feature. The target column names the Wolverine capability or the new
Cloudstrap type; "Routed" = belongs to a later deliverable, listed so the planner never builds it
here.

### Part A — `Nihdi.Core.Configuration.NServiceBus\` (composition layer)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `IHostApplicationBuilderExtensions.UseNServiceBusForNihdi` | **Redesign** | `AddCloudstrapMessaging(this IHostApplicationBuilder, Action<CloudstrapMessagingConfigurator>?)` → `CloudstrapMessagingBuilder` | The one-call node bootstrap is the deliverable's point; the shape is not — it takes a settings object and an `ILoggerFactory` the host already owns, and returns nothing chainable (the `UseSqlServer` seam needs a builder). |
| ↳ its `EnableNServiceBus` config gate | **Drop** | — | #5's D-2 precedent: whether a host is a messaging node is visible in `Program.cs`, not buried in a flag whose `false` branch silently no-ops. |
| `IHostBuilderExtensions` (both methods) | **Drop** | — | Legacy `IHostBuilder` callback model; Cloudstrap targets `IHostApplicationBuilder` only (suite-wide precedent since #1). |
| `UseNServiceBusForNihdiOptions` | **Redesign** | `CloudstrapMessagingConfigurator` | The options-with-delegates idea survives (the #5/#9 configurator precedent); the NServiceBus-typed delegates (`RoutingSettings`, `PipelineSettings`) and the default `RouteConfigurator` that fabricates a throwaway `RoutingSettings<LearningTransport>` do not. |
| ↳ `RouteConfigurator` delegate + `NServiceBusConfiguration.Destinations` dictionary | **Redesign** | config-driven command destination map + routing conventions (D-1) | NServiceBus cannot route without explicit endpoint mapping; Wolverine's conventional routing removes most of the need. What survives is the *configurable* part: where commands go. |
| ↳ `PipelineConfigurator` delegate, `ExcludeAssemblies`, `ExcludeTypes` | **Redesign** | `CloudstrapMessagingConfigurator.Wolverine : Action<WolverineOptions>?` (runs last, final say) + `Discovery` hook | One escape hatch to the real engine replaces three NServiceBus-typed knobs; the hard-coded `"Nihdi.Core.Configuration.Dashboard.Api"` exclusion is an enterprise artifact. |
| `EndpointConfigurationBuilder` (orchestrator) | **Replace** | Wolverine node configuration composed inside `AddCloudstrapMessaging` | 400 lines of NServiceBus assembly; each constituent behavior is adjudicated in its own row below. |
| ↳ diagnostics path (`!IsRunningInAks()` + `GetLogFilePath()`) | **Drop** | — | Finding 2; founding hosting posture — no on-prem file conventions, no environment sniffing. Wolverine diagnostics go through standard logging/OTel. |
| ↳ `SystemJsonSerializer` primary | **Port** (as default) | Wolverine's System.Text.Json default | Same wire default, now the engine's own. |
| ↳ `XmlSerializer` fallback deserializer | **Drop** | — | Exists for "incoming messages from legacy systems" — enterprise-interop-specific; no open-source consumer starts with XML-emitting legacy peers. Wolverine's serializer hook (via the `Wolverine` escape hatch) covers anyone who disagrees. |
| ↳ DLQ `SendFailedMessagesTo({BusinessSystemName}-error)` | **Redesign** | Wolverine dead-lettering + `{SystemName}-error` naming convention (D-2) | The convention is a founding-spec keep; the *mechanism* changes because Wolverine's default dead-letter store is the durable message store, not a transport queue — D-2. |
| ↳ audit queue (`AuditProcessedMessagesTo`) | **Drop** | — | Audit-message forwarding exists to feed ServiceControl/ServicePulse, which the founding spec drops; native Wolverine OTel traces/metrics are the replacement. |
| ↳ `InitializeServicePlatformMonitoring` (heartbeats, custom checks, saga audit, `Particular.*` queues) | **Drop** | — | Founding spec: ServicePlatform connector dropped. The hard-coded JSON blob and `Particular.ServiceControl` queue names die with it. |
| ↳ `EnableInstallers()` gated by environment | **Redesign** | `Cloudstrap:Messaging:AutoProvision` (`bool?`; `null` → on iff `Development`) | The capability (dev auto-provisioning) is a founding-spec keep; the `"LOC"` string check is the `IsRunningInAks` drop precedent — explicit overridable option with an environment-based *default*, identical across hosting targets. |
| ↳ `DisableFeature<AutoSubscribe>()` outside dev | **Drop** | — | NServiceBus-specific coupling of subscription creation to installers; in Wolverine, subscription provisioning is part of the same AutoProvision decision. |
| ↳ `EnableUniformSession()` | **Drop** | — | Founding table: Wolverine's `IMessageBus` is already uniform. |
| ↳ `InitializeRetries` (immediate + delayed counts) | **Port** | `Cloudstrap:Messaging:Retries` → Wolverine failure policies | The behavioral contract (N immediate, then N delayed, then dead-letter) and the 5/5 defaults carry over; the mechanism becomes Wolverine's error-handling rules. |
| ↳ `SetLicensFilePath` + `PARTICULARSOFTWARE_LICENSE` env var | **Drop** | — | Gone with NServiceBus (founding + De-NIHDI checklist); the hard-coded `\\riziv.*` UNC paths are checklist items on their own. |
| ↳ `SendOnly()` mode | **Drop** | — | An NServiceBus construct for skipping persistence/receive setup. In Wolverine a producer-only host is *emergent* — no handlers, no listeners — and a durable producer still wants the outbox. A flag would model the engine we left. |
| ↳ `ConfigurePersistence` dispatch (`None`/`Learning`/`SqlServer`) | **Redesign** | the `UseSqlServer()` provider seam on `CloudstrapMessagingBuilder` | Founding decision verbatim: SQL Server v1 behind a seam, PostgreSQL later without an API break. No durability configured = non-durable (buffered) node — the `None`/`Learning` cases collapse into the default. |
| ↳ `DefineCriticalErrorAction` | **Drop** | — | NServiceBus-specific lifecycle callback (with a bespoke `Console.Out` write); Wolverine integrates with the generic host's lifetime and standard logging. |
| ↳ `AddCorrelation` (5 pipeline registrations) | **Redesign** | one Wolverine middleware pair + one enforcement rule on #2's shipped surface (D-5) | Finding 5. |
| `NServiceBusConventions` (suffix conventions `*Command`/`*Event`/`*Message`) | **Port** | Cloudstrap routing/classification conventions (default suffixes kept, overridable) | The one convention consumers' *message contracts* depend on; explicitly kept by the founding table. |
| `EnvironmentBasedInstallerEnabler` | **Drop** | — | Superseded by the `AutoProvision` option above; the `"LOC"` literal is the environment-taxonomy checklist item. |
| `Transport\AzureServiceBusTransportBuilder` | **Replace** | `WolverineFx.AzureServiceBus` transport | Transport construction is the library's job; Cloudstrap owns only the options mapping. |
| ↳ `CreateTokenCredential` (AKS→`WorkloadIdentityCredential`, config secrets→`ClientSecretCredential`) | **Drop** | `DefaultAzureCredential` when `FullyQualifiedNamespace` is set | Finding 2; #1's OQ-2 ruling — no credential modeling, no secrets in the section, identical behavior across supported hosts. |
| ↳ migration topology (`EventsToMigrate`/`MigratedPublishedEvents`/`MigratedSubscribedEvents`, `MigrateFromNamedSingleTopic`) | **Drop** | — | NIHDI's own single-topic→topic-per-event transition state, built on an NServiceBus API already `[Obsolete]` in the source. Cloudstrap starts on the target topology; there is nothing to migrate *from*. |
| ↳ hard-coded transport tuning (WebSockets on, 30 s circuit breaker, 5× exponential ASB client retry) | **Drop** | Wolverine/Azure SDK defaults + the `Wolverine` escape hatch | Unexplained magic numbers Cloudstrap would own forever; the Azure SDK's defaults are maintained by the people who own the client. |
| `Transport\AzureServiceBusTransportDefinitionParameter` | **Drop** | — | Internal DTO shaped entirely by the dropped credential/migration features. |
| `Transport\NihdiConfigurationExtensions` (both mappers, incl. the Bridge one) | **Drop** | — | Mapping glue for the dropped DTO; the Bridge variant maps a dropped feature. |
| `Transport\SqlTransportBuilder` + `SqlTransportDefinitionParameters` | **Replace** | Wolverine SQL Server transport (via `WolverineFx.SqlServer`) | Same reasoning as ASB; the subscription-table and schema-for-queue plumbing is NServiceBus-internal. |
| `Transport\LearningBuilder` | **Replace** | Wolverine local (in-process) transport | Founding table: local transport replaces Learning — and it is Wolverine's built-in default, zero code. |
| `Transport\TransactionModeMapper` | **Drop** | — | Finding 6: `TransportTransactionMode` is an NServiceBus concept with no Wolverine equivalent to map to; durability semantics are expressed by the inbox/outbox instead. |
| `Transport\TypeLoader` | **Drop** | — | Finding 1: 247 lines of assembly-probing (`Assembly.LoadFrom`, directory scans) serving only the dropped migration topology. A liability, not a feature. |
| `TransportConfiguration\` folder (`LearningBuilder`, `SqlTransportBuilder`, `TransactionModeMapper`) | **Drop** | — | Stale duplicates of `Transport\` — the namespace has zero imports anywhere in the source repo. Dead code. |
| `Persistence\SqlServerPersistenceBuilder` | **Replace** | Wolverine SQL Server message store (durable inbox/outbox) behind `UseSqlServer()` | Finding 3: capability kept in full — table-prefix isolation becomes schema isolation (deliberate change D-list), installer gating becomes AutoProvision, subscription-cache tuning is NServiceBus-internal. |
| `DbcontextExtensions` (reflection DbContext factory over the storage session) | **Replace** | `WolverineFx.EntityFrameworkCore` shared-transaction DbContext integration, surfaced as `AddCloudstrapTransactionalMessaging<TDbContext>` | Finding 3: same design, library-owned. The constructor-probing `Activator.CreateInstance` factory and the `isConfidential` enterprise flag (confidential-compute constructor convention) are dropped; consumers configure their `DbContextOptions` through the standard delegate. |
| `TransactionalSession\TransactionalCommandExecutor` (+ obsolete root duplicate) | **Replace** | Wolverine `IMessageBus.InvokeAsync` + transactional middleware; no Cloudstrap executor type | Finding 4: a second mediator with a `Nihdi.Core.Functional.Result` coupling; Wolverine already is one. Consumers wanting functional results use LanguageExt in their own handlers (founding decision — no `Cloudstrap.Functional`). |
| `TransactionalSession\RequiresTransactionalSessionAttribute` (MVC resource filter) | **Drop** | documented `IDbContextOutbox` pattern (README + demo, AC-MSG8) | An implicit ambient transaction opened by an attribute is the kind of magic Cloudstrap avoids; the explicit outbox pattern is three lines and visible at the call site. |
| `Behaviors\CorrelationIncomingMessageBehavior` / `CorrelationOutgoingMessageBehavior` | **Redesign** | one Wolverine middleware pair bridging envelope header ⇄ `ICorrelationContextAccessor` (#2's shipped accessor, configured header name) | Finding 5 — same contract, #2's surface, no duplicate correlation ownership. |
| `Behaviors\CorrelationValidatorBehavior` (+ `.Log`) + the 3 validation behavior shells | **Redesign** | one enforcement check (require-for-all flag + `CorrelationRequired`/`AllowNoCorrelation` attribute walk + exclusion list), shape per D-5 | Five types collapse to one rule; the attributes already shipped in #2. The *outgoing* and *handler* validation points are kept (block send / block handle); the type-hierarchy attribute walk carries over. |
| `Bridge\BridgeConfigurationBuilder` + `EventTypesDestinationEnpointName` | **Drop** | — | Founding decision: MessagingBridge is NIHDI-migration-specific. Confirmed nothing else references it. |
| `Encryption\` (3 files: `EncryptedDataBusSerializer`, `EncryptionOrchestrator`, `NihdiConfigurationExtensions`) | **Drop** | — | Founding decision: property-level message encryption dropped permanently; TLS + ASB encryption at rest is the documented baseline (README states this). |
| databus / `TryUseDatabus` (blob claim check wiring inside `EndpointConfigurationBuilder`) | **Routed** | #15 `Cloudstrap.Messaging.AzureBlob` | AC-M4 belongs to the next deliverable; nothing here may pre-build it. |
| `AssemblyVisibility.cs` (`InternalsVisibleTo` tests) | **Port** | `InternalsVisibleTo("Cloudstrap.Messaging.Tests")` | Standard test visibility, renamed. |

### Part B — old Core `Settings\NServiceBus\` (13 files, config shape only)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `NServiceBusConfiguration` | **Redesign** | `CloudstrapMessagingOptions`, section `Cloudstrap:Messaging`, owned by this package | Finding 6 + roadmap refinement 1 (options live here, not in Core). The license constants (incl. three `\\riziv.*` UNC paths), encryption/certificate/HSM properties, `EnableNServiceBus`, and the `IValidatableObject` cascade are all dropped by rows above or by #1's validator pattern. |
| `TransportConfiguration` | **Redesign** | `CloudstrapMessagingOptions.Transport : MessagingTransport` (enum `Local`/`AzureServiceBus`/`SqlServer`, default `Local`) | Config-selected transport is genuinely valuable (local in dev, ASB in prod, no code change); the string-plus-validator model becomes a bound enum, `SendOnly`/`EnableOutbox`/`TransactionMode` fall away per Part A rows. |
| `AzureServiceBusTransportConfiguration` | **Redesign** | `AzureServiceBusOptions { FullyQualifiedNamespace, ConnectionStringName }` | Keeps exactly what selects and reaches a namespace. Dropped: `TenantId`/`ClientId`/`ClientSecret` (credential ruling), `TopicsBundleNamePrefix = "nihdi-default-bundle"` (checklist item; replaced by routing conventions, D-1), `EnableDatabus` (routed #15), the three `*Migrate*` arrays (dropped topology). |
| `SqlTransportConfiguration` | **Redesign** | `SqlTransportOptions { ConnectionStringName, SchemaName }` | Connection-string-by-name keeps the platform `ConnectionStrings:` convention; `SubscriptionTableName` is NServiceBus-internal storage layout. |
| `SqlPersistenceConfiguration` | **Redesign** | `DurabilityOptions { ConnectionStringName, SchemaName }` consumed by `UseSqlServer()` | Same shape, new engine; default schema derives from `WorkloadName` (AC-MSG13) replacing the `{WorkloadName}_` table prefix (deliberate change). |
| `RetryConfiguration` | **Port** | `RetryOptions { NumberOfImmediate = 5, NumberOfDelayed = 5 }` | The only settings class that survives nearly verbatim — small, meaningful, engine-agnostic. |
| `MonitoringConfiguration` | **Drop** | — | ServicePlatform monitoring; already slated for deletion by the roadmap. |
| `BridgeConfiguration` / `BridgeDestinationConfiguration` | **Drop** | — | MessagingBridge settings; already slated for deletion. |
| `NamingConventionsExtensions.EndpointQueueName` (`{WorkloadName}`) | **Port** | default endpoint/queue name = `ApplicationOptions.WorkloadName` (shipped #1), `EndpointName` override in options | The kept workload-naming opinion from the checklist, now backed by the shipped computed property. |
| `NamingConventionsExtensions.ErrorQueueName` (`{BusinessSystemName}-error`) | **Port** | `{SystemName}-error` dead-letter naming convention (mechanism per D-2), overridable | Founding table keeps it; `BusinessSystemName` → shipped `SystemName`. |
| `NamingConventionsExtensions.AuditQueueName` | **Drop** | — | Audit forwarding dropped with ServicePlatform. |
| `NamingConventionsExtensions.PersistenceTableNamePrefix` (`{WorkloadName}_`) | **Redesign** | workload-derived durability **schema** (AC-MSG13) | Wolverine isolates by schema, not table prefix; same isolation guarantee, recorded as a deliberate behavior change. |
| `NamingConventionsExtensions.TopicsBundleName` | **Drop** | — | Lower-cases a prefix for the dropped single-topic bundle topology. |
| `ValidTransportTypes` / `ValidPersistenceTypes` / `ValidTransactionMode` | **Drop** | real enums + `[OptionsValidator]` | Finding 6: hand-rolled string validation replaced by the type system and #1's validation pattern. |

**Tally**: 6 Port · 15 Redesign · 8 Replace · 27 Drop · 1 Routed *(57 rows)*.

---

## Public API Sketch

Namespace **`Cloudstrap.Messaging`** (single namespace, matching the package id). Everything
`public sealed`/`static`; middleware, validators and convention internals are `internal`. Wolverine's
own types (`WolverineOptions`, `IMessageBus`, `IDbContextOutbox`) are first-class in consumer code —
no Cloudstrap facade over the bus (finding 8).

```text
Cloudstrap.Messaging
├── HostApplicationBuilderExtensions (static)
│     AddCloudstrapMessaging(
│         this IHostApplicationBuilder builder,
│         Action<CloudstrapMessagingConfigurator>? configure = null)
│         : CloudstrapMessagingBuilder
│       — binds + validates CloudstrapMessagingOptions (Cloudstrap:Messaging) with ValidateOnStart;
│         boots the Wolverine node: selected transport (Local default / ASB / SQL), suffix-based
│         message conventions + routing (D-1), retry policies, dead-lettering (D-2), correlation
│         middleware (D-5), additive OTel source/meter registration (never an exporter),
│         AutoProvision per environment default. Second call → fail fast (AC-MSG14).
│
├── CloudstrapMessagingBuilder (public sealed — the provider seam, ⚠️ one-way door)
│     UseSqlServer(string? connectionStringName = null) : CloudstrapMessagingBuilder
│       — durable inbox/outbox on SQL Server; schema from Durability options (workload-derived
│         default). PostgreSQL later = a new leaf package adding UsePostgreSql(...) as an extension
│         method on THIS builder type — additive, no break (the reason the builder is public).
│     AddCloudstrapTransactionalMessaging<TDbContext>(
│         Action<DbContextOptionsBuilder>? optionsAction = null) : CloudstrapMessagingBuilder
│       — registers TDbContext wired into Wolverine's shared-transaction EF Core integration
│         (WolverineFx.EntityFrameworkCore): handler DbContext + cascaded/outbox messages commit
│         atomically (AC-M2/AC-MSG7); enables IDbContextOutbox<TDbContext> for non-handler code
│         (AC-MSG8). Requires a durability provider; without one → fail fast at startup naming
│         UseSqlServer.
│
├── CloudstrapMessagingConfigurator            — code-level hooks (the #5/#9 configurator precedent)
│     Conventions : Action<...>?               — replace/extend message classification + routing rules
│     Wolverine   : Action<WolverineOptions>?  — runs LAST, final say over everything (serializers,
│                                                discovery, listeners, endpoints, policies)
│
├── CloudstrapMessagingOptions                 — section Cloudstrap:Messaging (owned HERE)
│     const SectionName = "Cloudstrap:Messaging"
│     Transport        : MessagingTransport = Local
│     EndpointName     : string?             — default: ApplicationOptions.WorkloadName
│     AutoProvision    : bool?               — null → true iff Development (AC-MSG12)
│     AzureServiceBus  : AzureServiceBusOptions   { FullyQualifiedNamespace : string?,
│                                                   ConnectionStringName : string? }
│     SqlTransport     : SqlTransportOptions      { ConnectionStringName = "DefaultConnection",
│                                                   SchemaName : string? }
│     Durability       : DurabilityOptions        { ConnectionStringName = "DefaultConnection",
│                                                   SchemaName : string?  — default from WorkloadName }
│     Retries          : RetryOptions             { NumberOfImmediate = 5, NumberOfDelayed = 5 }
│     DeadLetter       : DeadLetterOptions        { QueueName : string?  — default {SystemName}-error }
│     Destinations     : IDictionary<string,string>  — command routing map: key = message
│                        namespace/type prefix, value = destination workload queue name (D-1)
│
├── MessageCorrelationOptions                  — section Cloudstrap:Correlation:Message (owned HERE,
│     const SectionName = "Cloudstrap:Correlation:Message"                                   D-5)
│     RequireForAllMessageHandlers : bool = false
│     ExcludeMessageHandlers       : IList<string>   — full type names exempted from enforcement
│
└── MessagingTransport (enum)  Local | AzureServiceBus | SqlServer
```

**Deliberately not shipped**: no `ICloudstrapBus`/mediator wrapper (finding 8) · no `SendOnly`,
`TransactionMode` or `PersistenceType` settings (Part A rows) · no credential settings of any kind
(AC-MSG3) · no `IHostBuilder` overloads · no message-contract base classes or marker interfaces
(suffix conventions keep contracts dependency-free, exactly as the source's conventions did for
NServiceBus).

**Configuration** — this package owns exactly one new section, `Cloudstrap:Messaging` (plus the
sibling `Cloudstrap:Correlation:Message` block, D-5 — symmetric with #2's shipped `Request:` block,
no shipped code changed). It consumes `Cloudstrap:Application` (workload/system
names, #1) and `Cloudstrap:Correlation:HeaderName` (#2) and redefines neither. Connection strings
resolve through the standard `ConnectionStrings:` section by name (platform-conventions posture).
`Destinations` is a dictionary — #1's binder-append caveat applies and the README notes it.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Activation | Nothing happens until `AddCloudstrapMessaging()` is called; no config flag toggles the node. | Call it, or don't. |
| Transport | `Local` — in-process queues, zero infrastructure, works on a fresh clone (AC-MSG1). | `Cloudstrap:Messaging:Transport` = `AzureServiceBus` / `SqlServer`. |
| ASB authentication | `FullyQualifiedNamespace` + `DefaultAzureCredential` (env vars / workload identity / managed identity — identical across Web Apps and AKS). Connection string by name is the documented fallback for local emulators. | `AzureServiceBus:ConnectionStringName`; anything deeper via `configurator.Wolverine`. |
| Endpoint identity | Queue/endpoint name = `WorkloadName` (`{system}-{subsystem}-{type}`, the kept checklist opinion). | `Cloudstrap:Messaging:EndpointName`. |
| Message classification | Suffixes `*Command` / `*Event` / `*Message` (source-compatible); commands are sent to the destination workload's `{WorkloadName}` queue via the `Destinations` map; events are published to a topic per event type with a subscription named after each consuming `WorkloadName` (D-1). | `configurator.Conventions`; `configurator.Wolverine` for full control. |
| Durability | None until a provider is chosen — the node runs buffered/non-durable and says so in one startup log line. `UseSqlServer()` turns on the durable inbox/outbox. | `UseSqlServer(...)`; PostgreSQL later via a leaf package on the same builder. |
| Durability isolation | Schema derived from `WorkloadName` (sanitized: non-alphanumerics → `_`), so N workloads share one database safely (AC-MSG13). | `Durability:SchemaName`. |
| EF transactional messaging | `AddCloudstrapTransactionalMessaging<TDbContext>()`: handler-path atomicity automatic; HTTP-path via `IDbContextOutbox<TDbContext>` (documented pattern, demoed in `Cloudstrap.Demo.Api`). | The `optionsAction` delegate for provider/interceptor configuration. |
| Retries | 5 immediate, then 5 scheduled with increasing cooldown, then dead-letter. | `Retries:NumberOfImmediate` / `:NumberOfDelayed`; full policy control via `configurator.Wolverine`. |
| Dead-lettering | Durable message store's dead-letter table (queryable, replayable) when durability is on; the `{SystemName}-error` name applies to the transport-level error queue wherever one materializes (e.g. non-durable ASB endpoints) — D-2; the posture in force is stated in the startup summary. | `DeadLetter:QueueName`; transport-level dead-lettering via `configurator.Wolverine`. |
| Provisioning | AutoProvision on in `Development`, off elsewhere; production resources come from IaC. | `Cloudstrap:Messaging:AutoProvision` (explicit value always wins). |
| Correlation | Envelope header = `Cloudstrap:Correlation:HeaderName` (#2's setting, default `X-Correlation-ID`); accessor populated on receive, header stamped on send; W3C `traceparent` flows via OTel regardless. Enforcement: `Cloudstrap:Correlation:Message` flags + #2's shipped attributes (D-5). | #2's `HeaderName`; `RequireForAllMessageHandlers` / `ExcludeMessageHandlers` / `AllowNoCorrelation`. |
| Observability | Wolverine's `ActivitySource`/`Meter` registered **additively** into whatever OTel pipeline the host has (Cloudstrap owner mode, contribute mode, or Aspire ServiceDefaults). No exporter, no tracer/meter provider of its own; no-op without a pipeline. *(AC-ASP1 posture, AC-MSG11)* | `configurator.Wolverine` to suppress or extend instrumentation. |
| Secrets & telemetry | Connection strings, namespaces and payloads never appear in logs, validation messages, exceptions or span tags — key *names* only; dead-letter logging carries message type + id, never the body. | None — rule, not convention. |
| Serialization | System.Text.Json (Wolverine default; matches the source's primary serializer). No XML fallback. | `configurator.Wolverine` (Wolverine's serializer API). |
| Message-contract dependencies | Contract assemblies need **zero** package references — classification is by naming convention, exactly as the source intended with `NServiceBusConventions`. | `configurator.Conventions` for explicit registration. |
| Aspire coexistence | Messaging is outside ServiceDefaults' remit; the only overlap is OTel, handled by the additive-registration row above. AC-ASP2 carried as a closure tripwire. | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in
`src/Test/UnitTest/Cloudstrap.Messaging.Tests`. The default suite runs entirely on the **local
transport with in-memory durability semantics** — no network, no Docker, no SQL (AC-M3). AC-M2/
AC-MSG7/AC-MSG8/AC-MSG13 require a real SQL Server (Wolverine's SQL message store cannot be
faked meaningfully) — they run against SQL Server **LocalDB** by default, overridable via the
`CLOUDSTRAP_TEST_SQL` environment variable (D-3). AC-M1 is a documented manual procedure against a
real ASB namespace (README section, the #4 AC-E5 precedent), never part of the automated suite.
Demo/E2E per AC-MSG16: `Cloudstrap.Demo.Worker` is the designated messaging node,
`Cloudstrap.Demo.Api` the producer through `AddCloudstrapTransactionalMessaging<TDbContext>`.

---

## Dependencies

| Package | Version (pin at plan time) | License | Why it is justified |
|---|---|---|---|
| `WolverineFx` | 6.31.0 | MIT (verified 2026-08-31) | The engine — replaces NServiceBus per the founding decision. Eliminates bespoke bus, mediator, outbox, retry and scheduling code wholesale. |
| `WolverineFx.AzureServiceBus` | 6.31.0 | MIT (verified) | ASB transport (AC-M1). Brings `Azure.Messaging.ServiceBus` (MIT), `Azure.Identity` (MIT) and — **disclosed footprint** — `WolverineFx.Newtonsoft` → `Newtonsoft.Json` (MIT) transitively. |
| `WolverineFx.SqlServer` | 6.31.0 | MIT (verified) | SQL transport + durable message store behind `UseSqlServer()` (founding "SQL Server only in v1" decision). Brings `Weasel.SqlServer` (JasperFx, MIT). |
| `WolverineFx.EntityFrameworkCore` | 6.31.0 | MIT (verified) | The `AddCloudstrapTransactionalMessaging<TDbContext>` contract (AC-M2) — replaces the source's reflection-based DbContext/session bridging. Brings `Microsoft.EntityFrameworkCore` 10.x (MIT). Kept in-box per D-4. |
| `Cloudstrap.Core` (project) | — | MIT | `ApplicationOptions` naming conventions, options/validation patterns. |
| `Cloudstrap.Observability` (project) | — | MIT | `ICorrelationContextAccessor`, correlation attributes, `Cloudstrap:Correlation:HeaderName`, and the OTel packages needed for additive source registration. |
| *(test/demo only, D-3)* SQL Server **LocalDB** — no package | — | — | SQL Server for AC-M2-class tests and the E2E demo, `CLOUDSTRAP_TEST_SQL` env-var override; `Testcontainers.MsSql` (MIT) noted as a follow-up only if CI ever leaves Windows runners. Nothing reaches a shipped closure. |

No other new dependencies. Zero `NServiceBus.*`, zero `Aspire.*`, zero `Nihdi.*` anywhere in the
closure (AC-MSG15). Wolverine performs runtime code generation via its JasperFx toolchain (MIT) —
normal for server workloads; noted because it is new to this suite.

---

## Deliberate Behavior Changes (vs the source library)

1. **Engine swap, no wire compatibility.** Wolverine envelopes/headers are not NServiceBus-
   compatible; a Cloudstrap node cannot exchange messages with an existing NServiceBus endpoint.
   Cloudstrap has no consumers, and NIHDI-interop is a founding Non-Goal.
2. **Durability isolation moves from table prefix (`{WorkloadName}_`) to a workload-derived SQL
   schema** — same shared-database guarantee, Wolverine's native mechanism (AC-MSG13).
3. **Dead-lettering mechanism** changes from an NServiceBus error *queue* to Wolverine's durable
   store-based dead-letter table (queryable, replayable); the `{SystemName}-error` naming convention
   is preserved wherever a transport-level error queue materializes (D-2, user-approved divergence
   from the founding table's literal wording).
4. **No XML fallback deserializer** — System.Text.Json only, by default.
5. **No audit queue, no ServicePlatform heartbeats/metrics** — native OTel traces/metrics replace
   the Particular toolchain (founding decision).
6. **Installer gating by environment string `"LOC"` → explicit `AutoProvision` option** with a
   `Development` default (works identically on Web Apps, containers and AKS).
7. **ASB credential selection by host sniffing → `DefaultAzureCredential`** with no secret-bearing
   configuration keys.
8. **The command-executor mediator (`ICommandHandler`/`Result<T>`) is not ported** — Wolverine's
   `InvokeAsync` + transactional middleware is the supported path; functional result types are the
   consumer's own choice (LanguageExt, per the founding `Cloudstrap.Functional` decision).
9. **Second registration fails fast** instead of being silently tolerated — Wolverine hosts one
   node per process, and the failure is contractual (AC-MSG14).

---

## Out of Scope

- **Blob claim-check / databus** — deliverable #15 (`Cloudstrap.Messaging.AzureBlob`, AC-M4).
- **Property-level message encryption** and everything in `Encryption\` — dropped permanently
  (founding); TLS + ASB encryption at rest is the documented baseline.
- **MessagingBridge** (`Bridge\`), the **ASB migration topology** (+ `TypeLoader`), **ServicePlatform
  /ServicePulse** connectivity, audit queues, NServiceBus license handling — dropped; the planner
  must not resurrect any row marked Drop above.
- **UniformSession** — meaningless under Wolverine.
- **PostgreSQL durability** — post-v1; this spec only guarantees the seam shape (public builder +
  extension-method growth path).
- **Sagas / scheduled recurring jobs** — Wolverine has saga support, but the source never exposed
  it and no AC asks for it (no gold-plating); recurring work is #16's Hangfire territory.
- **Dashboard queue peek/purge/retry tooling** — #19/#20.
- **A `Cloudstrap.Aspire` integration** — post-v1 leaf, per founding posture.

---

## Decision Log (gate answers, 2026-08-31 — zero Open Questions remain; spec is planner-ready)

All five Open Questions were answered by the user on 2026-08-31, in each case accepting the
analyst's recommendation (option a). The full evidence and rejected options remain on record in the
Code-reading findings, the Port Decision Table, and the Deliberate Behavior Changes rows they
resolved into.

| DL | Decision (final) | Rationale kept on record |
|---|---|---|
| **D-1** | **ASB topology & routing are workload-centric** (⚠️ one-way door: names cloud resources): commands are sent to the destination workload's `{WorkloadName}` queue via the config `Destinations` map (key = message namespace/type prefix, value = destination endpoint name); events publish to a **topic per event type** with a subscription named after each consuming `{WorkloadName}`; suffix conventions classify. Overridable via `configurator.Conventions` / `configurator.Wolverine`. | Preserves the source's operational model (one inbox queue per workload; the `{system}-{subsystem}-{type}` checklist opinion drives resource names) and keeps contract assemblies dependency-free. Rejected: Wolverine-native type-based naming (leaks engine naming into cloud resources Cloudstrap owns the opinion for) and no-conventions (abandons the opinionated-defaults charter). |
| **D-2** | **Dead-lettering defaults to the durable message store's dead-letter table** (queryable, replayable) when durability is on; the `{SystemName}-error` name applies to the transport-level error queue wherever one materializes (e.g. non-durable ASB endpoints). A **user-approved divergence** from the founding table's literal "`{system}-error` queue kept" wording — read as "keep the *naming convention* where a queue exists". #19/#20's Dashboard builds against this posture. | Better operations (SQL-queryable, replayable dead letters) and less bespoke code than always materializing an error queue and forgoing store-based replay. |
| **D-3** | **AC-M2-class tests and the E2E demo run on SQL Server LocalDB by default**, overridable via the `CLOUDSTRAP_TEST_SQL` env var; no new test dependency. Demo design: `Cloudstrap.Demo.Api` registers `AddCloudstrapMessaging` + `UseSqlServer` + `AddCloudstrapTransactionalMessaging<DemoDbContext>` and sends a command; `Cloudstrap.Demo.Worker` (same LocalDB, SqlServer transport) handles it; the E2E test observes the effect through a demo query endpoint. `Testcontainers.MsSql` (MIT) is the noted follow-up if CI ever leaves Windows runners. | LocalDB is present with VS and on `windows-latest` runners — full cross-process durability proven without Docker or cloud; the definition of done ("AC-M2 covered by tests") holds on a fresh clone. Rejected: manual-only AC-M2 (fails the definition of done). |
| **D-4** | **EF Core integration stays in-box** in `Cloudstrap.Messaging` (`AddCloudstrapTransactionalMessaging<TDbContext>` + the `WolverineFx.EntityFrameworkCore` reference), per the founding package map. The `Cloudstrap.Messaging.EntityFrameworkCore` leaf alternative was considered and rejected — the door is closed knowingly. | "One package, one call" ergonomics; every AC-M2 consumer needs EF anyway; EF Core is MIT and inert when unused on servers. |
| **D-5** | **Correlation enforcement reuses #2's shipped attributes** (`CorrelationRequired`/`AllowNoCorrelation` — their XML docs amended, doc-only, to cover message handlers; proposed at the gate under the standing pre-release amendment rule) and binds a **Messaging-owned** `MessageCorrelationOptions` to `Cloudstrap:Correlation:Message:{RequireForAllMessageHandlers, ExcludeMessageHandlers}` — symmetric with #2's shipped `Request:` block, zero shipped-code changes. | One attribute vocabulary for one concept; configuration sections are not owned by classes, so the Messaging package binds its own type to the sibling path. Rejected: `Cloudstrap:Messaging:Correlation:*` (asymmetric) and duplicate messaging-specific attributes. |
