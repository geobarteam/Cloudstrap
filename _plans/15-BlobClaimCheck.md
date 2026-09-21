# Plan: 15-BlobClaimCheck — A consumer adds `.UseAzureBlobClaimCheck()` to the #14 builder and any message whose serialized body exceeds a size threshold is stored transparently in a dedicated Azure Blob Storage container while only Wolverine's reference travels through the transport — the handler sees the whole message, contracts stay dependency-free, a missing blob dead-letters immediately, and the demo Api → Worker order flow proves it end to end on Azurite

## Overview

Deliverable #15 of the extraction roadmap: the new **`Cloudstrap.Messaging.AzureBlob`** leaf package over
`Cloudstrap.Messaging` (#14). **Binding spec: `_specs/15-BlobClaimCheck.md`** (APPROVED 2026-09-21, zero
Open Questions, Decision Log DL-1…DL-10 final). Its Port Decision Table (0 Port · 4 Redesign · 5 Replace ·
9 Drop), Public API Sketch, Behaviors & Conventions, Dependencies table, Deliberate Behavior Changes 1–8,
Out-of-Scope list and the two **Planner notes** are authoritative and are not re-litigated here.

This is a **rebuild against an observed contract**, and — per the spec's headline finding (DL-5) — the
package is a **seam, not middleware**: Wolverine ships the claim check (an `IMessageSerializer` decorator
with a pluggable `IClaimCheckStore`, a whole-body `AutoOffloadPayloadsLargerThan` threshold, and an Azure
Blob store in `WolverineFx.AzureBlobStorage`). Cloudstrap owns exactly: one builder call, one options
block, the client-resolution ladder, one failure rule, one posture log line, tripwires and documentation.
**Nothing the spec marked Drop or Out-of-Scope appears in this plan**: no bespoke offload
middleware/serializer/envelope rule, no `Enable*` flag, no `*DataBus` property convention, no encryption,
no leaf-owned account settings (`BlobServiceUri`/`ConnectionString`), no sweep/TTL/delete-after-handling,
no `WolverineFx.ClaimCheck.AzureBlobStorage`, no `Cloudstrap.Extensions` reference, no Testcontainers, no
`InternalsVisibleTo` grant to the leaf.

Reference patterns, all read before planning:

- **Plan-shape precedent: `_plans/14-Messaging.md`** — the package this leaf extends; brand-new-package
  RED mechanics, `PackageSurfaceTests` permanent guards, packaging step, LocalDB D-3 tests, demo + E2E
  demonstration slice, gates at slice boundaries only, "Wolverine-API caveat" handling.
  `_plans/25-WasmTestProjectSut.md` / `_plans/27-DemoAppsRestructure.md` — the demonstration-slice
  convention (rule 15).
- **Shipped #14 surfaces this plan amends or consumes (read on disk)**:
  `src/Cloudstrap.Messaging/CloudstrapMessagingBuilder.cs` (public sealed; `HostBuilder` public,
  `State` internal; `UseSqlServer`, `AddCloudstrapTransactionalMessaging<T>`),
  `MessagingRegistrationState.cs` (internal; carries `Wolverine`, `DurabilityProvider`,
  `TransactionalDbContexts`, `MessageStore`), `CloudstrapMessagingExtension.cs` (the `IWolverineExtension`
  tail: correlation rule → `CorrelationRequiredException → MoveToErrorQueue()` → consumer's `Wolverine`
  delegate → `RetryLadder.Apply` **last**), `RetryLadder.cs` (`OnException<Exception>()` ladder),
  `HostApplicationBuilderExtensions.cs` (mechanic (c) duplicate-call fail-fast via a marker service, eager
  bind+validate, `AddWolverineExtension<CloudstrapMessagingExtension>()`),
  `CloudstrapMessagingOptionsValidator.cs` (annotations validator + hand-written key-naming rules),
  `MessagingStartupSummaryLogger.cs` (`[LoggerMessage]` posture line, names not values),
  `Cloudstrap.Messaging.csproj`, `README.md`; tests `PackageSurfaceTests.cs` (approved-type list, closure
  prefixes, dropped concepts), `RegistrationTests.cs`, `SqlServerDurabilityTests.cs` (the **two-host SQL
  transport pattern** with `DisableConventionalLocalRouting()` on the sender),
  `TransactionalMessagingTests.cs` (the AC-MSG8 crash-recovery pattern), `Infrastructure/
  {MessagingTestHost, SqlServerTestDatabase, CapturingLoggerProvider}.cs`, `Fixtures/InvocationRecorder.cs`.
- **#4 blob registration the ladder consumes (read on disk)**:
  `src/Cloudstrap.Extensions/HostApplicationBuilderExtensions.cs` `AddCloudstrapBlobStorage` —
  `TryAddSingleton<BlobContainerClient>` built by `BlobStorageRegistration.CreateClient` from
  `Cloudstrap:Storage` (`BlobServiceUri` + credential, or `ConnectionString` /
  `ConnectionStrings:CloudstrapStorage` incl. `UseDevelopmentStorage=true`; container = lower-cased
  `SystemName` by default; client construction contacts nothing). `AzureCredentialSettings.cs` — the
  code-hook idiom `AzureBlobClaimCheckSettings` mirrors. `StorageOptionsValidator.cs` — the naming-the-key
  validator idiom. `Cloudstrap.Extensions.Tests/AddCloudstrapBlobStorageTests.cs` — offline client
  assertions (`client.Name`, `client.AccountName == "devstoreaccount1"`, `client.Uri`).
- **Wolverine 6.31.0 claim-check API (read from the local package's `Wolverine.xml`)**:
  `Wolverine.Persistence.WolverineOptionsClaimCheckExtensions.UseClaimCheck(WolverineOptions,
  Action<ClaimCheckConfiguration>)`; `Wolverine.Persistence.ClaimCheckConfiguration` —
  `AutoOffloadPayloadsLargerThan(long)`, `AutoOffloadThreshold`, `Store` (the active `IClaimCheckStore`;
  file-system default when unset), `StoreForMessage<T>(store, threshold?)`, `StoreForMessages(...)`,
  `StoreWhen(...)`, `UseFileSystem(path)`, `DeletePayloadsOlderThan` (**not used** — DL-2; the Azure store
  deliberately does not implement `IClaimCheckStoreWithExpiration`); `IClaimCheckStore`
  (`StoreAsync`/`LoadAsync`/`DeleteAsync`); `ClaimCheckToken(Id, ContentType, Length)`; headers
  `claim-check.$body` (whole-body offload), `claim-check.{property}`, `claim-check.$store`; the decorator
  restores the body from the store **before** the inner serializer deserializes on receive, and best-effort
  deletes uploads of a send that then failed; `[Blob]` = `Wolverine.Persistence.BlobAttribute`.
  Failure rules: `Wolverine.ErrorHandling.ErrorHandlingPolicyExtensions.OnException<T>(IWithFailurePolicies,
  Func<T,bool> filter, string description)` + `IFailureActions.MoveToErrorQueue()`. **Not present in
  6.31.0**: any claim-check-specific exception type — the store's own exception surfaces (Azure SDK
  `RequestFailedException`). `WolverineFx.AzureBlobStorage` itself is **not** in the local cache; its
  `cc.UseAzureBlobStorage(BlobContainerClient)` / `UseAzureBlobStorage(connectionString, containerName)`
  shape is the spec's 2026-09-03 evidence, verified at pin time (mechanic (d)).
- **Demonstration vehicles + harness (read on disk)**: `src/demo/Api/{Program.cs, Controllers/
  OrdersController.cs, Data/DemoDbContext.cs, appsettings.json, Cloudstrap.Demo.Api.csproj, README.md}`,
  `src/demo/Worker/{Program.cs, PlaceOrderCommandHandler.cs, Data/WorkerDbContext.cs, appsettings.json,
  Cloudstrap.Demo.Worker.csproj, README.md}`, `src/demo/Shared/Contracts/{PlaceOrderCommand.cs,
  OrderDtos.cs}`, `src/demo/README.md` (port map, harness notes), `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/
  {E2eFixture.cs, MessagingTests.cs, WorkerHostTests.cs, Infrastructure/SutProcess.cs, AssemblyInfo.cs
  (NonParallelizable), Cloudstrap.Demo.E2E.Tests.csproj}`, `.github/workflows/ci.yml` (the D-3
  `services: sqlserver` + `CLOUDSTRAP_TEST_SQL` shape of 7596f85 / 1c552b7), `src/Directory.Packages.props`
  (five `WolverineFx.*` 6.31.0 pins; `Azure.Storage.Blobs` 12.29.1). `Cloudstrap.WebApi` and
  `Cloudstrap.Worker` both already reference `Cloudstrap.Extensions`, so both demo hosts reach
  `AddCloudstrapBlobStorage` with no new project reference.

This is a library deliverable with **no database project** — every "DB changes" section below is
"none" (the only SQL artifacts are the demo's `demo.Orders` columns, created at runtime by the demo hosts'
`EnsureCreated`, and Wolverine's own auto-provisioned tables).

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-M4 (above threshold → blob + reference, transparent read) *(verbatim carry)* | 4 (LocalDB, fake client) · 8 (live on Azurite) |
| AC-M3 (default suite: no live storage account, no network) | 3 (pure in-process) · 4–6 are the D-3 LocalDB exception with a **no-network** blob double |
| AC-A3 / AC-ASP2 (zero `Nihdi.AspNetCore` / zero `Aspire.*`) | 7 (permanent guards) |
| AC-CK1 (above threshold: exactly one blob, reference header, stripped wire body) | 4 · 8 |
| AC-CK2 (at/below threshold: no blob, no header, body unchanged) | 4 · 8 |
| AC-CK3 (handler + contracts reference no leaf/Wolverine-claim-check/Azure types) | 4 (reflection tripwire) · 8 (`PlaceOrderCommand` stays zero-reference) |
| AC-CK4 (404 → dead-letter without retries, type+id+container logged, never payload/connection string; other storage failures ride the ladder) | 5 |
| AC-CK5 (retries re-read the store; one side effect; blob retained) | 4 |
| AC-CK6 (correlation unchanged; OTel additive; no exporter/provider) | 4 |
| AC-CK7 (no leaf call → everything travels whole; no blob client resolved; Messaging's closure has no leaf types) | 3 · 7 (#14's closure guard) |
| AC-CK8 (client ladder d > c > b; (a) fails naming both routes; no second client) | 3 |
| AC-CK9 (threshold ≤ 0 / invalid container name → startup fails naming the key) | 3 |
| AC-CK10 (one posture line: container, threshold, client source — no URI/connection string) | 3 · 8 (live) |
| AC-CK11 (build/tests/format, XML docs, metadata + README, closure exactly Messaging + AzureBlobStorage, identifier sweep, surface tripwire) | 7 |
| AC-CK12 (demo Api → Worker with above-threshold `Notes`; length + SHA-256 recorded; exactly one new blob; below-threshold adds none; pre-existing E2E green) | 8 |
| AC-CK13 (durable outbox recovers an offloaded command after a crash) | 6 |
| AC-CK14 (second `UseAzureBlobClaimCheck()` fails fast naming the duplicate) | 3 |
| DL-7 (family pin bump, lockstep, ≥ 6.33.0; #14 suites as regression gate) | 1 |
| DL-8 (`ConfigureEngine` seam: contributions before consumer delegate and before the ladder) | 2 |

### Dependency closure — ⚠️ dependency update to a shipped package (risk area, reviewed at Gate 1)

`src/Directory.Packages.props` changes (Step 1), each with the repo's license/justification comment:

- **Family bump in lockstep (DL-7)**: `WolverineFx`, `WolverineFx.AzureServiceBus`, `WolverineFx.SqlServer`,
  `WolverineFx.EntityFrameworkCore`, `WolverineFx.RuntimeCompilation` — from 6.31.0 to the **current latest
  stable** (floor **6.33.0**, the first release carrying the consolidated `WolverineFx.AzureBlobStorage`);
  see mechanic (m) for the pin-time procedure and the pin record.
- **New shipped-closure pin**: `WolverineFx.AzureBlobStorage` at the same version (MIT, JasperFx; brings
  `Azure.Storage.Blobs ≥ 12.27.0` — the suite's 12.29.1 pin satisfies it). `WolverineFx.ClaimCheck.
  AzureBlobStorage` is **never** referenced (superseded per the 6.32.0 notes).
- **No new pins otherwise**: the E2E project's new `Azure.Storage.Blobs` reference uses the existing
  12.29.1 pin (test/demo only — lists the Azurite container). No `Azure.Identity` in the leaf (finding 9),
  no `Testcontainers.*` (DL-10), no `Aspire.*`, `NServiceBus.*`, `Nihdi.*`.

Leaf project references: `Cloudstrap.Messaging` **only** (DL-3) — `Cloudstrap.Core` arrives transitively
for `ApplicationOptions.SystemName`. Leaf package reference: `WolverineFx.AzureBlobStorage` only. The
closure is therefore exactly `Cloudstrap.Messaging` + `WolverineFx.AzureBlobStorage` and their disclosed
transitives (AC-CK11) — made permanent by Step 7's guards.

### ⚠️ Risk areas (reviewed at the gates named)

- **Dependency update to shipped #14 (DL-7)** — the five-pin family bump + one new pin; the **full #14
  unit suite (`Cloudstrap.Messaging.Tests`, incl. its LocalDB tests) and the full E2E suite are the
  regression gate**; Weasel 9.30.0's schema comparison (6.33.0 notes) may `ALTER TABLE` persistent dev
  databases (`CloudstrapDemo`) at AutoProvision `CreateOrUpdate` — observed and recorded: **Gate 1**.
- **Shipped public API amendment to #14 (DL-8)** — `CloudstrapMessagingBuilder.ConfigureEngine(
  Action<IServiceProvider, WolverineOptions>)`, additive member on an existing sealed type; the ordering
  contract (contributions → consumer's `Wolverine` delegate → `RetryLadder`) is a one-way door leaf packages
  (this one, the future PostgreSQL leaf) build on: **Gate 1**.
- **All-new public API surface / one-way doors (spec sketch)** — `UseAzureBlobClaimCheck` name/shape,
  `AzureBlobClaimCheckOptions` keys under `Cloudstrap:Messaging:ClaimCheck`, `AzureBlobClaimCheckSettings`
  hooks; reviewed verbatim against the sketch at **Gate 2**, frozen by Step 7's guards at **Gate 3**.
- **Cloud-resource naming one-way door (DL-9)** — the default container `{SystemName}-claimcheck`
  (lower-cased) is a resource name that outlives code: **Gate 2**.
- **Failure/lifecycle semantics (DL-1/DL-2)** — 404-only immediate dead-letter, retention + lifecycle
  policy documentation: **Gate 3**.
- **E2E/CI infrastructure (DL-10)** — Azurite becomes an E2E prerequisite next to LocalDB (fixture-started
  by default; `CLOUDSTRAP_TEST_BLOB` override; CI service container): **final gate**.
- **Demo contract change** — `PlaceOrderCommand` gains `Notes`, `demo.Orders` gains two columns (demo-only
  `ALTER TABLE ... ADD` on a persistent dev database): **final gate**.

### Planner mechanics decided here (each reviewed at the named gate)

**(a) New-package RED mechanics.** For the first step of the new package (Step 3) the honest first
failure is the new test project failing to compile against missing types, followed by real red runs once
the types exist (the #11–#14 precedent). Every other step has ordinary red runs.

**(b) The options pipeline (the #1/#14 pattern).** `AzureBlobClaimCheckOptions` (`SectionName =
"Cloudstrap:Messaging:ClaimCheck"`, `OffloadThresholdBytes : long = 204_800`, `ContainerName : string?`)
is bound **eagerly at the `UseAzureBlobClaimCheck` call** (`configuration.GetSection(...).Get<...>() ??
new()`, then validated — the #14 fail-fast convention) **and** via `services.AddOptions<...>().Bind(...)
.ValidateOnStart()` with an internal `IValidateOptions<AzureBlobClaimCheckOptions>` registered through
`TryAddEnumerable`. The validator = a source-generated `[OptionsValidator]` partial
(`AzureBlobClaimCheckOptionsAnnotationsValidator`; `[Range(1, long.MaxValue)]` on the threshold) + a
hand-written rule on the **effective** container name (the configured value, else the lower-cased
`{SystemName}-claimcheck`) against Azure's container naming rules (3–63 chars, `[a-z0-9-]`, starts and
ends alphanumeric, no consecutive hyphens). Every failure names the exact key
(`'Cloudstrap:Messaging:ClaimCheck:OffloadThresholdBytes'` / `'...:ContainerName'`) and never echoes a
value (AC-CK9). Binding `Cloudstrap:Messaging` to #14's `CloudstrapMessagingOptions` ignores the unknown
`ClaimCheck` subsection (the binder's default) — the D-5 sibling-path posture, **no shipped
`CloudstrapMessagingOptions` change**.

**(c) One claim check per node, fail fast (AC-CK14).** `UseAzureBlobClaimCheck` registers an internal
marker `AzureBlobClaimCheckRegistrationState` (carrying the bound options, the settings and the
`ClaimCheckClientResolution` filled at engine bootstrap) in `builder.HostBuilder.Services`; a second call
finds the descriptor and throws `InvalidOperationException` naming `UseAzureBlobClaimCheck` **at the call
site** — #14's mechanic (c) applied to the leaf, no `InternalsVisibleTo` needed.

**(d) The engine contribution (DL-8 made concrete).** `CloudstrapMessagingBuilder.ConfigureEngine(
Action<IServiceProvider, WolverineOptions> contribution)` appends to a new
`MessagingRegistrationState.EngineContributions : List<Action<IServiceProvider, WolverineOptions>>`;
`CloudstrapMessagingExtension` gains an `IServiceProvider` constructor dependency and applies the list **in
registration order, after** the correlation rule and **before** the consumer's `Wolverine` delegate and
`RetryLadder.Apply` (so a contribution's failure rules precede the catch-all ladder — finding 5 — and the
consumer keeps final say). Not idempotent by design (each call appends). The leaf's contribution, in
order: (1) resolve the container client via the ladder (mechanic (e)) — fail fast naming both routes;
(2) `options.UseClaimCheck(cc => { cc.UseAzureBlobStorage(container); cc.AutoOffloadPayloadsLargerThan(
threshold); settings.ClaimCheck?.Invoke(cc); })`; (3) the DL-1 rule
`options.Policies.OnException<RequestFailedException>(ex => ex.Status == 404, "claim-check payload
missing").MoveToErrorQueue()`; (4) the posture line (mechanic (f)). ⚠️ *Wolverine-API caveat (the #14
mechanic (d) caveat, carried)*: the behavioral contract is fixed by the spec; the 6.31.0 names above are
verified from the local package; `WolverineFx.AzureBlobStorage`'s `UseAzureBlobStorage(BlobContainerClient)`
overload, whether `UseClaimCheck` is safe inside an `IWolverineExtension` (it registers a service only
when a TTL is configured — never here), and the exact exception the Azure store lets escape on a missing
blob (`RequestFailedException` with `Status == 404`, possibly wrapped) are **verified against the pinned
version's source at implementation time**; the executor reports any behavioral gap at the covering gate
instead of bending an AC. If the store wraps the SDK exception, the DL-1 filter matches the inner
exception (`ex => (ex as RequestFailedException ?? ex.InnerException as RequestFailedException)?.Status
== 404`) — still one rule, still before the ladder.

**(e) The client ladder (DL-9, AC-CK8).** Internal `ClaimCheckClientResolver.Resolve(IServiceProvider,
AzureBlobClaimCheckSettings, string containerName) : ClaimCheckClientResolution` — an internal record
`(BlobContainerClient Container, ClaimCheckClientSource Source)` with internal enum `Code |
BlobServiceClient | BlobContainerClient`. Order: `settings.ContainerClient` (source `code`; the
`ContainerName` setting is ignored — stated in the posture line) → `services.GetService<
BlobServiceClient>()` (consumer/Aspire) `.GetBlobContainerClient(containerName)` → `services.GetService<
BlobContainerClient>()` (#4's registration) `.GetParentBlobServiceClient().GetBlobContainerClient(
containerName)` (`Azure.Storage.Blobs.Specialized.SpecializedBlobExtensions`) → none → `InvalidOperation
Exception` naming `AddCloudstrapBlobStorage` and "register a `BlobServiceClient` or `BlobContainerClient`".
No credential is constructed anywhere in the leaf; no second client against another account is built.
Log labels: `code`, `BlobServiceClient`, `AddCloudstrapBlobStorage`.

**(f) The posture line (AC-CK10).** Logged **inside the contribution**, once, via `ILoggerFactory` from
the provided `IServiceProvider` (category `Cloudstrap.Messaging.AzureBlob`), by an internal static partial
`ClaimCheckPostureLogger` with one `[LoggerMessage]`: `Cloudstrap claim check: container '{Container}',
offload bodies larger than {ThresholdBytes} bytes, client from {ClientSource}` (+ "; ContainerName setting
ignored" when the source is `code`). No account URI, no connection string, ever — a tripwire test asserts
`devstoreaccount1`/`UseDevelopmentStorage` never appear.

**(g) No-network blob double (spec test strategy).** The leaf test project ships
`Fixtures/InMemoryBlobContainerClient : BlobContainerClient` (the SDK's protected mocking constructor)
overriding `GetBlobClient(string)` → `InMemoryBlobClient : BlobClient` whose upload / download / exists /
delete overloads read and write a shared `ConcurrentDictionary<string, byte[]>`; `CreateIfNotExists(Async)`
is a no-op; counters `Uploads`, `Downloads`; a `FailDownloadsWith(Exception)` switch (for `new
RequestFailedException(404, ...)` and a 503). **Executor latitude, reported at Gate 2**: the exact
overloads Wolverine's Azure store calls are read from the pinned package's source at GREEN time; if the
double proves brittle, the round-trip assertions fall back to a recording `IClaimCheckStore` assigned
through `settings.ClaimCheck = cc => cc.Store = fake` (the spec's sanctioned fallback), while the ladder
tests (Step 3) stay client-level and offline either way. The **real** Azure store is exercised only in
Step 8 on Azurite.

**(h) SQL test harness (D-3).** The leaf test project carries its own copies of #14's
`Infrastructure/{SqlServerTestDatabase, CapturingLoggerProvider, MessagingTestHost}.cs` and
`Fixtures/InvocationRecorder.cs` (database name `CloudstrapClaimCheckTests`; `CLOUDSTRAP_TEST_SQL`
honored) — `Cloudstrap.Testing` does not exist yet; hoisting the helpers into it is a recorded follow-up.
The LocalDB tests (Steps 4–6) **run by default** (D-3 rejects skip-when-absent) and depend on neither
`CLOUDSTRAP_TEST_BLOB` nor Azurite (Planner note 2, AC-M3).

**(i) Two-host SQL-transport pattern (why the round trip is not a local-transport test).** Wolverine's
buffered local queues hand the message object over in memory with **no serialization**, so the serializer
decorator — and with it the offload — never runs (the spec's Behaviors table states this). The behavioral
round trip therefore mirrors #14's `SqlTransport_TwoHostsOnOneDatabase_CommandCrossesFromSenderToListener`
exactly: two in-process hosts, distinct workloads (`contoso-orders-api` sender, `contoso-orders-worker`
listener), `Transport = SqlServer`, shared explicit `SqlTransport:SchemaName`, `Destinations` routing the
fixture contracts namespace to the listener's queue, `DisableConventionalLocalRouting()` on the sender,
`Durability.Mode = Solo`, AutoProvision on, **the same `InMemoryBlobContainerClient` instance supplied to
both hosts via `settings.ContainerClient`** (one "account", two nodes).

**(j) Full-suite check** (standing convention: `runTests` is not on the agent PATH — VERIFY invokes each
exe directly): `dotnet build src/Cloudstrap.sln`, then the **15** unit exes under
`src/Test/UnitTest/<Name>.Tests/bin/Debug/net10.0/<Name>.Tests.exe` (the 14 of #14's mechanic (j) plus
**Messaging.AzureBlob** — new in Step 3), then the E2E exe
`src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe`, then
`dotnet format src/Cloudstrap.sln --verify-no-changes`.

**(k) Demo topology (AC-CK12, DL-10 made concrete; final gate).** `PlaceOrderCommand(Guid OrderId,
string? Notes = null)` — the large payload rides the **command** (zero-reference record; teaching
comment: the threshold, not a naming convention, decides). `PlaceOrderDto(string Description, string?
Notes = null)`; `OrderDto` gains `NotesLength : int?`, `NotesSha256 : string?`. Both DbContexts' `Order`
gain the two columns; both `EnsureCreated` methods add `IF COL_LENGTH('demo.Orders','NotesLength') IS NULL
ALTER TABLE demo.Orders ADD NotesLength int NULL, NotesSha256 nvarchar(64) NULL;` (the persistent
`CloudstrapDemo` dev database already has the table). The Worker's handler records `Notes.Length` and the
hex SHA-256 of the UTF-8 notes — **never stores or logs the notes**. Both hosts: `builder.AddCloudstrap
BlobStorage();` before `AddCloudstrapMessaging()...UseAzureBlobClaimCheck()`; `appsettings.json`:
`Cloudstrap:Storage:ConnectionString = "UseDevelopmentStorage=true"` (explicit demo config) and a
commented `Cloudstrap:Messaging:ClaimCheck` block **left at defaults** (container `demo-claimcheck`,
threshold 204 800 — sender and receiver agree by convention, the teaching point). E2E: a new
`ClaimCheckTests` fixture boots its own Worker on health port **5352** (5350 `WorkerHostTests`, 5351
`MessagingTests`), posts orders with 300 000-character and 1 024-character `Notes`, and counts blobs in
`demo-claimcheck` through `Azure.Storage.Blobs` (a missing container counts as zero).

**(l) Azurite + `CLOUDSTRAP_TEST_BLOB` (Planner note 2 mirrored on 7596f85 / 1c552b7).** New
`Infrastructure/AzuriteProcess.cs` in the E2E project: when `CLOUDSTRAP_TEST_BLOB` is **unset**, locate
`azurite-blob` on PATH (probing `PATHEXT` on Windows for the npm `.cmd` shim — the blob-only binary ships
in the same `azurite` npm package and avoids the queue/table ports), start it with `--silent --location
<per-run temp dir> --blobHost 127.0.0.1 --blobPort 10000`, fail loudly with `npm install -g azurite` when
missing (the Playwright-install precedent: never a silent skip), stop + delete the temp dir at teardown;
when **set**, start nothing (attach mode). In **both** modes the fixture polls readiness with
`new BlobServiceClient(connectionString).GetPropertiesAsync()` (60 s deadline) **before** the Api boots.
`E2eFixture.BlobConnectionString` exposes the effective value (`CLOUDSTRAP_TEST_BLOB` or
`UseDevelopmentStorage=true`); `E2eFixture.CapturedApiOutput` exposes the Api's stdout. Every host spawn
point 1c552b7 touched forwards `--Cloudstrap:Storage:ConnectionString=<value>` when the variable is set:
`E2eFixture` (Api), `MessagingTests`, `WorkerHostTests` and the new `ClaimCheckTests` (Worker). CI:
`services: azurite: image: mcr.microsoft.com/azure-storage/azurite`, `ports: 10000:10000` (the image's
default command already binds `0.0.0.0`), preferred health check `nc -z 127.0.0.1 10000` (dropped in favor
of the fixture's readiness poll if the image lacks `nc`), and `CLOUDSTRAP_TEST_BLOB:
"UseDevelopmentStorage=true"` on the **"Run all MTP test executables"** step (the mapped port matches
Azurite's default). Fallback if the service container proves awkward: an `npm install -g azurite` step and
a background `azurite-blob` start.

**(m) The pin-time re-check (Planner note 1) — an explicit executor action inside Step 1.** *The planning
session had no network access; only 6.31.0 exists in the local NuGet cache, so the latest-stable version
cannot be recorded here.* Before editing `src/Directory.Packages.props` the executor: (1) runs
`dotnet package search WolverineFx --exact-match --take 1` (or reads nuget.org / the GitHub releases page)
and records the **current latest stable** `WolverineFx` version — **≥ 6.33.0 is the floor, not the
target**; (2) confirms `WolverineFx.AzureBlobStorage` exists at exactly that version (else pins the family
to the newest version at which all six packages exist together); (3) reads the release notes from 6.32.0
to the pinned version for anything touching #14's surface — SQL Server durability schema / Weasel, the EF
Core outbox, the ASB transport, failure-rule APIs, `ClaimCheckConfiguration` and the `UseAzureBlobStorage`
overloads — and (4) fills the **Pin record** below before GREEN. The #14 unit + E2E suites are the
regression gate.

#### Pin record *(filled by the executor in Step 1 — not a planning-time value)*

| Item | Value |
|---|---|
| Pinned `WolverineFx.*` family version (six packages, lockstep) | **6.39.1** — `WolverineFx`, `.AzureServiceBus`, `.SqlServer`, `.EntityFrameworkCore`, `.RuntimeCompilation`, `.AzureBlobStorage` all exist at exactly this version (the latest stable; released 2026-09-19). `WolverineFx.ClaimCheck.AzureBlobStorage` stops at 6.31.0 — superseded, never referenced. |
| Verified on | 2026-09-21 — nuget.org v3 flat-container index per package (`dotnet package search WolverineFx --exact-match` agreed) + the JasperFx/wolverine GitHub releases page (tags `V6.32.0` … `V6.39.1`). The 6.39.1 nuspec: MIT; depends on `WolverineFx 6.39.1` + `Azure.Storage.Blobs ≥ 12.27.0`. |
| Release notes read (6.32.0 → pinned) — items touching #14 | 6.32.0: `WolverineFx.AzureBlobStorage` introduced (claim-check store folded in); shutting-down node no longer dead-letters unrun work; scheduled promotion matches whole identity on SQL Server. 6.33.0: **Weasel 9.30.0** — schema differ compares character lengths both ways, corrects existing tables in place (`ALTER TABLE`). 6.34.0: three partial indexes on SQL Server durability tables (new schema objects at AutoProvision); batched durability commands chunked under SQL Server's 2100-parameter limit; ASB gains a configurable system-queue prefix + transport-wide default DLQ name (additive, not adopted); `ResourceMigrationFailureMode` for broker setup (additive); OTel executing span now covers the full async handler; new public type `Wolverine.Runtime.Agents.AgentUri` (CS0104 only if a consumer declares its own `AgentUri` — Cloudstrap does not). 6.36.0: `CircuitBreaker()` on a *buffered* local queue now fails startup — #14 never calls `CircuitBreaker`; each retry attempt's span links to the previous attempt (additive). 6.37.0: **breaking** `ServiceCapabilities.EventModel` type change — not referenced by #14; JasperFx 2.69.3 / Weasel 9.32.0. 6.38.0: `WolverineOptions.ServiceName` now also propagates to `JasperFxOptions.ServiceName` (#14 sets `ServiceName` — benign). 6.39.0: SQL Server tenant message stores named after their database (multi-tenancy only). 6.39.1: `MoveToErrorQueue` settles exactly once on ASB (fixes redundant settle; benign for #14's dead-letter rules); `codegen test` fix. **No API #14 calls (`PersistMessagesWithSqlServer`, `AddDbContextWithWolverineIntegration`, `UseEntityFrameworkCoreTransactions`, `OnException/RetryTimes/ScheduleRetry/MoveToErrorQueue`, `DisableConventionalLocalRouting`, ASB setup) was renamed or changed — no C# adaptation needed.** |
| `UseAzureBlobStorage` overloads confirmed | Read from `src/Persistence/Wolverine.AzureBlobStorage/ClaimCheck/AzureBlobClaimCheckExtensions.cs` at tag `V6.39.1`, namespace `Wolverine.ClaimCheck.AzureBlobStorage`: `ClaimCheckConfiguration UseAzureBlobStorage(this ClaimCheckConfiguration, BlobContainerClient containerClient)` (sets `config.Store = new AzureBlobClaimCheckStore(containerClient)`) and `UseAzureBlobStorage(this ClaimCheckConfiguration, string connectionString, string containerName)`. The store (`public class AzureBlobClaimCheckStore : IClaimCheckStore`) exposes `ContainerClient`; it does **not** implement `IClaimCheckStoreWithExpiration` (DL-2 holds). `ClaimCheckConfiguration` at `V6.39.1` still has `AutoOffloadPayloadsLargerThan(long)`, `AutoOffloadThreshold`, `Store`, `StoreForMessage<T>`, `StoreForMessages`, `StoreWhen`, `UseFileSystem`, `DeletePayloadsOlderThan`. |
| Missing-blob exception surface confirmed | **Unwrapped.** `AzureBlobClaimCheckStore.LoadAsync` calls `BlobClient.DownloadContentAsync` with no try/catch, and the core `ClaimCheckMessageSerializer.restoreOffloadedBodyAsync` (`src/Wolverine/Persistence/ClaimCheck/Internal/ClaimCheckMessageSerializer.cs` at `V6.39.1`) awaits `store.LoadAsync` with no try/catch either — so a missing blob surfaces to Wolverine's failure policies as the Azure SDK's `RequestFailedException` with `Status == 404`, unwrapped. The DL-1 rule can match `ex => ex.Status == 404` directly; the inner-exception variant is not needed (kept as a defensive fallback only). The store's `StoreAsync` uses `BlobClient.UploadAsync(BinaryData, BlobUploadOptions, CancellationToken)` and `DeleteAsync` uses `DeleteIfExistsAsync` — the overloads the Step 4 in-memory double must implement (mechanic (g)). |
| Weasel schema comparison observed on `CloudstrapDemo` | **Schema corrected in place, no recovery needed.** On the first E2E run on 6.39.1 (2026-09-21) `sys.tables.modify_date` shows Weasel touched `wolverine_incoming_envelopes`, `wolverine_outgoing_envelopes`, `wolverine_nodes`, `wolverine_node_records`, `wolverine_control_queue` and `wolverine_agent_restrictions` in both `demo_application_api` and `demo_application_worker` (last modified 2026-09-03 before the run); the 6.34.0 filtered indexes (`idx_wolverine_*_recover`, `_owner`, `_keep_until`, `_scheduled`, `idx_wolverine_dead_letters_replayable`) are present. `wolverine_dead_letters`, `wolverine_node_assignments` and the `demo_transport` queue tables were untouched. The whole E2E suite (60 tests) passed on that run; `CloudstrapDemo` was not dropped. |

**Target consumer composition** (the spec made concrete — also the demo `Program.cs` files, Step 8, and
the package README, Step 7):

```csharp
// Any host type — the blob registration (#4) supplies account + credential; the leaf adds the seam.
builder.AddCloudstrapBlobStorage();                         // Cloudstrap:Storage → BlobContainerClient (DefaultAzureCredential)

builder.AddCloudstrapMessaging()                            // Cloudstrap:Messaging (#14)
    .UseSqlServer()                                         // durable inbox/outbox (#14)
    .AddCloudstrapTransactionalMessaging<DemoDbContext>()   // outbox atomicity (#14)
    .UseAzureBlobClaimCheck();                              // Cloudstrap:Messaging:ClaimCheck — bodies > 200 KiB → {system}-claimcheck

// Code-level hooks (the AzureCredentialSettings idiom):
//   .UseAzureBlobClaimCheck(s => { s.ContainerClient = myClient;                  // wins over DI (AC-CK8d)
//                                  s.ClaimCheck = cc => cc.StoreForMessage<ExportReadyEvent>(otherStore, 64 * 1024); })
```

---

## Slice 1 — The engine family moves to the release that carries the Azure claim-check store, and the shipped #14 builder gains the leaf-extension door ⚠️ DEPENDENCY-UPDATE + SHIPPED-PUBLIC-API RISK AREA

---

## Step 1 — The node runs on the current Wolverine release: all five `WolverineFx.*` pins move in lockstep to the latest stable (≥ 6.33.0) and `WolverineFx.AzureBlobStorage` is pinned alongside them; a permanent lockstep tripwire guards the family; the whole #14 unit + E2E suite stays green on the new engine (DL-7; Planner note 1; mechanic (m)) ⚠️ dependency update to a shipped package

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Directory.Packages.props` *(modify)* — the five `WolverineFx.*` `PackageVersion` entries bumped to
  the pinned version (mechanic (m)); new `<PackageVersion Include="WolverineFx.AzureBlobStorage"
  Version="<pinned>" />` in the same `ItemGroup` with the comment: MIT (JasperFx), the consolidated Azure
  Blob Storage package (claim-check store + document/saga persistence) that superseded
  `WolverineFx.ClaimCheck.AzureBlobStorage` in 6.32.0; brings `Azure.Storage.Blobs` (MIT; ≥ 12.27.0, the
  suite pins 12.29.1); referenced by `Cloudstrap.Messaging.AzureBlob` (deliverable #15); the family comment
  updated with the pin date/source and the Weasel 9.30.0 schema-comparison note (6.33.0).
- `_plans/15-BlobClaimCheck.md` *(modify)* — the **Pin record** table filled (mechanic (m)).
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/DependencyPinTests.cs` *(create)* — the permanent
  lockstep tripwire (reads `src/Directory.Packages.props` from the repo root, located by walking up to
  `src/Cloudstrap.sln` — the `SutProcess.FindRepoRoot` idiom).

**RED** *(write these tests first, run them, confirm they fail — at 6.31.0 the floor assertion and the
missing `WolverineFx.AzureBlobStorage` entry both fail)*:
- Unit test file: `DependencyPinTests.cs`
  - `WolverineFamily_AllPackageVersionsAreInLockstep` — every `PackageVersion` whose `Include` starts
    with `WolverineFx` shares one `Version` string.
  - `WolverineFamily_VersionIsAtLeastTheClaimCheckConsolidationRelease` — that version parses as
    `System.Version` and is ≥ 6.33.0.
  - `WolverineFamily_IncludesAzureBlobStorage_AndNeverTheSupersededClaimCheckPackage` —
    `WolverineFx.AzureBlobStorage` is pinned; no `Include` starts with `WolverineFx.ClaimCheck`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "DependencyPinTests"
  ```

**GREEN**: perform mechanic (m) (record the version and the checked notes in the Pin record), edit the
six pins, `dotnet restore src/Cloudstrap.sln`. No production C# changes are expected; if a release between
6.31.0 and the pin renamed or changed a Wolverine API #14 calls (`PersistMessagesWithSqlServer`,
`AddDbContextWithWolverineIntegration`, `UseEntityFrameworkCoreTransactions`, `OnException/RetryTimes/
ScheduleRetry/MoveToErrorQueue`, `DisableConventionalLocalRouting`, ASB transport setup), the minimal
adaptation lands here and is called out at Gate 1 as part of the risk review.

**DB changes**: none — Wolverine's auto-provisioning owns its tables. Weasel's stricter schema comparison
may issue `ALTER TABLE` statements against the persistent `CloudstrapDemo` LocalDB database on the first
E2E run (AutoProvision `CreateOrUpdate` in Development); `CloudstrapMessagingTests` is dropped and
recreated per fixture and is unaffected. Documented recovery if the comparison fails: drop
`CloudstrapDemo` (demo-only; recreated by `EnsureCreated`).

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. `DependencyPinTests` → all pass: the family is provably in lockstep at a release that carries the
   Azure claim-check store — a guard that did not exist before.
2. **The regression gate for the bump (DL-7)**: full-suite check (mechanic (j) minus the not-yet-existing
   AzureBlob exe) — **all 14 unit exes green, including every `Cloudstrap.Messaging.Tests` LocalDB test,
   and the full E2E suite green unchanged**; zero build warnings; `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → the `Cloudstrap.Messaging.*.nupkg` nuspec lists the five
   `WolverineFx.*` dependencies at the pinned version.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Leaf packages can shape the engine at bootstrap without touching #14's internals: `CloudstrapMessagingBuilder.ConfigureEngine(Action<IServiceProvider, WolverineOptions>)` registers contributions that run in order **before** the consumer's `Wolverine` delegate and **before** the retry ladder — so a contribution's exception-specific failure rule dead-letters without retries while the consumer keeps final say and the ladder still catches everything else (DL-8; mechanic (d)) ⚠️ shipped public API amendment

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging/CloudstrapMessagingBuilder.cs` *(modify — ⚠️ additive public member on the
  shipped sealed type)* — `public CloudstrapMessagingBuilder ConfigureEngine(Action<IServiceProvider,
  WolverineOptions> contribution)`: guard clause, `State.EngineContributions.Add(contribution)`, returns
  `this`. XML docs state: the leaf-extension door (this leaf and the future PostgreSQL leaf walk through
  it), the ordering contract, "not idempotent — each call appends", "the documented consumer door remains
  `configurator.Wolverine`, which runs after all contributions", and that Wolverine forbids service
  registrations at this point (register services on `HostBuilder.Services` at registration time instead).
- `src/Cloudstrap.Messaging/MessagingRegistrationState.cs` *(modify)* — `public List<Action<
  IServiceProvider, WolverineOptions>> EngineContributions { get; } = [];` with XML docs.
- `src/Cloudstrap.Messaging/CloudstrapMessagingExtension.cs` *(modify)* — constructor gains
  `IServiceProvider services` (guarded); in `Configure`, after the correlation rule and before
  `_state.Configurator.Wolverine?.Invoke(options)`: `foreach (contribution in _state.EngineContributions)
  contribution(_services, options);`. Class remarks updated with the new order.
- `src/Cloudstrap.Messaging/README.md` *(modify)* — a short "Extending the engine from a leaf package"
  section documenting `ConfigureEngine`, its ordering contract and the consumer door.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/EngineContributionTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/PackageSurfaceTests.cs` *(modify)* — a member-level guard
  for the builder (the spec: "member-level assertions are updated alongside").
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Fixtures/ContributionFixtures.cs` *(create)* — a fixture
  `DeterministicFailureCommand` + handler throwing `DeterministicFailureException` (test-local type) and a
  recording marker service.

**RED** *(local transport — no SQL, no network; `MessagingTestHost` + `InvocationRecorder` +
`CapturingLoggerProvider`)*:
- Unit test file: `EngineContributionTests.cs`
  - `ConfigureEngine_Contribution_RunsBeforeTheConsumersWolverineDelegate` — a contribution sets
    `options.ServiceName = "contribution"`, the consumer's `Wolverine` delegate sets `"consumer"`; the
    resolved `WolverineOptions.ServiceName` is `"consumer"` (final say preserved) and a recorded call order
    shows the contribution first.
  - `ConfigureEngine_ContributionsRun_InRegistrationOrder` — two contributions append to a shared list;
    the order is `[first, second]`.
  - `ConfigureEngine_ContributionReceivesTheHostsServiceProvider` — the contribution resolves a test
    singleton registered on `builder.Services` and records it (the leaf resolves its blob client here).
  - `ConfigureEngine_ContributionFailureRule_PrecedesTheRetryLadder` — the contribution adds
    `options.Policies.OnException<DeterministicFailureException>().MoveToErrorQueue()`; retries configured
    `NumberOfImmediate = 3`; the handler throws `DeterministicFailureException`: after a bounded wait the
    handler was invoked **exactly once** (no retries — the rule matched before the ladder), while a
    second fixture command throwing a plain `InvalidOperationException` is invoked 1 + 3 times (the ladder
    still catches everything else).
  - `ConfigureEngine_NullContribution_ThrowsArgumentNullException`.
- Unit test file: `PackageSurfaceTests.cs`
  - `CloudstrapMessagingBuilder_PublicMembers_AreExactlyTheApprovedSet` — the builder's public
    instance members are exactly `HostBuilder`, `UseSqlServer`, `AddCloudstrapTransactionalMessaging`,
    `ConfigureEngine` (the approved-type list itself is unchanged — DL-8's "no new public type").
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = EngineContributionTests does not compile against the missing method
  src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe --filter "EngineContributionTests|PackageSurfaceTests"
  ```

**GREEN**: the Scope items — a list, a loop, one public method; full XML docs.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a package outside `Cloudstrap.Messaging` can now contribute failure rules that
   precede the ladder — behavior that did not exist before; every pre-existing #14 test green (the
   correlation rule → contributions → consumer → ladder order changed nothing for existing consumers).
2. Full-suite check (mechanic (j) minus the AzureBlob exe) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the engine bump and the leaf-extension door *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **dependency update to shipped #14 (DL-7)** — the `Directory.Packages.props`
diff (six pins, one version), the filled **Pin record** (version, source, release notes read, anything
touching #14's surface, the Weasel schema-comparison observation on `CloudstrapDemo`), any API adaptation
Step 1 needed, and the evidence that the **full #14 unit + E2E suites** passed on the new engine ·
**shipped public API amendment (DL-8)** — `ConfigureEngine`'s exact signature and XML docs reviewed
verbatim against the spec's amended-surface sketch; the ordering contract (correlation rule →
contributions → consumer's `Wolverine` → `RetryLadder`) confirmed as the one-way door the PostgreSQL leaf
will reuse; `CloudstrapMessagingExtension`'s new `IServiceProvider` dependency.

- [ ] Behavioral verification: `DependencyPinTests` green (lockstep, ≥ 6.33.0, AzureBlobStorage pinned,
  no superseded package); all 14 unit exes + E2E green on the bumped engine (Step 1);
  `EngineContributionTests` show contribution-before-consumer, registration order, a usable service
  provider, and the exactly-once dead-letter of the contribution's rule versus 1 + 3 attempts for the
  ladder; the builder member guard green (Step 2).
- [ ] Code review: the props comments (license, justification, pin date/source, Weasel note); no
  `WolverineFx.ClaimCheck.*` anywhere; `ConfigureEngine` is additive, guarded, documented, non-idempotent by
  design; `MessagingRegistrationState`/`CloudstrapMessagingExtension` changes minimal; README section
  accurate.
- [ ] User approved — implementation may continue past this gate

---

## Slice 2 — One call turns the node into a large-message node: `UseAzureBlobClaimCheck()` binds and validates its block, resolves the blob client from what the host already registered, states its posture, refuses misconfiguration and duplicates — and large bodies leave the message, travel as a reference and come back whole ⚠️ PUBLIC-API + CLOUD-RESOURCE-NAMING ONE-WAY DOORS

---

## Step 3 — `UseAzureBlobClaimCheck()` registers the claim check without touching storage: the `Cloudstrap:Messaging:ClaimCheck` block binds with the 200 KiB default and the `{system}-claimcheck` container, invalid values fail startup naming the exact key, the blob client is resolved at host start through the fixed ladder (code > `BlobServiceClient` > `AddCloudstrapBlobStorage`'s container client) or the host fails naming both routes, one posture line states container/threshold/client source, a second call fails fast, and a node without the call resolves no blob client at all (AC-CK7; AC-CK8; AC-CK9; AC-CK10; AC-CK14; mechanics (a)–(f))

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging.AzureBlob/Cloudstrap.Messaging.AzureBlob.csproj` *(create)* —
  `Microsoft.NET.Sdk`, `net10.0`, `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`;
  `ProjectReference` → `..\Cloudstrap.Messaging\Cloudstrap.Messaging.csproj` **only** (DL-3);
  version-less `PackageReference Include="WolverineFx.AzureBlobStorage"` **only**;
  `<InternalsVisibleTo Include="Cloudstrap.Messaging.AzureBlob.Tests" />`. Description/tags/README metadata
  land in Step 7 (packable from day one — the #14 precedent).
- `src/Cloudstrap.Messaging.AzureBlob/AzureBlobClaimCheckOptions.cs` *(create)* — `public sealed`; the
  sketch verbatim: `const string SectionName = "Cloudstrap:Messaging:ClaimCheck"`,
  `OffloadThresholdBytes : long = 204_800` (`[Range(1, long.MaxValue)]`), `ContainerName : string?`. XML
  docs carry the defaults, the "strictly larger" rule and the ASB-256-KB headroom rationale, the
  dedicated-container rationale (lifecycle-policy safety) and the "pointing it at
  `Cloudstrap:Storage:ContainerName` is allowed — you own the lifecycle risk" note.
- `src/Cloudstrap.Messaging.AzureBlob/AzureBlobClaimCheckSettings.cs` *(create)* — `public sealed`;
  `ContainerClient : BlobContainerClient?`, `ClaimCheck : Action<ClaimCheckConfiguration>?` (the
  `Wolverine.Persistence.ClaimCheckConfiguration` type pinned per mechanic (d); XML docs: runs last inside
  `UseClaimCheck`, for `StoreForMessage<T>`/`StoreWhen`; the `AzureCredentialSettings` idiom).
- `src/Cloudstrap.Messaging.AzureBlob/AzureBlobClaimCheckOptionsAnnotationsValidator.cs` *(create,
  internal, `[OptionsValidator]` partial)* and `AzureBlobClaimCheckOptionsValidator.cs` *(create,
  internal `IValidateOptions<AzureBlobClaimCheckOptions>`, constructor takes the bound `ApplicationOptions`
  for the effective container name)* — mechanic (b); container-name rule in an internal static
  `ContainerNames.IsValid(string)` + `ContainerNames.Default(string systemName)` (`$"{systemName}-claimcheck"`
  lower-cased).
- `src/Cloudstrap.Messaging.AzureBlob/AzureBlobClaimCheckRegistrationState.cs` *(create, internal
  sealed)* — mechanic (c): `Options`, `Settings`, `ContainerName` (effective), `Resolution :
  ClaimCheckClientResolution?` (set at engine bootstrap).
- `src/Cloudstrap.Messaging.AzureBlob/ClaimCheckClientResolver.cs` *(create, internal static)* +
  `ClaimCheckClientResolution.cs` *(create, internal record)* + `ClaimCheckClientSource.cs` *(create,
  internal enum)* — mechanic (e).
- `src/Cloudstrap.Messaging.AzureBlob/ClaimCheckPostureLogger.cs` *(create, internal static partial)* —
  mechanic (f).
- `src/Cloudstrap.Messaging.AzureBlob/CloudstrapMessagingBuilderExtensions.cs` *(create, public
  static)* — `UseAzureBlobClaimCheck(this CloudstrapMessagingBuilder builder,
  Action<AzureBlobClaimCheckSettings>? configure = null) : CloudstrapMessagingBuilder`: guard clauses;
  mechanic (c) duplicate fail-fast at the call site; mechanic (b) eager bind + validate and the options
  pipeline on `builder.HostBuilder.Services`; the settings delegate; **`builder.ConfigureEngine(...)`**
  with the mechanic (d) contribution — **this step wires (1) resolve + fail fast, (2) `UseClaimCheck` with
  `UseAzureBlobStorage(container)` + `AutoOffloadPayloadsLargerThan` + the `ClaimCheck` hook, and (4) the
  posture line; the DL-1 failure rule (3) lands in Step 5**. Full XML docs (composes in any order with
  `UseSqlServer` / `AddCloudstrapTransactionalMessaging`; one claim check per node; container created by
  Wolverine on first use independent of `Cloudstrap:Messaging:AutoProvision` — create rights or IaC).
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Cloudstrap.Messaging.AzureBlob.Tests.csproj`
  *(create)* — `net10.0`; ProjectReference → the leaf; version-less `Microsoft.Extensions.Hosting`,
  `Microsoft.Extensions.Configuration`, `Azure.Storage.Blobs` (the double; already pinned); NUnit/MTP
  wiring inherited from `src/Test/Directory.Build.props`. *(EF SqlServer + OTel InMemory refs join in
  Step 4.)*
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Infrastructure/{MessagingTestHost,
  CapturingLoggerProvider}.cs` *(create — copies, mechanic (h))*
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/RegistrationTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/OptionsValidationTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/ClientResolutionTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under `Test\UnitTest`.

**RED** *(mechanic (a): first failure = the test project does not compile; then real red runs. Every
test here is offline: client construction contacts nothing; failures fire at the call or at
`host.StartAsync()` before any storage I/O)*:
- Unit test file: `RegistrationTests.cs` *(local transport, `MessagingTestHost.ValidSettings()`,
  `Cloudstrap:Storage:ConnectionString = UseDevelopmentStorage=true` + `AddCloudstrapBlobStorage` is
  **not** used here — a `BlobContainerClient("UseDevelopmentStorage=true", "contoso")` is registered
  directly as the DI double for route (b); `AddCloudstrapBlobStorage` itself is exercised in Step 8)*
  - `UseAzureBlobClaimCheck_OnNullBuilder_ThrowsArgumentNullException`.
  - `UseAzureBlobClaimCheck_CalledTwice_ThrowsNamingTheDuplicateCall` (AC-CK14; at the call site; message
    contains `UseAzureBlobClaimCheck`).
  - `UseAzureBlobClaimCheck_ReturnsTheSameBuilder_AndComposesInAnyOrderWithUseSqlServerCalls` — returns
    the builder; calling it before or after `UseSqlServer()` registration-time code path does not throw
    (host not started — no SQL touched).
  - `AddCloudstrapMessaging_WithoutTheLeafCall_ResolvesNoBlobClient` (AC-CK7) — register
    `BlobContainerClient` and `BlobServiceClient` factories that **throw** when invoked; start the host
    with `AddCloudstrapMessaging()` alone; publish a 1 MB fixture message on the local transport; the
    handler receives it whole and neither factory was ever invoked.
  - `UseAzureBlobClaimCheck_StartupLogsOnePostureLine_NamingContainerThresholdAndSource_NeverTheConnectionString`
    (AC-CK10) — `CapturingLoggerProvider`: exactly one entry from category `Cloudstrap.Messaging.AzureBlob`
    containing `contoso-claimcheck`, `204800`, `AddCloudstrapBlobStorage`; no entry anywhere contains
    `devstoreaccount1` or `UseDevelopmentStorage`.
- Unit test file: `OptionsValidationTests.cs`
  - `Options_Defaults_Are200KiBAndTheSystemNameClaimCheckContainer` — bound defaults: `204800`; effective
    container `contoso-claimcheck` (asserted via the internal state's `ContainerName`).
  - `Options_ThresholdZeroOrNegative_FailsNamingTheThresholdKey` — `Cloudstrap:Messaging:ClaimCheck:
    OffloadThresholdBytes = 0` → the failure text contains
    `'Cloudstrap:Messaging:ClaimCheck:OffloadThresholdBytes'` (AC-CK9).
  - `Options_InvalidContainerName_FailsNamingTheContainerKey_NeverTheValue` — `ContainerName =
    "Bad_Name--x"` → names `'Cloudstrap:Messaging:ClaimCheck:ContainerName'`; the value does not appear.
  - `Options_SystemNameThatYieldsAnInvalidDefaultContainer_FailsNamingTheContainerKey` — `SystemName =
    "a_b"` → the failure names the `ContainerName` key as the fix.
  - `Options_ContainerNameOverride_Wins` — `ContainerName = "exports-claimcheck"` is the effective name.
- Unit test file: `ClientResolutionTests.cs` *(AC-CK8 — every assertion on constructed clients, offline)*
  - `Resolve_NoBlobClientRegistered_StartupFailsNamingAddCloudstrapBlobStorageAndTheRegisterAClientAlternative`
    (8a) — `host.StartAsync()` throws; message contains `AddCloudstrapBlobStorage` and `BlobServiceClient`.
  - `Resolve_RegisteredBlobContainerClient_OpensTheClaimCheckContainerOnTheSameAccount` (8b) — DI holds
    `new BlobContainerClient("UseDevelopmentStorage=true", "contoso")`; the resolution's container has
    `Name == "contoso-claimcheck"`, `AccountName == "devstoreaccount1"`, source `BlobContainerClient`.
  - `Resolve_RegisteredBlobServiceClient_IsPreferredOverTheContainerClient` (8c) — both registered, the
    service client on a **different** account (`AccountName=other;...` connection string / a second dev
    account URI); the resolution uses the service client's account, source `BlobServiceClient`.
  - `Resolve_CodeLevelContainerClient_WinsOverEverything_AndIgnoresTheContainerNameSetting` (8d) —
    `settings.ContainerClient = new BlobContainerClient(..., "handed-in")` + `ContainerName =
    "ignored-name"` in config: resolution container `Name == "handed-in"`, source `code`; the posture line
    contains "ContainerName setting ignored".
  - `Resolve_ConstructsNoCredentialAndNoSecondClient` — reflection tripwire: the leaf assembly references
    no `Azure.Identity` assembly and declares no member whose type is `TokenCredential`; plus the DI
    double's factory was invoked exactly once across the whole host start (no second client of the same
    kind constructed).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.Messaging.AzureBlob.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.AzureBlob.Tests.exe --filter "RegistrationTests|OptionsValidationTests|ClientResolutionTests"
  ```

**GREEN**: the Scope items — minimal implementations passing these tests; full XML docs on every public
member from the start; Wolverine API names per mechanic (d)'s caveat.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: one call now binds, validates, resolves and states a claim-check posture with
   fail-fast on every misconfiguration, without touching storage — behavior new to the suite.
2. Full-suite check (mechanic (j) — the 15th exe joins the set) — all green; zero build warnings;
   `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Messaging.AzureBlob.*.nupkg` appears under
   `src/Cloudstrap.Messaging.AzureBlob/bin/Release/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — Large bodies leave the message and come back whole: a command whose serialized body exceeds the threshold crosses two nodes over the SQL Server transport as one stored blob plus Wolverine's `claim-check.$body` reference with an empty wire body, the remote handler receives the fully rehydrated message with no knowledge of blobs, an at-or-below-threshold command writes nothing and carries no reference, a transiently failing handler re-reads the payload on every attempt with exactly one side effect and the blob still there afterwards, the correlation id and Wolverine's spans flow exactly as before, and the contracts/handler assemblies reference no leaf, claim-check or Azure types (AC-M4; AC-CK1; AC-CK2; AC-CK3; AC-CK5; AC-CK6; mechanics (g)–(i)) ⚠️ first LocalDB tests of the leaf

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Cloudstrap.Messaging.AzureBlob.Tests.csproj`
  *(modify)* — version-less `Microsoft.EntityFrameworkCore.SqlServer` (D-3; `Microsoft.Data.SqlClient`
  arrives transitively for `SqlServerTestDatabase`), `OpenTelemetry.Exporter.InMemory`,
  `OpenTelemetry.Extensions.Hosting` (AC-CK6).
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Infrastructure/SqlServerTestDatabase.cs`
  *(create — copy, database `CloudstrapClaimCheckTests`)*,
  `Fixtures/InvocationRecorder.cs` *(create — copy)*.
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Fixtures/InMemoryBlobContainerClient.cs` +
  `InMemoryBlobClient.cs` *(create)* — mechanic (g).
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Fixtures/Contracts/ExportContracts.cs`
  *(create)* — namespace `Cloudstrap.Messaging.AzureBlob.Tests.Fixtures.Contracts`, deliberately
  referencing **no** Wolverine/Cloudstrap/Azure types: `ExportReadyCommand(Guid ExportId, string Content)`
  (the AC-CK3 story), `SmallCommand(Guid Id)`, `FlakyExportCommand(Guid Id, string Content, int
  FailuresBeforeSuccess)`.
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Fixtures/ExportHandlers.cs` *(create)* — plain
  handlers recording `(message, envelope.Headers, correlationAccessor.CorrelationId)` into the
  `InvocationRecorder`; the flaky handler counts attempts and throws until `FailuresBeforeSuccess`.
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Infrastructure/TwoNodeHarness.cs` *(create)* —
  mechanic (i): builds the sender and listener hosts (`UseSqlServer()` + `UseAzureBlobClaimCheck(s =>
  s.ContainerClient = sharedDouble)`; optional `Retries` and OTel InMemory exporter), `Durability.Mode =
  Solo`, `DisableConventionalLocalRouting()` on the sender.
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/ClaimCheckRoundTripTests.cs` *(create)*

**RED** *(⚠️ D-3 LocalDB — real SQL, no Docker, no cloud, no Azurite; the blob "account" is the in-memory
double)*:
- Unit test file: `ClaimCheckRoundTripTests.cs` *(`[OneTimeSetUp]` → `SqlServerTestDatabase.ResetAsync()`)*
  - `AboveThreshold_ExactlyOneBlobIsWritten_TheEnvelopeCarriesTheBodyReference_AndTheHandlerSeesTheWholeMessage`
    (AC-CK1 + AC-CK3 + AC-M4) — threshold left at the default, `Content` = 300 000 ASCII chars; after the
    listener's handler fires: `double.Uploads == 1`, the stored payload length > 204 800, the recorded
    handler message equals the sent record (content byte-for-byte), and the recorded envelope headers
    contain a key starting `claim-check.` (Wolverine's reference — the `$body` header). *(Executor
    latitude on asserting the stripped wire body: the outgoing envelope's `Data` length observed through a
    Wolverine message-listener/observability hook if available, else the header + single-upload evidence
    suffices — the behavioral content is fixed.)*
  - `AtOrBelowThreshold_NoBlobIsWritten_AndNoReferenceHeaderTravels` (AC-CK2) — `OffloadThresholdBytes`
    set to exactly the serialized size of `SmallCommand` (computed in the test with
    `System.Text.Json`) and a 4 KiB `Content` variant: `double.Uploads == 0`, no `claim-check.*` header,
    message equal.
  - `TransientHandlerFailures_RereadThePayloadEachAttempt_OneSideEffect_BlobRetained` (AC-CK5) —
    listener `Retries:NumberOfImmediate = 3`, `FailuresBeforeSuccess = 2`, above-threshold content:
    handler attempts == 3, side effect recorded once, `double.Downloads >= 3` (or ≥ 1 with the executor
    documenting where Wolverine caches the rehydrated body between **inline** retries — the spec's
    "re-read on every attempt" is asserted at least across a scheduled retry), and the blob still exists in
    the double after success (retention, DL-2).
  - `Correlation_FlowsUnchangedForOffloadedMessages_AndWolverineSpansReachTheHostsPipeline_NoExporterRegistered`
    (AC-CK6) — sender sets `ICorrelationContextAccessor.CorrelationId = "corr-<guid>"`; the listener's
    recorded correlation equals it; the listener's InMemory-exported activities include Wolverine-sourced
    spans; a descriptor assertion over the leaf's registrations shows no exporter and no
    `TracerProvider`/`MeterProvider` (the #14 `ObservabilityTests` shape).
  - `ContractsAndHandlers_ReferenceNoLeafClaimCheckOrAzureTypes` (AC-CK3, permanent) — reflection over the
    fixture contract types and handler types: no attribute, base type, interface, field, property or
    parameter type from `Cloudstrap.Messaging.AzureBlob`, `Wolverine.Persistence` (`BlobAttribute`,
    `IClaimCheckStore`, `ClaimCheckToken`) or `Azure.*` (handlers may take Wolverine's `Envelope`/`IMessageBus`
    — the #14 posture).
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.AzureBlob.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.AzureBlob.Tests.exe --filter "ClaimCheckRoundTripTests"
  ```

**GREEN**: no new production code is expected — Step 3 wired `UseClaimCheck` + the Azure store + the
threshold; this step proves the behavior. If GREEN reveals a gap (e.g. the double's overloads, the
`UseAzureBlobStorage` overload shape, or an `IWolverineExtension` timing issue for `UseClaimCheck`), the
minimal fix lands in the leaf and is reported at Gate 2 (mechanic (d)/(g) latitude).

**DB changes**: none — Wolverine auto-provisions `CloudstrapClaimCheckTests`.

**VERIFY**:
1. Test exe → all pass: a large command now crosses nodes as a blob + reference and arrives whole, small
   ones untouched, retries re-read, correlation and telemetry unchanged — AC-M4 met on a fake account with
   no network.
2. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0. Record the LocalDB prerequisite
   and the double's verified overload set in the step report.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: the seam and its one-way doors *(covers Steps 3–4)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **all-new public API** — every public type reviewed **verbatim against the
spec's Public API Sketch**: `CloudstrapMessagingBuilderExtensions.UseAzureBlobClaimCheck(this
CloudstrapMessagingBuilder, Action<AzureBlobClaimCheckSettings>?)`, `AzureBlobClaimCheckOptions`
(`SectionName`, `OffloadThresholdBytes = 204_800`, `ContainerName`), `AzureBlobClaimCheckSettings`
(`ContainerClient`, `ClaimCheck : Action<ClaimCheckConfiguration>?` — the Wolverine type now pinned) ·
**the cloud-resource naming one-way door (DL-9)** — the `{SystemName}-claimcheck` default and the
lower-casing/validation rule · **the client ladder** (code > `BlobServiceClient` > `AddCloudstrapBlobStorage`)
and its fail-fast wording · mechanic (g)'s double vs. fallback decision and any Wolverine-API deviations
logged under mechanic (d)'s caveat · the leaf's `InternalsVisibleTo` for its **own** test project only
(no grant from #14 — DL-8 honored).

- [ ] Behavioral verification: test exe output shows — the duplicate-call and null-guard fail-fasts, the
  AC-CK7 no-leaf/no-client proof, the one posture line with no connection string, the key-naming
  validation failures, and the four-route ladder incl. the "names both routes" failure (Step 3); the
  above-threshold blob + reference + whole-message round trip, the at/below-threshold no-op, the
  re-read-on-retry with one side effect and a retained blob, unchanged correlation + additive spans, and
  the contracts/handlers reflection tripwire (Step 4).
- [ ] Code review: options/validator against the #1/#14 pattern (keys named, values never echoed);
  `sealed`/static/internal-by-default; single namespace `Cloudstrap.Messaging.AzureBlob`; full XML docs;
  the csproj → one PackageReference + one ProjectReference, nothing else; no credential constructed; no
  Drop-listed concept resurrected (no `Enabled`, no `DataBus`, no leaf-owned account settings).
- [ ] User approved — implementation may continue past this gate

---

## Slice 3 — Failure semantics, the durable outbox, and a publishable, guarded package ⚠️ FAILURE/LIFECYCLE SEMANTICS (DL-1/DL-2) + PUBLIC API FROZEN

---

## Step 5 — A missing payload is a deterministic failure and is treated like one: an envelope whose referenced blob answers 404 is dead-lettered immediately with no retries, logged with message type, id and container name and never the payload or a connection string, while a 503/timeout from storage rides the normal #14 retry ladder — the leaf's rule sits before the ladder through the DL-8 seam (AC-CK4; DL-1; mechanic (d) item 3)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging.AzureBlob/CloudstrapMessagingBuilderExtensions.cs` *(modify)* — inside the
  contribution, after `UseClaimCheck`: `options.Policies.OnException<RequestFailedException>(ex =>
  ex.Status == 404, "claim-check payload missing (HTTP 404)").MoveToErrorQueue()` (wrapped-exception
  variant per mechanic (d) if the pinned store wraps).
- `src/Cloudstrap.Messaging.AzureBlob/ClaimCheckFailureLogger.cs` *(create, internal static partial)* —
  the AC-CK4 log line (`Error`): message type, message id, container name — emitted from the failure path
  Wolverine exposes for matched rules (executor latitude: a `Wolverine` failure-rule side effect / the
  dead-letter interceptor `IDeadLetterInterceptor` / an `ILogger` in the contribution's rule description —
  the observable is fixed: the captured log names type + id + container and never the payload).
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/MissingPayloadTests.cs` *(create)*

**RED** *(D-3 LocalDB, two-node harness, the double's `FailDownloadsWith(...)` switch)*:
- Unit test file: `MissingPayloadTests.cs`
  - `MissingBlob_DeadLettersImmediately_WithoutRunningTheRetryLadder` — listener `Retries:NumberOfImmediate
    = 3`, `NumberOfDelayed = 2`; after the sender uploaded, the double is switched to
    `FailDownloadsWith(new RequestFailedException(404, "BlobNotFound"))`; a row for the message appears in
    the listener's `wolverine_dead_letters` table (the #14 `WaitForScalarAsync` idiom) with the handler
    **never invoked** and the load attempted **exactly once** (`double.Downloads == 1`).
  - `MissingBlob_IsLoggedWithTypeIdAndContainer_NeverThePayloadOrConnectionString` — the captured log
    names `ExportReadyCommand`, the dead-letter row's id and `contoso-claimcheck`; a sentinel in the
    content and `devstoreaccount1` appear nowhere.
  - `TransientStorageFailure_RidesTheRetryLadder_AndSucceedsWhenStorageRecovers` — the double fails the
    first two downloads with `RequestFailedException(503, "ServerBusy")` then recovers: the handler runs
    exactly once with the whole message, `double.Downloads == 3`, no dead-letter row.
  - `ConsumerWolverineDelegate_CanAddARulePrecedingTheLeafs` — the consumer's `configurator.Wolverine`
    adds `OnException<RequestFailedException>(ex => ex.Status == 404).Discard()`; the message is discarded
    (no dead-letter row) — contributions-before-consumer means the consumer's rule is registered **after**
    the leaf's, so this asserts the documented override door works as the spec states: verify against the
    pinned Wolverine's rule-matching order and, if consumer rules cannot precede a contribution's, report
    at Gate 3 and document "the leaf's 404 rule is final" instead of bending the assertion.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.AzureBlob.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.AzureBlob.Tests.exe --filter "MissingPayloadTests"
  ```

**GREEN**: the Scope items — one rule, one log line.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a 404 now dead-letters in one attempt with disciplined logging while transient
   storage faults still heal through the ladder — new observable behavior.
2. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — Offloaded messages survive a crash exactly like small ones: an above-threshold command staged through `IDbContextOutbox<TDbContext>` and committed while the process dies before dispatch is delivered by the next node that starts on the store, and its handler receives the rehydrated message — the offload happened inside `SaveChangesAndFlushMessagesAsync`, before the commit (AC-CK13; AC-MSG8 carried to offloaded messages; Behaviors "Outbox interplay")

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Fixtures/ExportsDbContext.cs` *(create)* — a
  minimal `DbContext` (one `Export` entity) on the EF SqlServer provider (the #14 `OrdersDbContext` shape).
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/OutboxInterplayTests.cs` *(create)*

**RED** *(D-3 LocalDB; the #14 `DbContextOutbox_CommittedButNotDispatched_IsRecoveredByANewNode`
pattern with an above-threshold command)*:
- Unit test file: `OutboxInterplayTests.cs`
  - `OffloadedCommand_CommittedButNotDispatched_IsRecoveredByANewNode_AndArrivesWhole` — host A
    (`UseSqlServer()` + `AddCloudstrapTransactionalMessaging<ExportsDbContext>()` +
    `UseAzureBlobClaimCheck(s => s.ContainerClient = sharedDouble)`, local transport, durable local
    queues): resolve `IDbContextOutbox<ExportsDbContext>`, stage an `Export` row + `SendAsync(new
    ExportReadyCommand(id, 300_000 chars))`, `SaveChangesAndFlushMessagesAsync` with dispatch suppressed
    (dispose host A before it flushes outgoing — the #14 technique); assert `double.Uploads == 1` **already**
    (the offload happened before the commit) and the outbox row exists; start host B on the same store
    with the **same** double and short durability polling: B's handler receives the command with the full
    content; the `Export` row exists exactly once.
  - `OffloadedCommand_HandlerThrows_RowAndMessageBothRollBack_BlobMayRemainAsDocumentedOrphan` (AC-M2
    carried) — a transactional handler stages an entity and cascades an above-threshold message, then
    throws: no row, no delivery; the test records whether the double still holds an upload (the
    documented orphan case of DL-2 — asserted as "≤ 1 upload, never an error") to keep the README's orphan
    statement honest.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.Messaging.AzureBlob.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.AzureBlob.Tests.exe --filter "OutboxInterplayTests"
  ```

**GREEN**: no new production code expected — composition of #14's outbox with Step 3's decorator; any
gap is fixed minimally in the leaf and reported at Gate 3.

**DB changes**: none — EF `EnsureCreated`/Wolverine AutoProvision own the test artifacts.

**VERIFY**:
1. Test exe → all pass: AC-MSG8's no-loss guarantee now demonstrably holds for offloaded messages, and
   the upload-before-commit timing the README documents is proven.
2. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 7 — The package is publishable and guarded forever: metadata, README (quick start, options table, the ladder, the lifecycle-policy sample and the orphan case, required data-plane rights and container creation, the outbox interplay, the 404 posture, the Aspire "Cloudstrap's blob registration or Aspire's — not both" clause, Wolverine's `[Blob]` as the engine-native per-property opt-in, the manual live-account verification procedure, migration notes on all eight Deliberate Behavior Changes), permanent tripwires on the closure, the public surface and the dropped concepts, the forbidden-identifier sweep, and the #14 README pointer (AC-CK11; AC-A3; AC-ASP2; DL-2 documentation)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Messaging.AzureBlob/Cloudstrap.Messaging.AzureBlob.csproj` *(modify)* — `<Description>`
  (transparent large-message handling for Cloudstrap.Messaging: bodies over a size threshold stored in a
  dedicated Azure Blob Storage container, a reference travels, handlers see the whole message; account
  and credential from the host's registered blob client; one call), `<PackageTags>$(PackageTags);
  messaging;wolverine;claim-check;azure-blob-storage;large-messages</PackageTags>`,
  `<PackageReadmeFile>README.md</PackageReadmeFile>` + pack item.
- `src/Cloudstrap.Messaging.AzureBlob/README.md` *(create)* — quick start (the Overview's composition);
  the `Cloudstrap:Messaging:ClaimCheck` options table (defaults, overrides — every convention has one) and
  the code hooks; the client ladder + fail-fast; **the dedicated-container rationale and a sample Azure
  Storage lifecycle-management policy** (delete base blobs older than N days on the claim-check container;
  N ≥ retry window + dead-letter replay window, suggested 14) + the **orphan case** (a rolled-back outbox
  transaction after serialization leaves a blob the policy removes); **required rights** (data-plane
  read/write on the container; create-container rights on the account **or** IaC pre-creation — Wolverine
  creates on first use, independent of `Cloudstrap:Messaging:AutoProvision`); **outbox interplay** (offload
  inside `SaveChangesAndFlushMessagesAsync`, upload latency on the HTTP request); the **404 → immediate
  dead-letter** posture and the consumer override door; **retention** (no delete-after-handling, N
  subscribers share one blob); **Aspire coexistence** (an Aspire app's `BlobServiceClient` from
  `Aspire.Azure.Storage.Blobs` is preferred by the ladder — "Cloudstrap's blob registration or Aspire's,
  not both"; zero Aspire reference); Wolverine's `[Blob]` documented as the per-property opt-in (costs a
  Wolverine reference in contracts) and `StoreForMessage<T>` via `settings.ClaimCheck`; the no-secrets
  rules; the **manual live-account verification procedure** (the AC-M1/AC-E5 precedent: real account +
  `DefaultAzureCredential`, post a large message, observe the blob, the posture line, the handler — never
  automated); migration notes (Deliberate Behavior Changes 1–8 verbatim, incl. `*DataBus` → threshold and
  "no wire compatibility").
- `src/Cloudstrap.Messaging/README.md` *(modify)* — a "Large payloads" pointer to
  `Cloudstrap.Messaging.AzureBlob` next to the Step 2 `ConfigureEngine` section.
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/PackageSurfaceTests.cs` *(create)* — permanent
  guards (the #14 shape, adapted).

**RED** *(the guard tests are tripwires against already-correct code and may pass immediately — the
honest failing state is in the artifacts: before GREEN the Release nupkg has no README/description/tags;
recorded per the #2…#14 precedent)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfTheLeaf_MatchTheApprovedClosure` — allowed prefixes: `System`, `netstandard`,
    `Microsoft.Extensions.`, `Wolverine`, `JasperFx`, `Azure.Core`, `Azure.Storage`, `Cloudstrap.Messaging`,
    `Cloudstrap.Core`; **forbidden**: `Azure.Identity` (finding 9), `Cloudstrap.Extensions` (DL-3),
    `NServiceBus`, `Particular`, `Aspire` (AC-ASP2), `Nihdi` (AC-A3), `Duende`, `MudBlazor`,
    `Microsoft.AspNetCore`. *(Executor latitude: trim the allow-list to the observed set; the forbidden
    list is fixed.)*
  - `PublicSurface_IsExactlyTheApprovedTypes` — exactly `CloudstrapMessagingBuilderExtensions`,
    `AzureBlobClaimCheckOptions`, `AzureBlobClaimCheckSettings`; all in namespace
    `Cloudstrap.Messaging.AzureBlob`; every class sealed or static; no public interfaces.
  - `PublicTypes_ContainNoForbiddenIdentifiers` — `(?i)nihdi|riziv|cfe|nservicebus|particular|dynatrace`.
  - `LeafAssembly_DeclaresNoDroppedConcepts` — no declared type/member name contains `DataBus`,
    `Databus`, `Encrypt`, `Certificate`, `ClientSecret`, `TenantId`, `BlobServiceUri`, `ConnectionString`,
    `Enable`, `Sweep`, `TimeToLive`, `Prefix` (the Drop rows and "deliberately not shipped" list stay dead).
  - `OptionsType_DeclaresNoSecretBearingOrAccountSetting` — reflection over `AzureBlobClaimCheckOptions`:
    exactly the two public properties `OffloadThresholdBytes` and `ContainerName` (no account/URI/secret
    settings, permanent — DL-9).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Messaging.AzureBlob.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.AzureBlob.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata and write both READMEs per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the five permanent guards.
2. `dotnet build src/Cloudstrap.sln -c Release` → `src/Cloudstrap.Messaging.AzureBlob/bin/Release/
   Cloudstrap.Messaging.AzureBlob.<version>.nupkg`; expand a `.zip` copy → `README.md`, `icon.png`,
   `lib/net10.0/Cloudstrap.Messaging.AzureBlob.dll` **and** `.xml`; nuspec shows the MIT license
   expression, description, tags, repository URL, and dependencies = exactly `Cloudstrap.Messaging` +
   `WolverineFx.AzureBlobStorage` — no `Azure.Identity`, no `Cloudstrap.Extensions`, no `Aspire.*`, no
   `NServiceBus.*`, no test/demo pin leakage (AC-CK11, AC-ASP2).
3. **AC-CK11 identifier sweep** (leaf + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Messaging.AzureBlob, src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|cfe|nservicebus|particular|databus|encrypt)'
   ```
   → zero matches beyond the guard tests' self-referential patterns and the README's migration notes
   naming the *source* concepts (read the hits, as in plans 2–14).
4. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: failure semantics, outbox interplay, the surface frozen *(covers Steps 5–7)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **failure/lifecycle semantics (DL-1/DL-2)** — the 404-only rule (its exact
exception filter against the pinned store, wrapped or not), the "consumer may precede" door as actually
observed (or the documented "leaf's rule is final" fallback), the orphan-blob observation from Step 6 and
the README's lifecycle-policy sample + rights statement reviewed for operational correctness · **public API
frozen** — Step 7's `PublicSurface_IsExactlyTheApprovedTypes` pins the Gate-2 surface forever; the expanded
Release nupkg (metadata + two-dependency list) reviewed · the README's Aspire clause and the `[Blob]` /
`StoreForMessage<T>` guidance reviewed against the spec's "deliberately not shipped" list.

- [ ] Behavioral verification: test exe output shows — the one-attempt 404 dead-letter with
  type+id+container logging and no payload/connection string, the 503 ride through the ladder to success,
  and the consumer-rule door (Step 5); the crash-recovery delivery of an offloaded command with the
  upload-before-commit timing, and the AC-M2 rollback (Step 6); the five permanent guards green, the
  expanded Release nupkg reviewed, the identifier sweep clean (Step 7).
- [ ] Code review: one failure rule, one log line — nothing bespoke around Wolverine's pipeline; no
  payload/URI/connection string in any log; README accuracy against as-built behavior (ladder, defaults,
  lifecycle sample, rights, outbox timing, migration notes); #14 README pointer present.
- [ ] User approved — implementation may continue past this gate

---

## Slice 4 — Demonstrated live: an order with 300 000 characters of notes crosses from the demo Api to the demo Worker as one blob in `demo-claimcheck` on Azurite, the Worker records the notes' length and SHA-256, a small order adds no blob, and E2E proves it through the running processes with Azurite fixture-started by default, `CLOUDSTRAP_TEST_BLOB` overriding, and CI providing the emulator

---

## Step 8 — The demo apps run the package (workflow rule 9; AC-CK12; AC-CK10 live; DL-10; Planner note 2; mechanics (k)–(l)): `Cloudstrap.Demo.Api` and `Cloudstrap.Demo.Worker` add `AddCloudstrapBlobStorage()` + `.UseAzureBlobClaimCheck()`, `PlaceOrderCommand` carries `Notes`, the Worker records length + SHA-256 (never the notes), a new E2E fixture proves the above/below-threshold flows against Azurite and reads the Api's posture line, the E2E harness owns Azurite (or attaches via `CLOUDSTRAP_TEST_BLOB`), CI runs an Azurite service container, and every pre-existing E2E test stays green ⚠️ E2E/CI INFRASTRUCTURE + DEMO CONTRACT RISK AREA

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/demo/Shared/Contracts/PlaceOrderCommand.cs` *(modify)* — `public sealed record PlaceOrderCommand(
  Guid OrderId, string? Notes = null);` — **zero package references** untouched; teaching comment (the
  size threshold decides, no attribute, no naming convention).
- `src/demo/Shared/Contracts/OrderDtos.cs` *(modify)* — `PlaceOrderDto(string Description, string? Notes =
  null)`; `OrderDto(Guid Id, string Status, string? ProcessedCorrelationId, int? NotesLength, string?
  NotesSha256)`.
- `src/demo/Api/Cloudstrap.Demo.Api.csproj` + `src/demo/Worker/Cloudstrap.Demo.Worker.csproj` *(modify)*
  — ProjectReference → `Cloudstrap.Messaging.AzureBlob` (`Cloudstrap.Extensions` already arrives through
  `Cloudstrap.WebApi` / `Cloudstrap.Worker`).
- `src/demo/Api/Program.cs` + `src/demo/Worker/Program.cs` *(modify)* — `builder.AddCloudstrapBlobStorage();`
  then `.UseAzureBlobClaimCheck()` appended to the existing three-call chain (teaching comments: account +
  credential come from `Cloudstrap:Storage`; both hosts land on `demo-claimcheck` by convention).
- `src/demo/Api/appsettings.json` + `src/demo/Worker/appsettings.json` *(modify)* — `Cloudstrap:Storage:
  ConnectionString = "UseDevelopmentStorage=true"` with a `//` comment (explicit demo config — Azurite;
  the E2E harness overrides from `CLOUDSTRAP_TEST_BLOB`); a `Cloudstrap:Messaging:ClaimCheck` `//` comment
  block stating the defaults in force (container `demo-claimcheck`, 204 800 bytes) — **no values set**.
- `src/demo/Api/Data/DemoDbContext.cs` + `src/demo/Worker/Data/WorkerDbContext.cs` *(modify)* — `Order`
  gains `NotesLength : int?`, `NotesSha256 : string?`; `EnsureCreated` adds the mechanic (k) `ALTER TABLE
  ... ADD` guard after the `CREATE TABLE` guard.
- `src/demo/Api/Controllers/OrdersController.cs` *(modify)* — `Place` sends `new PlaceOrderCommand(
  order.Id, dto.Notes)` (notes never stored on the row); `Get` returns the two new fields.
- `src/demo/Worker/PlaceOrderCommandHandler.cs` *(modify)* — records `order.NotesLength =
  command.Notes?.Length` and `order.NotesSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.
  GetBytes(command.Notes)))` when notes are present; log lines unchanged (type + id — **never the notes**;
  teaching comment).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/Cloudstrap.Demo.E2E.Tests.csproj` *(modify)* — version-less
  `Azure.Storage.Blobs` (existing pin; lists the container).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/Infrastructure/AzuriteProcess.cs` *(create)* — mechanic (l).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/E2eFixture.cs` *(modify)* — start Azurite (or attach) and poll
  readiness **before** the Api boots; forward `--Cloudstrap:Storage:ConnectionString=<CLOUDSTRAP_TEST_BLOB>`
  to the Api when set; new `BlobConnectionString` and `CapturedApiOutput` statics; dispose Azurite last.
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/MessagingTests.cs` + `WorkerHostTests.cs` *(modify)* — forward
  the same override to their Worker arguments when set (Planner note 2: **every** spawn point 1c552b7
  touched).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/ClaimCheckTests.cs` *(create)* — `[TestFixture]` on the
  `MessagingTests` pattern: Worker on health port **5352**, machine token from `demo-machine`, blob
  counting via `new BlobContainerClient(E2eFixture.BlobConnectionString, "demo-claimcheck")`.
- `.github/workflows/ci.yml` *(modify)* — mechanic (l): `services: azurite` next to `sqlserver`;
  `CLOUDSTRAP_TEST_BLOB: "UseDevelopmentStorage=true"` on the "Run all MTP test executables" step, with a
  comment mirroring the D-3 one.
- `src/demo/Api/README.md` + `src/demo/Worker/README.md` *(modify)* — feature-matrix rows for #15 (the
  call | the E2E test names) and harness notes (Azurite prerequisite: `npm install -g azurite`;
  `CLOUDSTRAP_TEST_BLOB` override; `demo-claimcheck` shared by convention; the Worker records length +
  hash, never the notes; port 5352). `src/demo/README.md` *(modify)* — port map row 5352 (`ClaimCheckTests`),
  the Azurite prerequisite in "Running the E2E tests", the harness bullet for `CLOUDSTRAP_TEST_BLOB` /
  fixture-started Azurite next to the LocalDB one.

**RED** *(write these tests first, run them, confirm they fail — today the command has no `Notes`, the
Worker records nothing, and no blob ever appears)*:
- E2E test file: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/ClaimCheckTests.cs`
  - `ClaimCheck_OrderWithNotesAboveTheThreshold_IsProcessedByTheWorker_WithLengthAndHashRecorded_AndExactlyOneNewBlobInTheClaimCheckContainer`
    — count blobs in `demo-claimcheck` (0 when the container does not exist yet); `POST api/v1/orders` with
    `notes` = 300 000 ASCII chars containing the sentinel `notes-sentinel-never-logged-7b2e`; poll `GET
    api/v1/orders/{id}` until `Processed` (30 s deadline, never a bare sleep); assert `notesLength ==
    300000`, `notesSha256 == SHA256(sent)` (hex), blob count == before + 1, and the Worker's captured stdout
    never contains the sentinel (AC-CK12 + the no-payload posture, live).
  - `ClaimCheck_OrderWithNotesBelowTheThreshold_IsProcessed_AndAddsNoBlob` — 1 024-char notes: length +
    hash recorded, blob count unchanged.
  - `ClaimCheck_ApiStartupPostureLine_NamesContainerThresholdAndClientSource_NeverTheConnectionString`
    (AC-CK10 live) — `E2eFixture.CapturedApiOutput` contains `demo-claimcheck`, `204800` and
    `AddCloudstrapBlobStorage`; contains neither `UseDevelopmentStorage` nor `devstoreaccount1`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "ClaimCheckTests"
  ```

**GREEN**: the Scope items. **Every pre-existing E2E test must stay green unchanged** — in particular
`MessagingTests` (the `Notes`-less path still works: `Notes = null`, below threshold, no blob) and
`WorkerHostTests` (the Worker still boots with only `UseDevelopmentStorage=true` in its settings — client
construction contacts nothing; the container is created lazily). If any existing test is disturbed, the
executor reports it at the gate rather than weakening any assertion. Azurite absent locally → the fixture
fails loudly with the install command (never a silent skip).

**DB changes**: none in the repo — the two `demo.Orders` columns are added at runtime by the demo hosts'
`EnsureCreated` (Development), the Wolverine tables by AutoProvision.

**VERIFY**:
1. E2E exe → the three new tests pass **and every pre-existing E2E test passes unchanged** (build first;
   `npm install -g azurite` once; one-time `playwright.ps1 install chromium` if needed). Then re-run with
   `CLOUDSTRAP_TEST_BLOB=UseDevelopmentStorage=true` against a manually started `azurite-blob` → attach mode
   starts no emulator and the same tests pass (the CI shape exercised locally).
2. Manual smoke (optional but recorded): run Azurite, IdP, Api, Worker per the READMEs; post an order with
   large notes; watch the Api's posture line and the Worker's log (type + id only); list `demo-claimcheck`
   in Azurite (Storage Explorer or the SDK) → one blob; query the order → `Processed` + length + hash.
3. Full-suite check (mechanic (j)) — all green; `dotnet format` exit 0; the demo projects still pack
   nothing; the CI workflow YAML is valid (the `services:` block parses; the executor pushes nothing —
   CI runs on the user's push).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #15 complete *(covers Step 8; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the
user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

⚠️ **Risk areas at this gate**: **the E2E-suite-wide Azurite prerequisite (DL-10)** — the fixture now
starts `azurite-blob` for **every** E2E run unless `CLOUDSTRAP_TEST_BLOB` is set (a fresh clone needs one
`npm install -g azurite`; `Testcontainers.Azurite` stays the recorded follow-up) — **confirm** · **the CI
change** — the Azurite service container + `CLOUDSTRAP_TEST_BLOB` on the test step, mirroring 7596f85;
first CI run observed after the user's push (fallback: npm install step) · **the demo contract change** —
`PlaceOrderCommand.Notes`, the two `demo.Orders` columns via a demo-only `ALTER TABLE ... ADD` on the
persistent `CloudstrapDemo` database, `OrderDto`'s two new fields — **confirm** · mechanic (k)'s demo
topology (`UseDevelopmentStorage=true` as explicit demo config; both hosts on `demo-claimcheck` by
convention; defaults deliberately unset in `appsettings.json`) — **confirm**.

- [ ] Behavioral verification: the three new E2E tests pass
  (`ClaimCheck_OrderWithNotesAboveTheThreshold_..._AndExactlyOneNewBlobInTheClaimCheckContainer`,
  `ClaimCheck_OrderWithNotesBelowTheThreshold_IsProcessed_AndAddsNoBlob`,
  `ClaimCheck_ApiStartupPostureLine_..._NeverTheConnectionString`) in **both** fixture-started and attach
  mode, and **all pre-existing E2E tests pass unchanged**; the full-suite check (build + 15 unit exes +
  E2E exe + `dotnet format --verify-no-changes`) is green end to end.
- [ ] Spec acceptance sign-off: walk **AC-M4, AC-M3, AC-A3, AC-ASP2, AC-CK1…AC-CK14** against the step
  evidence using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop /
  Out-of-Scope lists was resurrected (no bespoke middleware/serializer/envelope rule, no `Enable*` flag,
  no `*DataBus` convention, no encryption, no leaf-owned account settings, no sweep/TTL/delete code, no
  `WolverineFx.ClaimCheck.AzureBlobStorage`, no `Cloudstrap.Extensions` reference, no Testcontainers, no
  `InternalsVisibleTo` grant from #14) and every De-NIHDI row is closed (no hard-coded account URI, no
  settings-borne credential, `ClaimCheck` vocabulary throughout, `nihdi`-free container default, neutral
  fixtures, no cryptography remnants).
- [ ] Docs review: `src/Cloudstrap.Messaging.AzureBlob/README.md` matches as-built behavior (ladder,
  defaults, lifecycle sample + orphan case, rights, outbox timing, 404 posture, Aspire clause, `[Blob]`
  guidance, manual live-account procedure); `src/Cloudstrap.Messaging/README.md` documents `ConfigureEngine`
  and points to the leaf; the Api/Worker/demo READMEs cite the real E2E test names, the Azurite
  prerequisite, `CLOUDSTRAP_TEST_BLOB` and port 5352. **Recorded follow-ups (not in this plan)**:
  `Testcontainers.Azurite` (DL-10), hoisting the duplicated test helpers into `Cloudstrap.Testing`, the
  `configure-wolverine` skill (CLAUDE.md pending artefacts), the PostgreSQL durability leaf as the second
  `ConfigureEngine` consumer, #19/#20 Dashboard visibility of offloaded payloads.
- [ ] User approved — deliverable #15 done; project-manager flips the ROADMAP row to ✅.
