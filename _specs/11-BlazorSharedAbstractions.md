# Spec: Blazor Shared Abstractions — `Cloudstrap.BlazorCommon` (Roadmap Deliverable #11)

> Status: **APPROVED — zero Open Questions (all five resolved by the user 2026-08-22, see Decision
> Log); planner-ready.** Source: `Nihdi.Core.Configuration.BlazorCommon` (six files, standalone, zero
> ProjectReferences). This package is the leaf of the Blazor band — #12 (BlazorServer), #13 (BlazorWasm)
> and #20 (Dashboard UI) build on the contracts it ships, so every shape decided here is a mini one-way
> door (⚠️ public API risk area).
>
> ⚠️ One follow-up owned by the user, not the planner: the founding-spec package-map amendment
> recorded under D-3 in the Decision Log (`_specs/Cloudstrap.md` is user-amended only).

---

## Code-reading findings that shaped this spec

The source package has **no test project of its own** (the `BlazorCommon.Tests` suite that exists asserts
contract *implementability* only — the source repo's own review flagged them as "tests that cannot fail",
`_reviews/2026-07-03-full-codebase-review.md` P2-36). The real contract therefore comes from call sites:

1. **`INavigationService` has zero production call sites.** A repo-wide search finds it only in its own
   package, its own shape-only tests, and an unrealized checklist line in `Test\WasmTestProject\PLAN.md`.
   Both test hosts register it (via `AddBlazorCommonForNihdi`) but **nothing ever injects it** — pages that
   navigate inject `NavigationManager` directly (so does Cloudstrap's own demo,
   `src/demo/BlazorWasm/Presentation/Doctors/DoctorsPage.razor.cs`). The implementation wraps exactly one
   of `NavigationManager`'s many members behind a one-method interface.
2. **`IErrorHandler` earns its keep — but only part of its surface is exercised.** Both test hosts implement
   it with MudBlazor (`MudBlazorErrorHandler : ISnackbar`-based) and register it **themselves** (scoped);
   ViewModels inject it and call `HandleError(ex)` in `catch` blocks
   (`Test\WasmTestProject\src\Presentation\Doctors\ViewModels\DoctorsViewModel.cs:83,119`). No call site
   anywhere invokes `ShowWarning` or `ShowSuccess`; notably, `DoctorsViewModel` shows its *own* validation
   errors via `ISnackbar` directly rather than through `IErrorHandler.ShowError`.
3. **`IViewModel` is the band's real workhorse.** Every ViewModel in both test hosts implements it (via
   `IDoctersViewModel : IViewModel` etc.), pages drive it from `OnInitializedAsync` (`DoctorsPage.razor.cs:23`),
   and all five Dashboard VM interfaces extend it. Two caveats: (a) `InitializeAsync()` takes no
   `CancellationToken` — the source repo's own `_plans/RemediationBreakingChanges.md:101` already scheduled
   adding one; (b) `Dashboard.Components.Shared` **duplicated** `IViewModel` locally
   (`Dashboard.Components.Shared\Shared\IViewModel.cs`) instead of referencing BlazorCommon — evidence the
   "shared" contract failed to be shared once; #20 must consume Cloudstrap's copy (noted for #20, not here).
4. **`AddPresentationServices<T>` is used by both test hosts** (`PresentationModule.cs` in each) and removes
   real boilerplate: one call registers every `*ViewModel`/`*Service` class as its implemented interfaces,
   transient. The suffixes and lifetime are hard-coded — the repo's "every convention has an override" rule
   is violated today.
5. **`AddBlazorCommonForNihdi` is a composite that composes almost nothing** — it only calls
   `AddNavigationService`. Consumers still had to call `AddPresentationServices<T>` and register
   `IErrorHandler` separately, so the "one entry point" promise was never real.
6. **`NihdiWasmControls` is a static mutable assembly registry** (lock + grow-only cached array, throws if
   read before written) whose only producer is `Dashboard.Components.BlazorWasm.AddDashboardForWasm`
   (`Extensions\ServiceCollectionExtensions.cs:66`) and whose only reader is the host's `Routes.razor`
   (`AdditionalAssemblies="@(NihdiWasmControls.GetAssemblies())"`). It exists so the Dashboard package's
   `@page` components are routable without the app naming the assembly — a one-line convenience bought with
   global static state and temporal coupling.
7. **Dependency verification**: `Microsoft.AspNetCore.Components` 10.0.9 is a plain NuGet package (deps:
   `Microsoft.AspNetCore.Authorization` + `Microsoft.AspNetCore.Components.Analyzers` only — **no**
   `Microsoft.AspNetCore.App` framework reference), so it is WASM-consumable; it is needed **only** by
   `NavigationService`. `Scrutor` 7.0.0 is the current release (2025-11-24), MIT, actively maintained
   (khellang/Scrutor). If OQ-1 resolves to Drop, the package needs no Blazor dependency at all and becomes
   a pure contracts + convention-scan package.

---

## User Story

**As an** ASP.NET Core developer building a Blazor application (Server or WebAssembly) on Cloudstrap,
**I want** a small shared-abstractions package with a ViewModel initialization contract, a consumer-implemented
error-feedback contract, and one-call convention-based DI registration of my presentation layer,
**so that** my ViewModels and services are testable, consistently registered, and reusable across Blazor
hosting models — and the higher Cloudstrap Blazor packages (#12/#13/#20) share the same contracts.

---

## Acceptance Criteria

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim — tripwire only; this package has no Aspire overlap)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried verbatim — stays green; this package references no auth packages)* |
| AC-BC1 | A plain `ServiceCollection` (WASM-style host) containing an assembly with classes ending in the convention suffixes | `AddCloudstrapBlazorCommon<TAssemblyMarker>()` is called with that assembly's marker | Every concrete class matching a suffix is resolvable via each of its implemented interfaces with the default (transient) lifetime; two resolutions of the same interface yield distinct instances. |
| AC-BC2 | A `WebApplicationBuilder` (Blazor Server-style host) | The same entry point is called | Registrations resolve identically — the package contains nothing tied to a single Blazor hosting model, and its `.nupkg` carries no `Microsoft.AspNetCore.App` framework reference (the package references no Blazor package at all, D-3). |
| AC-BC3 | A consumer overrides the convention (custom suffix list and/or lifetime) via the configure delegate | Registration runs | Only the overridden suffixes are scanned and the chosen lifetime is applied; the built-in defaults are fully replaceable ("every convention has an override"). |
| AC-BC4 | An assembly containing classes that match no configured suffix (and abstract/non-public types) | Registration runs | None of them are registered; the scan never touches types outside the marker assembly (plus explicitly added assemblies). |
| AC-BC5 | A consumer implements `IErrorHandler` and registers it (any lifetime they choose) | A convention-registered ViewModel injects `IErrorHandler` and a failure occurs | The consumer's implementation receives the call — the package ships the contract only, no implementation and no default registration. |
| AC-BC6 | A ViewModel implementing `IViewModel` registered by convention | A component awaits `InitializeAsync(CancellationToken)` during `OnInitializedAsync` | Initialization completes; the contract carries a `CancellationToken` (last parameter, defaultable) per the repo's async-API rule. *(D-1)* |
| AC-BC7 | An app with **no** Cloudstrap configuration section at all | The entry point is called | Registration succeeds — this package defines **no** `Cloudstrap:` configuration section and reads no `IConfiguration` (explicit statement required by the hand-off brief: conventions here are code-level, set at the registration call site). |
| AC-BC8 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv` | All green; XML docs on all public API; package metadata complete (MIT, icon, README, SourceLink); zero forbidden identifiers; every dependency OSI-licensed and CPM-pinned. |
| AC-BC9 | The BlazorWasm demo app (`src/demo/BlazorWasm/Presentation`) restructured to the ViewModel pattern and registered through this package's entry point | The E2E suite runs | All pre-existing E2E tests stay green and ≥ 1 new E2E test in `Cloudstrap.Demo.E2E.Tests` proves, through the running browser, a page rendering data via a convention-registered ViewModel (standing demo rule / workflow rule 9). |

---

## Port Decision Table

*(All verdicts final — the five Open Questions were resolved by the user 2026-08-22; see Decision Log.)*

| Source artefact (`Nihdi.Core.Configuration.BlazorCommon\`) | Verdict | Target | Justification |
|---|---|---|---|
| `Presentation\IViewModel.cs` — `IViewModel.InitializeAsync()` | **Redesign** (signature only) | `Cloudstrap.BlazorCommon.IViewModel` — `Task InitializeAsync(CancellationToken cancellationToken = default)` | Heavily used at call sites (finding 3); the only defect is the missing `CancellationToken`, which the source repo itself had scheduled as a breaking fix and which the repo's public-API rule mandates. *(D-1)* |
| `ErrorHandling\IErrorHandler.cs` — `HandleError`, `ShowError`, `ShowWarning`, `ShowSuccess` | **Redesign** | `Cloudstrap.BlazorCommon.IErrorHandler` — trimmed to the evidenced surface (`HandleError(Exception)`, `ShowError(string)`) | The handle-and-notify contract is genuinely consumed (finding 2), but `ShowWarning`/`ShowSuccess` have zero call sites anywhere — general-purpose notification is the UI library's job (MudBlazor `ISnackbar` already does severities). YAGNI: members can be added pre-v1 if #20 produces evidence. *(D-2, user-approved)* |
| `Navigation\INavigationService.cs` + `Navigation\NavigationService.cs` | **Drop** | — | Zero production consumers in the whole source repo (finding 1); a one-method wrapper over `NavigationManager`, which is itself designed for test subclassing (the source's own tests fake it) and is what every real page injects. An abstraction with one method, one implementation and no callers does not earn permanent maintenance. Dropping also removes the package's only Blazor dependency. The founding-spec package-map wording conflict was raised as an Open Question and resolved by the user in favor of the drop, with an approved founding-spec amendment. *(D-3, user-approved)* |
| `Extensions\ServiceCollectionExtensions.cs` → `AddNavigationService` | **Drop** | — | Registers only the dropped service; falls with it. *(D-3)* |
| `Extensions\ServiceCollectionExtensions.cs` → `AddPresentationServices<TAssemblyMarker>` | **Redesign** | The package's single entry point `AddCloudstrapBlazorCommon<TAssemblyMarker>` with overridable conventions (suffixes, lifetime, additional assemblies) | The Scrutor scan is the package's proven value (finding 4), but the hard-coded `ViewModel`/`Service` suffixes and transient lifetime violate the every-convention-has-an-override rule; folded into the composite entry point per naming convention `AddCloudstrap<Feature>`. *(D-4, user-approved)* |
| `Extensions\ServiceCollectionExtensions.cs` → `AddBlazorCommonForNihdi` | **Redesign** (superseded by the composite) | `AddCloudstrapBlazorCommon<TAssemblyMarker>` | The old composite composed almost nothing (finding 5) and carries a `*ForNihdi` name (De-NIHDI item). Replaced by one honest entry point that does what consumers actually did at every call site: run the presentation scan. *(D-4)* |
| `AdditionalControls\NihdiWasmControls.cs` (static assembly registry for router discovery) | **Drop** (defer any replacement to #20) | — | Its only producer/consumer pair is the Dashboard WASM package + host `Routes.razor` (finding 6); the design is static mutable state with temporal coupling (`InvalidOperationException` on read-before-write). The framework-native alternative costs the consumer one explicit line (`AdditionalAssemblies="new[] { typeof(SomeDashboardComponent).Assembly }"`) and Cloudstrap zero code. If #20 wants automatic discovery it designs a DI-based seam there — a "Common" package is the wrong home for a dashboard-only mechanism. *(D-5, user-approved)* |
| `.csproj` — `Microsoft.AspNetCore.Components` 10.0.9 | **Drop** | — | Needed only by the dropped `NavigationService`; falls with it (D-3). The shipped package references no Blazor package at all. |
| `.csproj` — `Scrutor` 7.0.0 | **Port** | CPM entry `Scrutor` 7.0.0 | MIT, current (7.0.0 released 2025-11-24), actively maintained; powers the convention scan — the alternative is ~40 lines of bespoke reflection code Cloudstrap would own forever. Founding spec and `.claude/instructions/blazor.md` both already assume it. |
| `.csproj` — `StyleCop.Analyzers.Unstable` | **Drop** (already decided) | — | Repo-wide decision #0: SDK analyzers, no StyleCop. |

---

## Public API Sketch

*(Shapes and names, not implementations. Final — reflects the user-approved answers to all five Open
Questions. Single flat namespace — the package is too small to justify per-folder namespaces, and one
`@using Cloudstrap.BlazorCommon` in `_Imports.razor` covers everything.)*

```csharp
namespace Cloudstrap.BlazorCommon;

/// Contract for ViewModels that load state when their page/component initializes.
public interface IViewModel
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// Consumer-implemented contract for surfacing failures to the user (e.g. MudBlazor Snackbar).
/// Cloudstrap ships NO implementation and NO default registration — the consumer registers theirs.
public interface IErrorHandler
{
    void HandleError(Exception exception);
    void ShowError(string message);
    // ShowWarning / ShowSuccess: dropped (D-2) — zero call sites; may be added pre-v1 if #20 produces evidence.
}

/// Code-level conventions for the presentation scan. NOT bound to IConfiguration —
/// this package has no `Cloudstrap:` section (AC-BC7).
public sealed class BlazorCommonOptions
{
    /// Class-name suffixes registered as their implemented interfaces. Default: ["ViewModel", "Service"].
    public IList<string> ConventionSuffixes { get; }

    /// Lifetime for convention-registered types. Default: ServiceLifetime.Transient.
    public ServiceLifetime Lifetime { get; set; }

    /// Extra assemblies to scan besides TAssemblyMarker's. Default: empty.
    public IList<Assembly> AdditionalAssemblies { get; }
}

public static class ServiceCollectionExtensions
{
    /// Scans TAssemblyMarker's assembly (plus AdditionalAssemblies) and registers every concrete
    /// public class whose name ends in a convention suffix as its implemented interfaces.
    public static IServiceCollection AddCloudstrapBlazorCommon<TAssemblyMarker>(
        this IServiceCollection services,
        Action<BlazorCommonOptions>? configure = null);
}
```

Dropped from the public surface (vs. source): `INavigationService`, `NavigationService`,
`AddNavigationService`, `AddPresentationServices<T>` (folded in), `AddBlazorCommonForNihdi` (renamed/folded),
`NihdiWasmControls`.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Convention scan source | The assembly of `TAssemblyMarker` | `BlazorCommonOptions.AdditionalAssemblies` adds more; calling the entry point again with another marker also works |
| Registered classes | Concrete public classes whose name ends in `ViewModel` or `Service` (ordinal comparison, per source) | `ConventionSuffixes` — fully replaceable list |
| Registration shape | As **implemented interfaces** (a class with no interfaces registers nothing — Scrutor behavior, kept) | Consumers needing self-registration or decorators call Scrutor directly (documented escape hatch — Scrutor is a normal public dependency of the package) |
| Lifetime | Transient (matches source; correct for per-component ViewModels in both hosting models) | `BlazorCommonOptions.Lifetime` |
| Repeated calls | Registrations append (standard `IServiceCollection` semantics — Scrutor does not de-duplicate); documented, not "fixed" | Consumer calls once per assembly |
| `IErrorHandler` | Contract only — **no default implementation, no registration** (source behavior kept: both old hosts registered their own MudBlazor implementation) | Consumer registers any implementation at any lifetime |
| Configuration | None — no `Cloudstrap:` section, no `IConfiguration` read (AC-BC7) | All conventions are set in code at the call site |
| Package docs drift | — | `.claude/instructions/blazor.md` currently describes the old surface (`INavigationService`, `Add<Feature>ForCloudstrap` naming) — updating it to the shipped surface is part of this deliverable's definition of done (artefacts-catalog drift rule), handled by the planner outside this spec's API scope |

---

## Dependencies

| Package | Version (CPM) | License | Justification |
|---|---|---|---|
| `Scrutor` | 7.0.0 | MIT (verified — khellang/Scrutor, current release 2025-11-24, active repo) | Powers the convention scan; replaces bespoke reflection code Cloudstrap would otherwise own. New CPM entry — flagged per CLAUDE.md rule 4. |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | (repo-pinned) | MIT | `IServiceCollection`/`ServiceLifetime` in the public surface. |
| ~~`Microsoft.AspNetCore.Components`~~ | — | MIT | **Not referenced** — its only consumer was the dropped `NavigationService` (D-3). (Verification kept for the record: it is a plain NuGet package, deps only `Microsoft.AspNetCore.Authorization` + `Components.Analyzers`, no `Microsoft.AspNetCore.App` framework reference — so #12/#13 may reference it safely.) |

Zero `Aspire.*` (AC-ASP2), zero `Nihdi.*` (AC-A3/AC-BC8).

---

## Deliberate Behavior Changes (vs. the source library)

| # | Change | Why |
|---|---|---|
| D-1 | `IViewModel.InitializeAsync` gains `CancellationToken cancellationToken = default` | Repo public-API rule (async methods take a CT last); the source repo's own breaking-changes plan (`_plans/RemediationBreakingChanges.md:101`) had already scheduled exactly this. |
| D-2 | `IErrorHandler` trimmed to `HandleError` + `ShowError` *(final, user-approved)* | Zero call sites for `ShowWarning`/`ShowSuccess` anywhere in the source repo; severity toasts are the UI library's native job. |
| D-3 | `INavigationService`/`NavigationService`/`AddNavigationService` not ported *(final, user-approved — incl. the founding-spec package-map amendment)* | Zero production consumers; wraps one method of a framework type that is injectable and test-fakeable itself. |
| D-4 | One composite entry point `AddCloudstrapBlazorCommon<T>` with overridable conventions (suffixes, lifetime, additional assemblies) replaces `AddPresentationServices<T>` + `AddBlazorCommonForNihdi` *(final, user-approved)* | `AddCloudstrap<Feature>` naming convention; the old composite composed nothing real; hard-coded suffixes/lifetime violated the every-convention-has-an-override rule; "call Scrutor directly" stays the documented escape hatch for anything beyond the three knobs. |
| D-5 | `NihdiWasmControls` static assembly registry not ported *(final, user-approved)* | Static mutable state + temporal coupling serving exactly one consumer (Dashboard WASM); the framework-native `AdditionalAssemblies` one-liner is explicit, testable and costs zero library code — #20's README documents it; #20 decides if it needs a DI-based discovery seam. |

---

## Edge Cases

| Case | Expected behavior |
|---|---|
| Marker assembly contains no matching classes | Registration succeeds; nothing added (no throw, no log requirement). |
| A matching class implements no interfaces | Not registered (Scrutor `AsImplementedInterfaces` semantics) — documented. |
| A class name matches two suffixes (e.g. `FooServiceViewModel`) | Registered once per matching rule pass; with default ordinal `EndsWith` it matches `ViewModel` only — suffix matching is per-suffix, duplicates across suffixes are not de-duplicated (documented; consumers should not name classes to match multiple suffixes). |
| Abstract / non-public classes with matching names | Excluded (Scrutor `AddClasses` default: public, concrete). |
| `ConventionSuffixes` cleared to empty | Nothing scanned — legal (a consumer may want a no-op call in one host profile); guard clauses still validate `services`. |
| Entry point called twice with the same marker | Duplicate transient registrations appended; last-wins for single resolution, both returned by `IEnumerable<T>` resolution — standard DI semantics, documented. |
| `cancellationToken` passed to `InitializeAsync` is already cancelled | Contract-level: implementations should honor it; the package ships no implementation, so behavior is the implementer's (XML docs state the expectation). |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- **`INavigationService` / `NavigationService` / `AddNavigationService`** (dropped, D-3).
- **`NihdiWasmControls`** and any assembly-discovery mechanism (dropped, D-5) — if wanted, #20 designs a DI-based seam there; #20 documents the framework-native `AdditionalAssemblies` one-liner.
- **`ShowWarning` / `ShowSuccess`** on `IErrorHandler` (dropped, D-2).
- Any default `IErrorHandler` implementation (MudBlazor or otherwise) — MudBlazor stays out of this package; #20 is the MudBlazor deliverable.
- `INotifyPropertyChanged` base classes, MVVM frameworks, state containers — the source never had them (no gold-plating).
- Typed HttpClients, Refit, cookie/XSRF auth, tracing — #12/#13 territory.
- Any `Cloudstrap:` configuration section or options validation — this package has none (AC-BC7).
- Founding-spec global out-of-scope items: message encryption, MessagingBridge, Dynatrace, ServicePlatform, `Cloudstrap.Functional`.

---

## Decision Log (gate answers, 2026-08-22 — zero Open Questions remain; spec is planner-ready)

All five Open Questions were answered by the user, in each case accepting the analyst's recommendation.
The full evidence and rejected options remain in the Port Decision Table and Code-reading findings above.

| OQ | Decision (final) | Rationale kept on record |
|---|---|---|
| OQ-1 | **Drop `INavigationService`/`NavigationService`/`AddNavigationService` entirely**, and **amend the founding spec's package map** (user-approved amendment). | Zero production injections repo-wide; both old hosts registered it and never used it; real pages inject `NavigationManager` directly, which is test-fakeable (the source's own `NavigationServiceTests` builds a `TestNavigationManager`). Removes the package's only Blazor dependency. Rejected: port as-is (forever-maintained one-method abstraction), port-and-grow (gold-plating). |
| OQ-2 | **Trim `IErrorHandler` to `HandleError(Exception)` + `ShowError(string)`.** | Only the evidenced surface ships; `ShowWarning`/`ShowSuccess` had zero call sites and severity toasts are the UI library's native job. Members can be added pre-v1 if #20 produces evidence. Rejected: port all four, split into two interfaces. |
| OQ-3 | **Single composite entry point `AddCloudstrapBlazorCommon<TAssemblyMarker>(Action<BlazorCommonOptions>?)`.** | Matches the `AddCloudstrap<Feature>` naming table and the #5/#6/#7 composite precedent; one obvious call. Rejected: granular-only, both (double surface for no proven need). |
| OQ-4 | **Three convention knobs as options: `ConventionSuffixes`, `Lifetime`, `AdditionalAssemblies`; "use Scrutor directly" documented as the escape hatch.** | Covers every variation the every-convention-has-an-override rule plausibly requires in three members. Rejected: suffixes-only (no scoped-lifetime recourse), full predicate filter (at that point the consumer should call Scrutor themselves). |
| OQ-5 | **Drop `NihdiWasmControls` from #11; #20 documents the framework-native `AdditionalAssemblies` one-liner and decides there whether it needs a DI-based seam.** | Static mutable state + temporal coupling serving one consumer does not belong in the band's foundation; the framework-native line costs zero owned code. Rejected: redesign-now DI seam (speculative before #20's requirements are read), port renamed (carries the defect forward). |

**Founding-spec amendment (user-approved under OQ-1, application pending — user-only edit).** This spec's
author is constrained from modifying `_specs/Cloudstrap.md`; the approved amendment to apply there is, in
the Package Map row for `Nihdi.Core.Configuration.BlazorCommon` → `Cloudstrap.BlazorCommon`, to replace the
area description "Shared Blazor abstractions (ErrorHandler, Navigation, ViewModel)" wording with
**"Shared Blazor abstractions (ErrorHandler, ViewModel, convention scan)"** — reflecting the D-3 drop.
(Also mirrored in CLAUDE.md's project-structure comment for `Cloudstrap.BlazorCommon`, which repeats the
old triple — same owner, same edit pass.) Until applied, the founding spec's wording and this spec diverge
deliberately, with this log as the audit trail.
