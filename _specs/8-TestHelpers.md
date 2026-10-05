# Spec: Test Helpers — shared test infrastructure (Roadmap Deliverable #8)

> Status: **DRAFT 2026-09-26. All seven Open Questions answered 2026-10-05 (D-1 to D-7, every recommendation accepted).
> Ready for the planner.** The founding Package Map row was amended by the user's D-1 answer.
>
> In short: this deliverable consolidates Cloudstrap's own duplicated test infrastructure. It is not a port.
> The source package (`Nihdi.Core.Testing`) has **zero consumers**, and each of its three public members is either
> defective or a thin wrapper over something the framework already ships. The analyst's position is that **none of
> it earns a place in Cloudstrap**. What does earn a place is the demand #14–#16 created. That is one SQL Server test
> database helper (three sites, and two of them share a catalog), one message recorder, and one "valid settings +
> host builder" pair. Log capture, which has six copies in two shapes, is better **replaced** by Microsoft's
> `Microsoft.Extensions.Diagnostics.Testing` than hoisted.
>
> Sources: `_plans/ROADMAP.md` §8 (goal, status history, hand-off brief with verified inventory, 2026-09-26) ·
> `_specs/Cloudstrap.md` (Package Map line 95, De-NIHDI rows 111/118/120, Aspire Coexistence AC-ASP2, Decisions Made) ·
> shape precedent `_specs/16-HangfireScheduler.md` (verdict table + Decision Log; DD-8 "link, #8 hoists") ·
> `_specs/14-Messaging.md` D-3 (LocalDB + `CLOUDSTRAP_TEST_SQL`) · `_specs/15-BlobClaimCheck.md` DL-10 ·
> `_specs/5-WebApiBootstrap.md` (the `EnableAuthentication`/`EnableHttps` flags moved into code: `AddCloudstrapJwtBearer`
> and HSTS options) · `_specs/9-ClientCredentialsAuth.md` D-5 (test-only `Cloudstrap.TestIdentityProvider` precedent).
>
> **Source reference repo (read-only), every file opened**: `Nihdi.Core.Testing\CustomWebApplicationFactory.cs`,
> `ServiceCollectionExtension.cs`, `Nihdi.Core.Testing.csproj`, `README.md`. Also the source repo's own review,
> `_reviews/2026-07-03-full-codebase-review.md` (items P2-33 and P2-34 at lines 203–204, which are defects in this
> package), and `.claude/instructions/tests.md` (a *pattern description* of a factory; no code consumer). A consumer
> grep over the whole source repo (bin/obj excluded) found the three source files, the `.sln`, and agent/plan/review
> markdown. **No code consumer exists.**
>
> **Cloudstrap code read**: all three `SqlServerTestDatabase` sites (two copies plus the Hangfire link) and every call
> site. All six `CapturingLoggerProvider` copies plus the link, and every `.Entries` assertion across the suites
> (listed in finding 7). Both `MessagingTestHost` copies. All three `InvocationRecorder` types. All four
> `CapturingPrimaryHandler` variants and both `RecordingDistributedCache` variants. The seven SQL-override forwarding
> sites in the E2E suite (`E2eFixture.cs`, `WorkerHostTests.cs`, `MessagingTests.cs`, `ClaimCheckTests.cs`,
> `HangfireTests.cs` ×2, `BlazorServerTests.cs`). Also `src/Test/Directory.Build.props`, the Messaging, AzureBlob,
> Hangfire, E2E and TestIdentityProvider csprojs, `src/Directory.Packages.props`, `.github/workflows/ci.yml`, the
> demo `appsettings.json` connection strings, and `src/Cloudstrap.Core/ApplicationOptions.cs`.
>
> **External evidence gathered 2026-09-26 (nuget.org / Microsoft Learn)**:
> - [Microsoft.Extensions.Diagnostics.Testing](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Testing)
>   is **MIT** (Microsoft, `dotnet/extensions` repo), with 17.6 M downloads. It releases monthly: 10.3.0 (2026-02-10)
>   through **10.10.0 (2026-09-09)**, the latest stable.
>   - 10.10.0 net10.0 dependencies: `Microsoft.Extensions.Logging ≥ 10.0.12`,
>     `Microsoft.Extensions.Options.ConfigurationExtensions ≥ 10.0.12`,
>     `Microsoft.Extensions.Telemetry.Abstractions ≥ 10.10.0`.
>   - **10.8.0 (2026-07-14)** net10.0 dependencies: `Microsoft.Extensions.Logging ≥ 10.0.10`,
>     `…Options.ConfigurationExtensions ≥ 10.0.10`, `…Telemetry.Abstractions ≥ 10.8.0`. This is the same
>     `dotnet/extensions` train as the repo's pinned `Microsoft.Extensions.Http.Resilience` 10.8.0.
> - [FakeLogCollector API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.logging.testing.fakelogcollector)
>   offers `Count`, `LatestRecord`, `GetSnapshot(bool clear)`, `Clear()` and `GetLogsAsync(CancellationToken)`.
>   `FakeLogRecord` carries `Category`, `Level`, `Id`, `Message` (the formatted message), `Exception`, `State` /
>   `StructuredState`, `Scopes` and `Timestamp`. `FakeLoggerProvider(FakeLogCollector?)` plugs into `ILoggingBuilder`.
> - [Integration tests in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests) and
>   [`IDbContextOptionsConfiguration<TContext>`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.infrastructure.idbcontextoptionsconfiguration-1?view=efcore-10.0)
>   document the stock pattern. A test replaces a `DbContext` by removing EF's option registrations inside
>   `ConfigureTestServices` / `ConfigureWebHost` on a plain `WebApplicationFactory<TEntryPoint>`, not with a helper.
>
> **Risk areas this deliverable touches**:
> - **Public-API one-way door, only if something ships.** Under the recommended OQ-1 (a), nothing ships and the door
>   stays closed.
> - **Dependency update.** `Microsoft.Extensions.Diagnostics.Testing` is new and test-only (OQ-3).
> - **Shared contract between test executables.** The `CLOUDSTRAP_TEST_SQL` semantics and the CI connection string
>   change (OQ-4).
> - **Regression risk.** The repoint touches seven unit-test projects and the E2E suite at once. Per-suite test counts
>   are the safety net (AC-TS8).
> - **CI packaging consequence.** Any packable project under `src/` is published by `ci.yml`'s preview step (line 110).
> - **Uncommitted #16 working tree.** `Cloudstrap.Hangfire.Tests.csproj` and related files are modified but not
>   committed on `dev`. Commit #16 before #8 implementation starts so the diffs stay separable.
> - **Aspire overlap: none functionally.** AC-ASP2 is carried as a tripwire, and `Aspire.Hosting.Testing` is never a
>   dependency option.
>
> **Standing constraint**: nothing is published to nuget.org yet, so breaking changes are allowed. This spec makes
> **no change to any shipped package's public API**. Every change is under `src/Test/`, plus `ci.yml` (OQ-4).

## Code-reading findings that shaped this spec

1. **The source package has no users, and its README describes code that does not exist.** "Common test fixtures and
   base classes" and "mocks" are claimed, but the package contains one factory subclass and two DI helpers. No project
   in the source repo references it; only a markdown pattern description in the source's `tests.md` mentions a
   `CustomWebApplicationFactory`. There is no usage contract to preserve.
2. **`CustomWebApplicationFactory` adds nothing correct on top of `WebApplicationFactory<T>`.**
   - Its only additions are these:
     - Four `Environment.SetEnvironmentVariable` calls (`CustomWebApplicationFactory.cs:58–61`). These are
       process-wide, never restored, and leak across parallel factories.
     - The source's own review item P2-34 adds a further point. The `Nihdi:Security:*` names use `:`, which does not
       bind from environment variables on Linux, so the auth-disable silently did nothing on Linux CI.
     - A non-standard `UnitTest` environment name, which the founding De-NIHDI row 111 removes.
     - A mandatory `TDbContext` type parameter, even for apps without EF.
   - The two flags it flips (`EnableAuthentication`, `EnableHttps`) no longer exist in Cloudstrap. #5 moved both into
     code (`AddCloudstrapJwtBearer`; HSTS options), so the "disable security for tests" trick has nothing to target.
   - The stock factory already covers everything else: `UseEnvironment`, `UseSetting`, `ConfigureTestServices` and
     `WithWebHostBuilder`, all scoped to one factory instance.
3. **`ReplaceDbContextAsSingleton` is defective, and its intent is an anti-pattern.**
   - The defect: it looks up `DbContextOptions<DbContext>` (`ServiceCollectionExtension.cs:48–49`), while EF registers
     `DbContextOptions<TContext>`. The removal is therefore a no-op, and the app's real context registration stays
     alongside the appended singleton (P2-33 in the source review).
   - Even when fixed, a **singleton `DbContext`** shares one change tracker across concurrent test-server requests.
     `DbContext` is not thread-safe.
   - EF Core 9+ also registers `IDbContextOptionsConfiguration<TContext>`, so the "right" removal set changes between
     EF versions. That is knowledge a helper would have to track forever.
4. **`ReplaceAsSingleton` is a one-liner that fails when used for real.** It uses `SingleOrDefault`, which **throws**
   when a service has more than one registration (`ServiceCollectionExtension.cs:24`). That is precisely the case
   where a test wants a replacement. The in-box `services.RemoveAll<T>()` followed by `AddSingleton(instance)`
   (`Microsoft.Extensions.DependencyInjection.Extensions`) does the job correctly and costs nothing.
5. **Cloudstrap has zero `WebApplicationFactory` demand.** No test project references `Microsoft.AspNetCore.Mvc.Testing`.
   Seven suites build `TestServer` hosts directly, with per-package `*TestHost` builders. Only
   `src/demo/BlazorServer/Program.cs` exposes a `Program` type. Across sixteen delivered deliverables, no plan has
   needed a factory helper (roadmap §8 status history).
6. **`SqlServerTestDatabase` behaves correctly per suite, but its override semantics make catalogs collide.**
   - Each copy hard-codes its LocalDB catalog: `CloudstrapMessagingTests` (Messaging) and `CloudstrapClaimCheckTests`
     (AzureBlob). Hangfire *links* the Messaging copy, so it resets `CloudstrapMessagingTests` too.
   - `CLOUDSTRAP_TEST_SQL` replaces the **whole** connection string. Under CI, all three suites therefore drop and
     recreate **one** catalog, `CloudstrapCi` (`ci.yml:77`), and the E2E demo hosts use that same catalog because the
     E2E suite forwards the whole string.
   - This is safe today only because `ci.yml`'s loop runs executables sequentially. The E2E suite sorts first
     (`src/Test/E2E` < `src/Test/UnitTest`), so the unit suites drop the E2E database *after* the E2E run.
   - The ci.yml comment at line 76 is also stale: it omits AzureBlob.
   - `ResetAsync` string-interpolates the catalog into DDL, which is safe only because the names are constants.
   - It also calls `SqlConnection.ClearAllPools()`, which empties every pool in the process, not just this catalog's.
7. **Every log assertion in the suite fits `FakeLogRecord`.** Across the seven suites, assertions read:
   - `Category`: AzureBlob `ClientResolutionTests:113` and `RegistrationTests:111`.
   - `Level`: everywhere.
   - `Message`: everywhere.
   - `Message + " " + Exception`: `SqlServerDurabilityTests:125`, `CorrelationEnforcementTests:42`,
     `MissingPayloadTests:88`.
   - A single `Exception`: `RecurringTaskRunnerTests:137`.
   - Exact counts: `TokenLifecycleTests`, `FailureModeTests`, `CachePostureTests`, `CredentialTests`,
     `TokenRefreshTests`, `AuthenticationEndpointTests`.
   - Absence ("payload / secret never appears"): `RegistrationTests:118–119`, `SchedulingServiceTests:42–43`,
     `SecretHygieneTests`, `CredentialTests:160–161`.

   `FakeLogRecord` exposes `Category`/`Level`/`Message`/`Exception`, `FakeLogCollector.GetSnapshot()` gives an ordered
   thread-safe snapshot, and LINQ `Count`/`None` cover exact counts and absence. Neither in-repo shape carries anything
   `FakeLogRecord` lacks. `FakeLogRecord` additionally exposes `StructuredState`, which could harden "never appears"
   assertions against structured properties that a formatted message omits (not required here; see Out of Scope).
8. **Two of the "duplicates" are not duplicates.**
   - The four `CapturingPrimaryHandler`s have four contracts:
     - Extensions: `LastRequest` only, empty 200.
     - ClientCredentials: plus `RequestCount` and `SeenBearerTokens`.
     - OpenIdConnect: plus the bearer token echoed as the response body.
     - BlazorWasm: a `Requests` list and a JSON `{"isAuthenticated":false}` body.
   - The two `RecordingDistributedCache`s have two contracts:
     - Mvc wraps `MemoryDistributedCache` and counts reads and writes.
     - ClientCredentials is a null store recording written keys.
   - A shared configurable type would be more code than any single copy and would couple unrelated suites.
9. **The "valid `Cloudstrap:Application` section" literal is everywhere, but its values are intentional per suite.**
   There are 51 occurrences across 44 files, including 2 in E2E (the brief's figure of 42 files predates the Hangfire
   test hosts). Core suites assert validation over these values, and suites pick their own workload names. Only the
   two `MessagingTestHost` copies are byte-identical (apart from the namespace).
10. **`InvocationRecorder` naming collides.** The two Messaging copies are identical `public sealed` instance classes.
    They are public because Wolverine fixture handlers are public, and they are Wolverine-free.
    `Cloudstrap.Hangfire.Tests.Fixtures.InvocationRecorder` is an unrelated **static** job-id log with the same simple
    name, which is ambiguous once both namespaces are imported.
11. **Test-tree mechanics constrain naming.**
    - `src/Test/Directory.Build.props` sets `IsPackable=false`, and it wires the MTP runner and NUnit **only for
      projects whose name ends with `.Tests`**.
    - `ci.yml` runs every `src/Test/**/*.Tests.csproj` as an executable, and it publishes every
      `src/**/bin/Release/*.nupkg`.
    - A shared library under `src/Test/` that does not end in `.Tests` is therefore never packed, never run as a test
      executable, and never forced onto NUnit. `Cloudstrap.TestIdentityProvider` (#9 D-5) is the precedent.
12. **The version choice has consequences.** `Microsoft.Extensions.Diagnostics.Testing` 10.10.0 floors
    `Microsoft.Extensions.Logging` and `…Options.ConfigurationExtensions` at 10.0.12. The repo pins the
    `Microsoft.Extensions.*` family at 10.0.10 with `CentralPackageTransitivePinningEnabled`, so adopting 10.10.0 would
    force a family-wide bump. 10.8.0 matches the family floor exactly and the already-pinned `dotnet/extensions` train
    (`Microsoft.Extensions.Http.Resilience` 10.8.0).

---

## User Story

**As a** Cloudstrap maintainer writing tests for a new package (the next SQL-backed messaging provider, the #17/#18
proxies, the dashboard),
**I want to** get the SQL Server test database, message recorder and valid-settings host builder from **one** test-only
project, and capture logs with Microsoft's maintained fake logger instead of a seventh copy,
**So that** a fix lands once, no two test executables can drop each other's database, CI and local runs mean the same
thing, and Cloudstrap owns no test code that the framework or a Microsoft library already provides.

*(The consumer-facing story, "as a developer testing my own Cloudstrap app", is deliberately **not** served by a
Cloudstrap package. Stock `WebApplicationFactory<T>` plus `FakeLogger` already serve it. See OQ-1.)*

---

## Acceptance Criteria

> The founding spec defines **no Testing ACs**; its only relevant row is Package Map line 95. AC-ASP2 is carried
> **verbatim**. AC-TS1 to AC-TS13 are new, spec-specific criteria (prefix `AC-TS`). Criteria marked *(OQ-n)* assume
> the recommended answer and are rewritten if the user chooses otherwise.

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim; tripwire only. Under OQ-1 (a) this deliverable ships nothing, and the test-only library also references no `Aspire.*`. `Aspire.Hosting.Testing` is not a dependency option.)* |
| AC-TS1 | The solution tree after the deliverable | A case-sensitive search for `class SqlServerTestDatabase`, `class InvocationRecorder` (the instance recorder) and `class MessagingTestHost` (or its successor, OQ-6) | Each exists **exactly once**, in the shared test-only library. Both former copies of each are deleted. `Cloudstrap.Hangfire.Tests.csproj` contains **no** `<Compile Include=… Link=…>` item (lines 26–27 today are gone). No `.cs` file is linked across test projects anywhere under `src/Test`. |
| AC-TS2 | The solution tree after the deliverable | A search for `CapturingLoggerProvider` and `CapturedLogEntry` | Zero hits. The seven former call-site suites (WebApi, Mvc, Messaging, Messaging.AzureBlob, Hangfire, OpenIdConnect, ClientCredentials) capture logs through `Microsoft.Extensions.Logging.Testing.FakeLoggerProvider` / `FakeLogCollector`. Every former assertion survives with the same meaning: category, level, formatted message, exception, exact counts, and "never appears". `Cloudstrap.Observability.Tests`' private `FakeLoggerProvider` (a provider-preservation fixture, not a capture helper) is untouched. *(OQ-3)* |
| AC-TS3 | The shared SQL Server test database helper | A suite constructs it | It **requires** a catalog name. There is no default catalog and no static connection string. Messaging uses `CloudstrapMessagingTests`, AzureBlob uses `CloudstrapClaimCheckTests`, and Hangfire uses **`CloudstrapHangfireTests`** (new). No two test executables pass the same catalog name, and the Messaging/Hangfire sharing of `CloudstrapMessagingTests` has ended. *(OQ-4)* |
| AC-TS4 | `CLOUDSTRAP_TEST_SQL` **unset** | The helper computes its connection string for catalog `X` | `Server=(localdb)\MSSQLLocalDB`, integrated security, `TrustServerCertificate=true`, `Initial Catalog=X`. This is today's LocalDB default, with the catalog parameterized. |
| AC-TS5 | `CLOUDSTRAP_TEST_SQL` **set** (e.g. CI's `Server=localhost,1433;Database=CloudstrapCi;User Id=sa;…`) | The helper computes its connection string for catalog `X` | The override supplies the server, authentication and options. **`Initial Catalog` is always replaced by `X`**, whatever `Database=`/`Initial Catalog=` the override carries. Proven by unit tests that need no SQL Server. *(OQ-4)* |
| AC-TS6 | A catalog name that is empty, is a system database (`master`, `model`, `msdb`, `tempdb`, compared case-insensitively), or contains anything outside `[A-Za-z0-9_]` (or does not start with a letter) | The helper is constructed | An `ArgumentException` naming the parameter is thrown **before any SQL runs**. The DDL in `ResetAsync` only ever sees validated, bracket-quoted names. |
| AC-TS7 | A suite calls `ResetAsync()` | — | The catalog is dropped if it exists (`SINGLE_USER WITH ROLLBACK IMMEDIATE`) and recreated empty. Only the **pool for this helper's connection string** is cleared; other pools in the process are left alone. `TableExistsAsync`, `ScalarAsync` and `WaitForScalarAsync` (250 ms polling, `null` on deadline) keep today's semantics. Every async member accepts a `CancellationToken`. |
| AC-TS8 | The full suite after the repoint, with a baseline taken at plan start (reference: #16's gate recorded 16 unit executables / 926 tests and E2E 68/68) | All unit executables and the E2E suite run under the LocalDB default **and** under the `CLOUDSTRAP_TEST_SQL` override (CI) | All green, and every pre-existing executable's test count equals its baseline. The only additions are the shared library's own test suite (AC-TS11). |
| AC-TS9 | The E2E suite (`Cloudstrap.Demo.E2E.Tests`) | It starts demo hosts that need SQL Server | With the override unset, nothing is forwarded (the demo `appsettings.json` LocalDB `CloudstrapDemo` stays authoritative). With it set, every SQL-needing host receives `--ConnectionStrings:DefaultConnection=` composed by the **shared helper** for catalog `CloudstrapDemo`, so local and CI use the same database name. Zero inline `Environment.GetEnvironmentVariable("CLOUDSTRAP_TEST_SQL")` remains in the E2E project (seven sites in six files today). `ci.yml`'s override no longer relies on its `Database=` segment, and its comment names every honoring suite. *(OQ-4, OQ-5)* |
| AC-TS10 | The solution tree and every csproj | A search for `CustomWebApplicationFactory`, `ReplaceAsSingleton`, `ReplaceDbContextAsSingleton`, `Microsoft.AspNetCore.Mvc.Testing`, and any EF Core reference added by this deliverable | Zero hits for the three source members and for `Mvc.Testing`. No EF Core reference is introduced. *(OQ-2)* |
| AC-TS11 | The shared test-only library | It is built and inspected | It lives under `src/Test/` with a name **not** ending in `.Tests`. It inherits `IsPackable=false` (no `.nupkg`, so the CI preview publish is unaffected). No shipped package and no demo app references it. It references no NUnit, Moq or `Aspire.*` package. Its own companion suite `…Tests` unit-tests AC-TS4 to AC-TS6 without SQL Server. *(OQ-1, OQ-7)* |
| AC-TS12 | The files this deliverable adds or touches | A case-insensitive search for `Nihdi`, `NIHDI`, `Riziv`, the environment name `UnitTest`, and `Security:EnableAuthentication` | Zero hits. Fixtures use neutral values (`contoso`, `example.com`). |
| AC-TS13 | The CLAUDE.md workflow rule 9 (demonstration slice plus at least one E2E test) | The deliverable closes | The rule is satisfied **as interpreted by OQ-5**. The recommended interpretation: the E2E suite *consumes* the shared helper (AC-TS9), the unchanged E2E suite green under both SQL modes is the proof, and the "at least one **new** E2E test" clause is recorded as a user-approved deviation for a test-audience deliverable. |

---

## Port Decision Table

One row per source public type or feature. Part B rows each in-repo duplicate the brief inventoried. "Replace" means a
maintained library does the job, and "Hoist" means consolidate to one copy in the shared test-only library (a Port
whose source is Cloudstrap's own test tree).

### Part A: `Nihdi.Core.Testing\` (3 code/build files + README)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `CustomWebApplicationFactory<TProgram, TDbContext> : WebApplicationFactory<TProgram>` (public, unsealed) | **Drop** | Stock `WebApplicationFactory<TEntryPoint>` (+ `WithWebHostBuilder`, `UseEnvironment`, `UseSetting`, `ConfigureTestServices`) | Finding 2. Its only additions are defects: process-wide env vars that are never restored and are Linux-blind (P2-34), a non-standard `UnitTest` environment, and security flags that no longer exist since #5. It also forces an EF type parameter. It has zero consumers in either repo (findings 1 and 5). |
| ├─ `WithServiceCollectionOverrides(Action<IServiceCollection>)` | **Drop** | `WithWebHostBuilder(b => b.ConfigureTestServices(...))` | This is the framework's own hook, one line and instance-scoped. |
| ├─ `WithDbContext(TDbContext)` | **Drop** | EF's documented pattern inside `ConfigureTestServices` | Finding 3. It delegates to the defective helper below. |
| └─ `ConfigureWebHost`: `SetEnvironmentVariable(ASPNETCORE_/DOTNET_ENVIRONMENT = "UnitTest")`, `Nihdi:Security:EnableAuthentication/EnableHttps = false` | **Drop** | `builder.UseEnvironment(Environments.Development)` / `UseSetting(...)` per test, if a consumer wants it | This is a De-NIHDI item (row 111 environment taxonomy) and a cross-test leak. The target settings were removed by #5. |
| `ServiceCollectionExtensions.ReplaceAsSingleton<T>(T)` | **Drop** | In-box `services.RemoveAll<T>(); services.AddSingleton(instance);` | Finding 4. `SingleOrDefault` throws in the multi-registration case. The framework one-liner is correct and costs nothing to own. |
| `ServiceCollectionExtensions.ReplaceDbContextAsSingleton<T>(T)` | **Drop** (never ported as-is, and not fixed either) | EF Core's documented replacement pattern (remove option registrations, re-add the context with a test provider) | Finding 3. The descriptor type is wrong (P2-33). A singleton `DbContext` is not thread-safe under a test server. The correct removal set is EF-version-specific (`IDbContextOptionsConfiguration<T>`, EF 9+), which is maintenance debt for zero consumers. |
| `Nihdi.Core.Functional.Preconditions.NotNull` usage | **Drop** (moot) | — | Both callers are dropped. Where the shared library has guards, they use `ArgumentNullException.ThrowIfNull` / `ArgumentException.ThrowIfNullOrWhiteSpace`. No LanguageExt.Core reference (founding "Functional not ported"). |
| `Nihdi.Core.Testing.csproj`: `Microsoft.AspNetCore.Mvc.Testing` + `Microsoft.EntityFrameworkCore` 10.0.9, `<Version>0.4-prerelease</Version>`, StyleCop, `GeneratePackageOnBuild` | **Drop** | Test-only csproj under `src/Test/` (OQ-1/OQ-7), references limited to `Microsoft.Data.SqlClient` (already pinned) + `Microsoft.Extensions.Hosting`/`Configuration` (already pinned) | Both source package references back dropped members. The hard-coded version is replaced by GitVersion suite-wide anyway (moot when not packed). StyleCop is gone suite-wide. |
| `README.md` ("mocks, fixtures, base classes", "within the Nihdi infrastructure", `Copyright © NIHDI/RIZIV`) | **Drop** | — (the test-only library needs no package README; XML summaries on its types suffice) | Finding 1: its claims are unbacked. De-NIHDI rows 118/120. |

### Part B: in-repo duplicated test helpers (Cloudstrap's own demand; verified on disk 2026-09-26)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `SqlServerTestDatabase`: copies in `Cloudstrap.Messaging.Tests/Infrastructure` (`CloudstrapMessagingTests`) and `Cloudstrap.Messaging.AzureBlob.Tests/Infrastructure` (`CloudstrapClaimCheckTests`), plus the **link** at `Cloudstrap.Hangfire.Tests.csproj:26` | **Hoist + Redesign** | One `SqlServerTestDatabase` in the shared library, an **instance per catalog** (AC-TS3 to AC-TS7). Hangfire moves to its own `CloudstrapHangfireTests` catalog. | This is the deliverable's real demand, with three consumers (#14/#15/#16) and a live shared-catalog hazard (finding 6). The redesign parameterizes the catalog, defines the override semantics (OQ-4), validates the name, and clears only its own pool. Everything else ports verbatim. |
| E2E `CLOUDSTRAP_TEST_SQL` → `--ConnectionStrings:DefaultConnection=` forwarding: seven sites in six files (`E2eFixture.cs:86`, `WorkerHostTests.cs:41`, `MessagingTests.cs:41`, `ClaimCheckTests.cs:44`, `HangfireTests.cs:44/62`, `BlazorServerTests.cs:33`) | **Hoist** (consumer of the above) | The E2E fixtures ask the shared helper for the override-composed connection string for catalog `CloudstrapDemo`, or for nothing when the override is unset (AC-TS9) | Once the override semantics change, the E2E suite must apply the **same** rule or CI and local diverge. There is one definition of the env var and its meaning. This also gives the deliverable its demonstration vehicle (OQ-5). |
| `CapturingLoggerProvider`, record shape (`CapturedLogEntry(Category, Level, Message, Exception)`): WebApi, Mvc, Messaging, Messaging.AzureBlob copies, plus the **link** at `Cloudstrap.Hangfire.Tests.csproj:27` | **Replace** | `Microsoft.Extensions.Diagnostics.Testing`: `FakeLoggerProvider` + `FakeLogCollector` + `FakeLogRecord` | Finding 7: a field-for-field match with a thread-safe ordered snapshot. It is a Microsoft MIT package on a monthly release train. Cloudstrap then owns **zero** lines of log-capture code (CLAUDE.md rule 4: less code wins). OQ-3. |
| `CapturingLoggerProvider`, tuple shape (`(LogLevel Level, string Message)`, `ConcurrentQueue`): `Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/CapturingLoggerProvider.cs` + nested in `Cloudstrap.Authentication.ClientCredentials.Tests/Infrastructure/TestDoubles.cs:362–403` | **Replace** | same | This shape is a strict subset of `FakeLogRecord`. The nested copy is deleted from `TestDoubles.cs`, and the rest of that file stays. |
| `Cloudstrap.Observability.Tests/HostApplicationBuilderExtensionsTests.cs` private `FakeLoggerProvider` (NullLogger) | **Keep local** (out of the hoist) | — | It is not a capture helper. It is a sentinel provider proving "consumer providers survive". It has a single use. |
| `MessagingTestHost` ×2 (byte-identical; `ValidSettings()` = `contoso`/`orders`/`worker`, `WorkloadName` const, `CreateBuilder`) | **Hoist** (renamed, see OQ-6/OQ-7) | One neutral "valid Cloudstrap settings + generic-host builder" helper in the shared library (sketch name `CloudstrapTestHost`). Adoption by other suites is **opportunistic, not swept**. | Two identical copies, Wolverine-free, and the next SQL messaging provider suite is the likely third consumer. The 44-file literal sweep is rejected (finding 9): the values are intentionally per suite, and Core tests assert over them. |
| `InvocationRecorder` (instance, `WaitForNextAsync`) ×2: `Cloudstrap.Messaging.Tests/Fixtures`, `Cloudstrap.Messaging.AzureBlob.Tests/Fixtures` | **Hoist** | One `public sealed InvocationRecorder` in the shared library | These are identical, Wolverine-free, and awaitable without polling (finding 10). |
| `Cloudstrap.Hangfire.Tests/Fixtures/InvocationRecorder` (static job-id log) | **Keep, rename** | Stays in `Cloudstrap.Hangfire.Tests`, renamed (sketch: `TaskRunLog`) | It has a different contract, so it is not a duplicate. The rename removes the simple-name clash with the hoisted type (finding 10). |
| `CapturingPrimaryHandler` ×4 (Extensions, OpenIdConnect, ClientCredentials, BlazorWasm nested) | **Drop from the hoist** (stay local) | — | Finding 8: four behaviorally different contracts under one name. A configurable shared handler costs more than the copies. |
| `RecordingDistributedCache` ×2 (Mvc wrapper counting reads/writes; ClientCredentials null store recording keys) | **Drop from the hoist** (stay local) | — | Finding 8: two different contracts. |
| E2E `CLOUDSTRAP_TEST_BLOB` forwarding blocks | **Out of scope** | — | This is an intra-E2E tidy. `AzuriteProcess.EnvironmentVariable` already centralizes the name, and the semantics do not change. It is not a shared-library concern. |

**Tally** (20 rows): **Part A** (9 rows incl. 3 sub-rows): 0 Port · 0 Redesign · 0 Replace · **9 Drop** ·
**Part B** (11 rows): 4 Hoist (1 with Redesign) · 2 Replace · 1 Keep-rename · 1 Keep-local · 2 Drop-from-hoist ·
1 Out of scope.

---

## Public API Sketch (test-only library; not a shipped surface under OQ-1 (a))

Placement and name are pending OQ-7. The recommended placement is
`src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/`, with namespace `Cloudstrap.TestInfrastructure` and a
companion `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/`. Types are `public`, because consuming test
assemblies need them and the library is never packed. They are sealed or static. Guard clauses are required. XML docs
are optional (`GenerateDocumentationFile=false` in `src/Test`), but kept as brief summaries by house style.

```text
Cloudstrap.TestInfrastructure
├── SqlServerTestDatabase (public sealed)                           — AC-TS3…AC-TS7
│     const string EnvironmentVariable = "CLOUDSTRAP_TEST_SQL"
│     SqlServerTestDatabase(string catalog)                          — validates the name (AC-TS6)
│     string Catalog { get; }
│     string ConnectionString { get; }                               — LocalDB default, or override with
│                                                                      Initial Catalog := Catalog (AC-TS4/5)
│     Task ResetAsync(CancellationToken = default)                   — drop-if-exists + create; clears only
│                                                                      this connection string's pool
│     Task<bool> TableExistsAsync(string schema, string table, CancellationToken = default)
│     Task<object?> ScalarAsync(string sql, CancellationToken, params (string Name, object Value)[] parameters)
│                                                                      (parameter order is the planner's call;
│                                                                      semantics unchanged)
│     Task<object?> WaitForScalarAsync(string sql, TimeSpan timeout, CancellationToken = default)
│     static string? OverrideConnectionStringFor(string catalog)    — null when the env var is unset; else the
│                                                                      override with Initial Catalog := catalog.
│                                                                      The E2E forwarding entry point (AC-TS9).
│
├── InvocationRecorder (public sealed, IDisposable)                  — hoisted verbatim
│     IReadOnlyCollection<object> Received
│     void Record(object message)
│     Task<object> WaitForNextAsync(TimeSpan timeout)                (+ CancellationToken, planner's call)
│
└── CloudstrapTestHost (public static; successor of MessagingTestHost — name per OQ-6/OQ-7)
      const string WorkloadName = "contoso-orders-worker"
      Dictionary<string, string?> ValidSettings()                    — Cloudstrap:Application SystemName/
                                                                       SubsystemName/SubsystemType =
                                                                       contoso/orders/worker
      HostApplicationBuilder CreateBuilder(IDictionary<string, string?> settings)
```

**Per-suite usage pattern.** Each SQL suite owns one static instance, e.g.
`new SqlServerTestDatabase("CloudstrapHangfireTests")`. The concrete holder is the planner's call.

**Log capture has no Cloudstrap type.** Suites reference `Microsoft.Extensions.Diagnostics.Testing` and write
`FakeLogCollector logs = new(); builder.Logging.AddProvider(new FakeLoggerProvider(logs));`, then assert over
`logs.GetSnapshot()`.

**Deliberately not provided**: a `WebApplicationFactory` subclass · DI replace helpers · a log-capture wrapper · a
shared `HttpMessageHandler`/`IDistributedCache` double · a test-framework dependency (NUnit/Moq) in the library ·
anything a demo app or shipped package could reference.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Test SQL Server | LocalDB `(localdb)\MSSQLLocalDB`, integrated security, `TrustServerCertificate=true` (the #14 D-3 posture) | `CLOUDSTRAP_TEST_SQL` supplies server, authentication and options (e.g. CI's Linux container, or Azure SQL/Docker locally). |
| Catalog | **Required constructor argument**, one per test executable (`CloudstrapMessagingTests`, `CloudstrapClaimCheckTests`, `CloudstrapHangfireTests`) | None. The catalog is a code-level choice per suite. Any `Database=` in the override is **ignored** (OQ-4). |
| Isolation | Distinct catalogs mean test executables can run concurrently against one server without dropping each other's databases. Within one executable, fixtures sharing the catalog must not run in parallel (today: NUnit's non-parallel default, and Hangfire's `[assembly: NonParallelizable]`). | A suite needing parallel fixtures uses one helper instance (one catalog) per fixture. |
| Reset | Drop-if-exists (`SINGLE_USER WITH ROLLBACK IMMEDIATE`) + `CREATE DATABASE`, then clear this connection string's pool only | — |
| E2E demo database | Override unset: nothing forwarded, and the demo `appsettings.json` (`CloudstrapDemo`) wins. Override set: `CloudstrapDemo` on the override's server. | The demo appsettings / the env var. |
| Log capture | `FakeLoggerProvider` + `FakeLogCollector` per test host; the host's normal logging filters apply (as today) | `FakeLogCollectorOptions` (filtered levels/categories) where a suite wants it. |
| Packaging | Never packed (`IsPackable=false` inherited), never run as a test executable (name does not end in `.Tests`) | — |

---

## Dependencies

| Package | Version (pin at plan time) | License | Scope | Why it is justified |
|---|---|---|---|---|
| `Microsoft.Extensions.Diagnostics.Testing` | **10.8.0** (2026-07-14), recommended over latest 10.10.0 (2026-09-09); see finding 12 and OQ-3 | MIT (Microsoft, `dotnet/extensions`) | **Test projects only** (the seven former call-site suites); never shipped | It deletes six copies and one link of bespoke log capture with no Cloudstrap-owned replacement. It is maintained on a monthly train with 17.6 M downloads. Its transitive dependency `Microsoft.Extensions.Telemetry.Abstractions` (MIT, same repo) is already present in suites using `Microsoft.Extensions.Http.Resilience` 10.8.0. 10.8.0 matches the repo's `Microsoft.Extensions.*` 10.0.10 floor under transitive pinning, so no family bump is needed. |
| `Microsoft.Data.SqlClient` | 6.1.7 (already CPM-pinned, #16) | MIT | Shared test-only library | This is what the SQL helper is built on. Today it arrives transitively in the SQL suites, and the shared library takes a direct reference. It is not new to the repo. |
| `Microsoft.Extensions.Hosting` / `Microsoft.Extensions.Configuration` | 10.0.10 (already pinned) | MIT | Shared test-only library | Needed for `CloudstrapTestHost.CreateBuilder` / `AddInMemoryCollection`. Not new. |

**Considered and rejected**:
- `Testcontainers.MsSql` (MIT). #14 D-3 reserves it for "only if CI leaves Windows LocalDB". CI already runs a SQL
  container via the override, and adding it would change the test posture, which is not this deliverable's remit.
- `Microsoft.AspNetCore.Mvc.Testing` and EF Core. They back only the dropped source members.
- A Cloudstrap-owned `CapturingLoggerProvider`. It is more code for the same capability (OQ-3 option b).
- `Aspire.Hosting.Testing`. Excluded by the Aspire posture.
- `MartinCostello.Logging.XUnit`-style sinks. They are test-framework-specific.

Closure summary: **one new test-only package** (MIT). Zero new runtime dependencies, zero shipped-package changes,
zero `Aspire.*`, zero `Nihdi.*`.

---

## Deliberate Behavior Changes

**Versus the source library**:

1. **No `Cloudstrap.Testing` package ships** (OQ-1 (a)). The founding row "Rename only" is not honored and needs the
   user's amendment.
2. **No `WebApplicationFactory` subclass.** No process-wide environment mutation, no `UnitTest` environment, and no
   security-disable flags.
3. **No DI replace helpers.** The framework's `RemoveAll`/`AddSingleton` and EF's documented pattern are the path.

**Versus Cloudstrap's current test infrastructure**:

4. **`CLOUDSTRAP_TEST_SQL` semantics change** from "the whole connection string" to "server, authentication and
   options; the catalog comes from the suite". CI's `Database=CloudstrapCi` becomes inert, and `ci.yml` drops it and
   fixes its comment.
5. **Hangfire's storage tests move** from the Messaging suite's `CloudstrapMessagingTests` to their own
   `CloudstrapHangfireTests`.
6. **Under the override, the E2E demo hosts use `CloudstrapDemo`** (as they do locally) instead of `CloudstrapCi`.
7. **`ResetAsync` clears only its own pool and validates the catalog name.** It no longer calls
   `SqlConnection.ClearAllPools()` or interpolates unvalidated names.
8. **Log assertions read `FakeLogRecord`** instead of `CapturedLogEntry` or a tuple. The meaning is unchanged
   (AC-TS2).
9. **The Hangfire suite's static `InvocationRecorder` is renamed.**

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| The override has no `Database=` | The catalog is added. No error. |
| The override names `Database=master` | Ignored. The suite's catalog replaces it, so the helper never targets `master` (AC-TS5/AC-TS6). |
| The override uses `Authentication=Active Directory Default` (Azure SQL) | It is passed through untouched. The pinned SqlClient 6.1.7 supports it in-package (#16 pin rationale). The identity needs `CREATE DATABASE` permission, which is documented in the helper's summary. |
| Two suites run concurrently against one server (a local parallel runner) | Safe: distinct catalogs. |
| Two fixtures in one suite run in parallel against one catalog | Not supported. One resets the other's database. This is today's constraint, now stated explicitly (Behaviors). |
| LocalDB is not installed and the override is unset | The first `OpenAsync` fails with the provider's error, which is today's behavior. No new skip logic (no gold-plating). |
| `FakeLogCollector` used across a host restart in one test | The snapshot accumulates, as today's providers did. `Clear()` exists if a test wants a boundary. |
| A test asserted `entry.Exception` via string concatenation (`Message + " " + Exception`) | The same expression works over `FakeLogRecord`, whose `Exception` property has the same type. |
| The E2E suite runs with the override on a server where `CloudstrapDemo` already exists from a previous run | The demo hosts' EF/Wolverine/Hangfire provisioning is idempotent (the #16 edge case "persistent dev database"). The E2E suite never resets `CloudstrapDemo`, as today. |

---

## Out of Scope (the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace,
  ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- Everything Dropped in Part A: `CustomWebApplicationFactory`, `ReplaceAsSingleton`, `ReplaceDbContextAsSingleton`, the
  source csproj's `Mvc.Testing`/EF Core references, and the source README.
- A **shipped** `Cloudstrap.Testing` NuGet package, and any consumer-facing test-helper surface (OQ-1). The
  `Cloudstrap.Testing.*` id family stays reserved for demand-driven test-audience packages such as deferred **#26**
  (`Cloudstrap.Testing.IdentityProvider`). This deliverable neither schedules nor pre-empts #26, and it does not touch
  `src/Test/TestIdentityProvider/`.
- Consumer-facing "how to test your Cloudstrap app" documentation (stock `WebApplicationFactory` + `FakeLogger`). It
  belongs with a docs deliverable if the user wants it.
- A sweep of the 44-file "valid `Cloudstrap:Application`" literal. Suites may adopt `CloudstrapTestHost` later,
  opportunistically.
- Hoisting `CapturingPrimaryHandler` ×4 or `RecordingDistributedCache` ×2 (different contracts, finding 8).
- Consolidating the E2E `CLOUDSTRAP_TEST_BLOB` blocks, or any change to `CLOUDSTRAP_TEST_BLOB` semantics.
- `Testcontainers.*`, and any change to CI's SQL/Azurite container setup beyond the override string and its comment.
- Strengthening "never appears" assertions to cover `FakeLogRecord.StructuredState`. This is a worthwhile follow-up
  that each package's owner can pick up, but it changes test meaning and is therefore not part of a
  count-preserving repoint.
- Moving the repo's `Microsoft.Extensions.*` family from 10.0.10 to 10.0.12 (the prerequisite for
  `Diagnostics.Testing` 10.10.0). That is a separate dependency-update change.
- Updating CLAUDE.md's "Project Structure" line `Cloudstrap.Testing/ # Test helper utilities`. Flagged for whoever owns
  CLAUDE.md once OQ-1/OQ-7 are answered. It is not a spec artefact.

---

## Decision Log

### Gate answers

| # | Decision | Answered |
|---|---|---|
| D-1 | **OQ-1 → (a): Cloudstrap does not ship `Cloudstrap.Testing`.** Shared test infrastructure is a test-only, non-packable library under `src/Test/`. Founding Package Map row amended to "— *(not ported)*". | User, 2026-10-05 |
| D-2 | **OQ-2 → (a): drop all three source members** (`CustomWebApplicationFactory`, both DI helpers). Stock `WebApplicationFactory<T>`, `RemoveAll` + `AddSingleton`, and EF's documented test pattern replace them. Nothing from `Nihdi.Core.Testing` is ported. | User, 2026-10-05 |
| D-3 | **OQ-3 → (a): replace every `CapturingLoggerProvider` copy with `Microsoft.Extensions.Diagnostics.Testing` pinned at 10.8.0** (fits the current M.E.* 10.0.10 pins). Test-only dependency; the 10.10.0 / M.E.* 10.0.12 family bump is a separate change. | User, 2026-10-05 |
| D-4 | **OQ-4 → (a): `CLOUDSTRAP_TEST_SQL` supplies server, authentication and options only; the helper always sets the catalog.** Per-suite catalogs everywhere (E2E uses `CloudstrapDemo`); `ci.yml` drops `Database=CloudstrapCi` and fixes the stale comment; pool clearing is scoped to the helper's own connection string. | User, 2026-10-05 |
| D-5 | **OQ-5 → (a): the E2E suite consumes the shared SQL helper; the unchanged E2E suite green (LocalDB locally, override in CI) is the proof. No new E2E test** — user-approved deviation from CLAUDE.md workflow rule 9. | User, 2026-10-05 |
| D-6 | **OQ-6 → (a): hoist the SQL helper, `InvocationRecorder` and the `MessagingTestHost` pair (as neutral `CloudstrapTestHost`), plus the E2E SQL forwarding; replace log capture (D-3). Everything else stays local.** | User, 2026-10-05 |
| D-7 | **OQ-7 → (a): `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/`** (namespace `Cloudstrap.TestInfrastructure`, non-packable) plus `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/`. `Cloudstrap.Testing.*` stays reserved for shipped packages. | User, 2026-10-05 |

### Analyst decisions (proposed, approved together with the spec)

| # | Decision | Basis |
|---|---|---|
| DD-1 | **`CapturingPrimaryHandler` ×4 and `RecordingDistributedCache` ×2 stay local.** They are not duplicates. | Finding 8. |
| DD-2 | **The Observability suite's private `FakeLoggerProvider` stays local.** It is a sentinel, not a capture helper, so there is no name clash with Microsoft's type in that suite (it will not reference the package). | Finding 7. |
| DD-3 | **Catalog names**: Messaging keeps `CloudstrapMessagingTests`, AzureBlob keeps `CloudstrapClaimCheckTests`, and Hangfire gets `CloudstrapHangfireTests`. E2E uses `CloudstrapDemo` (the demo appsettings name). | Least churn. AC-TS3. |
| DD-4 | **Catalog-name validation**: letter-first `[A-Za-z0-9_]`, system databases rejected, and bracket-quoting in DDL. | Finding 6. The helper issues `DROP DATABASE`, so a proportionate guard is warranted. |
| DD-5 | **The Hangfire static `InvocationRecorder` is renamed** (sketch `TaskRunLog`). The hoisted instance type keeps the name `InvocationRecorder`, which minimizes churn in the messaging suites. | Finding 10. |
| DD-6 | **The shared library gets its own small companion suite**, proving the connection-string composition and name validation without SQL Server. This follows the precedent of `Cloudstrap.TestIdentityProvider.Tests`. | AC-TS11. It is the only new tests this deliverable adds. |
| DD-7 | **No new runtime dependency and no shipped-package change.** Every edit is under `src/Test/`, plus `ci.yml` (override string and comment). | Scope. |

---

## Open Questions

### OQ-1: Ship `Cloudstrap.Testing` or not? (founding-spec conflict) — ✅ ANSWERED (a), see D-1

**What I found.**
- The founding row (`_specs/Cloudstrap.md:95`) maps `Nihdi.Core.Testing` → `Cloudstrap.Testing`, "Rename only".
- After the Part A verdicts, the source contributes **nothing**:
  - The factory's additions are defects (finding 2).
  - The DI helpers are wrong or trivial (findings 3 and 4).
  - The package has zero consumers (finding 1).
- The in-repo demand is Cloudstrap's own test plumbing:
  - A helper that **drops databases** named by an environment variable. Shipping that to consumers is a footgun.
  - A Wolverine-fixture recorder.
  - Fixed `contoso` settings.
- Consumer-grade log capture already exists as a Microsoft package.

**Why it matters.** A shipped test package is a public-API one-way door. It also has an immediate CI consequence: any
packable `src/**` project is preview-published (`ci.yml:110`).

**Options.**
- **(a) Test-only shared library under `src/Test/`, and nothing ships.** The founding row is amended to
  "`Nihdi.Core.Testing` → — *(not ported)*. Cloudstrap's own shared test infrastructure lives in the test-only
  `Cloudstrap.TestInfrastructure`. The `Cloudstrap.Testing.*` id family is reserved for demand-driven test-audience
  packages (#26)." The #15 amendment is the format precedent.
- **(b) Ship a small consumer `Cloudstrap.Testing`**, e.g. a fixed factory plus a valid-settings fixture, alongside
  the test-only library for SQL/Wolverine helpers.
- **(c) Ship everything.**

**Recommendation: (a).** No consumer need has been demonstrated, so shipping would publish a one-way door with nothing
worth putting behind it. The #26 posture ("demand-driven, promote once shape-proven by internal consumers") applies
equally here. If demand appears, a shipped `Cloudstrap.Testing` can be *promoted* later from proven internal code.

### OQ-2: Source verdicts (factory and DI helpers) — ✅ ANSWERED (a), see D-2

**What I found.** Findings 2–4. The source repo's own review flags P2-33 (wrong descriptor) and P2-34 (process-wide,
Linux-blind env vars).

**Why it matters.** Porting and fixing means owning EF-version-specific DI internals and a factory whose only value is
flags Cloudstrap removed.

**Options.**
- **(a) Drop all three.** The stock `WebApplicationFactory<T>`, `RemoveAll` + `AddSingleton`, and EF's documented
  pattern replace them.
- **(b) Port and fix the factory** (no env vars, no forced `TDbContext`, standard environment) and **drop** the DI
  helpers.
- **(c) Port and fix all three**, with a regression test for P2-33.

(b) and (c) are only meaningful under OQ-1 (b) or (c).

**Recommendation: (a).** A fixed factory is `WebApplicationFactory<T>` with nothing added. A fixed
`ReplaceDbContextAsSingleton` still builds a thread-unsafe singleton context. Dropping is the only verdict that
reduces owned code without losing a capability.

### OQ-3: Log capture: adopt `Microsoft.Extensions.Diagnostics.Testing` or build one Cloudstrap provider? — ✅ ANSWERED (a), see D-3

**What I found.** Finding 7: every assertion maps onto `FakeLogRecord` (`Category`, `Level`, `Message`, `Exception`),
with an ordered thread-safe `GetSnapshot()`, and exact counts and absence work via LINQ. The package is MIT, Microsoft,
monthly releases, 17.6 M downloads. Finding 12: 10.10.0 needs the M.E.* 10.0.12 family, while 10.8.0 fits the current
10.0.10 pins.

**Why it matters.**
- This is a new test-only dependency, a dependency-update risk area.
- The alternative is keeping about 80 lines of Cloudstrap-owned code in one hoisted copy.

**Options.**
- **(a) Replace with the package, pinned at 10.8.0**, referenced by the seven suites.
- **(b) Hoist one unified Cloudstrap `CapturingLoggerProvider`** (the record shape) into the shared library.
- **(c) Replace, but pin 10.10.0 and bump the M.E.* family to 10.0.12** in the same deliverable.

**Recommendation: (a).** It deletes every copy and leaves zero owned code (CLAUDE.md rule 4 and the "Replace beats
Port" cost rule). 10.8.0 keeps this deliverable free of a family-wide version move; the family bump is a separate
change.

### OQ-4: Catalog parameterization and `CLOUDSTRAP_TEST_SQL` semantics (CI impact) — ✅ ANSWERED (a), see D-4

**What I found.** Finding 6. Today the override replaces the whole connection string, so under CI **all** SQL suites
**and** the E2E demo hosts share `CloudstrapCi` (`ci.yml:77`). This is safe only because execution is sequential and
alphabetical. `ClearAllPools()` is process-wide.

**Why it matters.** This is the shared contract between test executables. A per-suite catalog is meaningless if the
override overrides it too.

**Options.**
- **(a) The override supplies server, authentication and options; the helper always sets the catalog.** Suites get
  their own catalogs, and the E2E suite uses `CloudstrapDemo`. `ci.yml` drops `Database=CloudstrapCi` and fixes the
  stale comment. Pool clearing is scoped to the helper's own connection string.
- **(b) Keep whole-string semantics.** Per-suite catalogs apply only to LocalDB, and CI keeps sharing one catalog
  (hazard retained, and CI no longer equals local).
- **(c) Treat the override's catalog as a prefix** (`CloudstrapCi_CloudstrapHangfireTests`). More rules, no added
  value.

**Recommendation: (a).** It is the only option under which "no two suites share a catalog" holds everywhere and CI
means the same thing as a local run. The `ci.yml` edit is one line plus a comment.

### OQ-5: Demo/E2E rule (CLAUDE.md workflow rule 9) for a test-audience deliverable — ✅ ANSWERED (a), see D-5

**What I found.**
- This deliverable changes no running-app behavior.
- The E2E suite has seven SQL-override forwarding sites that must follow the new OQ-4 rule anyway.
- A consumer-style factory example would need `Microsoft.AspNetCore.Mvc.Testing` and `public partial class Program`
  on a demo host, which the OQ-2 drop makes pointless.

**Why it matters.** Rule 9 asks for a demonstration slice plus at least one E2E test. There is nothing new to
demonstrate in a demo app.

**Options.**
- **(a) The E2E suite consumes the shared helper** (AC-TS9). The proof is the unchanged E2E suite (68 tests) green
  under LocalDB locally and under the override in CI. **No new E2E test**, recorded as a user-approved deviation.
- **(b) A consumer-style `WebApplicationFactory` integration example against a demo host.** This contradicts OQ-2 (a)
  and adds a test dependency.
- **(c) A full waiver.** The E2E suite is untouched, and its forwarding keeps whole-string semantics, which conflicts
  with OQ-4 (a).

**Recommendation: (a).** The E2E suite is a real consumer of the hoisted helper, so running it proves the hoist. A new
test would only restate an existing one.

### OQ-6: Scope of the sweep — ✅ ANSWERED (a), see D-6

**What I found.** Findings 8–10:
- The handler and cache doubles are four and two different contracts.
- The settings literal appears in 44 files with intentionally varying values.
- `MessagingTestHost` and `InvocationRecorder` are identical pairs.

**Why it matters.** Scope drives regression surface. The repoint already touches seven suites plus E2E.

**Options.**
- **(a) Hoist the SQL helper, `InvocationRecorder` and the `MessagingTestHost` pair** (as a neutral
  `CloudstrapTestHost`), plus the E2E SQL forwarding. Replace log capture. Leave everything else local.
- **(b) (a) plus a sweep of all per-suite `*TestHost` valid-settings literals** onto the shared helper.
- **(c) (a) plus hoisting `CapturingPrimaryHandler` / `RecordingDistributedCache`** as configurable shared doubles.
- **(d) Minimal: the SQL helper and log capture only.** Leave the messaging pair duplicated.

**Recommendation: (a).** It removes every true duplicate and leaves near-duplicates with different contracts alone.
(b) churns 40+ files for no defect. (c) owns more code than it deletes. (d) leaves two identical pairs whose third
consumer, the next messaging provider, is already on the roadmap.

### OQ-7: Naming and placement — ✅ ANSWERED (a), see D-7

**What I found.** Finding 11:
- A shared library under `src/Test/` must not end in `.Tests`.
- The test-only precedent is `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/`.
- `Cloudstrap.Testing.*` is the id family #26 reserves for **shipped** test-audience packages.
- CLAUDE.md lists `src/Cloudstrap.Testing/` as a target project.

**Why it matters.** Naming a never-shipped project `Cloudstrap.Testing` squats the id a future shipped package would
want, and blurs "shipped vs test-only" in the family.

**Options.**
- **(a) `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/`** (namespace `Cloudstrap.TestInfrastructure`),
  plus `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/`. `Cloudstrap.Testing.*` stays reserved for shipped
  packages.
- **(b) `src/Test/Cloudstrap.Testing/`**, test-only but using the founding name.
- **(c) `src/Cloudstrap.Testing/`**, packable. This is only coherent with OQ-1 (b) or (c).

**Recommendation: (a).** It mirrors the `Cloudstrap.TestIdentityProvider` precedent, keeps the shipped-family name
free, and makes the test-only status legible from the path alone.
