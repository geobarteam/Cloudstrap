# Plan: 11-BlazorSharedAbstractions — A consumer registers their whole Blazor presentation layer (ViewModels + Services, by convention, every convention overridable) with one `AddCloudstrapBlazorCommon<TAssemblyMarker>()` call, and the Blazor band (#12/#13/#20) gets its shared `IViewModel` / `IErrorHandler` contracts

## Overview

Deliverable #11 of the extraction roadmap: the new `Cloudstrap.BlazorCommon` package — the suite's
**tenth**, and the leaf the whole Blazor band builds on (#12 BlazorServer and #13 BlazorWasm reference
it; #20 Dashboard UI consumes it transitively — every shape shipped here is a mini one-way door).
**Binding spec: `_specs/11-BlazorSharedAbstractions.md`** (APPROVED 2026-08-22, zero Open Questions).
Its Port Decision Table (**3 Redesign · 1 Port (the Scrutor dependency) · 5 Drop**), Public API Sketch,
Behaviors & Conventions table, Dependencies table, Deliberate Behavior Changes D-1…D-5, Edge Cases,
Out of Scope list and Decision Log are authoritative and are not re-litigated here. Nothing the spec
marked Drop appears in this plan: no `INavigationService`/`NavigationService`/`AddNavigationService`
(D-3), no `NihdiWasmControls` or any assembly-discovery mechanism (D-5), no `ShowWarning`/`ShowSuccess`
on `IErrorHandler` (D-2), no default `IErrorHandler` implementation (MudBlazor stays out — #20's
territory), no `Microsoft.AspNetCore.Components` reference (the shipped package references **no Blazor
package at all**), no `Cloudstrap:` configuration section and no `IConfiguration` read (AC-BC7), no
MVVM base classes / state containers, no `AddPresentationServices` or `AddBlazorCommonForNihdi` names
(both folded into the single composite, D-4).

Reference patterns, all read in full before planning:

- **Primary shape precedent: `_plans/7-WorkerBootstrap.md` / `_plans/6-MvcBootstrap.md`** — the newest
  new-package precedents: slice/step/gate granularity, brand-new-project RED mechanics (the honest
  first failure is the test project failing to compile against missing types), `PackageSurfaceTests`
  permanent guards, the packaging step shape, the demo + E2E demonstration slice, the final-gate AC
  walk.
- **Registration pattern to match: `src/Cloudstrap.Core/ServiceCollectionExtensions.cs`** —
  `IServiceCollection`-returning extension, guard clause, XML-doc style. Note the deliberate
  difference: Core binds `IConfiguration` options; this package **must not** (AC-BC7) — its
  `BlazorCommonOptions` is a code-level knob object consumed at the call, never registered, never
  bound.
- **Demonstration vehicle (read on disk)**: `src/demo/BlazorWasm/` — `Client/Program.cs` (the WASM
  composition root where the entry-point call lands), `Presentation/` RCL (`DoctorsPage.razor` +
  `.razor.cs` — the page this plan restructures to the ViewModel pattern; `_Imports.razor`;
  `Shared/MainLayout.razor` already renders `<MudSnackbarProvider />`, so the consumer error handler
  needs no layout change), `Bff/Controllers/DoctorController.cs` (gains the blank-name rejection that
  makes the error path reachable), `README.md` (the feature matrix this plan extends).
- **E2E harness (read on disk)**: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/DoctorsTests.cs` — the
  `PageTestBase` + `BrowserSignIn.SignInAsync` + `GetByTestId` + `ConsoleErrors` patterns the new test
  follows; `E2eFixture` boots IdP 5310 → Api 5330 → Bff 5300 unchanged.
- **Test infrastructure**: `src/Test/Directory.Build.props` (`.Tests`-suffix NUnit/MTP wiring
  inherited), `src/Test/UnitTest/Cloudstrap.Mvc.Tests/PackageSurfaceTests.cs` (the guard-test shapes
  Step 3 mirrors, adapted — this package **does** publish interfaces).

This is a library deliverable with no database, no endpoints and no UI of its own: the plan-template's
endpoint-integration block does not apply literally. Its equivalent here is that **every step's tests
build a real `ServiceCollection` / generic-host builder, run the real Scrutor scan over real fixture
types, and assert observable resolution behavior** — plus the mandatory demonstration slice (Step 4)
driving the real browser against the restructured demo page.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-BC1 (default scan: suffix classes resolvable via implemented interfaces, transient, distinct instances) | 1 |
| AC-BC2 (host-model parity; no Blazor / no `Microsoft.AspNetCore.App` in the shipped closure) | 1 (parity halves) · 3 (closure guard + nupkg inspection) |
| AC-BC3 (custom suffixes + lifetime fully replace the defaults) | 2 |
| AC-BC4 (non-matching / abstract / non-public excluded; scan bounded to marker + explicit assemblies) | 1 (exclusions) · 2 (assembly boundary) |
| AC-BC5 (`IErrorHandler` contract-only: no implementation, no default registration; consumer's implementation receives the call) | 1 (package half) · 4 (live consumer half) |
| AC-BC6 (`IViewModel.InitializeAsync(CancellationToken = default)` — D-1 signature) | 1 (signature pin) · 4 (live: the demo page awaits it) |
| AC-BC7 (no `Cloudstrap:` section, no `IConfiguration` read — registration succeeds with zero configuration) | 1 (behavioral) · 3 (permanent no-Configuration-reference guard) |
| AC-BC8 (build/tests/format, XML docs, metadata, closure review, identifier sweep, OSI + CPM-pinned deps) | 3 |
| AC-BC9 (demo restructured to a convention-registered ViewModel; existing E2E green; ≥ 1 new E2E) | 4 |
| AC-ASP2 (zero `Aspire.*` in the closure) | 3 (permanent guard) |
| AC-A3 (zero `Nihdi.AspNetCore` references) | 3 (permanent guard; this package references no auth package at all) |

### New CPM entry: **`Scrutor` 7.0.0** ⚠️ *(dependency update — risk area, reviewed at Gate 1)*

The spec's one new external dependency (MIT, khellang/Scrutor, current release 2025-11-24, verified in
the spec): a new `<PackageVersion Include="Scrutor" Version="7.0.0" />` in `src/Directory.Packages.props`
with the repo's license/justification comment (CLAUDE.md rule 4). It powers the convention scan and is
a **normal public dependency** of the package — "call Scrutor directly" is the documented escape hatch,
so it is deliberately not wrapped behind an abstraction. The only other reference is the already-pinned
`Microsoft.Extensions.DependencyInjection.Abstractions`. **Zero project references** — the package is
standalone (depends on deliverable #0 repo infrastructure only). The test project adds only
already-pinned packages (`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting`).
The demo/E2E projects gain no new package reference.

### ⚠️ Risk areas (reviewed at the gates named)

- **Public API one-way doors (new package = all-new API, consumed by #12/#13/#20)** — `IViewModel`,
  `IErrorHandler`, `BlazorCommonOptions`, `AddCloudstrapBlazorCommon<TAssemblyMarker>` are the Blazor
  band's foundation surface, signed off **verbatim against the spec's Public API Sketch** at **Gate 1**.
  The D-1 (`CancellationToken`) and D-2 (two-method `IErrorHandler`) doors are pinned by permanent
  signature tests.
- **New external dependency (`Scrutor` 7.0.0)** — new CPM entry, license + closure reviewed at
  **Gate 1** and the **final gate** (nupkg dependency list).
- **Shared demo contracts / demo auth surface** — Step 4 touches the Bff's `DoctorController` (a
  validation guard) and the Presentation RCL, but no shared DTO in `Cloudstrap.Demo.Contracts` changes
  and no auth code changes; the existing E2E suite staying green unchanged is the tripwire (final gate).

### Planner mechanics decided here (no spec conflict; each flagged for review at the named gate)

**(a) Options consumed eagerly at the call — never registered.** `AddCloudstrapBlazorCommon<TAssemblyMarker>`
creates `new BlazorCommonOptions()`, applies `configure?.Invoke(options)`, then runs the scan with the
resulting values. Nothing is added to DI for the options themselves (no `IOptions<BlazorCommonOptions>`),
no `IConfiguration` is touched — the AC-BC7 posture made structural. `ConventionSuffixes` and
`AdditionalAssemblies` are mutable `IList<>` properties initialized to `["ViewModel", "Service"]` /
empty (get-only properties, list contents mutable — the spec sketch's shape); `Lifetime` defaults to
`ServiceLifetime.Transient`. *(Gate 1.)*

**(b) The scan shape.** One `services.Scan(...)` pass per call: `FromAssemblies(marker + AdditionalAssemblies)`
→ for each configured suffix, `AddClasses(c => c.Where(t => t.Name.EndsWith(suffix, StringComparison.Ordinal)), publicOnly: true)`
→ `AsImplementedInterfaces()` → `WithLifetime(options.Lifetime)`. Consequences pinned by tests rather
than re-decided: public concrete classes only (abstract/non-public excluded — Scrutor default),
a matching class with no interfaces registers nothing, per-suffix passes do not de-duplicate a name
matching two suffixes, and repeated entry-point calls append (standard `IServiceCollection` semantics —
documented in the README, deliberately not "fixed"). Exact Scrutor fluent chain is executor latitude;
the behaviors are pinned. *(Gate 1.)*

**(c) Guard posture.** `ArgumentNullException.ThrowIfNull(services)`. A `ConventionSuffixes` entry that
is null/empty/whitespace throws `ArgumentException` at the call (fail loud — a silent match-everything
`EndsWith("")` would be a trap); an entirely **empty** suffix list is legal and scans nothing (the
spec's edge case: a deliberate no-op profile). *(Gate 1 — the null-entry rule is planner-added within
the spec's guard-clause rule; flagged for explicit confirmation.)*

**(d) Fixture strategy — no second fixture project.** The test project itself is the scanned assembly:
public top-level fixture types under `Fixtures/` (`SampleViewModel : ISampleViewModel`,
`SampleService : ISampleService`, plus the exclusion cast: `AbstractSampleViewModel` (abstract),
`InternalSampleViewModel` (internal), `PlainHelper` (no matching suffix), `OrphanViewModel` (no
interfaces), `CustomSuffixPresenter : ICustomSuffixPresenter` for the override tests). The
`AdditionalAssemblies` test inverts roles: marker = the **package** assembly (which contains no
matching classes — itself a meaningful assertion), `AdditionalAssemblies = [test assembly]` → fixtures
registered proves extra assemblies are scanned and the marker assembly alone yields nothing (AC-BC4's
boundary both ways). No `InternalsVisibleTo` needed — the entire behavior is observable through
resolution. *(Gate 1.)*

**(e) AC-BC2's "WebApplicationBuilder" read.** The parity halves are proven with a plain
`ServiceCollection` (WASM-style) and `Host.CreateApplicationBuilder().Services` (server-style host
builder) resolving identically — **without** adding an ASP.NET dependency to the unit-test project.
The criterion's second clause (no `Microsoft.AspNetCore.App` framework reference, no Blazor package)
is proven where it lives: the Step 3 closure guard (no referenced assembly starts
`Microsoft.AspNetCore`) plus the final-gate nupkg inspection. *(Gate 1 — flagged as a deliberate
interpretation; a literal `WebApplicationBuilder` test would drag a framework reference into the test
project for no additional proof.)*

**(f) Contract-shape pins as permanent tests.** The D-1/D-2 one-way doors become reflection tests in
Step 1 (not Step 3): `IViewModel` has exactly one method `InitializeAsync` returning `Task` with a
single optional `CancellationToken` parameter; `IErrorHandler` has exactly `HandleError(Exception)` +
`ShowError(string)` and nothing else; the package assembly declares **no** type implementing
`IErrorHandler` and the entry point adds **no** `IErrorHandler` registration (AC-BC5's package half).
*(Gate 1.)*

**(g) Docs drift owned by this deliverable vs. the user.** `.claude/instructions/blazor.md` currently
describes the old surface (`INavigationService`, four-method `IErrorHandler`,
`AddPresentationServices`, `Add<Feature>ForCloudstrap` naming) — the spec makes updating it part of
this deliverable's definition of done: Step 3 rewrites its **BlazorCommon rows/sections only** to the
shipped surface and leaves the BlazorServer/BlazorWasm sections for #12/#13 (with a one-line drift
note). **Not in this plan** (user-owned edits, per the spec's Decision Log): the founding-spec package-map
amendment in `_specs/Cloudstrap.md` and the matching CLAUDE.md project-structure comment
("ErrorHandler, Navigation, ViewModel" → "ErrorHandler, ViewModel, convention scan") — the final gate
reminds the user. *(Final gate.)*

**(h) Full-suite check** (the standing convention: `runTests` is not on the agent PATH — VERIFY invokes
each exe directly). The check means: `dotnet build src/Cloudstrap.sln`, then the **11** unit exes under
`src/Test/UnitTest/<Name>.Tests/bin/Debug/net10.0/<Name>.Tests.exe` (Core, Observability,
Observability.AzureMonitor, Extensions, WebApi, Mvc, Worker, TestIdentityProvider,
Authentication.ClientCredentials, Authentication.OpenIdConnect, **BlazorCommon** — new in Step 1),
then the E2E exe `src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe`,
then `dotnet format src/Cloudstrap.sln --verify-no-changes`.

**Target consumer composition** (the spec sketch made concrete — also the demo Client, Step 4, and the
README example, Step 3):

```csharp
// WASM client (or any Blazor host) — Program.cs
builder.Services.AddCloudstrapBlazorCommon<IDoctorsViewModel>();          // scans the marker's assembly
builder.Services.AddScoped<IErrorHandler, SnackbarErrorHandler>();        // consumer-owned, any lifetime
// Escape hatch for anything beyond the three knobs: call Scrutor directly — it is a public dependency.
```

---

## Slice 1 — One call registers the presentation layer by convention, and the Blazor band's contracts exist ⚠️ PUBLIC-API / NEW-DEPENDENCY RISK AREA

---

## Step 1 — One `AddCloudstrapBlazorCommon<TAssemblyMarker>()` call on a plain, configuration-free `ServiceCollection` registers every public concrete `*ViewModel`/`*Service` as its implemented interfaces (transient, distinct instances), excludes everything else, resolves identically on a server-style host builder, and ships the band's two contracts with their door-pinning signatures — while registering no `IErrorHandler` of its own (AC-BC1; AC-BC2's parity halves; AC-BC4's exclusions; AC-BC5's package half; AC-BC6's signature; AC-BC7)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Directory.Packages.props` *(modify)* — the new `Scrutor` 7.0.0 pin in its own `ItemGroup` with
  the repo's license comment (MIT, khellang/Scrutor — powers the Cloudstrap.BlazorCommon convention
  scan; the spec's Dependencies-table justification). ⚠️ New external dependency — Gate 1.
- `src/Cloudstrap.BlazorCommon/Cloudstrap.BlazorCommon.csproj` *(create)* — Sdk project
  (`Microsoft.NET.Sdk`, **not** Razor — the package contains no components), `TargetFramework=net10.0`,
  `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`; `<PackageReference>` `Scrutor` +
  `Microsoft.Extensions.DependencyInjection.Abstractions` (version-less, CPM); **zero
  `<ProjectReference>`, zero `<FrameworkReference>`** — the D-3 headline fact. Description/tags/README
  metadata land in Step 3 (the #6/#7 precedent — packable from day one). No `InternalsVisibleTo`
  (mechanic (d): nothing internal to test).
- `src/Cloudstrap.BlazorCommon/IViewModel.cs` *(create)* — `public interface IViewModel` with
  `Task InitializeAsync(CancellationToken cancellationToken = default);` (D-1, spec sketch verbatim).
  XML docs state the contract expectation for an already-cancelled token (implementer's duty — the
  spec's edge case) and the intended call site (`OnInitializedAsync`).
- `src/Cloudstrap.BlazorCommon/IErrorHandler.cs` *(create)* — `public interface IErrorHandler` with
  exactly `void HandleError(Exception exception);` + `void ShowError(string message);` (D-2). XML docs
  state: consumer-implemented, Cloudstrap ships no implementation and no registration; register at any
  lifetime.
- `src/Cloudstrap.BlazorCommon/BlazorCommonOptions.cs` *(create)* — `public sealed class` per
  mechanic (a): get-only `IList<string> ConventionSuffixes` initialized `["ViewModel", "Service"]`,
  `ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient`, get-only
  `IList<Assembly> AdditionalAssemblies` initialized empty. XML docs state it is a code-level knob
  object — **not** bound to `IConfiguration`, no `Cloudstrap:` section exists for this package (AC-BC7).
- `src/Cloudstrap.BlazorCommon/ServiceCollectionExtensions.cs` *(create)* —
  `public static IServiceCollection AddCloudstrapBlazorCommon<TAssemblyMarker>(this IServiceCollection services, Action<BlazorCommonOptions>? configure = null)`:
  guard clause, eager options (mechanic (a)), suffix validation (mechanic (c)), the Scrutor scan
  (mechanic (b)). XML docs carry: what is scanned and registered, the default conventions and their
  overrides, the interfaces-only registration shape, the append-on-repeat semantics, and the
  call-Scrutor-directly escape hatch.
- `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/Cloudstrap.BlazorCommon.Tests.csproj` *(create)* —
  `net10.0`, `<ProjectReference>` to the package, version-less `<PackageReference>`s
  `Microsoft.Extensions.DependencyInjection` (for `BuildServiceProvider`) +
  `Microsoft.Extensions.Hosting` (for the parity test); NUnit/MTP wiring inherited from
  `src/Test/Directory.Build.props` via the `.Tests` suffix.
- `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/Fixtures/` *(create)* — the mechanic (d) fixture
  types (each in its own file, public top-level unless the cast requires otherwise):
  `ISampleViewModel.cs`/`SampleViewModel.cs` (implements `ISampleViewModel` **and** `IViewModel` — so
  one fixture also proves a convention-registered class resolves via `IViewModel`),
  `ISampleService.cs`/`SampleService.cs`, `AbstractSampleViewModel.cs` (abstract, implements
  `ISampleViewModel`), `InternalSampleViewModel.cs` (internal), `PlainHelper.cs` (public concrete,
  implements an interface, name matches no suffix), `OrphanViewModel.cs` (public concrete, zero
  interfaces).
- `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/AddCloudstrapBlazorCommonTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/ContractShapeTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under the
  `Test\UnitTest` solution folder (same nesting as the existing ten).

**RED** *(write these tests first; for a brand-new project the honest first failure is the test project
failing to compile against missing types — the #5/#6/#7 precedent — followed by real red runs once the
types exist)*:
- Unit test file: `AddCloudstrapBlazorCommonTests.cs`
  - `AddCloudstrapBlazorCommon_OnNullServices_ThrowsArgumentNullException` (guard clause).
  - `AddCloudstrapBlazorCommon_DefaultConventions_RegistersSuffixClassesAsTheirInterfaces` — on a
    plain `ServiceCollection` **with no configuration of any kind**, marker = the test assembly:
    `ISampleViewModel` resolves as `SampleViewModel`, `ISampleService` as `SampleService` (AC-BC1 +
    AC-BC7's registration-succeeds-with-zero-config clause in one behavioral test).
  - `AddCloudstrapBlazorCommon_DefaultLifetime_IsTransientWithDistinctInstances` — two resolutions of
    `ISampleViewModel` from the same scope yield distinct instances; the registered
    `ServiceDescriptor.Lifetime` is `Transient` (AC-BC1's lifetime clause).
  - `AddCloudstrapBlazorCommon_RegisteredViewModel_ResolvesViaIViewModel` — `SampleViewModel` is also
    reachable through `IViewModel` (Scrutor `AsImplementedInterfaces` covers the package's own
    contract — the shape #12/#13 pages will rely on).
  - `AddCloudstrapBlazorCommon_NonMatchingAbstractAndInternalTypes_AreNotRegistered` — after the call
    there is no descriptor whose implementation type is `AbstractSampleViewModel`,
    `InternalSampleViewModel` or `PlainHelper` (AC-BC4's exclusion set).
  - `AddCloudstrapBlazorCommon_MatchingClassWithNoInterfaces_RegistersNothing` — zero descriptors
    involve `OrphanViewModel` (spec edge case, documented Scrutor semantics kept).
  - `AddCloudstrapBlazorCommon_OnHostBuilderServices_ResolvesIdentically` — the same registrations
    made on `Host.CreateApplicationBuilder().Services` resolve the same set from the built host
    (AC-BC2's parity halves, mechanic (e)).
  - `AddCloudstrapBlazorCommon_AddsNoErrorHandlerRegistration` — after the call, no descriptor has
    `ServiceType == typeof(IErrorHandler)` (AC-BC5's no-default-registration half).
- Unit test file: `ContractShapeTests.cs` *(mechanic (f) — one-way-door pins; genuinely red while the
  contracts don't exist / have the wrong shape)*
  - `IViewModel_InitializeAsync_TakesAnOptionalCancellationTokenAndReturnsTask` (D-1 / AC-BC6).
  - `IErrorHandler_DeclaresExactlyHandleErrorAndShowError` (D-2 — member count and signatures exact).
  - `BlazorCommonAssembly_DeclaresNoErrorHandlerImplementation` — no type in the package assembly
    implements `IErrorHandler` (AC-BC5's contract-only half, made permanent).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.BlazorCommon.Tests\bin\Debug\net10.0\Cloudstrap.BlazorCommon.Tests.exe --filter "AddCloudstrapBlazorCommonTests|ContractShapeTests"
  ```

**GREEN**: the Scope items. Full XML docs on every public member (the four public types are the
package's entire surface).

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: a plain, configuration-free service collection now gains a whole
   convention-registered presentation layer from one call — interfaces-only registration, transient
   distinct instances, the exclusion set honored, identical resolution on a host builder, the two
   band contracts shipped with their pinned signatures, and no error-handler registration smuggled
   in — none of which existed before.
2. Full-suite check (mechanic (h)) — all green (the new exe joins the set); zero build warnings;
   `dotnet format` exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.BlazorCommon.*.nupkg` appears under
   `src/Cloudstrap.BlazorCommon/bin/Release/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Every convention has an override: custom suffixes fully replace the defaults, the lifetime knob is honored, extra assemblies are scanned while the marker's boundary holds, an emptied suffix list is a legal no-op, invalid suffix entries fail loud, and repeated calls append by documented design (AC-BC3; AC-BC4's boundary; the spec's edge-case table)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorCommon/ServiceCollectionExtensions.cs` *(modify — only if Step 1's minimal
  implementation hard-coded anything; the mechanics (a)–(c) design should make most of this step
  pass-through behavior pinned red-first)*
- `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/Fixtures/ICustomSuffixPresenter.cs` +
  `Fixtures/CustomSuffixPresenter.cs` *(create)* — public concrete, name ends in `Presenter`, matches
  no default suffix.
- `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/BlazorCommonOptionsTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `BlazorCommonOptionsTests.cs`
  - `AddCloudstrapBlazorCommon_CustomSuffix_ReplacesTheDefaultsEntirely` — `ConventionSuffixes`
    cleared and set to `["Presenter"]`: `ICustomSuffixPresenter` resolves; `ISampleViewModel` and
    `ISampleService` do **not** (AC-BC3 — "only the overridden suffixes are scanned"; the built-in
    defaults are fully replaceable, not merely extendable).
  - `AddCloudstrapBlazorCommon_LifetimeOverride_IsAppliedToEveryConventionRegistration` —
    `Lifetime = ServiceLifetime.Scoped`: every convention descriptor is Scoped; two resolutions in
    one scope are the same instance, across scopes distinct (AC-BC3's lifetime clause, behaviorally).
  - `AddCloudstrapBlazorCommon_AdditionalAssemblies_AreScannedAndTheMarkerBoundaryHolds` — mechanic
    (d) inverted: marker = the **package** assembly, `AdditionalAssemblies.Add(test assembly)` →
    fixtures resolve; and with the package assembly alone (no additions) → zero convention
    descriptors (AC-BC4's "never touches types outside the marker assembly plus explicitly added
    assemblies", both directions).
  - `AddCloudstrapBlazorCommon_EmptySuffixList_ScansNothing` — `ConventionSuffixes.Clear()`:
    registration succeeds, zero convention descriptors (spec edge case — the legal no-op profile;
    `services` guard still applies).
  - `AddCloudstrapBlazorCommon_WhitespaceSuffix_ThrowsArgumentException` — a null/empty/whitespace
    entry throws at the call, naming the offending value (mechanic (c) — flagged planner rule).
  - `AddCloudstrapBlazorCommon_CalledTwiceWithTheSameMarker_AppendsDuplicateRegistrations` — two
    calls: two descriptors per convention interface; single resolution returns the last, `IEnumerable
    <ISampleViewModel>` returns both (spec edge case — standard DI semantics, documented not "fixed";
    the README states "call once per assembly", Step 3).
  - `AddCloudstrapBlazorCommon_ClassMatchingTwoSuffixes_IsRegisteredPerMatchingPass` — with suffixes
    `["ViewModel", "Service"]` and marker = test assembly, `SampleServiceViewModel :
    ISampleServiceViewModel` *(add this fixture pair in this step)* registers once (ordinal `EndsWith`
    matches `ViewModel` only — the spec's documented per-suffix matching semantics pinned).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.BlazorCommon.Tests\bin\Debug\net10.0\Cloudstrap.BlazorCommon.Tests.exe --filter "BlazorCommonOptionsTests"
  ```

**GREEN**: minimal — these behaviors are the mechanics (a)–(c) design's consequences, pinned
red-first; permitted production changes are the suffix validation and whatever Step 1 left hard-coded.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: every convention now has a working override — the "every convention has an
   override" repo rule the source package violated is observable behavior, and every edge case in the
   spec's table is pinned by a test.
2. Full-suite check (mechanic (h)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the Blazor band's foundation surface is frozen *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **public API one-way doors** — `IViewModel` (D-1 `CancellationToken`),
`IErrorHandler` (D-2 two-method trim), `BlazorCommonOptions` (three knobs, get-only lists),
`AddCloudstrapBlazorCommon<TAssemblyMarker>(this IServiceCollection, Action<BlazorCommonOptions>?)`
against the spec's Public API Sketch **verbatim** — #12/#13/#20 build on exactly these shapes; any
deviation needs naming · **the new `Scrutor` 7.0.0 CPM entry** — license (MIT), comment, and the fact
it is a deliberate *public* dependency (escape-hatch posture, not wrapped) · mechanic (c)'s
planner-added whitespace-suffix `ArgumentException` (confirm, or direct silent-skip) · mechanic (e)'s
AC-BC2 reading (generic-host parity + Step 3 closure guard instead of a literal `WebApplicationBuilder`
test — confirm) · the append-on-repeat and match-once-per-suffix semantics (documented-not-fixed —
confirm the spec's edge-case reading).

- [x] Behavioral verification: test exe output shows — one configuration-free call registering the
  convention set as interfaces with transient distinct instances, `IViewModel` reachability, the
  exclusion cast (abstract/internal/non-matching/no-interface), host-builder parity, and no
  `IErrorHandler` registration (Step 1); the full-replacement suffix override, the lifetime knob, the
  additional-assemblies scan with the marker boundary both ways, the legal empty no-op, the loud
  whitespace failure, the documented append-on-repeat, and the once-per-suffix match (Step 2); plus
  the two contract-shape pins and the no-implementation guard.
- [x] Code review: the four public types vs the spec sketch, verbatim; `sealed` on
  `BlazorCommonOptions`, static extension class, full XML docs incl. the escape-hatch and
  append-semantics remarks; `Cloudstrap.BlazorCommon.csproj` → **zero project references, zero
  framework references**, exactly the two package references; `src/Directory.Packages.props` diff —
  the Scrutor pin and its license comment only.
- [x] User approved — implementation may continue past this gate

---

## Slice 2 — Publishable, permanently guarded, and demonstrated live: the flagship demo's doctors page runs on a convention-registered ViewModel with a consumer-owned error handler

---

## Step 3 — The package is publishable and guarded forever: metadata, README (quick start, knob table, escape hatch, migration notes D-1…D-5), permanent tripwires on the closure (no Aspire, no Nihdi, no `Microsoft.AspNetCore.*`, no `Microsoft.Extensions.Configuration.*`), the forbidden identifiers, the dropped-type resurrections — and the instruction-file drift closed (AC-BC2's no-Blazor-reference half, AC-BC7's guard, AC-BC8, AC-ASP2, AC-A3; mechanic (g))

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.BlazorCommon/Cloudstrap.BlazorCommon.csproj` *(modify)* — `<Description>` (shared
  Blazor abstractions for Cloudstrap apps: the `IViewModel` initialization contract, the
  consumer-implemented `IErrorHandler` feedback contract, and one-call Scrutor convention registration
  of `*ViewModel`/`*Service` classes with overridable suffixes, lifetime and assemblies — usable from
  Blazor Server and WebAssembly alike),
  `<PackageTags>$(PackageTags);blazor;viewmodel;mvvm;scrutor;di;conventions</PackageTags>`,
  `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.BlazorCommon/README.md` *(create)* — quick start (the Overview's consumer snippet:
  the one call + the consumer's own `IErrorHandler` registration; mirrors the Step 4 demo composition);
  the three-knob table (`ConventionSuffixes` default `["ViewModel", "Service"]` · `Lifetime` default
  Transient · `AdditionalAssemblies` default empty) with the **no-configuration statement** (code-level
  knobs at the call site — this package has no `Cloudstrap:` section, AC-BC7); registration semantics
  (interfaces-only — a class with no interfaces registers nothing; append-on-repeat — call once per
  assembly; ordinal suffix matching — don't name classes to match two suffixes); the **escape hatch**
  ("Scrutor is a normal public dependency — call `services.Scan(...)` directly for self-registration,
  decorators, or predicate filters"); the `IErrorHandler` posture (contract only, no implementation,
  no default registration, any lifetime — MudBlazor/Snackbar implementations belong to the consumer);
  the `IViewModel` usage pattern (`OnInitializedAsync` → `InitializeAsync(ct)`, cancellation is the
  implementer's duty); migration notes vs the source (D-1 `CancellationToken` added · D-2
  `ShowWarning`/`ShowSuccess` dropped · D-3 `INavigationService` dropped — inject `NavigationManager`
  directly · D-4 `AddPresentationServices`/`AddBlazorCommonForNihdi` → `AddCloudstrapBlazorCommon`
  with overridable conventions · D-5 `NihdiWasmControls` dropped — use the router's native
  `AdditionalAssemblies` parameter).
- `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/PackageSurfaceTests.cs` *(create)* — permanent
  guards mirroring `Cloudstrap.Mvc.Tests/PackageSurfaceTests.cs`, adapted (this package publishes
  interfaces).
- `.claude/instructions/blazor.md` *(modify — docs, mechanic (g); human-reviewed at the final gate)* —
  the BlazorCommon surface rewritten to the shipped truth: project-roles row → "Shared contracts
  (`IErrorHandler`, `IViewModel`) + convention scan — Scrutor only"; the BlazorCommon section →
  `AddCloudstrapBlazorCommon<TAssemblyMarker>(Action<BlazorCommonOptions>?)`, two-method
  `IErrorHandler`, `InitializeAsync(CancellationToken)`, the three knobs, the escape hatch, no
  navigation abstraction; a one-line drift note that the BlazorServer/BlazorWasm sections still
  describe the source surface until #12/#13 ship. No other section edited.

**RED** *(the guard tests are tripwires against already-correct code and may pass immediately — the
honest failing state is in the artifacts: before GREEN the Release nupkg has no README/description/tags
and the instruction file still describes the dropped surface; recorded per the #2…#7 precedent)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfBlazorCommonAssembly_MatchTheApprovedClosure` — every referenced assembly
    starts with `System` or `Microsoft.Extensions.DependencyInjection` or equals `Scrutor`
    (+ `netstandard`/core facades as observed); explicitly **zero** names starting `Aspire` (AC-ASP2),
    `Nihdi` (AC-A3), `Microsoft.AspNetCore` (the D-3 no-Blazor-package fact — AC-BC2's second half),
    `Microsoft.Extensions.Configuration` (AC-BC7 made structural), `MudBlazor`, `Cloudstrap` (the
    standalone-leaf fact: no project references may ever leak in).
  - `PublicTypes_OfBlazorCommonAssembly_ContainNoForbiddenIdentifiers` — no public type/member matches
    `(?i)nihdi|riziv|dynatrace|nservicebus`.
  - `PublicSurface_IsExactlyTheFourApprovedTypes` — exported types are exactly `IViewModel`,
    `IErrorHandler`, `BlazorCommonOptions`, `ServiceCollectionExtensions`, all in namespace
    `Cloudstrap.BlazorCommon`; every public class sealed or static.
  - `BlazorCommonAssembly_DeclaresNoDroppedConcepts` — the D-3/D-5 drops made permanent: no declared
    type name contains `Navigation` or `WasmControls` (case-insensitive), and no static mutable
    assembly-registry pattern returns (no public static member of type `Assembly[]`).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.BlazorCommon.Tests\bin\Debug\net10.0\Cloudstrap.BlazorCommon.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata, write `README.md`, edit `blazor.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the four new permanent guards.
2. `dotnet build src/Cloudstrap.sln -c Release` →
   `src/Cloudstrap.BlazorCommon/bin/Release/Cloudstrap.BlazorCommon.<version>.nupkg`; expand a `.zip`
   copy → contains `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.BlazorCommon.dll` **and** `.xml`;
   the nuspec shows the MIT license expression, description, tags, repository URL, and a dependency
   list of exactly `Scrutor` + `Microsoft.Extensions.DependencyInjection.Abstractions` — no
   `Cloudstrap.*`, no `Aspire.*`, no `Microsoft.AspNetCore.*`, no framework reference (AC-BC8,
   AC-ASP2, AC-BC2).
3. **AC-BC8 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.BlazorCommon, src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the
   hits, as in plans 2–7).
4. Doc check: `.claude/instructions/blazor.md` BlazorCommon content matches the shipped surface; the
   #12/#13 drift note is present; no other section changed.
5. Full-suite check (mechanic (h)) — all green; `dotnet format` exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — The flagship demo runs on the package: the doctors page is restructured to a convention-registered `DoctorsViewModel` (initialized through `IViewModel` with a real `CancellationToken`) whose failures surface through the consumer's own MudBlazor `IErrorHandler` — proven in the browser by a new E2E test while every pre-existing E2E test stays green (AC-BC5 live, AC-BC6 live, AC-BC9; demonstration slice — workflow rule 9)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/demo/BlazorWasm/Presentation/Cloudstrap.Demo.BlazorWasm.Presentation.csproj` *(modify)* —
  `<ProjectReference>` to `..\..\..\Cloudstrap.BlazorCommon\Cloudstrap.BlazorCommon.csproj`.
- `src/demo/BlazorWasm/Presentation/Doctors/IDoctorsViewModel.cs` *(create)* —
  `public interface IDoctorsViewModel : IViewModel` exposing the page's state and actions:
  `bool SignedIn`, `string SignedInName`, `IReadOnlyList<DoctorDto>? Doctors`, `string NewName` /
  `string NewSpecialty` (settable), `Task AddDoctorAsync()`.
- `src/demo/BlazorWasm/Presentation/Doctors/DoctorsViewModel.cs` *(create)* — `public sealed class
  DoctorsViewModel : IDoctorsViewModel`, constructor-injects `HttpClient` + `IErrorHandler`.
  `InitializeAsync(ct)` performs the auth-state probe and, when signed in, the doctors fetch (passing
  the token to the `HttpClient` calls — AC-BC6 live); `AddDoctorAsync` posts the new doctor inside
  `try/catch` and routes failures to `IErrorHandler.HandleError` / a non-success response to
  `ShowError` with the server's message instead of throwing (AC-BC5 live — the consumer's
  implementation receives the call). Navigation stays **out** of the ViewModel: the D-3 posture —
  pages inject `NavigationManager` directly.
- `src/demo/BlazorWasm/Presentation/Shared/SnackbarErrorHandler.cs` *(create)* — `public sealed class
  SnackbarErrorHandler : IErrorHandler` over MudBlazor `ISnackbar` (`ShowError` → `Severity.Error`
  snackbar; `HandleError` → a generic error snackbar; consumer demo code — the name deliberately ends
  in `Handler`, outside the scan, registered explicitly).
- `src/demo/BlazorWasm/Presentation/Doctors/DoctorsPage.razor.cs` *(modify)* — the page thins to
  framework concerns: inject `IDoctorsViewModel` + `NavigationManager`; `OnInitializedAsync` awaits
  `ViewModel.InitializeAsync()` and redirects to `account/login?returnUrl=/doctors` (forceLoad) when
  `!ViewModel.SignedIn`; `HttpClient` is no longer injected by the page.
- `src/demo/BlazorWasm/Presentation/Doctors/DoctorsPage.razor` *(modify)* — bindings move to
  `ViewModel.*`; existing `data-testid` attributes unchanged (the pre-existing E2E contract).
- `src/demo/BlazorWasm/Presentation/_Imports.razor` *(modify)* — `@using Cloudstrap.BlazorCommon`.
- `src/demo/BlazorWasm/Client/Program.cs` *(modify)* — the demonstration's headline lines:
  `builder.Services.AddCloudstrapBlazorCommon<IDoctorsViewModel>();` +
  `builder.Services.AddScoped<IErrorHandler, SnackbarErrorHandler>();` with the teaching comments
  (convention scan over the Presentation assembly; consumer-owned error handler, package ships none).
- `src/demo/BlazorWasm/Bff/Controllers/DoctorController.cs` *(modify)* — the error path made
  reachable: `Add` rejects a blank/whitespace name with `ValidationProblem` (400) before opening the
  business span (consumer demo code; no shared-contract change, no auth change).
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/DoctorsTests.cs` *(modify)* — one new test (below); the six
  pre-existing tests untouched.
- `src/demo/BlazorWasm/README.md` *(modify)* — feature-matrix row for #11
  (`AddCloudstrapBlazorCommon<IDoctorsViewModel>()` + consumer `IErrorHandler` | the new E2E test name
  · `DoctorsPage_Loads_ShowsSeededDoctors` as the VM-rendered proof) and a harness note (the doctors
  page is the ViewModel-pattern demonstration: convention-registered, `IViewModel`-initialized,
  errors via the consumer's `SnackbarErrorHandler`).

**RED** *(write this test first, run it, confirm it fails — today a blank-name add returns 200 and no
snackbar exists, so the assertion cannot pass)*:
- E2E test file: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/DoctorsTests.cs`
  - `AddDoctor_WithBlankName_ShowsTheConsumersErrorHandlerSnackbar` — sign in, land on `/doctors`,
    leave the name blank, click Add → a MudBlazor snackbar with `Severity.Error` content becomes
    visible (the Bff's 400 travelled: controller → ViewModel `catch`/non-success branch → the
    consumer's `SnackbarErrorHandler` → the browser — AC-BC5 and the whole ViewModel wiring live),
    and the grid gains no blank row. *(Executor note: do not assert `ConsoleErrors` empty in this
    test — Chromium logs the failed 400 fetch as a console error by design; the pre-existing tests
    keep their empty-console assertions untouched.)*
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "AddDoctor_WithBlankName_ShowsTheConsumersErrorHandlerSnackbar"
  ```

**GREEN**: the Scope items. **Every pre-existing E2E test must stay green unchanged** — the restructure
preserves the page's observable contract (`data-testid`s, the auto-login redirect, the seeded grid,
the add round-trip, the `AddDoctor` business span): that is AC-BC9's "page rendering data via a
convention-registered ViewModel" proof, carried by the existing tests now exercising the new wiring.
*(If any existing test is disturbed, the executor reports it at the gate rather than weakening the
assertion.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the new test passes **and all pre-existing E2E tests pass unchanged** (build first;
   one-time `playwright.ps1 install chromium` if needed) — in particular all six `DoctorsTests`, whose
   green runs now flow through `AddCloudstrapBlazorCommon` → `DoctorsViewModel` → `IViewModel.
   InitializeAsync(ct)`.
2. Manual smoke (optional but recorded): run IdP + Api + Bff per `src/demo/README.md`, browse
   `/doctors`, sign in, submit a blank name → error snackbar; submit a valid doctor → row appears.
3. Full-suite check (mechanic (h)) — all green; `dotnet format` exit 0; the demo projects still pack
   nothing (`IsPackable=false` inherited).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #11 complete *(covers Steps 3–4; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires
the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

⚠️ **Risk areas at this gate**: the **packaging check** — the expanded Release
`Cloudstrap.BlazorCommon.<version>.nupkg` (README/icon/dll/xml, MIT, dependency list of exactly
`Scrutor` + `Microsoft.Extensions.DependencyInjection.Abstractions`, no framework reference, no
`Cloudstrap.*`) · the **instruction-file edit** (`.claude/instructions/blazor.md` — reviewed verbatim,
BlazorCommon sections only) · the **demo-consumer changes** (the `DoctorController` validation guard
and the Presentation restructure — no shared-contract or auth-surface change; the untouched
pre-existing E2E suite is the tripwire).

- [x] Behavioral verification: the new
  `AddDoctor_WithBlankName_ShowsTheConsumersErrorHandlerSnackbar` E2E passes and **all pre-existing
  E2E tests pass unchanged** (the six `DoctorsTests` now running through the ViewModel wiring); the
  four `PackageSurfaceTests` guards and the two `ContractShapeTests` pins are green; the expanded
  Release nupkg contents were reviewed; the identifier sweep is empty (self-referential hits only);
  the full-suite check (build + 11 unit exes + E2E exe + `dotnet format --verify-no-changes`) is
  green end to end.
- [x] Spec acceptance sign-off: walk **AC-BC1…AC-BC9 + AC-ASP2 + AC-A3** against the step evidence
  using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop / Out-of-Scope
  lists was resurrected (no `INavigationService`/`NavigationService`/`AddNavigationService`, no
  `NihdiWasmControls` or assembly registry, no `ShowWarning`/`ShowSuccess`, no default `IErrorHandler`
  implementation, no MudBlazor in the package, no `Cloudstrap:` section or `IConfiguration` read, no
  MVVM frameworks/state containers, no `Microsoft.AspNetCore.Components` reference, zero `Aspire.*`,
  zero `Nihdi.*`) and that every De-NIHDI row is closed (`AddBlazorCommonForNihdi` →
  `AddCloudstrapBlazorCommon`, `NihdiWasmControls` gone, no company headers, neutral fixtures).
- [x] Docs review: `src/Cloudstrap.BlazorCommon/README.md` matches as-built behavior (quick start
  mirrors the demo Client composition, knob table, no-configuration statement, registration
  semantics, escape hatch, `IErrorHandler`/`IViewModel` postures, migration notes D-1…D-5);
  `src/demo/BlazorWasm/README.md` matrix row cites the real E2E test names;
  `.claude/instructions/blazor.md` BlazorCommon content is the shipped surface with the #12/#13 drift
  note. **User-owned follow-up (not in this plan, per the spec's Decision Log)**: apply the approved
  founding-spec package-map amendment in `_specs/Cloudstrap.md` and the matching CLAUDE.md
  project-structure comment — "Shared Blazor abstractions (ErrorHandler, ViewModel, convention scan)".
- [x] User approved — deliverable #11 done; project-manager flips the ROADMAP row to ✅.
