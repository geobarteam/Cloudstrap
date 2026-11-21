# Plan: 8-TestHelpers — Cloudstrap's own test suites take the SQL Server test database, the message recorder and the valid-settings host builder from one test-only library, each SQL suite on its own catalog. CI and local runs give `CLOUDSTRAP_TEST_SQL` the same meaning, and log capture runs on Microsoft's `FakeLogger` instead of six hand-written copies.

## Overview

Deliverable #8 of the extraction roadmap. **Binding spec: `_specs/8-TestHelpers.md`**. All seven Open Questions are
answered (Decision Log D-1…D-7, user, 2026-10-05; every recommendation accepted), and analyst decisions DD-1…DD-7
stand. The spec's Port Decision Table, Public API Sketch, Behaviors, Dependencies, Deliberate Behavior Changes, Edge
Cases and Out-of-Scope list are authoritative and are not re-litigated here.

**Nothing ships (D-1), and nothing from `Nihdi.Core.Testing` is ported (D-2).** The plan therefore contains:
- no `CustomWebApplicationFactory`;
- no `ReplaceAsSingleton` / `ReplaceDbContextAsSingleton`;
- no `Microsoft.AspNetCore.Mvc.Testing`, no EF Core reference in the new library;
- no Cloudstrap-owned log-capture type;
- no shared `CapturingPrimaryHandler` / `RecordingDistributedCache` (DD-1);
- no sweep of the per-suite `Cloudstrap:Application` settings literals;
- no change to `CLOUDSTRAP_TEST_BLOB`;
- no `Testcontainers.*`;
- no change to any shipped package (DD-7).

This is a **consolidation of Cloudstrap's own test infrastructure**:
- **Hoist + redesign (D-4, D-6):** one `SqlServerTestDatabase` with an instance per catalog, catalog-name validation
  (DD-4), override semantics where the catalog always comes from the suite, and pool clearing scoped to its own
  connection string.
- **Hoist (D-6):** one `InvocationRecorder` and one neutral `CloudstrapTestHost` (successor of the two
  `MessagingTestHost` copies). The E2E suite's seven `CLOUDSTRAP_TEST_SQL` forwarding sites consume the shared
  helper.
- **Replace (D-3):** every `CapturingLoggerProvider` copy (six physical copies + the Hangfire link, two shapes)
  gives way to `Microsoft.Extensions.Diagnostics.Testing` 10.8.0 (`FakeLoggerProvider` / `FakeLogCollector` /
  `FakeLogRecord`). The Observability suite's private sentinel `FakeLoggerProvider` stays (DD-2).
- **Keep + rename (DD-5):** the Hangfire suite's static job-id log becomes `TaskRunLog`.

**Placement (D-7, DD-6):** the test-only, never-packed library `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/`
(namespace `Cloudstrap.TestInfrastructure`) plus its companion suite `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/`.
The precedent is `Cloudstrap.TestIdentityProvider` + `Cloudstrap.TestIdentityProvider.Tests`.

Reference patterns, all read on disk before planning:
- **Plan-shape precedents**: `_plans/16-HangfireScheduler.md` (mechanics section, Pin record, full-suite check
  convention, gate shape) and `_plans/15-BlobClaimCheck.md`.
- **The test-only library precedent**:
  - `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/Cloudstrap.TestIdentityProvider.csproj`
    (no packaging properties; it inherits `IsPackable=false`);
  - `src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests/Cloudstrap.TestIdentityProvider.Tests.csproj`
    (one `ProjectReference`; NUnit + MTP wired by `src/Test/Directory.Build.props` for `*.Tests` only);
  - their nesting under the `Test` / `TestIdentityProvider` / `UnitTest` solution folders in `src/Cloudstrap.sln`.
- **The code being consolidated** (all read):
  - `Cloudstrap.Messaging.Tests/Infrastructure/{SqlServerTestDatabase, CapturingLoggerProvider, MessagingTestHost}.cs`
    and `Fixtures/InvocationRecorder.cs`;
  - the AzureBlob copies of the same four;
  - `Cloudstrap.Hangfire.Tests/Cloudstrap.Hangfire.Tests.csproj` (the two `<Compile … Link=…>` lines 26–27),
    `Infrastructure/HangfireSqlHost.cs`, `Fixtures/InvocationRecorder.cs` (static);
  - `Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/CapturingLoggerProvider.cs` and the nested copy in
    `Cloudstrap.Authentication.ClientCredentials.Tests/Infrastructure/TestDoubles.cs:362–403`;
  - every `SqlServerTestDatabase.*` call site and every `.Entries` log assertion (grep).
- **Repo-file tripwire idiom**: `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/DependencyPinTests.cs` (`FindRepoRoot()`
  walks up to `src/Cloudstrap.sln`, then `XDocument.Load` of `src/Directory.Packages.props`).
- **E2E + CI**:
  - `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/{E2eFixture.cs:86, WorkerHostTests.cs:41, MessagingTests.cs:41,
    ClaimCheckTests.cs:44, HangfireTests.cs:44/62, BlazorServerTests.cs:33}` and the E2E csproj;
  - the demo `appsettings.json` connection strings (all three on `CloudstrapDemo`);
  - `.github/workflows/ci.yml:76–77` and `.github/workflows/release.yml:94–95` (the same override string);
  - `src/Directory.Packages.props` (transitive pinning on; `Microsoft.Extensions.*` at 10.0.10;
    `Microsoft.Extensions.Http.Resilience` 10.8.0 in the test-only group; `Microsoft.Data.SqlClient` 6.1.7).

There is **no database project and no demo-app change**. Every "DB changes" section reads "none". The SQL suites
create their catalogs at runtime through `ResetAsync`.

### Starting point

#16 is committed (`03eee2b`), so `dev` is clean apart from the untracked `evolution/` folder, which this deliverable
does not touch. Every diff in this plan is #8's own.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-ASP2 (zero `Aspire.*`; tripwire) | 1 (library csproj guard) · 6 (pin tripwire) |
| AC-TS1 (each hoisted type exactly once; no `<Compile … Link=…>` anywhere under `src/Test`) | 4 (types once) · 6 (no links) |
| AC-TS2 (zero `CapturingLoggerProvider` / `CapturedLogEntry`; seven suites on `FakeLoggerProvider`; same assertion meaning; Observability sentinel untouched) | 6 (record shape, five suites) · 7 (tuple shape, two suites + permanent guard) |
| AC-TS3 (catalog required; Messaging / AzureBlob / **Hangfire** catalogs distinct; sharing ended) | 1 (constructor) · 2 (Hangfire) · 3 (Messaging) · 4 (AzureBlob) |
| AC-TS4 (override unset → LocalDB default with the catalog) | 1 |
| AC-TS5 (override set → server/auth/options kept, catalog always replaced; no SQL Server needed) | 1 |
| AC-TS6 (invalid / system / unsafe catalog → `ArgumentException` naming the parameter, before any SQL) | 1 |
| AC-TS7 (`ResetAsync` drop + recreate; own-pool clearing only; helper semantics kept; `CancellationToken` everywhere) | 1 (implementation) · 2–4 (LocalDB suites exercise it) |
| AC-TS8 (all unit exes + E2E green, per-exe counts equal the baseline, under LocalDB **and** the override) | Baseline record · every step's VERIFY · 5 (both modes) · final gate |
| AC-TS9 (E2E forwards only via the shared helper, catalog `CloudstrapDemo`; zero inline env-var reads; `ci.yml` override catalog-free, comment fixed) | 5 |
| AC-TS10 (zero dropped source members, zero `Mvc.Testing`, no EF Core added) | 7 (permanent guard) |
| AC-TS11 (library under `src/Test`, not `.Tests`, never packed, unreferenced by shipped/demo projects, no NUnit/Moq/Aspire; companion suite proves AC-TS4–6 SQL-free) | 1 |
| AC-TS12 (no `Nihdi`/`NIHDI`/`Riziv`/`UnitTest` environment/`Security:EnableAuthentication` in added or touched files) | 7 (permanent guard over the library) + final-gate grep |
| AC-TS13 (rule 9 as interpreted by D-5) | Statement below · final gate |

### Workflow rule 9 / planner rule 15: user-approved deviation (D-5, AC-TS13)

This plan has **no demonstration slice and adds no E2E test**. The user approved this deviation on 2026-10-05
(spec D-5). The deliverable changes no running-app behavior, so there is nothing new to demonstrate in a demo app.
The demonstration instead is that **the E2E suite consumes the shared SQL helper** (Step 5, AC-TS9), and the proof is
the **unchanged** E2E suite (baseline count, expected 68) passing under the LocalDB default and under the
`CLOUDSTRAP_TEST_SQL` override. No demo app under `src/demo` changes code, so no feature matrix changes. The final
🛑 gate records the deviation.

### ⚠️ Risk areas (reviewed at the gates named)

- **Shared contract between test executables (D-4)**: the new `CLOUDSTRAP_TEST_SQL` semantics (server, auth and
  options only; the catalog comes from the suite) and the CI override string. Reviewed at **Gate 1**.
- **Dependency update (D-3)**: the new test-only pin `Microsoft.Extensions.Diagnostics.Testing` 10.8.0, its
  transitive closure under `CentralPackageTransitivePinningEnabled`, and its filled Pin record. Gated on its own at
  **Gate 2**, right after the step that introduces it.
- **Regression risk**: seven unit suites and the E2E suite are repointed. Per-executable test counts against the
  Baseline record are the safety net, at **every gate**.
- **Assertion meaning (AC-TS2)**: log assertions are retyped, never weakened. The predicate diff is reviewed at
  **Gate 2** (record shape) and the **final gate** (tuple shape).
- **No public API one-way door**: nothing is packed (AC-TS11, guarded from Step 1).

### Planner mechanics decided here (each reviewed at the named gate)

**(a) Baseline record: filled by the executor before Step 1 (all gates).** Build `src/Cloudstrap.sln`, run every
test executable **with `CLOUDSTRAP_TEST_SQL` unset**, and record the per-executable totals here. These numbers are
the AC-TS8 reference. The only executable whose count may change afterwards is the new
`Cloudstrap.TestInfrastructure.Tests`.

| Executable | Baseline tests (passed / total) |
|---|---|
| Cloudstrap.Core.Tests | 54 / 54 |
| Cloudstrap.Extensions.Tests | 76 / 76 |
| Cloudstrap.Observability.Tests | 106 / 106 |
| Cloudstrap.Observability.AzureMonitor.Tests | 38 / 38 |
| Cloudstrap.WebApi.Tests | 109 / 109 |
| Cloudstrap.Mvc.Tests | 66 / 66 |
| Cloudstrap.Worker.Tests | 30 / 30 |
| Cloudstrap.Authentication.ClientCredentials.Tests | 41 / 41 |
| Cloudstrap.Authentication.OpenIdConnect.Tests | 66 / 66 |
| Cloudstrap.BlazorCommon.Tests | 22 / 22 |
| Cloudstrap.BlazorServer.Tests | 47 / 47 |
| Cloudstrap.BlazorWasm.Tests | 41 / 41 |
| Cloudstrap.Messaging.Tests | 62 / 62 |
| Cloudstrap.Messaging.AzureBlob.Tests | 37 / 37 |
| Cloudstrap.Hangfire.Tests | 105 / 105 |
| Cloudstrap.TestIdentityProvider.Tests | 26 / 26 |
| **Unit total (16 exes; #16's gate recorded 926)** | **926 / 926** |
| Cloudstrap.Demo.E2E.Tests (#16's gate recorded 68) | 68 / 68 |
| Recorded on / by | 2026-10-05, `dev` @ `d116510`, `CLOUDSTRAP_TEST_SQL` unset (LocalDB) / executor (Claude) |

**(b) Full-suite check (the standing convention: `runTests` is not on the agent PATH, so every executable is run
directly).**
1. `dotnet build src/Cloudstrap.sln`.
2. **Every** unit executable `src\Test\UnitTest\<Name>\bin\Debug\net10.0\<Name>.exe`: the 16 above plus
   `Cloudstrap.TestInfrastructure.Tests` from Step 1.
3. `src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe` (LocalDB required; run
   the one-time `playwright.ps1 install chromium` if needed).
4. `dotnet format src/Cloudstrap.sln --verify-no-changes`.

Each step's VERIFY compares per-executable counts with the Baseline record.

**(c) The SQL helper's shape (spec sketch, made concrete; Gate 1).**
`public sealed class SqlServerTestDatabase` in `Cloudstrap.TestInfrastructure`:
- `public const string EnvironmentVariable = "CLOUDSTRAP_TEST_SQL";`
- `public SqlServerTestDatabase(string catalog)`. It validates (DD-4) **before anything else**:
  - `ArgumentNullException.ThrowIfNull`;
  - otherwise `ArgumentException` with `paramName: "catalog"` when the name:
    - is empty or white-space;
    - is a system database (`master`, `model`, `msdb`, `tempdb`, `OrdinalIgnoreCase`);
    - fails `^[A-Za-z][A-Za-z0-9_]*$`.
  - It then computes `ConnectionString` once from the environment variable.
- `public string Catalog { get; }` · `public string ConnectionString { get; }`.
- `public Task ResetAsync(CancellationToken cancellationToken = default)`:
  - opens a connection to `master` on the same server/auth;
  - runs `IF DB_ID(N'{catalog}') IS NOT NULL BEGIN ALTER DATABASE [{catalog}] SET SINGLE_USER WITH ROLLBACK
    IMMEDIATE; DROP DATABASE [{catalog}]; END; CREATE DATABASE [{catalog}];`. Only the validated name is ever
    interpolated, and it is bracket-quoted.
  - Before the DDL it calls `SqlConnection.ClearPool(new SqlConnection(ConnectionString))`, which clears **this
    connection string's pool only**. `SqlConnection.ClearAllPools()` is gone (AC-TS7).
- `public Task<bool> TableExistsAsync(string schema, string table, CancellationToken cancellationToken = default)`.
- `public Task<object?> ScalarAsync(string sql, IReadOnlyList<(string Name, object Value)>? parameters = null,
  CancellationToken cancellationToken = default)`.
  - **Planner's call on the parameter order, which the spec delegated.** The sketch's `(sql, CancellationToken,
    params …)` would put the token before the parameters. That conflicts with CLAUDE.md's "`CancellationToken` last"
    rule and risks CA1068.
  - The parameters therefore become an optional list. The five call sites that pass parameters switch from
    `("@id", x)` to `[("@id", x)]`. Semantics are unchanged.
- `public Task<object?> WaitForScalarAsync(string sql, TimeSpan timeout, CancellationToken cancellationToken =
  default)`: 250 ms polling (`Task.Delay(250, cancellationToken)`), `null` on the deadline, `DBNull` treated as no
  value. This is today's behavior.
- `public static string? OverrideConnectionStringFor(string catalog)`: validates the catalog. It returns `null` when
  the variable is unset or white-space, and otherwise the override with `Initial Catalog := catalog` (the AC-TS9 E2E
  entry point).
- **Testability seam: tests never read or mutate the environment variable.** CI sets `CLOUDSTRAP_TEST_SQL` while
  **every** executable runs, including the companion suite. A test asserting "unset → LocalDB" through the public
  property would therefore fail in CI, and mutating a process-wide variable is the very defect the spec drops (P2-34).
  The composition is two `internal static` pure functions, both reached through `<InternalsVisibleTo
  Include="Cloudstrap.TestInfrastructure.Tests" />`:
  - `ComposeConnectionString(string? overrideConnectionString, string catalog)`: `SqlConnectionStringBuilder` over
    the override, or over the LocalDB default `Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;`,
    then `InitialCatalog = catalog`. `SqlConnectionStringBuilder` normalizes `Database=` and `Initial Catalog=`
    alike, so any catalog in the override is replaced.
  - `ComposeOverride(string? overrideConnectionString, string catalog)`.

  The public members pass `Environment.GetEnvironmentVariable(EnvironmentVariable)`.
- XML `<summary>` on every type and member (house style; docs are off under `src/Test`). The class summary states
  three things: the identity needs `CREATE DATABASE`; fixtures sharing one catalog must not run in parallel; and the
  Azure SQL `Authentication=Active Directory Default` override passes through untouched.

**(d) Per-suite catalog holder (Gate 1).** Each SQL suite owns exactly one instance in a new
`Infrastructure/TestDatabase.cs`:
`internal static class TestDatabase { public static SqlServerTestDatabase Instance { get; } = new("<Catalog>"); }`.
Catalogs per DD-3: `CloudstrapHangfireTests` (Hangfire, new), `CloudstrapMessagingTests` (Messaging),
`CloudstrapClaimCheckTests` (AzureBlob). Call sites change from `SqlServerTestDatabase.X` to `TestDatabase.Instance.X`.
This is the "concrete holder" the spec left to the planner.

**(e) Step order is forced by the links.** `Cloudstrap.Hangfire.Tests` compiles Messaging's
`SqlServerTestDatabase.cs` and `CapturingLoggerProvider.cs` through links:
- Hangfire leaves the SQL link **first** (Step 2), before Messaging deletes its copy (Step 3).
- Messaging's `CapturingLoggerProvider.cs` **survives Slice 1** because the Hangfire link still compiles it. Both go
  together in Step 6.

**(f) RED for repoint steps (Gate 1).** AC-TS8 freezes every pre-existing executable's count, so repoint steps
cannot add tests to the suites they repoint. Their RED is a **repo-hygiene tripwire in the companion suite**:
- `RepositoryHygieneTests` uses the `DependencyPinTests` `FindRepoRoot()` idiom.
- It scans files under the repo and excludes `bin/` and `obj/`.
- It excludes the companion project's own directory from content scans, because the tripwires necessarily contain
  the forbidden literals.
- Each tripwire fails before its step and passes after it.

Behavioral proof comes from the repointed suite itself: the same tests, the same count, green, on the new helper and
catalog. The tripwires are **permanent**. They keep copies, links and inline env-var reads from creeping back.

**(g) Log-capture translation, applied identically in every suite (Gates 2 and final).**

| Today | After |
|---|---|
| `CapturingLoggerProvider logs = new(); builder.Logging.AddProvider(logs);` | `FakeLogCollector logs = new(); builder.Logging.AddProvider(new FakeLoggerProvider(logs));` |
| `logs.Entries` (snapshot) | `logs.GetSnapshot()` |
| `CapturedLogEntry` / the `(LogLevel Level, string Message)` tuple | `FakeLogRecord` (`Category`, `Level`, `Message` = formatted message, `Exception`) |
| A harness property or parameter typed `CapturingLoggerProvider` (`TwoNodeHarness.ListenerLogs`, `DashboardTestApp.Logs`, `HangfireSqlHost.CreateBuilder(logs:)`, `HygieneHost.Logs`, the `DurableHost` / `BuildHost` out-params and tuples) | `FakeLogCollector` |
| Code that needs the provider object itself (e.g. `ClaimCheckStoreTests.CreateStore(blobs, provider)`) | `new FakeLoggerProvider(collector)` at that call, while the test keeps asserting over the collector |

Assertion predicates (`Where`/`Count`/`Single`/`Any`/`Has.None.Contains`, `Message + " " + Exception`) are carried
**verbatim**. Only type names and the snapshot accessor change.

Why counts keep their meaning: both hand-written providers return `IsEnabled => true`, and so does `FakeLogger` by
default. Level filtering happens in `LoggerFactory` before either provider sees an entry, so exact-count and
"never appears" assertions observe the same stream.

`using Microsoft.Extensions.Logging.Testing;` is added where needed. `Cloudstrap.Observability.Tests` is **not**
touched and does not reference the package (DD-2: its private sentinel `FakeLoggerProvider` would otherwise clash by
simple name). The shared library does **not** reference the package (spec: "Log capture has no Cloudstrap type").
Each of the seven suites references it directly.

**(h) Pin-time procedure for `Microsoft.Extensions.Diagnostics.Testing` (Step 6, Gate 2).** The planning session
had no network access. Before editing `src/Directory.Packages.props` the executor:
1. Confirms **10.8.0** on nuget.org and reads its net10.0 dependency group. The spec's evidence (2026-09-26):
   `Microsoft.Extensions.Logging ≥ 10.0.10`, `…Options.ConfigurationExtensions ≥ 10.0.10`,
   `Microsoft.Extensions.Telemetry.Abstractions ≥ 10.8.0`. The version is fixed by D-3 and **not** raised to 10.10.0,
   even if newer versions exist.
2. Adds the pin to the existing **test-only** ItemGroup next to `Microsoft.Extensions.Http.Resilience` 10.8.0. The
   comment states: MIT (Microsoft, `dotnet/extensions`), test projects only, never shipped, replaces six
   hand-written log-capture copies (deliverable #8 D-3), same `dotnet/extensions` train as the resilience pin, and
   10.10.0 needs the `Microsoft.Extensions.*` 10.0.12 family bump, which is a separate change.
3. Restores. If restore raises any NU1101–NU1608 / NU1901–NU1904 warning (fatal under `TreatWarningsAsErrors`), the
   executor **stops and reports**. No floor pin is added without the user.
4. Fills the Pin record below.

#### Pin record *(filled by the executor in Step 6; not a planning-time value)*

| Item | Value |
|---|---|
| `Microsoft.Extensions.Diagnostics.Testing` version pinned + net10.0 dependency group as read | **10.8.0** (MIT, `dotnet/extensions` @ `8f88b00`); net10.0 group: `Microsoft.Extensions.Telemetry.Abstractions` ≥ 10.8.0, `Microsoft.Extensions.Logging` ≥ 10.0.10, `Microsoft.Extensions.Options.ConfigurationExtensions` ≥ 10.0.10 — matches the spec's 2026-09-26 evidence. nuget.org also lists 10.9.0 and 10.10.0; not taken (D-3). |
| `Microsoft.Extensions.Telemetry.Abstractions` resolved version in the seven suites (expected 10.8.0, the version the resilience pin already brings) | 10.8.0 in the five Step-6 suites (`project.assets.json`); `Logging` / `Options.ConfigurationExtensions` stay 10.0.10 (Hangfire takes them from the ASP.NET Core shared framework). Auth suites (Step 7, 2026-11-22): OpenIdConnect and ClientCredentials both resolve `Diagnostics.Testing` 10.8.0, `Telemetry.Abstractions` 10.8.0, `Logging` / `Options.ConfigurationExtensions` 10.0.10. No `Microsoft.Extensions.*` family move. |
| Restore / audit warnings (expected none) | None (`dotnet restore src/Cloudstrap.sln`: no NU* output; build 0 warnings) |
| Verified on / source | 2026-10-05, nuget.org flat-container index + `10.8.0` nuspec |
| Regression evidence (the five repointed suites + every other exe at baseline count) | 2026-10-05: Hangfire 105, Messaging 62, AzureBlob 37, WebApi 109, Mvc 66; Observability 106 untouched; all 16 pre-existing unit exes = 926 and E2E = 68 at baseline; companion 62 |

---

## Slice 1: One shared test-infrastructure library, and every SQL-backed suite, the E2E suite and CI on their own catalog with one meaning of `CLOUDSTRAP_TEST_SQL` ⚠️ SHARED TEST CONTRACT

---

## Step 1: The shared SQL helper composes a per-suite connection string and refuses unsafe catalogs (AC-TS3 constructor; AC-TS4; AC-TS5; AC-TS6; AC-TS7 implementation; AC-TS11; AC-ASP2 tripwire; mechanic (c))

After this step:
- `new SqlServerTestDatabase("X")` yields a LocalDB connection string on catalog `X` when the override is unset.
- With the override set, it keeps the override's server, authentication and options and always targets `X`.
- An empty, system or unsafe catalog name throws before any SQL runs.
- The new library is in the solution, never packed, and referenced by no shipped package or demo app.
- No suite consumes it yet.

- [x] Done *(checked by the executor when VERIFY passes; user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `_plans/8-TestHelpers.md` *(modify)*: the **Baseline record** filled (mechanic (a)) **before** any code change.
- `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/Cloudstrap.TestInfrastructure.csproj` *(create)*:
  - `Microsoft.NET.Sdk`, `net10.0`, the `Cloudstrap.TestIdentityProvider.csproj` shape;
  - **no** packaging properties (inherits `IsPackable=false`; the name does not end in `.Tests`, so no NUnit/MTP);
  - version-less `PackageReference`s `Microsoft.Data.SqlClient`, `Microsoft.Extensions.Hosting`,
    `Microsoft.Extensions.Configuration` (all already pinned; the latter two are needed by Step 3's
    `CloudstrapTestHost`, referenced now so the closure is reviewed once);
  - `<InternalsVisibleTo Include="Cloudstrap.TestInfrastructure.Tests" />`.
- `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/SqlServerTestDatabase.cs` *(create, public sealed)*:
  mechanic (c) in full, including `ResetAsync`, `TableExistsAsync`, `ScalarAsync`, `WaitForScalarAsync` and
  `OverrideConnectionStringFor`.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/Cloudstrap.TestInfrastructure.Tests.csproj` *(create)*:
  `net10.0`, one `ProjectReference` → the library (the `Cloudstrap.TestIdentityProvider.Tests.csproj` shape).
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/SqlServerTestDatabaseTests.cs` *(create)*.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/RepositoryHygieneTests.cs` *(create)*: mechanic (f) scanner
  helpers (`FindRepoRoot`, file enumeration excluding `bin`/`obj`/the companion directory) plus this step's
  tripwires.
- `src/Cloudstrap.sln` *(modify)*:
  - a `TestInfrastructure` solution folder under `Test` containing the library (the `TestIdentityProvider` folder
    precedent);
  - the companion suite under `Test\UnitTest`.

**RED** *(first failure: the companion suite does not compile against the missing types; after that, real red
runs)*:
- `SqlServerTestDatabaseTests.cs` (SQL-free; only the internal pure seams and the constructor):
  - `ComposeConnectionString_OverrideUnset_IsLocalDbIntegratedTrustedOnTheSuiteCatalog` (AC-TS4). The assertion
    parses the result with `SqlConnectionStringBuilder`, never by string equality: `DataSource ==
    @"(localdb)\MSSQLLocalDB"`, `IntegratedSecurity`, `TrustServerCertificate`, `InitialCatalog == "X"`.
  - `ComposeConnectionString_OverrideSet_KeepsServerAuthenticationAndOptions` (AC-TS5): the CI-shaped
    `Server=localhost,1433;User Id=sa;Password=…;TrustServerCertificate=true;` keeps `DataSource`, `UserID`,
    `Password` and `TrustServerCertificate`.
  - `ComposeConnectionString_OverrideNamesACatalog_TheSuiteCatalogReplacesIt`, with `[TestCase]`s:
    `Database=CloudstrapCi`, `Initial Catalog=CloudstrapCi`, `Database=master` (spec edge case: never targets
    `master`).
  - `ComposeConnectionString_OverrideWithoutACatalog_TheSuiteCatalogIsAdded`
  - `ComposeConnectionString_EntraIdAuthenticationOverride_IsPassedThroughUntouched`:
    `Authentication=Active Directory Default` survives.
  - `ComposeOverride_OverrideUnsetOrWhiteSpace_ReturnsNull`, with `[TestCase(null)]`, `[TestCase("")]`,
    `[TestCase("  ")]`.
  - `ComposeOverride_OverrideSet_ReturnsTheOverrideOnTheGivenCatalog` (the AC-TS9 rule for `CloudstrapDemo`).
  - `Constructor_InvalidCatalog_ThrowsArgumentExceptionNamingTheParameter` (AC-TS6), with `[TestCase]`s: `""`,
    `"  "`, `"master"`, `"MASTER"`, `"model"`, `"msdb"`, `"TempDb"`, `"1Catalog"`, `"_Catalog"`,
    `"Cloudstrap-Tests"`, `"Cloudstrap Tests"`, `"x];DROP DATABASE y;--"`. Each asserts `ParamName == "catalog"`.
    No SQL is reachable from the constructor.
  - `Constructor_NullCatalog_ThrowsArgumentNullException`
  - `Constructor_ValidCatalog_ExposesTheCatalog_AndAConnectionStringTargetingIt`. This holds in either environment
    mode, because the catalog is always replaced. That makes it the one assertion on the public
    `ConnectionString` that is CI-safe.
  - `OverrideConnectionStringFor_InvalidCatalog_Throws` (validation applies to the E2E entry point too).
  - `EnvironmentVariable_IsCloudstrapTestSql`
- `RepositoryHygieneTests.cs` (AC-TS11, AC-ASP2):
  - `TestInfrastructureLibrary_LivesUnderSrcTest_AndIsNotATestExecutable`: the csproj exists at the D-7 path, its
    file name does not end in `.Tests`, and it sets no `IsPackable` / `GeneratePackageOnBuild` true.
  - `TestInfrastructureLibrary_ReferencesNoTestFrameworkMockingOrAspirePackage`: no `PackageReference` whose
    `Include` starts with `NUnit`, `Moq`, `Aspire.`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore`.
  - `NoShippedPackageOrDemoApp_ReferencesTheTestInfrastructure`: no `src/Cloudstrap.*/**/*.csproj` and no
    `src/demo/**/*.csproj` contains `Cloudstrap.TestInfrastructure`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the companion suite does not compile against the missing types
  src\Test\UnitTest\Cloudstrap.TestInfrastructure.Tests\bin\Debug\net10.0\Cloudstrap.TestInfrastructure.Tests.exe --filter "SqlServerTestDatabaseTests|RepositoryHygieneTests"
  ```

**GREEN**: the Scope items, per mechanic (c). `ResetAsync` / `TableExistsAsync` / `ScalarAsync` /
`WaitForScalarAsync` port today's bodies verbatim apart from three changes: the instance catalog, own-pool
`ClearPool`, and token flow-through. They are exercised against LocalDB from Step 2 onwards.

**DB changes**: none.

**VERIFY** *(when all green, mark `Done` and continue straight to Step 2)*:
1. Companion exe → all pass. Per-suite connection strings with the D-4 override rule, and catalog validation, now
   exist as one tested helper. That is new behavior.
2. Full-suite check (mechanic (b)): all 16 pre-existing exes and E2E at **baseline counts**; zero warnings;
   `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → no `Cloudstrap.TestInfrastructure*.nupkg` anywhere under `src/`
   (AC-TS11; the CI preview publish is unaffected).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2: The Hangfire storage tests run on their own `CloudstrapHangfireTests` catalog through the shared helper, and the static job-id log is `TaskRunLog` (AC-TS3 Hangfire; AC-TS7 exercised; DD-3; DD-5; mechanics (d), (e))

After this step:
- `Cloudstrap.Hangfire.Tests` no longer links Messaging's SQL helper, and no longer resets the Messaging suite's
  catalog. Its storage and health-check tests drop and recreate `CloudstrapHangfireTests` through the shared helper.
- The suite's static job-id log no longer shares a simple name with the hoisted recorder.
- The `CapturingLoggerProvider` link stays until Step 6 (mechanic (e)).

- [x] Done

**Scope**:
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Cloudstrap.Hangfire.Tests.csproj` *(modify)*:
  - add a `ProjectReference` → `..\..\TestInfrastructure\Cloudstrap.TestInfrastructure\Cloudstrap.TestInfrastructure.csproj`;
  - remove the `SqlServerTestDatabase.cs` `<Compile … Link=…>` line (26);
  - reword the DD-8 comment to cover the one remaining link ("the log-capturing provider stays linked until #8
    Step 6 replaces it").
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Infrastructure/TestDatabase.cs` *(create)*: mechanic (d), catalog
  `CloudstrapHangfireTests`.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Infrastructure/HangfireSqlHost.cs` *(modify)*:
  `TestDatabase.Instance.ConnectionString`, plus `using Cloudstrap.TestInfrastructure;`. The
  `Cloudstrap.Messaging.Tests.Infrastructure` using stays for the still-linked provider.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/SqlServerStorageTests.cs`, `HealthCheckTests.cs` *(modify)*:
  `TestDatabase.Instance.ResetAsync()` / `.TableExistsAsync(…)`; doc comments name the catalog.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Fixtures/InvocationRecorder.cs` → **renamed**
  `Fixtures/TaskRunLog.cs` *(class `TaskRunLog`, same static members `Runs` / `Record(string)` / `Reset()`)*.
- `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Fixtures/Tasks/RecordingTask.cs`, `Fixtures/Tasks/NoOverlapGuardTask.cs`,
  `RecurringTaskRunnerTests.cs`, `SqlServerStorageTests.cs` *(modify)*: `InvocationRecorder.` → `TaskRunLog.`.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/RepositoryHygieneTests.cs` *(modify)*.

**RED** *(mechanic (f))*:
- `RepositoryHygieneTests.cs`:
  - `SqlSuite_DeclaresItsOwnCatalog_ThroughTheSharedHelper(string suite, string catalog)`, with **this step's case
    only**: `[TestCase("Cloudstrap.Hangfire.Tests", "CloudstrapHangfireTests")]`. Under that suite directory:
    - exactly one `new("CloudstrapHangfireTests")` / `new SqlServerTestDatabase("CloudstrapHangfireTests")`
      declaration;
    - no `SqlServerTestDatabase.cs` file;
    - no csproj `Compile` item naming `SqlServerTestDatabase`.

    Fails today on the link.
  - `HangfireSuite_HasNoTypeNamedInvocationRecorder` (DD-5). Fails today.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.TestInfrastructure.Tests\bin\Debug\net10.0\Cloudstrap.TestInfrastructure.Tests.exe --filter "RepositoryHygieneTests"
  ```

**GREEN**: the Scope items. No Hangfire test is added, removed or changed in meaning.

**DB changes**: none (the suite now creates `CloudstrapHangfireTests` at runtime).

**VERIFY**:
1. Companion exe → all pass.
2. `Cloudstrap.Hangfire.Tests.exe` → all pass at its **baseline count**, including the LocalDB `SqlServerStorageTests`
   and `HealthCheckTests`. This is the behavioral proof: the real round trips now run on the shared helper's
   `ResetAsync` and the new catalog.
3. `Cloudstrap.Messaging.Tests.exe` → all pass at baseline. It still uses its own copy and is no longer reset from
   under it.
4. Optional evidence (recorded): `sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "SELECT name FROM sys.databases"` lists
   `CloudstrapHangfireTests`.
5. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 3: The Messaging suite runs on the shared SQL helper, recorder and host builder (AC-TS3 Messaging; AC-TS7 exercised; D-6 hoist of `InvocationRecorder` + `CloudstrapTestHost`)

After this step:
- The shared library also offers `InvocationRecorder` (awaitable, Wolverine-free) and `CloudstrapTestHost` (the
  neutral `contoso`/`orders`/`worker` settings + generic-host builder).
- `Cloudstrap.Messaging.Tests` uses all three shared types on its own `CloudstrapMessagingTests` catalog.
- Its own copies of the SQL helper, the host builder and the recorder are deleted. Its `CapturingLoggerProvider.cs`
  stays until Step 6 (mechanic (e)).

- [x] Done

**Scope**:
- `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/InvocationRecorder.cs` *(create, public sealed,
  `IDisposable`)*:
  - hoisted verbatim (`Received`, `Record(object)`, `Dispose`);
  - `WaitForNextAsync(TimeSpan timeout, CancellationToken cancellationToken = default)`. The token is the planner's
    call per the sketch; existing call sites compile unchanged. It passes `timeout` + token to
    `SemaphoreSlim.WaitAsync`, then throws `TimeoutException` on timeout exactly as today;
  - guard on `Record(null)`.
- `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/CloudstrapTestHost.cs` *(create, public static)*:
  - `public const string WorkloadName = "contoso-orders-worker";`
  - `public static Dictionary<string, string?> ValidSettings()` (a fresh dictionary per call);
  - `public static HostApplicationBuilder CreateBuilder(IDictionary<string, string?> settings)` (guard,
    `Host.CreateApplicationBuilder()` + `AddInMemoryCollection`). This is the `MessagingTestHost` body with a
    neutral summary.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/InvocationRecorderTests.cs`,
  `CloudstrapTestHostTests.cs` *(create)*.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Cloudstrap.Messaging.Tests.csproj` *(modify)*: `ProjectReference`
  → the library.
- `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Infrastructure/TestDatabase.cs` *(create)*: catalog
  `CloudstrapMessagingTests`.
- **Delete**: `Cloudstrap.Messaging.Tests/Infrastructure/SqlServerTestDatabase.cs`,
  `Infrastructure/MessagingTestHost.cs`, `Fixtures/InvocationRecorder.cs`.
- *(modify; call sites from the on-disk grep)*:
  - `SqlServerDurabilityTests.cs` and `TransactionalMessagingTests.cs`: `TestDatabase.Instance.*`;
    `ScalarAsync(sql, [("@id", x)])` (mechanic (c)).
  - `MessagingTestHost.*` → `CloudstrapTestHost.*` and/or the `InvocationRecorder` namespace move, in:
    `AzureServiceBusConfigTests.cs`, `AutoProvisionTests.cs`, `RetryPolicyTests.cs`, `RegistrationTests.cs`,
    `ObservabilityTests.cs`, `LocalNodeTests.cs`, `CorrelationFlowTests.cs`, `ConventionTests.cs`,
    `CorrelationEnforcementTests.cs`, `EngineContributionTests.cs`, `Fixtures/{FlakyCommand,
    TransactionalFixtures, CorrelationFixtures, PoisonCommand, PingCommand}.cs`.
  - `using Cloudstrap.TestInfrastructure;` wherever needed.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/RepositoryHygieneTests.cs` *(modify)*.

**RED**:
- `InvocationRecorderTests.cs`:
  - `WaitForNextAsync_AfterRecord_ReturnsThatMessage`
  - `WaitForNextAsync_NothingRecorded_ThrowsTimeoutException` (a short timeout)
  - `WaitForNextAsync_TokenCancelled_ThrowsOperationCanceledException`
  - `Received_ListsRecordedMessagesInArrivalOrder`
  - `Record_NullMessage_ThrowsArgumentNullException`
- `CloudstrapTestHostTests.cs`:
  - `ValidSettings_IsTheNeutralContosoOrdersWorkerApplicationSection`: the three keys and values.
  - `ValidSettings_ReturnsAFreshDictionaryPerCall`: a mutation does not leak.
  - `CreateBuilder_ConfigurationCarriesTheGivenSettings`
  - `CreateBuilder_NullSettings_ThrowsArgumentNullException`
  - `WorkloadName_IsContosoOrdersWorker`
- `RepositoryHygieneTests.cs`: add `[TestCase("Cloudstrap.Messaging.Tests", "CloudstrapMessagingTests")]` to
  `SqlSuite_DeclaresItsOwnCatalog_ThroughTheSharedHelper`. Fails on the local `SqlServerTestDatabase.cs`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = InvocationRecorderTests/CloudstrapTestHostTests do not compile
  src\Test\UnitTest\Cloudstrap.TestInfrastructure.Tests\bin\Debug\net10.0\Cloudstrap.TestInfrastructure.Tests.exe --filter "InvocationRecorderTests|CloudstrapTestHostTests|RepositoryHygieneTests"
  ```

**GREEN**: the Scope items. Wolverine resolves the fixture handlers' `InvocationRecorder` parameter from the host's
singleton exactly as before. Only its namespace moved. The type is public in the library, so the public Wolverine
fixture handlers still compile.

**DB changes**: none.

**VERIFY**:
1. Companion exe → all pass.
2. `Cloudstrap.Messaging.Tests.exe` → all pass at its **baseline count**, including the LocalDB durability and
   transactional tests on the shared helper.
3. `Cloudstrap.Hangfire.Tests.exe` → all pass at baseline (its link to Messaging's `CapturingLoggerProvider.cs`
   still compiles).
4. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4: The AzureBlob suite runs on the shared helpers, and every hoisted type exists exactly once (AC-TS1 types; AC-TS3 AzureBlob; AC-TS7 exercised)

After this step:
- `Cloudstrap.Messaging.AzureBlob.Tests` uses the shared `SqlServerTestDatabase` (catalog
  `CloudstrapClaimCheckTests`), `InvocationRecorder` and `CloudstrapTestHost`. Its three copies are deleted.
- Across `src/`, `SqlServerTestDatabase`, the instance `InvocationRecorder` and `CloudstrapTestHost` are each
  declared once, and `MessagingTestHost` is declared nowhere.

- [x] Done

**Scope**:
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Cloudstrap.Messaging.AzureBlob.Tests.csproj` *(modify)*:
  `ProjectReference` → the library. The "Microsoft.Data.SqlClient arrives transitively" comment is reworded,
  because it now also arrives through the library.
- `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Infrastructure/TestDatabase.cs` *(create)*: catalog
  `CloudstrapClaimCheckTests`.
- **Delete**: `Cloudstrap.Messaging.AzureBlob.Tests/Infrastructure/SqlServerTestDatabase.cs`,
  `Infrastructure/MessagingTestHost.cs`, `Fixtures/InvocationRecorder.cs`. Its `CapturingLoggerProvider.cs` is
  untouched until Step 6.
- *(modify)*:
  - `ClaimCheckRoundTripTests.cs`, `OutboxInterplayTests.cs`, `MissingPayloadTests.cs` and
    `Infrastructure/TwoNodeHarness.cs`: `TestDatabase.Instance.*`; `ScalarAsync(sql, [("@id", x)])`.
  - `RegistrationTests.cs`, `ClientResolutionTests.cs`, `OptionsValidationTests.cs`, `Fixtures/LargePayloadCommand.cs`,
    `Fixtures/ExportHandlers.cs`: `CloudstrapTestHost` / the recorder namespace.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/RepositoryHygieneTests.cs` *(modify)*.

**RED**:
- `RepositoryHygieneTests.cs`:
  - add `[TestCase("Cloudstrap.Messaging.AzureBlob.Tests", "CloudstrapClaimCheckTests")]` to
    `SqlSuite_DeclaresItsOwnCatalog_ThroughTheSharedHelper`;
  - `SqlSuites_NeverShareACatalog`: every `new SqlServerTestDatabase("…")` / holder catalog literal found under
    `src/Test/UnitTest` is distinct, and the set is exactly {`CloudstrapMessagingTests`, `CloudstrapClaimCheckTests`,
    `CloudstrapHangfireTests`};
  - `SharedHelper_IsDeclaredExactlyOnce_InTheTestInfrastructure(string declaration)`, with `[TestCase]`s
    `"class SqlServerTestDatabase"`, `"class InvocationRecorder"`, `"class CloudstrapTestHost"`. A case-sensitive
    scan of `src/**/*.cs` finds exactly one declaration, located in the library. Fails today on the AzureBlob
    copies.
  - `MessagingTestHost_IsDeclaredNowhere`
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.TestInfrastructure.Tests\bin\Debug\net10.0\Cloudstrap.TestInfrastructure.Tests.exe --filter "RepositoryHygieneTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none.

**VERIFY**:
1. Companion exe → all pass.
2. `Cloudstrap.Messaging.AzureBlob.Tests.exe` → all pass at its **baseline count**, including the LocalDB claim-check
   round trips, the outbox interplay and the missing-payload tests on the shared helper.
3. Full-suite check (mechanic (b)): all green at baseline counts; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 5: The E2E suite forwards the SQL override only through the shared helper on `CloudstrapDemo`, and CI's override carries no catalog (AC-TS9; AC-TS8 both modes; D-4; D-5) ⚠️ shared test contract

After this step:
- With `CLOUDSTRAP_TEST_SQL` unset, the E2E suite forwards nothing, and the demo `appsettings.json` LocalDB
  `CloudstrapDemo` stays authoritative.
- With it set, every SQL-needing demo host the suite boots (Api, Worker ×4 fixtures, BlazorServer ×2 fixtures)
  receives `--ConnectionStrings:DefaultConnection=` composed by the shared helper for `CloudstrapDemo`.
- CI's override supplies server and credentials only, and its comment names every honoring suite.
- Local and CI runs now use the same database names.

- [x] Done

**Scope**:
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/Cloudstrap.Demo.E2E.Tests.csproj` *(modify)*: `ProjectReference` →
  `..\..\TestInfrastructure\Cloudstrap.TestInfrastructure\Cloudstrap.TestInfrastructure.csproj`.
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/Infrastructure/DemoDatabase.cs` *(create, internal static)*. This is the
  single forwarding site inside the E2E suite:
  - `public const string Catalog = "CloudstrapDemo";` (the demo appsettings name, DD-3);
  - `public static void AddConnectionStringOverride(ICollection<string> arguments)` appends
    `"--ConnectionStrings:DefaultConnection=" + override` when
    `SqlServerTestDatabase.OverrideConnectionStringFor(Catalog)` is non-null, and appends nothing otherwise.
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/{E2eFixture.cs, WorkerHostTests.cs, MessagingTests.cs, ClaimCheckTests.cs,
  HangfireTests.cs, BlazorServerTests.cs}` *(modify)*:
  - the seven inline `Environment.GetEnvironmentVariable("CLOUDSTRAP_TEST_SQL")` blocks become
    `DemoDatabase.AddConnectionStringOverride(arguments)`;
  - `HangfireTests` calls it for both its Worker and its BlazorServer argument lists;
  - `BlazorServerTests` switches from `null` arguments to a list when needed, behavior otherwise unchanged;
  - the `E2eFixture.cs:84` comment is reworded (override = server/credentials; catalog `CloudstrapDemo`).
  - The `CLOUDSTRAP_TEST_BLOB` blocks are **not** touched (spec Out of Scope).
- `.github/workflows/ci.yml` *(modify, lines 76–77)*:
  - the override becomes `"Server=localhost,1433;User Id=sa;Password=Cloudstrap_CI_P4ssw0rd!;TrustServerCertificate=true;"`;
  - the comment reads, in substance: "Server, credentials and options only (#8 D-4): each suite sets its own catalog
    (Messaging → CloudstrapMessagingTests, Messaging.AzureBlob → CloudstrapClaimCheckTests, Hangfire →
    CloudstrapHangfireTests), and the E2E suite forwards it to the demo hosts on CloudstrapDemo, which they create."
- `.github/workflows/release.yml` *(modify, lines 94–95)*: the same string and comment. **Planner interpretation,
  confirm at Gate 1**: D-4 / DD-7 name only `ci.yml`, but `release.yml` repeats CI's test leg ("a release never ships
  what CI didn't verify") with the same now-inert `Database=CloudstrapCi` and an equally stale comment ("Messaging SQL
  fixtures"). Leaving it would make the two workflows describe different contracts.
- `src/demo/README.md` *(modify, the "SQL Server (spec D-3)" harness bullet at line 144, doc-only)*: the override
  supplies server/credentials, and the E2E suite forwards it as `ConnectionStrings:DefaultConnection` on
  `CloudstrapDemo` through the shared helper. **Planner interpretation, confirm at Gate 1**: a one-line doc
  correction outside DD-7's literal file list, made because this bullet documents the E2E harness contract this step
  changes. No demo code, appsettings or per-app README changes. The per-app READMEs' generic "overrides the
  connection string" wording stays accurate enough and is listed as a follow-up.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/RepositoryHygieneTests.cs` *(modify)*.

**RED** *(mechanic (f); D-5 forbids a new E2E test, so the RED lives in the companion suite)*:
- `RepositoryHygieneTests.cs`:
  - `E2eSuite_ReadsTheSqlOverrideOnlyThroughTheSharedHelper`: no `.cs` file under `src/Test/E2E` contains the literal
    `CLOUDSTRAP_TEST_SQL`, and exactly one file (`Infrastructure/DemoDatabase.cs`) calls
    `OverrideConnectionStringFor`. Fails today (seven sites).
  - `Workflow_SqlOverride_CarriesNoCatalog(string workflow)`, with `[TestCase("ci.yml")]`, `[TestCase("release.yml")]`:
    the `CLOUDSTRAP_TEST_SQL:` value parsed with `SqlConnectionStringBuilder` has an empty `InitialCatalog`. Fails
    today (`CloudstrapCi`).
  - `CiWorkflow_SqlOverrideComment_NamesEveryHonoringSuite`: the comment line above the override in `ci.yml`
    mentions `Messaging`, `AzureBlob`, `Hangfire` and `E2E`. Fails today: it omits AzureBlob.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.TestInfrastructure.Tests\bin\Debug\net10.0\Cloudstrap.TestInfrastructure.Tests.exe --filter "RepositoryHygieneTests"
  ```

**GREEN**: the Scope items.

**DB changes**: none. Under the override the demo hosts create `CloudstrapDemo` on the override's server, which is
idempotent (spec edge case). The E2E suite never resets it, as today.

**VERIFY** *(both SQL modes, AC-TS8)*:
1. Companion exe → all pass.
2. **LocalDB mode** (`CLOUDSTRAP_TEST_SQL` unset): full-suite check (mechanic (b)). Every unit exe and the **E2E
   exe** are green at **baseline counts**. This is the D-5 proof: the unchanged E2E suite runs on the shared helper.
3. **Override mode, locally**: the override points at LocalDB and **deliberately carries a misleading catalog**, to
   prove the catalog is ignored:
   ```powershell
   $env:CLOUDSTRAP_TEST_SQL = 'Server=(localdb)\MSSQLLocalDB;Database=CloudstrapCi;Integrated Security=true;TrustServerCertificate=true;'
   src\Test\UnitTest\Cloudstrap.Messaging.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.Tests.exe
   src\Test\UnitTest\Cloudstrap.Messaging.AzureBlob.Tests\bin\Debug\net10.0\Cloudstrap.Messaging.AzureBlob.Tests.exe
   src\Test\UnitTest\Cloudstrap.Hangfire.Tests\bin\Debug\net10.0\Cloudstrap.Hangfire.Tests.exe
   src\Test\UnitTest\Cloudstrap.TestInfrastructure.Tests\bin\Debug\net10.0\Cloudstrap.TestInfrastructure.Tests.exe
   src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe
   Remove-Item Env:CLOUDSTRAP_TEST_SQL
   ```
   - All green at baseline counts. The companion suite is green too, which proves its tests are independent of the
     environment.
   - Evidence (recorded): drop any pre-existing `CloudstrapCi` first. Afterwards, `sys.databases` lists
     `CloudstrapMessagingTests`, `CloudstrapClaimCheckTests`, `CloudstrapHangfireTests` and `CloudstrapDemo`, and no
     `CloudstrapCi` was created by this run.
4. `dotnet format` exit 0.
5. *(CI evidence at push time, not blocking this step)*: the first CI run after push is green on the Linux SQL
   container with the catalog-free override.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE: end of Slice 1, shared library + per-suite catalogs + one override contract *(covers Steps 1–5)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval. Do not start the next step.*

⚠️ **Risk areas at this gate**:
- **Shared test contract (D-4)**: review the new `CLOUDSTRAP_TEST_SQL` meaning end to end:
  - mechanic (c)'s composition;
  - the `ci.yml` diff;
  - the E2E `DemoDatabase` forwarding;
  - the override-mode evidence (no `CloudstrapCi` created; four distinct catalogs).
- **Planner interpretations to confirm**:
  - `release.yml` edited alongside `ci.yml`;
  - the one-line `src/demo/README.md` harness-bullet correction;
  - `ScalarAsync`'s optional parameter list instead of `params` after the token (mechanic (c));
  - the internal pure-composition seam + `InternalsVisibleTo`, so that no test touches the environment variable;
  - the `TestDatabase.Instance` holder name (mechanic (d));
  - companion-suite repo-hygiene tripwires as the RED of repoint steps (mechanic (f));
  - AC-TS7's own-pool `ClearPool` proven by the LocalDB suites plus code review only, with no dedicated test,
    because DD-6 keeps the companion suite SQL-free.

- [x] Behavioral verification:
  - The companion suite is green: `SqlServerTestDatabaseTests` (AC-TS4/5/6), `InvocationRecorderTests`,
    `CloudstrapTestHostTests`, and `RepositoryHygieneTests` (AC-TS11; per-suite catalogs; types declared once; E2E
    forwarding only through the helper; workflow overrides catalog-free).
  - Hangfire, Messaging and AzureBlob are green at baseline counts on their own catalogs, in **both** SQL modes.
  - The E2E suite is green at its baseline count in **both** modes, with no new E2E test (D-5).
  - Every other executable is at its baseline count.
- [x] Code review:
  - the library csproj closure (SqlClient + Hosting + Configuration only; no NUnit/Moq/Aspire/EF/Mvc.Testing);
  - the validated, bracket-quoted DDL in `ResetAsync`;
  - no `ClearAllPools`;
  - `CancellationToken` on every async member;
  - the deleted copies (`SqlServerTestDatabase` ×2, `MessagingTestHost` ×2, `InvocationRecorder` ×2), the removed
    Hangfire SQL link, the `TaskRunLog` rename;
  - the seven E2E sites reduced to one helper call each;
  - the `CLOUDSTRAP_TEST_BLOB` code untouched.
- [x] User approved: implementation may continue past this gate.

---

## Slice 2: Every suite captures logs with Microsoft's fake logger, and Cloudstrap owns no log-capture code ⚠️ NEW TEST-ONLY DEPENDENCY

---

## Step 6: The record-shape suites capture logs through `FakeLoggerProvider` / `FakeLogCollector`, and no source file is linked across test projects any more (AC-TS1 links; AC-TS2 record shape; D-3; mechanics (g), (h)) ⚠️ dependency add

After this step:
- `Microsoft.Extensions.Diagnostics.Testing` 10.8.0 is pinned as a test-only dependency.
- The five record-shape suites (Hangfire, Messaging, Messaging.AzureBlob, WebApi, Mvc) assert their log lines over
  `FakeLogCollector.GetSnapshot()` with unchanged predicates.
- Their four `CapturingLoggerProvider` copies and the Hangfire link are deleted.
- `Cloudstrap.Hangfire.Tests.csproj` carries no `<Compile … Link=…>` at all.

- [x] Done

**Scope**:
- `src/Directory.Packages.props` *(modify)*: mechanic (h). `<PackageVersion Include="Microsoft.Extensions.Diagnostics.Testing" Version="10.8.0" />`
  goes in the existing test-only ItemGroup, and that group's header comment is extended.
- `_plans/8-TestHelpers.md` *(modify)*: the **Pin record** filled.
- *(modify, csproj)*: add a version-less `<PackageReference Include="Microsoft.Extensions.Diagnostics.Testing" />` to
  `Cloudstrap.Hangfire.Tests`, `Cloudstrap.Messaging.Tests`, `Cloudstrap.Messaging.AzureBlob.Tests`,
  `Cloudstrap.WebApi.Tests` and `Cloudstrap.Mvc.Tests`. In `Cloudstrap.Hangfire.Tests.csproj`, also **remove** the
  remaining `CapturingLoggerProvider.cs` link, its `ItemGroup` and the DD-8 comment.
- **Delete**: `Cloudstrap.Messaging.Tests/Infrastructure/CapturingLoggerProvider.cs`,
  `Cloudstrap.Messaging.AzureBlob.Tests/Infrastructure/CapturingLoggerProvider.cs`,
  `Cloudstrap.WebApi.Tests/Infrastructure/CapturingLoggerProvider.cs`,
  `Cloudstrap.Mvc.Tests/Infrastructure/CapturingLoggerProvider.cs`.
- *(modify, mechanic (g); call sites from the on-disk grep)*:
  - **Hangfire**: `SchedulingServiceTests.cs`, `RecurringTaskRunnerTests.cs`, `RecurringJobsSchedulerTests.cs`,
    `SqlServerStorageTests.cs`, `DashboardAuthorizationTests.cs`, `Infrastructure/HangfireSqlHost.cs`,
    `Infrastructure/DashboardTestApp.cs`. Each drops the `Cloudstrap.Messaging.Tests.Infrastructure` using.
  - **Messaging**: `AutoProvisionTests.cs`, `CorrelationEnforcementTests.cs`, `ConventionTests.cs`,
    `SqlServerDurabilityTests.cs`.
  - **AzureBlob**: `ClaimCheckStoreTests.cs`, `ClientResolutionTests.cs`, `RegistrationTests.cs`,
    `MissingPayloadTests.cs`, `Infrastructure/TwoNodeHarness.cs`.
  - **WebApi**: `ProblemDetailsExceptionTests.cs`.
  - **Mvc**: `ExceptionHandlingTests.cs`.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/DependencyPinTests.cs` *(create)* and
  `RepositoryHygieneTests.cs` *(modify)*.

**RED**:
- `DependencyPinTests.cs` (the `Cloudstrap.Hangfire.Tests` props-reader idiom):
  - `DiagnosticsTesting_IsPinnedAt10_8_0`. Fails today (no pin).
  - `NoAspirePackage_IsPinned` (the AC-ASP2 tripwire for this deliverable's pin set).
- `RepositoryHygieneTests.cs`:
  - `NoTestProject_LinksASourceFileFromAnotherProject`: no `src/Test/**/*.csproj` contains a `Compile` item with a
    `Link` attribute (AC-TS1). Fails today on the Hangfire link.
  - `RecordShapeSuite_DeclaresNoLogCaptureProvider(string suite)`, with `[TestCase]`s for the five suites: no
    `class CapturingLoggerProvider` and no `CapturedLogEntry` under the suite directory, and no `Link` naming it.
    Fails today.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.TestInfrastructure.Tests\bin\Debug\net10.0\Cloudstrap.TestInfrastructure.Tests.exe --filter "DependencyPinTests|RepositoryHygieneTests"
  ```

**GREEN**: run mechanic (h) and record the Pin record, then the Scope items per mechanic (g). **No assertion
predicate is weakened, removed or re-scoped.** If any assertion would need a different meaning to pass (e.g. a count
differs), the executor stops and reports it instead of adjusting the test.

**DB changes**: none.

**VERIFY**:
1. Companion exe → all pass.
2. Hangfire, Messaging, AzureBlob, WebApi and Mvc exes → all pass at their **baseline counts**. Particular attention
   goes to:
   - Category: `ClientResolutionTests`, `RegistrationTests`.
   - Message + exception: `SqlServerDurabilityTests`, `CorrelationEnforcementTests`, `MissingPayloadTests`.
   - A single exception: `RecurringTaskRunnerTests`.
   - Absence: AzureBlob `RegistrationTests`, `SchedulingServiceTests`.
3. `Cloudstrap.Observability.Tests.exe` at baseline, untouched (DD-2).
4. Full-suite check (mechanic (b)): every unit exe + E2E green at baseline counts; zero warnings, including no
   NU* restore warning; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE: dependency review, `Microsoft.Extensions.Diagnostics.Testing` adopted *(covers Step 6; extra gate after a ⚠️ dependency-update step)*

*Executor: STOP here. Present the results and WAIT for user approval. Do not start Step 7.*

⚠️ **Risk areas at this gate**:
- **Dependency update (D-3)**: review:
  - the `Directory.Packages.props` diff and its comment;
  - the **filled Pin record** (version, net10.0 dependency group, resolved `Telemetry.Abstractions`, no restore/audit
    warnings);
  - confirmation that the `Microsoft.Extensions.*` family stays at 10.0.10 (no family bump; 10.10.0 is a separate
    change).
- **Assertion meaning (AC-TS2)**: review the five suites' diffs. Only type names (`FakeLogCollector`,
  `FakeLogRecord`) and `Entries` → `GetSnapshot()` changed; every predicate is verbatim.

- [x] Behavioral verification:
  - `DependencyPinTests.DiagnosticsTesting_IsPinnedAt10_8_0` and
    `RepositoryHygieneTests.NoTestProject_LinksASourceFileFromAnotherProject` /
    `RecordShapeSuite_DeclaresNoLogCaptureProvider` pass.
  - The five repointed exes are green at baseline counts, and every other exe + E2E is at baseline.
  - *Evidence (2026-11-21)*: `dotnet build src/Cloudstrap.sln` 0 warnings; all 17 unit exes pass; E2E 68/68 pass;
    `dotnet format --verify-no-changes` clean.
- [x] Code review: the deleted copies (four files + the last Hangfire link); the harness types retyped to
  `FakeLogCollector`; no `Cloudstrap.TestInfrastructure` reference to the package (the library owns no log-capture
  type); `Cloudstrap.Observability.Tests` untouched.
- [x] User approved: implementation may continue past this gate. *(Owner, 2026-11-21: new test-only dependency
  `Microsoft.Extensions.Diagnostics.Testing` 10.8.0 and the five rewritten suites approved.)*

---

## Step 7: The tuple-shape auth suites capture logs through the fake logger, and the closure guards make the consolidation permanent (AC-TS2 complete; AC-TS10; AC-TS12; mechanic (g))

After this step:
- `Cloudstrap.Authentication.OpenIdConnect.Tests` and `Cloudstrap.Authentication.ClientCredentials.Tests` count and
  inspect log lines over `FakeLogCollector`.
- The OpenIdConnect copy is deleted, and the nested copy is removed from `TestDoubles.cs`. The rest of that file
  (handlers, caches, DD-1) stays.
- No `CapturingLoggerProvider` or `CapturedLogEntry` remains anywhere in `src/`.
- Permanent guards keep the dropped source members, `Mvc.Testing`, EF Core and enterprise identifiers out.

- [x] Done *(2026-11-22. RED: `NoCloudstrapOwnedLogCaptureType_RemainsAnywhere` failed on 9 auth-suite files; the two
  characterization tripwires were green on arrival. VERIFY: companion 65/65; OpenIdConnect 66/66; ClientCredentials
  41/41; every other unit exe at baseline (unit total 926 + companion 65); E2E 68/68; build 0 code warnings; `dotnet
  format --verify-no-changes` exit 0. Grep evidence: in files touched since `03eee2b`, `Nihdi|Riziv` /
  `Security:EnableAuthentication` / `"UnitTest"` appear only in the AC-TS12 tripwire's own pattern, and `UnitTest` only as
  the `src/Test/UnitTest` path / solution-folder name. `OpenIdConnect.Tests/Infrastructure/CapturingLoggerProvider.cs`
  is deleted (`git rm`, Step 7 verify correction).)*

**Scope**:
- *(modify, csproj)*: add a version-less `<PackageReference Include="Microsoft.Extensions.Diagnostics.Testing" />` to
  `Cloudstrap.Authentication.OpenIdConnect.Tests` and `Cloudstrap.Authentication.ClientCredentials.Tests`.
- **Delete**: `Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/CapturingLoggerProvider.cs`.
- `Cloudstrap.Authentication.ClientCredentials.Tests/Infrastructure/TestDoubles.cs` *(modify)*: remove the nested
  `CapturingLoggerProvider` (lines 362–403) only. `CapturingPrimaryHandler`, `RecordingDistributedCache`, the
  backchannel handlers and the rest stay (DD-1).
- *(modify, mechanic (g))*:
  - **OpenIdConnect**: `TokenRefreshTests.cs`, `AuthenticationEndpointTests.cs`, `SecretHygieneTests.cs`, and
    `Infrastructure/HygieneHost.cs` (`Logs` becomes `FakeLogCollector`).
  - **ClientCredentials**: `TokenLifecycleTests.cs`, `CredentialTests.cs`, `CachePostureTests.cs`,
    `FailureModeTests.cs`.
  - The tuple fields `Level` / `Message` map one-to-one onto `FakeLogRecord`, so predicates stay verbatim.
- `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/RepositoryHygieneTests.cs` *(modify)*.

**RED**:
- `RepositoryHygieneTests.cs`:
  - `NoCloudstrapOwnedLogCaptureType_RemainsAnywhere` (AC-TS2): zero occurrences of `CapturingLoggerProvider` or
    `CapturedLogEntry` in `src/**/*.cs` and `src/**/*.csproj`, excluding the companion directory. Fails today on the
    two auth suites.
  - The following are **characterization tripwires, green on arrival and labelled as such** in the step report.
    They are permanent closure guards, not behavior drivers:
    - `DroppedSourceMembers_AndMvcTesting_AppearNowhere` (AC-TS10): zero `CustomWebApplicationFactory`,
      `ReplaceAsSingleton`, `ReplaceDbContextAsSingleton` in `src/**/*.cs`, and zero
      `Microsoft.AspNetCore.Mvc.Testing` in `src/**/*.csproj` and `src/Directory.Packages.props`.
    - `TestInfrastructureLibrary_CarriesNoEnterpriseIdentifiers` (AC-TS12): the library directory contains no
      `Nihdi` / `NIHDI` / `Riziv` (case-insensitive), no `Security:EnableAuthentication`, no `"UnitTest"` environment
      name.
- Failing-run command:
  ```powershell
  src\Test\UnitTest\Cloudstrap.TestInfrastructure.Tests\bin\Debug\net10.0\Cloudstrap.TestInfrastructure.Tests.exe --filter "RepositoryHygieneTests"
  ```

**GREEN**: the Scope items. As in Step 6, no predicate is weakened, and a count that differs stops the step.

**DB changes**: none.

**VERIFY**:
1. Companion exe → all pass.
2. OpenIdConnect and ClientCredentials exes → all pass at their **baseline counts**. Particular attention goes to
   the exact-count tests (`TokenLifecycleTests`, `FailureModeTests`, `CachePostureTests`, `CredentialTests`,
   `TokenRefreshTests`, `AuthenticationEndpointTests`) and the absence tests (`SecretHygieneTests`,
   `CredentialTests:160–161`).
3. Full-suite check (mechanic (b)): every unit exe + E2E green at baseline counts (`Cloudstrap.TestInfrastructure.Tests`
   is the only addition); zero warnings; `dotnet format` exit 0.
4. Final-gate grep evidence (recorded):
   - case-insensitive `Nihdi|Riziv` and `Security:EnableAuthentication` over every file added or touched by #8 (the
     diff since `03eee2b`) → zero hits;
   - `UnitTest` appears only as the `src/Test/UnitTest` path segment, never as an environment name.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE: final, deliverable #8 complete *(covers Step 7; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's
explicit go-ahead (CLAUDE.md: no push without confirmation). After the push, the first CI run is the evidence for
Step 5's override mode on the Linux SQL container.*

⚠️ **Risk areas at this gate**:
- **Assertion meaning (AC-TS2, tuple shape)**: the two auth suites' diffs; exact counts and "never appears"
  unchanged.
- **Regression**: the per-executable counts table (baseline vs final) for all 16 pre-existing unit exes + E2E.

- [ ] Behavioral verification (Step 7): `RepositoryHygieneTests.NoCloudstrapOwnedLogCaptureType_RemainsAnywhere`
  passes; the OpenIdConnect and ClientCredentials exes are green at baseline counts.
- [ ] Regression: the full-suite check (build + every unit exe + E2E exe + `dotnet format --verify-no-changes`) is
  green end to end. Every pre-existing executable's count equals the Baseline record, and the only new executable is
  `Cloudstrap.TestInfrastructure.Tests`.
- [ ] Spec acceptance sign-off:
  - Walk **AC-ASP2 and AC-TS1…AC-TS13** against the step evidence using the AC coverage map; all must be met.
  - **AC-TS13 / D-5**: record the user-approved deviation from workflow rule 9: no demonstration slice and no new
    E2E test. The demonstration is the E2E suite consuming the shared helper (Step 5), and the proof is the unchanged
    E2E suite green under LocalDB (local) and the override (local override mode in Step 5; CI after push).
  - Confirm nothing Dropped or Out-of-Scope was resurrected:
    - no `CustomWebApplicationFactory` / DI replace helpers / `Mvc.Testing` / EF Core;
    - no Cloudstrap log-capture type;
    - no shared `CapturingPrimaryHandler` / `RecordingDistributedCache`;
    - no settings-literal sweep;
    - no `CLOUDSTRAP_TEST_BLOB` change;
    - no `Testcontainers.*`;
    - no shipped-package or demo-code change;
    - `src/Test/TestIdentityProvider/` untouched;
    - the Observability sentinel provider untouched.
- [ ] Docs review: the `ci.yml` / `release.yml` comments, the `src/demo/README.md` harness bullet, and the
  library's type summaries (CREATE DATABASE permission, the parallel-fixture constraint, Entra ID pass-through).
  **Recorded follow-ups (not in this plan)**:
  - CLAUDE.md "Project Structure" still lists `Cloudstrap.Testing/ # Test helper utilities`; it should name the
    test-only `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure`, owner: whoever maintains CLAUDE.md (spec
    Out of Scope);
  - a `Test/TestInfrastructure/` row in `.claude/instructions/tests.md`'s Project Layout table;
  - the per-app demo READMEs / appsettings comments describing `CLOUDSTRAP_TEST_SQL` as "overrides the connection
    string" (Api, Worker, BlazorServer);
  - hardening "never appears" assertions over `FakeLogRecord.StructuredState`;
  - the `Microsoft.Extensions.*` 10.0.12 family bump that would allow `Diagnostics.Testing` 10.10.0;
  - opportunistic adoption of `CloudstrapTestHost` by other suites;
  - the intra-E2E `CLOUDSTRAP_TEST_BLOB` tidy.
- [ ] User approved: deliverable #8 is done; the project-manager flips the ROADMAP row to ✅.
