# Cloudstrap Extraction Roadmap

> Owned by the project-manager agent. High-level deliverables only —
> detailed steps live in _plans/<Deliverable>.md (planner).
> Status: ⬜ not started · 📝 planning · 🔨 in progress · ⛔ blocked · ✅ done
>
> **File naming convention**: specs and plans carry the roadmap number as a prefix —
> `_specs/<N>-<Deliverable>.md` / `_plans/<N>-<Deliverable>.md`
> (e.g. `_specs/0-RepoScaffolding.md`, `_plans/0-RepoScaffolding.md`).
> Applies to all future deliverables.
>
> **The `#` column is a stable identifier, not a sequence number** (convention made explicit
> 2026-08-05). Numbers are referenced by `_specs/<N>-…` / `_plans/<N>-…` filenames, by
> cross-references inside shipped code and docs (e.g. the `IAccessTokenHandlerProvider`
> failure message names the packages of #9/#10), and by every Change log entry — so
> **deliverables are never renumbered**. Re-prioritisation moves a **row** in the Overview
> table instead: **the Overview table is in execution order, top to bottom**, while the
> *Deliverable details* sections below stay sorted by `#` as a lookup index.
>
> **Source reference repo (read-only, verified 2026-07-25)**:
> `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\`
> (solution: `Nihdi.Core.Configuration.sln`). All "Source material" paths below are
> relative to that `src\` folder.
>
> **Not a deliverable**: `Nihdi.Core.Functional` is NOT ported — functional primitives
> come from the **LanguageExt.Core** NuGet package (MIT), referenced directly by
> consuming packages. Out of scope everywhere: message encryption, MessagingBridge,
> Dynatrace, ServicePlatform/ServicePulse.
>
> **Aspire posture (founding spec, decided 2026-07-25)**: coexist **without depending** —
> zero `Aspire.*` references in any shipped package (Aspire appears only in docs/samples,
> AC-ASP2); composability with Aspire ServiceDefaults is an explicit spec concern for
> deliverables 2 (AC-ASP1) and 4 (AC-ASP3); `Cloudstrap.Aspire` is NOT a v1 deliverable
> (post-v1 option, user-approved only).
>
> **Standing rule — SUT demonstration (user-directed 2026-08-01, deliverable #25)**: every
> deliverable's definition of done implicitly includes **demonstrating its headline behavior
> in the WASM SUT** (`src/Test/WasmTestProject`) **with ≥ 1 passing E2E test** in
> `Cloudstrap.WasmTestProject.E2E.Tests`, planned as the final slice of its `_plans/` file
> (planner rule 15). This applies to deliverables 3–24 even though their entries below do
> not repeat it.

## Dependency analysis (actual `<ProjectReference>` graph, verified 2026-07-25)

Old-package edges extracted from the source `.csproj` files:

| Old project | ProjectReferences | Notable internal PackageReferences (to replace) |
|---|---|---|
| Nihdi.Core.Configuration | Functional, **Dashboard.Contracts (inverted — cut)** | Nihdi.StyleCop.MsBuildProperties |
| Nihdi.Core.Configuration.Common | Configuration | Nihdi.AspNetCore.Localization, Nihdi.AspNetCore.Authentication.JwtBearer / Authorization / AccessTokenManagement, Nihdi.Core.Health |
| WebApi | Common | Nihdi.AspNetCore.Authentication.JwtBearer, Nihdi.Core.Health |
| Mvc | Common | — |
| Worker | Common, Configuration | — |
| OpenIdConnect | Configuration **only** | Nihdi.AspNetCore.* suite |
| OAuth | Configuration **only** | Nihdi.AspNetCore.Authentication.ClientCredentials, AccessTokenManagement |
| BlazorServer | Common | Nihdi.AspNetCore.AccessTokenManagement |
| BlazorWasm | — (standalone) | — |
| BlazorCommon | — (standalone) | — |
| NServiceBus | Common, Configuration | NServiceBus* suite, Nihdi.Core.Health, Nihdi.Core.NServiceBus.Cryptography |
| Hangfire | Configuration **only** | — (Hangfire.* LGPL) |
| Proxy | Common | — (YARP) |
| Hangfire.Proxy | Hangfire, Proxy | — |
| CookieConsent | — (standalone) | — (orejime JS bundled) |
| Analytics.Matomo | — (standalone) | — |
| Dashboard.Contracts | — (leaf) | — |
| Dashboard.Api | Configuration, NServiceBus, Functional, Dashboard.Contracts | Azure.Messaging.ServiceBus |
| Dashboard.Components.Shared | Dashboard.Contracts | Nihdi.Common.MudBlazor8.DesignSystem |
| Dashboard.Components | Configuration, Common, Functional, Components.Shared, Contracts | Nihdi.Common.MudBlazor8.DesignSystem, Newtonsoft.Json |
| Dashboard.Components.BlazorWasm | BlazorCommon, BlazorWasm, Components.Shared, Contracts | Nihdi.Common.MudBlazor8.DesignSystem |
| Nihdi.Core.Testing | Functional | — |

**Discrepancies / refinements vs the baseline band order** (actual graph wins):

1. **Old Core → Dashboard.Contracts is inverted and is cut** (spec decision): dashboard
   settings (`Nihdi.Core.Configuration\Settings\Dashboard\`) move to the Dashboard
   deliverable. New `Cloudstrap.Core` becomes a true leaf. Likewise the per-feature
   settings folders in old Core (`Settings\NServiceBus\`, `Settings\Hangfire\`,
   `Settings\Security\`, `Settings\Swagger|Scalar\`, `Settings\Logging\Dynatrace`) move
   to their owning Cloudstrap packages (or are deleted — Dynatrace/Bridge/ServicePlatform).
2. **Auth needs only Core, not hosting**: OpenIdConnect/OAuth reference only
   `Nihdi.Core.Configuration` — the baseline "hosting before auth" order is a choice,
   not a graph constraint. Kept (hosting first) because the new ClientCredentials feeds
   the typed-HttpClient registration that lives in `Cloudstrap.Extensions`.
3. **Hangfire needs only Core** (not Common/Extensions) — it is not blocked by
   Messaging or the hosting band; it stays in band 8 by choice, but can be pulled
   forward if priorities change. `Proxy` needs Extensions; `Hangfire.Proxy` needs both.
4. **BlazorCommon, BlazorWasm, CookieConsent, Analytics.Matomo, Dashboard.Contracts and
   Testing are standalone** (zero project references) — schedulable at any point; kept
   in their bands to preserve one-deliverable-in-flight focus.
5. **Old Common bundles what becomes Extensions + Observability**: `DistributedTracing\`,
   `Logging\`, `Correlation\`, `HealthChecks\` → Observability; `KeyVault\`,
   `BlobStorage\`, `HttpClient\`, `Extensions\`, `Host\`, `Options\` → Extensions;
   `Scalar\` → WebApi (per spec); `Dynatrace\` → deleted. Extensions consumes the
   correlation delegating handler and bootstrap logger, so **Observability ports before
   Extensions** (baseline confirmed by actual code coupling).
6. **Old Common → Nihdi.AspNetCore.Localization**: `Cloudstrap.Extensions` must NOT
   depend on `Cloudstrap.Localization` — drop the edge; Localization is a late,
   standalone thin layer. ⚠️ Its source is NOT in the reference repo (internal package)
   — it is a reimplementation over stock ASP.NET Core localization, not a port.
7. **Nihdi.Core.Health (internal) is used by Common, WebApi, NServiceBus** — replaced by
   `Microsoft.Extensions.Diagnostics.HealthChecks` (+ `AspNetCore.HealthChecks.*` where
   needed) inside Observability/WebApi/Worker; there is no separate health package.
8. **Dashboard.Api → NServiceBus** confirms Dashboard must follow Messaging.
9. Old Common references `Microsoft.AspNet.WebApi.Client` and
   `NWebsec.AspNetCore.Middleware` (last release 2019) — ⚠️ review both during the
   Extensions/Mvc ports; prefer dropping or replacing with maintained equivalents.

## Overview

| # | Deliverable | Packages | Depends on | Status | Plan |
|---|-------------|----------|------------|--------|------|
| 0 | Repo scaffolding | `src/Cloudstrap.sln`, `Directory.Build.props` (SDK analyzers, no StyleCop), `Directory.Packages.props` (CPM), `.editorconfig`, `GitVersion.yml`, `global.json`, `nuget.config`, CI workflows | — | ✅ | `_plans/0-RepoScaffolding.md` |
| 1 | Core settings model | Cloudstrap.Core | 0 | ✅ | `_plans/1-CoreSettingsModel.md` |
| 2 | Observability base | Cloudstrap.Observability | 1 ✅ | ✅ | `_plans/2-ObservabilityBase.md` |
| 3 | Azure Monitor exporter | Cloudstrap.Observability.AzureMonitor | 2 ✅ | ✅ | `_plans/3-AzureMonitorExporter.md` |
| 4 | Config/KeyVault/HTTP extensions | Cloudstrap.Extensions | 1 ✅, 2 ✅ | ✅ | `_plans/4-ConfigKeyVaultHttpExtensions.md` |
| 5 | WebApi bootstrap | Cloudstrap.WebApi | 4 ✅ | ✅ | `_plans/5-WebApiBootstrap.md` |
| 9 | Client-credentials auth ← **next** *(pulled forward 2026-08-05)* | Cloudstrap.Authentication.ClientCredentials | 1 ✅, 4 ✅ | 📝 spec approved 2026-08-05 — awaiting plan | `_specs/9-ClientCredentialsAuth.md` |
| 10 | OIDC login | Cloudstrap.Authentication.OpenIdConnect | 1 ✅ (Duende ATM patterns de-risked by 9) | ⬜ | — |
| 6 | MVC bootstrap | Cloudstrap.Mvc | 4 ✅ | ⬜ | — |
| 7 | Worker bootstrap | Cloudstrap.Worker | 4 ✅ | ⬜ | — |
| 8 | Test helpers | Cloudstrap.Testing | 0 ✅ (flexible — pull earlier if a prior plan needs it) | ⬜ | — |
| 11 | Blazor shared abstractions | Cloudstrap.BlazorCommon | 0 | ⬜ | — |
| 12 | Blazor Server helpers (+ TestProject SUT) | Cloudstrap.BlazorServer | 4, 9, 11 | ⬜ | — |
| 13 | Blazor WASM helpers (+ WasmTestProject SUT) | Cloudstrap.BlazorWasm | 11 | ⬜ | — |
| 14 | Messaging (Wolverine) | Cloudstrap.Messaging | 1, 2, 4 | ⬜ | — |
| 15 | Blob claim-check | Cloudstrap.Messaging.AzureBlob | 14 | ⬜ | — |
| 16 | Hangfire scheduler | Cloudstrap.Hangfire | 1 | ⬜ | — |
| 17 | YARP trusted-subsystem proxy | Cloudstrap.Proxy | 4 | ⬜ | — |
| 18 | Hangfire dashboard proxy | Cloudstrap.Hangfire.Proxy | 16, 17 | ⬜ | — |
| 19 | Dashboard backend | Cloudstrap.Dashboard.Contracts + Cloudstrap.Dashboard.Api | 1, 14 | ⬜ | — |
| 20 | Dashboard UI (MudBlazor) | Cloudstrap.Dashboard.Components.Shared + .Components + .Components.BlazorWasm | 11, 13, 19 | ⬜ | — |
| 21 | Cookie consent | Cloudstrap.CookieConsent | 0 | ⬜ | — |
| 22 | Analytics abstraction + Matomo | Cloudstrap.Analytics + Cloudstrap.Analytics.Matomo | 21 | ⬜ | — |
| 23 | Google Analytics adapter | Cloudstrap.Analytics.GoogleAnalytics | 22 | ⬜ | — |
| 24 | Localization setup | Cloudstrap.Localization | 1 | ⬜ | — |
| 25 | WASM SUT + E2E demonstration harness | `src/Test/WasmTestProject` (SUT + E2E tests — not a shipped package) | 1, 2 | ✅ | `_plans/25-WasmTestProjectSut.md` |

**Reconciled with reality 2026-08-05, fourth pass (post-#5 delivery)**: `_plans/5-WebApiBootstrap.md`
verified box-by-box on disk — the final 🛑 gate's four boxes are all `[x]` including
"User approved — deliverable #5 done *(2026-08-03)*"; `src/Cloudstrap.WebApi/` and
`src/Test/UnitTest/Cloudstrap.WebApi.Tests/` exist, and the SUT-demonstration rule is met
(`WebApiTests.cs` + `ScalarPageTests.cs` in `Cloudstrap.WasmTestProject.E2E.Tests`, 11 new E2E tests).
**#5 → ✅.** ⚠️ **One bookkeeping discrepancy, not a blocker**: the *Gate 4* line
"User approved — implementation may continue past this gate" is still `[ ]` even though the executor's
Gate 4 report, Steps 10–11 and the user-approved final gate all post-date it — an unchecked box the
executor forgot to tick, not unfinished work. Plan files are the planner's/executor's to edit, so this
roadmap records it rather than fixing it. Full `src/**/*.csproj` sweep: **five shipped packages**
(Core, Observability, Observability.AzureMonitor, Extensions, WebApi) + five unit-test projects + the
five WASM-SUT/E2E projects — no orphan or undeclared project; **no `Cloudstrap.Authentication.*`
project on disk yet**. Tally: **7 ✅ (#0, #1, #2, #3, #4, #5, #25) · 0 in flight · 18 remaining
(#6–#24 minus #25)**. **#9 is next** (user-directed re-prioritisation 2026-08-05, see Change log) —
entering technical analysis; no second port may start while it is open.

**Reconciled with reality 2026-08-02, third pass (post-#4 delivery)**: `_plans/4-ConfigKeyVaultHttpExtensions.md`
verified box-by-box on disk — **zero unchecked boxes**, final 🛑 gate user-approved 2026-08-02;
`src/Cloudstrap.Extensions/` and `src/Test/UnitTest/Cloudstrap.Extensions.Tests/` exist in the
solution and the SUT-demonstration rule is met (`ExtensionsTests.cs` in
`Cloudstrap.WasmTestProject.E2E.Tests`, 3 new E2E tests). **#4 → ✅.** Refreshed at the #5 spec
gate (2026-08-02): tally **6 ✅ (#0, #1, #2, #3, #4, #25) · 1 in flight (#5, 📝) · 19 remaining
(#6–#24)**. Full `src/**/*.csproj` sweep confirms exactly four shipped packages (Core,
Observability, Observability.AzureMonitor, Extensions) + four unit-test projects + the five
WASM-SUT/E2E projects — no orphan or undeclared project (no `Cloudstrap.WebApi` project on disk
yet). **#5 (`Cloudstrap.WebApi`) is in flight**: its only dependency (4) is ✅,
`_specs/5-WebApiBootstrap.md` was approved 2026-08-02 with all three Open Questions resolved, so
it moves ⬜ → 📝 and the `planner` writes `_plans/5-WebApiBootstrap.md` next. No second port may
start while it is open (one-deliverable-in-flight rule).

## Deliverable details

### 0. Repo scaffolding
- **Goal**: A contributor can clone Cloudstrap, run `dotnet build src/Cloudstrap.sln`, tests, and format checks, and CI enforces all three — before any package exists.
- **Spec**: `_specs/0-RepoScaffolding.md` — approved 2026-07-25, zero Open Questions; its Decision Log and file inventory are authoritative for this deliverable.
- **Source material**: `Nihdi.Core.Configuration.sln` (solution layout only); source `Directory.Build.props` + `.editorconfig` (conventions to redesign — everything StyleCop-related is dropped, see spec Port Decision Table); `Test\TestProject\src\nuget.config` (`<clear/>` + single-feed pattern). No `GitVersion.yml` or GitHub workflows exist in the source repo (Azure DevOps) — author fresh per spec.
- **Depends on**: —
- **Migration decisions**: GitVersion + tags on `main`, `-preview.N` on `dev`; **NUnit 4 + NUnit3TestAdapter + NUnit.Analyzers on Microsoft.Testing.Platform** (user gate decision — deviates from the founding spec's MSTest v4; founding-spec amendment still pending with the user); **no StyleCop** — `Nihdi.StyleCop.MsBuildProperties` dropped outright (supersedes the earlier "reproduce its effect inline" instruction), replaced by .NET SDK analyzers (`AnalysisLevel=latest-recommended`), `EnforceCodeStyleInBuild` + `.editorconfig` as the single style authority, `TreatWarningsAsErrors` kept, `dotnet format --verify-no-changes` as the style gate; CS1591 XML-doc enforcement on in `src/`, off under `src/Test/`; **Central Package Management** (`src/Directory.Packages.props`); SourceLink via the SDK built-in (no package reference); publishing model: `-preview.N` packages from `dev` pushes only → **GitHub Packages**, stable via tag-push `v*` (`release.yml`) → **nuget.org only**, scheduled `cleanup-previews.yml` keeps the last 20 preview versions per package; `net10.0` only.
- **De-NIHDI items**: internal NuGet feed → nuget.org (root `nuget.config`); `Nihdi.StyleCop.MsBuildProperties` + per-project `stylecop.json` (`companyName = Riziv-Inami`) dropped outright — no StyleCop at all, .NET SDK analyzers replace it; no company copyright headers — licensing carried by `LICENSE` + `PackageLicenseExpression`, no per-file headers.
- **Definition of done**: `src/Cloudstrap.sln` (near-empty) + `src/Directory.Build.props` + `src/Directory.Packages.props` + `src/.editorconfig` + `GitVersion.yml` + `global.json` + `nuget.config` + `.github/workflows/ci.yml`/`release.yml`/`cleanup-previews.yml` + package icon asset (file details owned by the spec's inventory); build/test/format green locally and in CI (placeholder NUnit test project proves the test leg); zero `Nihdi`/`NIHDI`/`Riziv` identifiers; spec AC-R1…AC-R11 met.
- **Risks**: ⚠️ Analyzer ruleset choices are fixed afterwards (CLAUDE.md: `Directory.Build.props` rules are immutable) — get them right here. ~~⚠️ Reserve the `Cloudstrap.` nuget.org ID prefix before first publish~~ — **resolved: prefix reserved 2026-07-26**.
- **Status**: ✅ — done 2026-07-26. Both 🛑 gates approved and every box in `_plans/0-RepoScaffolding.md` checked. Local rails (build · test · format · CPM · SDK analyzers) and all three workflows are proven on GitHub: `ci.yml` on PR (AC-R4), `dev`-branch preview-publish no-op (AC-R5), `v0.1.0` tag through `release.yml` (AC-R6), `cleanup-previews.yml` dispatch, GitVersion probes (AC-R9), workflow code review + empty De-NIHDI sweep (AC-R8). **Publishing prerequisites are all satisfied**: the `Cloudstrap.` package ID prefix is reserved on nuget.org, the `Cloudstrap-GitHubActions-Release` Trusted Publishing policy is active, and the `NUGET_USER` secret is set — no operational item blocks the first package push.

### 1. Core settings model
- **Goal**: A consumer can bind and validate the `Cloudstrap:` configuration section into a typed `CloudstrapOptions` model (retrieved via `GetCloudstrapOptions()`).
- **Spec**: `_specs/1-CoreSettingsModel.md` — approved 2026-07-25, both Open Questions resolved; its verdict table (3 Port · 12 Redesign · 8 Drop · 25 Move-out) is authoritative for this deliverable.
- **Plan**: `_plans/1-CoreSettingsModel.md` — fully executed (5 steps, 3 🛑 gates, 19/19 boxes checked; gates approved 2026-07-26).
- **Source material**: `Nihdi.Core.Configuration\` — `Settings\*` (Application, Correlation, Logging, OpenTelemetry, HealthChecks, HttpClient), `ConfigurationBuilderExtensions.cs`, `ConfigurationExtensions.cs`, `ConfigurationException.cs`, `EnvironmentConstants.cs`, `BootstrapConfiguration.cs`.
- **Depends on**: 0
- **Migration decisions**: section `Nihdi:` → `Cloudstrap:`; root settings type named **`CloudstrapOptions`** with `GetCloudstrapOptions()` accessor (**OQ-1** — repo `*Options` convention; founding-spec amendment `CloudstrapConfiguration` → `CloudstrapOptions` approved by the user, technical-analyst applying it to `_specs/Cloudstrap.md`); **`AppRegistrationConfiguration` dropped outright** (**OQ-2** — no Cloudstrap type models client-id/secret credentials in appsettings; deliverable 14 builds transport auth on `TokenCredential`/`DefaultAzureCredential` + standard `AZURE_*` environment variables); **cut the inverted Dashboard.Contracts reference** (dashboard settings move to deliverable 19); per-feature settings move out (`Settings\NServiceBus\` → 14, `Settings\Hangfire\` → 16, `Settings\Security\` → 9/10, `Settings\Swagger|Scalar\` → 5); delete `Settings\Logging\DynatraceConfiguration.cs` and Bridge settings; the source `Nihdi.Core.Functional` ProjectReference is **dead code — zero usage** in any source file, so new Core takes **no LanguageExt.Core dependency** (exact LanguageExt type-mapping decision defers to the first package that genuinely consumes Functional types — likely Testing or Dashboard); validation via `Microsoft.Extensions.Options` + DataAnnotations replaces the bespoke validation cascade; only `Microsoft.Extensions.*` dependencies — host-agnostic (no `Microsoft.AspNetCore.App` framework reference), WASM-loadable, a true leaf.
- **De-NIHDI items**: environment taxonomy LOC/DEV/TST/VAL/PRD → standard ASP.NET Core environments + `Cloudstrap:Application:EnvironmentTier`; drop machine-name/log-path parsing from settings; keep documented workload naming `{system}-{subsystem}-{type}` (overridable); neutral test fixture values.
- **Definition of done**: build/tests/format green; XML docs on all public API; package metadata complete; zero `Nihdi`/`Riziv` identifiers; validation failures surface through the Options + DataAnnotations pipeline per the spec; no reference to any Dashboard type; spec acceptance criteria met.
- **Risks**: ⚠️ Public API surface for every later package — shape mistakes propagate; ⚠️ deciding which settings stay in Core vs move out changes every later deliverable. (Resolved: the LanguageExt type-mapping decision no longer lands here — Core has zero LanguageExt dependency.)
- **Delivered (2026-07-26)** — `src/Cloudstrap.Core/`, single namespace `Cloudstrap.Core`, everything `public sealed`, full XML docs; `src/Test/UnitTest/Cloudstrap.Core.Tests/` (NUnit 4 on MTP, 49 tests). Verified on the final tree: build 0 warnings/0 errors · 49/49 tests pass (exit 0) · `dotnet format --verify-no-changes` exit 0 · Release build produces `.nupkg` + `.snupkg` with 0 warnings. AC-C1…AC-C10 + AC-ASP2 signed off at the Slice-3 gate.
  - **Public surface**: `CloudstrapOptions` (root, section `Cloudstrap`), `ApplicationOptions`, `LoggingOptions` (+ `ConsoleLoggingOptions`/`FileLoggingOptions`), `OpenTelemetryOptions` (+ `OpenTelemetryMode`: `Disabled|Console|Otlp|AzureMonitor`), `CorrelationOptions` (+ `CorrelationRequestOptions`/`CorrelationMessageOptions`), `HealthChecksOptions`, `HttpClientServiceOptions` (+ `TokenRequestOptions`); entry points `ServiceCollectionExtensions.AddCloudstrapCore()` (binds + `ValidateOnStart` on all six options types) and `ConfigurationExtensions.GetCloudstrapOptions()` (eager pre-host bind + validate); `ConfigurationValidationException` (`IReadOnlyList<string> Failures`). Validators are `internal`, one rule in exactly one class, `CloudstrapOptionsValidator` the single shared cascade — one rule set, two entry points. The `OpenTelemetryMode.AzureMonitor` member is the enum seam deliverables 2 and 3 fill.
  - **Dependency closure** (all Microsoft-maintained, MIT, CPM-pinned at 10.0.10): `Microsoft.Extensions.Configuration.Abstractions`, `.Configuration.Binder`, `.DependencyInjection.Abstractions`, `.Logging.Abstractions`, `.Options`, `.Options.ConfigurationExtensions`. Zero `Aspire.*` (AC-ASP2), zero `LanguageExt.*`, **no `Microsoft.AspNetCore.App` framework reference** — Core is host-agnostic and Blazor-WASM-loadable. Test-only additions: `Microsoft.Extensions.Configuration`, `.DependencyInjection`.
  - **Facts every later deliverable inherits** (established here, do not re-litigate per package):
    1. **No `Microsoft.Extensions.Options.DataAnnotations`** — deliberately not taken despite the spec's dependency table listing it; the `[OptionsValidator]` source generator emits the `[Required]` checks directly. User-confirmed at the Slice-2 gate. Later packages follow the same source-generated-validator pattern.
    2. **Collection/dictionary options properties are get-only initialized**, so the configuration binder populates them in place and configured values **append to defaults rather than replacing them**. Only `CorrelationRequestOptions.HealthEndpoints` ships non-empty defaults (`["/healthz","/ready"]`). Documented in the package README; every consuming deliverable inherits this caveat and must document it for its own collection settings.
    3. **A broken section-level rule is reported twice at host startup** — once root-relative (`Application:SystemName: …`) and once section-relative (`SystemName: …`) — because both the root graph and each section are registered. User-acknowledged; startup fails either way and both messages name the member.
    4. **Zero LanguageExt.Core dependency in Core** (spec finding 1: the source `Nihdi.Core.Functional` reference was dead code). The founding spec's functional-type-mapping decision moves to the first package that genuinely consumes functional types — still open, likely deliverable 8 (Testing) or 19/20 (Dashboard).
- **Status**: ✅ — done 2026-07-26 (plan's final 🛑 gate checked; definition of done holds). Not yet *published* — nothing has been pushed to a feed yet — but no longer blocked from publishing: deliverable 0 is complete and the `Cloudstrap.` prefix is reserved, so `Cloudstrap.Core` ships whenever a `dev` push (preview) or a `v*` tag (stable) says so.

### 2. Observability base
- **Goal**: A consumer calls `UseCloudstrapObservability` to get Serilog bootstrap logging plus a vendor-neutral OTel traces/metrics/logs pipeline (modes `Disabled | Console | Otlp`, `AzureMonitor` enum reserved) with correlation and noise filtering.
- **Spec**: `_specs/2-ObservabilityBase.md` — acceptance criteria (AC-O2/O3/O4, AC-ASP1/ASP2, AC-B1…AC-B13, + amended AC-C6) walked and signed off at the plan's final gate.
- **Plan**: `_plans/2-ObservabilityBase.md` — fully executed (12 steps, 6 🛑 gates, all boxes checked; final gate accepted 2026-07-30).
- **Source material** *(file list re-verified against the reference repo 2026-07-26)*: `Nihdi.Core.Configuration.Common\` —
  - `DistributedTracing\` — `ServiceCollectionExtensions.cs` (the pipeline; spec says it ports largely unchanged), `BlazorHubSampler.cs`, `NihdiResourceAttributes.cs`, `IBusinessTrace.cs`/`BusinessTrace.cs`, `IBusinessTraceScope.cs`/`BusinessTraceScope.cs`;
  - `Logging\` — `BootstrapLoggerFactory.cs`, `DeferredLoggerFactory.cs`, `NihdiConfigurationExtensions.cs`, `NihdiConsoleFormatter.cs`, `TracingEnricher.cs`, `MessageIdEnricher.cs`, `W3CTracingMiddleware.cs`;
  - `Correlation\` — `CorrelationHeader.cs`, `ICorrelationContext`/`DefaultCorrelationContext`, `ICorrelationContextAccessor`/`DefaultCorrelationContextAccessor`, `ICorrelationSource`/`DefaultCorrelationSource`, `CorrelationExtensions.cs`, `CorrelationValidationMiddleware.cs`, `CorrelationRequiredAttribute.cs`/`AllowNoCorrelationAttribute.cs`, **`CorrelationHttpDelegatingHandler.cs` + `IHttpClientBuilderExtensions.cs`** (the pieces deliverable 4 consumes — they confirm Observability ports before Extensions);
  - `HealthChecks\` — `ApiLivenessHealthCheck.cs`, `ServiceCollectionExtensions.cs`;
  - **Read-and-delete, do not port**: `Dynatrace\*` (5 files) and the `BootstrapLoggerFactory` Dynatrace branch.
  - Also relevant: old Core `Settings\Logging\*` + `Settings\OpenTelemetry\*` + `Settings\Correlation\*` + `Settings\HealthChecks\*` are **already shipped** as `LoggingOptions`/`OpenTelemetryOptions`/`CorrelationOptions`/`HealthChecksOptions` in `Cloudstrap.Core` — this deliverable **consumes** them and must not redefine them.
- **Depends on**: 1 ✅
- **Migration decisions**: Dynatrace removed entirely — delete `Common\Dynatrace\*` and the `BootstrapLoggerFactory` Dynatrace branch; generic OTLP keeps a configurable headers dictionary (no `Api-Token` helper); Serilog stays for bootstrap/console/file; runtime logs via OTel; `Nihdi.Core.Health` → `Microsoft.Extensions.Diagnostics.HealthChecks`. **Aspire coexistence**: `UseCloudstrapObservability` supports pipeline-**owner** (default) and **contribute** modes — contribute adds only samplers/noise filters/enrichment/`IBusinessTrace` to an existing (e.g. ServiceDefaults) OTel pipeline, no duplicate exporters (AC-ASP1).
- **De-NIHDI items**: correlation header `NIHDI.Correlation` → `X-Correlation-ID` (configurable); resource attributes `nihdi.*` → `cloudstrap.*`/standard semconv; probe path `/probe.aspx` → `/healthz` + `/ready` (configurable); log path `D:\logsint` default removed.
- **Definition of done**: build/tests/format green; XML docs; AC-O2 (Otlp mode, no Azure dependency loaded), AC-O3 (probe/`_blazor` noise filtered), AC-O4 (zero "Dynatrace" occurrences) verifiable; AC-ASP1 (contribute mode composes with a pre-existing OTel pipeline — no duplicate exporters) covered by test; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Largest port of the foundation bands; ⚠️ new external deps: Serilog suite (Apache-2.0), OpenTelemetry.* (Apache-2.0) — versions to re-pin (CPM: `src/Directory.Packages.props`, which currently holds only Microsoft.Extensions 10.0.10 + the NUnit trio); ⚠️ splitting Common cleanly so Extensions (4) doesn't drag observability internals; ⚠️ **Aspire overlap** — OTel wiring and health checks are exactly what Aspire ServiceDefaults also does, so owner/contribute composability (AC-ASP1) and additive `IHealthChecksBuilder` registration are spec-level requirements, not implementation details; ⚠️ this is the first package with an `Microsoft.AspNetCore.App` framework reference — keep any WASM-relevant surface out of it, and do not regress Core's host-agnostic closure.
- **Delivered (2026-07-30)** — `src/Cloudstrap.Observability/` + `src/Test/UnitTest/Cloudstrap.Observability.Tests/`. Verified on the final tree: build zero warnings · Cloudstrap.Core.Tests 52/52 · Cloudstrap.Observability.Tests 91/91 · `dotnet format --verify-no-changes` clean · Release `.nupkg` contents reviewed (README, icon, XML docs, nuspec metadata).
  - **Shipped surface**: Serilog bootstrap + host logging (`CloudstrapBootstrapLogger`, `UseCloudstrapObservability`); vendor-neutral OTel pipeline with **owner** (default) and **contribute** modes across `Disabled|Console|Otlp|AzureMonitor` (AC-ASP1 proven end to end); trace noise filter + Blazor hub sampler; OTLP per-signal exporter setup; AzureMonitor fail-fast guard; `Cloudstrap.Observability.Correlation` (accessor, source, middleware, attributes); `IBusinessTrace`/`IBusinessTraceScope`; `CloudstrapActivitySources`; full package metadata + README; `PackageSurfaceTests` guards the public surface.
  - **Seams and facts later deliverables inherit** (established here, do not re-litigate per package):
    1. `MarkExporterContributed()` — the seam deliverable 3 fills to lift the AzureMonitor fail-fast guard.
    2. `CorrelationHttpDelegatingHandler` + `AddCloudstrapCorrelationHandler` — the typed-HttpClient correlation seam deliverable 4 consumes.
    3. `CloudstrapHealthCheckTags` (`"live"`/`"ready"`) — the cross-package health-tag contract consumed by deliverables 4, 5, 7 and 12.
    4. **Core amendment (AC-C6)**: `OTEL_EXPORTER_OTLP_ENDPOINT` now satisfies the Otlp endpoint-required rule on both Core validation paths (DI `ValidateOnStart` and eager `GetCloudstrapOptions()`); Cloudstrap.Core.Tests grew to 52.
    5. **First `Microsoft.AspNetCore.App` framework reference** in the suite (gate decision OQ-1 — one-package posture; the runtime-image consequence is documented in the package README). Core's host-agnostic closure is unregressed.
    6. **First non-Microsoft dependencies**: Serilog 4.4.0 family + OpenTelemetry 1.17.0 family (all Apache-2.0, CPM-pinned in `src/Directory.Packages.props`); zero `Azure.*`, zero `Aspire.*` (AC-ASP2).
- **Status**: ✅ — done 2026-07-30 (plan's final 🛑 gate checked; definition of done holds; user accepted 2026-07-30).

### 3. Azure Monitor exporter
- **Goal**: A consumer sets `Cloudstrap:OpenTelemetry:Mode = AzureMonitor` + a connection string and telemetry lands in Application Insights, correlated by operation ID.
- **Spec**: `_specs/3-AzureMonitorExporter.md` (AC-AM1…AC-AM12).
- **Plan**: `_plans/3-AzureMonitorExporter.md` — 7 steps / 4 gates; steps 1–7 all done, gates 1–3 approved.
- **Source material**: new package (spec "Observability Migration" §1); mode plumbing from `Common\DistributedTracing\ServiceCollectionExtensions.cs`.
- **Depends on**: 2 ✅
- **Migration decisions**: `Azure.Monitor.OpenTelemetry.Exporter` (`AddAzureMonitorTraceExporter`/`MetricExporter`/`LogExporter`); connection string from setting or `APPLICATIONINSIGHTS_CONNECTION_STRING`; AAD credential support; expose fixed-rate sampling + `AlwaysOnSampler` dev flag.
- **De-NIHDI items**: none beyond naming (new code).
- **Definition of done**: build/tests/format green; XML docs; AC-O1 demonstrable against a real App Insights resource (manual verification documented — unit tests mock at boundary); base package (2) still loads zero Azure assemblies in Otlp mode (AC-O2 regression); SUT demo (standing rule): Bff boots in `AzureMonitor` mode + `AzureMonitorTests` E2E green.
- **Risks**: ⚠️ New dependency `Azure.Monitor.OpenTelemetry.Exporter` (MIT) — keep isolated so base stays exporter-agnostic.
- **Delivered (2026-08-02)** — `src/Cloudstrap.Observability.AzureMonitor/` + `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/`; all 7 steps / 4 🛑 gates closed; AC-O1, AC-O2, AC-ASP2 and AC-AM1…AC-AM12 signed off at the final gate. Shipped: chained `AddAzureMonitor()` entry point, safe (inert) in every mode; `AzureMonitorOptions` under `Cloudstrap:AzureMonitor` (validated, both connection-string sources — setting wins, `APPLICATIONINSIGHTS_CONNECTION_STRING` fallback); per-signal exporters with the #2 `MarkExporterContributed()` guard lifted; sampling policy (platform default · `SamplingRatio` · `TracesPerSecond` · `AlwaysOnSampler` dev flag); Entra ID ingestion auth (`UseDefaultAzureCredential` flag, hook-supplied credential wins); base-package amendment for Blazor hub-span parity (AC-AM9, `EnableBlazorHubTracing` override) with AC-O2/loaded-assembly tripwires re-proven; AC-O1 manual verification procedure documented in the package README. SUT demo: Bff boots in `AzureMonitor` mode; `AzureMonitorTests` E2E green. New deps (CPM-pinned, MIT): `Azure.Monitor.OpenTelemetry.Exporter`, `Azure.Identity` — first `Azure.*` deps in the suite, quarantined in this leaf; base package closure unchanged.
- **Status**: ✅ — done 2026-08-02 (final 🛑 gate checked `[x]`; definition of done incl. the SUT demonstration verified on disk).

### 4. Config/KeyVault/HTTP extensions
- **Goal**: A consumer bootstraps KeyVault-backed configuration, Azure Blob DataProtection, typed `HttpClient` registration (`AddCloudstrapHttpServiceClient<TI,TImpl>`), and hosting helpers with one call each.
- **Spec**: `_specs/4-ConfigKeyVaultHttpExtensions.md` — approved 2026-08-02, all 3 Open Questions resolved (AC-E1…AC-E7 + AC-ASP3).
- **Plan**: `_plans/4-ConfigKeyVaultHttpExtensions.md` — fully executed (all boxes checked; final 🛑 gate user-approved 2026-08-02).
- **Source material** *(file inventory re-verified against the reference repo 2026-08-02)*: `Nihdi.Core.Configuration.Common\` —
  - `KeyVault\` — `AddAzureKeyvaultForNihdi.cs`, `PrefixKeyVaultSecretManager.cs`;
  - `BlobStorage\` — `IHostApplicationBuilderExtension.cs`, `ServiceCollectionExtensions.cs`;
  - `HttpClient\` — `ConfigurationExtension.cs`, `ServiceCollectionExtensions.cs`;
  - `Extensions\` — `IHostApplicationBuilderExtensions.cs` (⚠️ hard-coded KeyVault naming lives here), `IHostBuilderExtensions.cs`, `WebApplicationBuilderExtensions.cs`, `ApplicationBuilderExtensions.cs`, `EndpointRouteBuilderExtension.cs`, `LoggingBuilderExtensions.cs`, `ProbeHealthCheckExtensions.cs`;
  - `Host\HostRunner.cs`; `Options\` — `AddWebOptions.cs`, `UseWebOptions.cs`; `Serialization\DictionaryTKeyEnumTValueConverter.cs`; `Services\ServiceCollectionExtensions.cs`; `AssemblyVisibility.cs` (`InternalsVisibleTo` — Worker consumes Common internals; decide public seam vs re-established `InternalsVisibleTo` here, see §7).
  - **Read-and-route, not this deliverable**: `Correlation\`, `DistributedTracing\`, `Logging\`, `HealthChecks\`, `Dynatrace\` already handled/deleted by #2; `Scalar\*` moves to #5 (WebApi).
- **Depends on**: 1 ✅, 2 ✅ (correlation delegating handler + bootstrap logger)
- **Migration decisions**: `Scalar\*` moves to deliverable 5 (WebApi); drop `Nihdi.AspNetCore.Localization` dependency entirely; auth-token attachment for typed clients becomes an integration seam filled by deliverable 9 (no dependency from 4 to 9); `Nihdi.Core.Health` → stock health checks. **Aspire coexistence**: `AddCloudstrapHttpServiceClient<TI,TImpl>` tolerates resilience handlers already applied via `ConfigureHttpClientDefaults` — no stacked resilience (AC-ASP3); KeyVault config documented "Cloudstrap's or Aspire's, not both" (secret-prefix filter is the differentiator); support standard `ConnectionStrings:` names where sensible.
- **De-NIHDI items**: hard-coded KeyVault naming → `Cloudstrap:KeyVault:VaultUri` (+ optional secret-prefix defaulting to `Application:WorkloadName`); hard-coded storage naming → `Cloudstrap:Storage:BlobServiceUri` (container defaults to `Application:SystemName`); `AddAzureKeyvaultForNihdi` → `AddCloudstrapKeyVault`.
- **Definition of done**: build/tests/format green; XML docs; no reference to any auth package; ⚠️-flagged deps reviewed (`Microsoft.AspNet.WebApi.Client`, `NWebsec.AspNetCore.Middleware` — drop or justify); AC-ASP3 (no stacked resilience) covered by test; zero `Nihdi` identifiers.
- **Risks**: ⚠️ New external deps: `Azure.Identity` (already CPM-pinned by #3), `Azure.Extensions.AspNetCore.Configuration.Secrets`, `Azure.Extensions.AspNetCore.DataProtection.{Blobs,Keys}` (all MIT); ⚠️ NWebsec appears unmaintained — decide replace/drop here.
- **Delivered (2026-08-02)** — `src/Cloudstrap.Extensions/` (net10.0, packable, README + full XML docs) + `src/Test/UnitTest/Cloudstrap.Extensions.Tests/`; the suite's **second `Microsoft.AspNetCore.App` framework reference** (after Observability). All plan boxes checked; the spec's acceptance criteria signed off at the final gate.
  - **Public surface**: `AddCloudstrapHttpServiceClient<TInterface,TImplementation>` (on `IServiceCollection`); `AddCloudstrapKeyVault` / `AddCloudstrapBlobStorage` / `AddCloudstrapDataProtection` (on `IHostApplicationBuilder`); `MapCloudstrapHealthChecks` (on `IEndpointRouteBuilder`); options `KeyVaultOptions`, `StorageOptions`, `DataProtectionOptions`, `KeyVaultConnectionSettings`, `AzureCredentialSettings`; and the seam interface **`IAccessTokenHandlerProvider`** — declared here, implemented by #9/#10, so this package keeps **zero auth dependencies**.
  - **Config sections owned**: `Cloudstrap:KeyVault`, `Cloudstrap:Storage`, `Cloudstrap:DataProtection`.
  - **Additive `Cloudstrap.Core` amendment**: `HttpClientServiceOptions.HealthCheckPath` (default `/healthz`). Core stays Azure-free and host-agnostic.
  - **New dependencies**: `AspNetCore.HealthChecks.Uris` 9.0.0 (**Apache-2.0 — the suite's first non-Microsoft *runtime* dependency**, i.e. first outside the Serilog/OTel observability families); MIT `Azure.Extensions.AspNetCore.Configuration.Secrets` 1.5.1, `Azure.Extensions.AspNetCore.DataProtection.Blobs` 1.5.3, `.DataProtection.Keys` 1.6.3, `Azure.Identity` 1.21.0, `Azure.Storage.Blobs` 12.29.1. Test-only: `Microsoft.Extensions.Http.Resilience` 10.8.0, `Microsoft.AspNetCore.TestHost` 10.0.10.
  - **Security decision (suite-wide, inherited by every later deliverable)**: `CentralPackageTransitivePinningEnabled` is now **on** in `src/Directory.Packages.props`, raising `System.Security.Cryptography.Xml` to the patched **10.0.10** — the Azure DataProtection packages otherwise resolve 10.0.7, which carries five high-severity advisories. A direct `PackageReference` is not usable (the SDK prunes framework-provided packages → NU1510). **Accepted consequence**: transitive pinning makes the Extensions nuspec list the OpenTelemetry/Serilog versions `Cloudstrap.Observability` already requires.
  - **Executor deviations accepted at gates** (facts later hosting deliverables inherit): the dependency health check uses `AddUrlGroup` + a reconciling `IConfigureOptions<HealthCheckServiceOptions>` because `UriHealthCheck` is `internal` in Uris 9.0.0 — the probe `HttpClient` is therefore **always named `{client}-liveness`**; `MapCloudstrapHealthChecks` idempotence uses a **marker `EndpointDataSource`** rather than scanning existing endpoints; the typed-client entry point calls `AddCloudstrapCorrelation()` and `AddHealthChecks()` itself so **one call is self-sufficient**; AC-E7 fails at `ValidateOnStart`/first resolution rather than literally at the registration call.
  - **Verification**: **282 tests green** — Core 54 · Extensions 73 · Observability.AzureMonitor 38 · Observability 98 · E2E 17 (3 new `ExtensionsTests`). SUT demo: the WASM SUT now **runs on this package** — `MapCloudstrapHealthChecks` replaced the two hand-mapped probe endpoints (the 14 pre-existing E2E tests pass unchanged against it) and a `SelfApi` typed client drives `GET api/diagnostics/outbound`.
  - **Not proven by automated test** (documented and accepted): the real `AddAzureKeyVault` configuration-source addition — covered by the README's eight-step manual verification procedure (AC-E5) plus code review, mirroring #3's AC-O1 posture.
- **Status**: ✅ — done 2026-08-02 (final 🛑 gate checked `[x]`; definition of done incl. the SUT demonstration + E2E test verified on disk).

### 5. WebApi bootstrap
- **Goal**: A consumer calls `AddCloudstrapWebApi` to get API versioning, OpenAPI + Scalar UI, hardened exception/correlation middleware, health endpoints, and `AddCloudstrapJwtBearer`.
- **Spec**: `_specs/5-WebApiBootstrap.md` — approved 2026-08-02, all 3 Open Questions resolved (AC-W1…AC-W15 + carried AC-ASP2/AC-A3); its verdict tally (**4 Port · 13 Redesign · 3 Replace · 10 Drop · 6 Superseded-reuse**) and Port Decision Table are authoritative for this deliverable.
- **Plan**: `_plans/5-WebApiBootstrap.md` — fully executed (5 slices · 11 steps · 5 🛑 gates; gate 4 was the dedicated ⚠️ auth risk-area gate covering steps 7–9, gate 5 the final gate after the WASM-SUT demonstration slice, AC-W15). All 11 step boxes and all final-gate boxes `[x]`; final gate user-approved 2026-08-03.
- **Source material** *(file inventory verified against the reference repo 2026-08-02)*: `Nihdi.Core.Configuration.WebApi\` —
  - `WebApi\` — `WebApplicationBuilderExtensions.cs` (the entry point), `DefaultApiVersionConvention.cs`, `NormalizedQueryStringApiVersionReader.cs`, `UrlHelper.cs`;
  - `ExceptionHandlers\` — `WebApiExceptionHandler.cs`, `WebApiExceptionHandlerForDevTst.cs` (⚠️ environment-taxonomy split — collapse to one handler with an explicit "include details" option, per the #1 `IsRunningInAks()` drop precedent);
  - `Swagger\` — `SwaggerBootstrapper.cs`, `OperationProcessors\AspNetCoreOperationSecurityScopeProcessorCustom.cs`;
  - `Correlation\CorrelationMiddleware.cs` — **superseded, dropped** (analyst diff 2026-08-02): a strict subset of #2's shipped `CloudstrapCorrelationMiddleware` — it only copies an inbound header value, with no generation when absent, no requirement enforcement and a hard-coded header name. Nothing to add; the pipeline calls `UseCloudstrapCorrelation()`.
  - Moved here from elsewhere: `Nihdi.Core.Configuration.Common\Scalar\` — `ServiceCollectionExtensions.cs`, `EndpointRouteBuilderExtensions.cs`; old Core `Settings\Swagger\` — `SwaggerConfiguration.cs`, `SwaggerOAuthConfiguration.cs`; old Core `Settings\Scalar\` — `ScalarConfiguration.cs`, `ScalarOAuthConfiguration.cs` (these four become the new `Cloudstrap:OpenApi`/`Cloudstrap:Scalar` options — Core does not own them).
  - Source `PackageReference` set to replace: `Asp.Versioning.Mvc` + `.Mvc.ApiExplorer` 10.0.0 (keep, + add `Asp.Versioning.OpenApi`), `NSwag.AspNetCore` 14.7.1 (**dropped** — dead-code-only reference, see Migration decisions), `Nihdi.AspNetCore.Authentication.JwtBearer` 5.2.5 (→ stock), `Nihdi.Core.Health` 1.0.24 (→ already replaced by #4's `MapCloudstrapHealthChecks`).
- **Depends on**: 4 ✅ (consumes `MapCloudstrapHealthChecks`, `AddCloudstrapHttpServiceClient<TI,TImpl>`, and the `IAccessTokenHandlerProvider` seam) · 1 ✅, 2 ✅ transitively.
- **Migration decisions** *(the spec's three Open Questions are closed — user-resolved 2026-08-02)*:
  - **OpenAPI stack (OQ-1) — NSwag is dropped.** The stack is .NET 10's built-in `Microsoft.AspNetCore.OpenApi` + `Asp.Versioning.OpenApi` 10.0.0 (one document per discovered API version, sunset/deprecation policies in the box) + `Scalar.AspNetCore` UI — all MIT. Grounded in the analyst's finding that the source's NSwag path is **dead code**: `SwaggerBootstrapper` and the operation processor have zero references, the live path already ran `AddOpenApi()` + `MapScalarApiReference()`, and the `NSwag.AspNetCore` 14.7.1 reference existed only to keep the dead code compiling. ⚠️ **Founding-spec amendment (user-authorized 2026-08-02)**: the Package Map WebApi row "Versioning, **NSwag**/Scalar, middleware" becomes "Versioning, OpenAPI (built-in) + Scalar, middleware" — the analyst is applying it to `_specs/Cloudstrap.md`.
  - **JWT hardened defaults (OQ-2)** — ⚠️ auth risk area, explicit user sign-off 2026-08-02: `ClockSkewSeconds = 60` (stock 300), `MapInboundClaims = false` (raw JWT claim names, source parity), `RequireHttpsMetadata` enforced everywhere except `Development`, and registering the bearer applies a **require-auth fallback policy** on mapped controllers (`[AllowAnonymous]` opts out per endpoint, `RequireAuthenticatedEndpoints = false` globally). All four overridable, with `Action<JwtBearerOptions>` as the final escape hatch; auth activation moves from the `Security:EnableAuthentication` config flag to the explicit `AddCloudstrapJwtBearer` call.
  - **Composite pipeline (OQ-3)**: one `UseCloudstrapWebApi` call owns middleware order, with four hook points (before-routing / before-authorization / before-endpoints / configure-endpoints) plus a `MapControllers` switch; the granular pieces (`AddCloudstrapJwtBearer`, `MapCloudstrapHealthChecks`, `UseCloudstrapCorrelation`) stay independently callable as the escape hatch. **This is the pipeline pattern #6 (Mvc) and #7 (Worker) inherit.**
  - `Nihdi.AspNetCore.Authentication.JwtBearer` → stock `Microsoft.AspNetCore.Authentication.JwtBearer`; `Nihdi.Core.Health` → **do not re-implement** — reuse #4's `MapCloudstrapHealthChecks` and the `CloudstrapHealthCheckTags` `"live"`/`"ready"` contract from #2; **correlation comes from #2** — the WebApi `CorrelationMiddleware` is dropped as a strict subset, not re-shipped.
  - **One exception handler.** Both source handlers were broken by construction — the DevTst handler is unreachable behind `UseDeveloperExceptionPage`, and the `/error` re-execution is dead because `TryHandleAsync` always returns `true` — so they collapse into a single RFC 9457 `application/problem+json` handler with an explicit `IncludeDetails` option (unset → details in `Development` only).
  - **CORS/HSTS/security headers are activated, not merely registered.** `AddNihdiWebApiProtections` registered HSTS + CORS that the WebApi pipeline never turned on; the new pipeline activates exactly what it registers, and **"no origins configured → no CORS policy"** replaces the source's insecure `AllowAnyOrigin` fallback.
  - **Dropped as enterprise-only or superseded-by-stock**: `UrlHelper` + the `AddLegacyIssuer` flag (legacy Keycloak realm only), `NormalizedQueryStringApiVersionReader` (`?api-version=v1` compat for deprecated internal clients), `DictionaryTKeyEnumTValueConverter` (stock `System.Text.Json` has handled enum-keyed dictionaries since .NET 5).
- **De-NIHDI items**: API naming `AddNihdiX` → `AddCloudstrapX`; the `ForDevTst` environment taxonomy → an explicit, overridable option; neutral fixtures (`example.com`, `contoso`) in Scalar samples and OAuth scope defaults; no internal IdP/authority URLs in defaults — the Keycloak `/protocol/openid-connect/token` path convention becomes an explicit `TokenUrl` option.
- **Definition of done**: build/tests/format green; XML docs; integration tests verify DI registration + the OpenAPI document and Scalar endpoint; JWT hardened defaults covered by tests; problem-details shape asserted for both detail modes; zero `Nihdi` identifiers; SUT demonstration (standing rule) — the Bff host is already a Web API host, so `AddCloudstrapWebApi` should replace its hand-rolled wiring with ≥ 1 E2E test proving a versioned endpoint + the Scalar UI + the hardened error response.
- **Risks**: ⚠️ **Auth code** (risk area — human review): `AddCloudstrapJwtBearer` is the first shipped authentication surface; the *defaults* are signed off (OQ-2), the *implementation* still gets explicit human review at its gate. ~~⚠️ OpenAPI stack is a one-way door~~ — **decided 2026-08-02 (OQ-1)**: built-in `Microsoft.AspNetCore.OpenApi` + `Asp.Versioning.OpenApi` + `Scalar.AspNetCore`, NSwag dropped, founding-spec Package Map note amended; the door is closed, and the residual risk is only that the resulting options shape (`Cloudstrap:OpenApi`/`Cloudstrap:Scalar`) is public API. ⚠️ New deps `Asp.Versioning.Mvc`/`.Mvc.ApiExplorer`/`.OpenApi` 10.0.0, `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer` — all MIT, each needs a CPM pin; the closure must contain no `NSwag.*` (AC-W14). ⚠️ Third `Microsoft.AspNetCore.App` framework reference in the suite (after #2, #4). ⚠️ **Aspire overlap**: health-check endpoints — register additively through the stock `IHealthChecksBuilder` and do not re-map what #4 already maps (AC-ASP3 posture); the OpenAPI/versioning surface itself does not overlap ServiceDefaults. ~~⚠️ Sets the middleware-pipeline pattern that #6/#7 inherit — a genuine design decision~~ — **decided 2026-08-02 (OQ-3)**: composite `UseCloudstrapWebApi`, four hooks + `MapControllers` switch. The shape is fixed, but a *defect* in it still propagates to #6 and #7 — review at the pipeline slice's gate.
- **Delivered (2026-08-03)** — `src/Cloudstrap.WebApi/` + `src/Test/UnitTest/Cloudstrap.WebApi.Tests/`; all 11 steps / 5 🛑 gates closed; **AC-W1…AC-W15 + AC-ASP2 + AC-A3** signed off at the final gate. The suite's **third `Microsoft.AspNetCore.App` framework reference** (after #2, #4).
  - **Public surface**: `AddCloudstrapWebApi` / `UseCloudstrapWebApi` — the composite pipeline with **four hook points** (before-routing / before-authorization / before-endpoints / configure-endpoints) plus a `MapControllers` switch, with the granular pieces (`AddCloudstrapJwtBearer`, `MapCloudstrapHealthChecks`, `UseCloudstrapCorrelation`) still independently callable as the escape hatch; **`AddCloudstrapJwtBearer`** — the suite's first shipped authentication surface, **inbound JWT *validation* only**, with the four D-2 hardened defaults (`ClockSkewSeconds = 60`, `MapInboundClaims = false`, `RequireHttpsMetadata` outside `Development`, require-auth fallback policy on mapped endpoints) all overridable and an `Action<JwtBearerOptions>` hook running last; per-version OpenAPI documents + Scalar UI; RFC 9457 `application/problem+json` error contract with an explicit `IncludeDetails` option.
  - **Config sections owned**: `Cloudstrap:WebApi`, `Cloudstrap:OpenApi`, `Cloudstrap:Scalar`, `Cloudstrap:JwtBearer`.
  - **Additive `Cloudstrap.Observability` (#2) amendments** — made under the user's standing pre-release permission to break/extend already-shipped packages; **they change #2's shipped surface**: (a) **`HttpContext.GetCloudstrapCorrelationId()`** plus an `HttpContext.Items` stash, so the correlation id is reachable without an `ICorrelationContextAccessor` resolve (consumed by `CloudstrapWebApiExceptionHandler` to stamp problem-details payloads); (b) a correlation-id **response-header echo** governed by the new **`Cloudstrap:Correlation:Request:EchoInResponse` (default `true`)** on `CorrelationRequestOptions`. Every later deliverable inherits both — do not re-implement correlation echoing or a per-package correlation accessor.
  - **Pipeline-pattern sign-off (⚠️ inherited by #6 and #7)**: the as-built `Add`/`Use` pair, the four hook points, the `MapControllers` switch and the canonical middleware order were explicitly approved at the final gate as the shape #6 (Mvc) and #7 (Worker) copy — including every executor deviation reported at Gates 1–4 (notably: the auth-middleware trigger predicate is the registered **scheme map**, not the presence of an `IAuthenticationSchemeProvider`, because MVC registers the authentication core services unconditionally).
  - **Verification**: **408 tests solution-wide, all green** — including **109** in `Cloudstrap.WebApi.Tests` and **28 E2E** (the 17 pre-existing tests pass **unchanged** against a Bff whose entire pipeline is now one `UseCloudstrapWebApi` call, plus 11 new). Zero build warnings in **both Debug and Release**; `dotnet format --verify-no-changes` exit 0; expanded Release `.nupkg` contents reviewed; identifier sweep empty (zero `Nihdi`/`NIHDI`/`Riziv`, zero `Nihdi.AspNetCore` → AC-A3), zero `NSwag.*` in the closure (AC-W14), zero `Aspire.*` (AC-ASP2).
- **Status**: ✅ — done 2026-08-03 (final 🛑 gate checked `[x]` and user-approved; definition of done incl. the SUT demonstration + E2E tests verified on disk 2026-08-05). ⚠️ Bookkeeping only: the *Gate 4* user-approval box is still `[ ]` in the plan although the work it gates was completed and covered by the approved final gate — see the reconciliation note above.

### 6. MVC bootstrap
- **Goal**: A consumer calls `AddCloudstrapMvc` for session hardening, correlation, and secure-header middleware in server-rendered apps.
- **Source material**: `Nihdi.Core.Configuration.Mvc\` (small — README’d package, references Common only).
- **Depends on**: 4
- **Migration decisions**: none specific beyond the Common split; **inherits the composite-pipeline pattern decided in #5** (OQ-3, 2026-08-02) — one `UseCloudstrapMvc` call owning middleware order, same hook-point + map-switch shape.
- **De-NIHDI items**: naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: low — smallest hosting package.
- **Status**: ⬜ — **deferred behind #9** (user-directed re-prioritisation 2026-08-05); dependencies unchanged and still ✅, so it is schedulable the moment #9 closes.

### 7. Worker bootstrap
- **Goal**: A consumer bootstraps a headless worker service with observability and a health listener on a configurable port.
- **Source material**: `Nihdi.Core.Configuration.Worker\` (references Common + Core; `InternalsVisibleTo` from Common — remove or invert cleanly).
- **Depends on**: 4
- **Migration decisions**: health listener port configurable, default 9000; **inherits the composite-pipeline pattern decided in #5** (OQ-3, 2026-08-02) — one composite Add/Use pair with hook points, adapted to a headless host.
- **De-NIHDI items**: naming; probe conventions → `/healthz`+`/ready`.
- **Definition of done**: build/tests/format green; XML docs; health listener port override covered by test; zero `Nihdi` identifiers.
- **Risks**: low.
- **Status**: ⬜ — **deferred behind #9** (user-directed re-prioritisation 2026-08-05); dependencies unchanged and still ✅.

### 8. Test helpers
- **Goal**: A consumer (and Cloudstrap's own test projects) gets `WebApplicationFactory`/EF test utilities from one package.
- **Source material**: `Nihdi.Core.Testing\` (references Functional + `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore`).
- **Depends on**: 0 — **flexible slot**: pull forward if an earlier deliverable's plan needs the factory helpers (e.g. WebApi integration tests).
- **Migration decisions**: Functional reference → LanguageExt.Core (or drop if unused after port).
- **De-NIHDI items**: rename only; neutral fixtures (`example.com`, `contoso`).
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: low.
- **Status**: ⬜ — **deferred behind #9** (user-directed re-prioritisation 2026-08-05); still a flexible slot — pull it forward if #9's or a later plan needs the factory helpers.

### 9. Client-credentials auth ← **next (⬜ — entering technical analysis)**
- **Goal**: A consumer registers machine-to-machine token acquisition (cached, transparently renewed) and **an outbound typed `HttpClient` that already sets `AddClientAccessToken: true` starts carrying a bearer token with no consumer code change** — only a reference to the new package plus its registration call.
- **Source material** *(file inventory verified against the reference repo 2026-08-05)*: `Nihdi.Core.Configuration.OAuth\` — **exactly one source file**, `Extensions\WebApplicationBuilderExtensions.cs` (45 lines: `AddOAuthForNihdi` gated on `Security.EnableAuthentication`, delegating to three internal-package calls `AddClientCredentialsConfiguration()` / `AddClientAccessTokenManagement()` / `AddOAuthConfiguration()`), plus its `README.md` and `.csproj` (references `Nihdi.AspNetCore.Authentication.ClientCredentials` + `Nihdi.AspNetCore.AccessTokenManagement`, both 5.2.5, both **unavailable** — internal feed). ⚠️ **The port surface is therefore behavioral, not textual**: the real logic lives in packages we cannot read, so this deliverable is a **rebuild on Duende ATM against an observed contract**, not a code port. The observable contract is reconstructible from the *call sites*, which are in the repo: `Common\HttpClient\ServiceCollectionExtensions.cs` (`AddUserAccessTokenHandler` / `AddClientAccessTokenHandler` + `MapTokenRequestParameters`), `Proxy\ServiceCollectionExtensions.cs` (same handler on a YARP forwarder), `WebApi\WebApi\WebApplicationBuilderExtensions.cs` (`AddNihdiAccessTokenManagement()`, two call sites), `OpenIdConnect\Extensions\WebApplicationBuilderExtensions.cs` (`.AddNihdiAccessTokenManagement(accessTokenManagementOptionsBuilder)` — the #10 half), `BlazorServer` (`Nihdi.AspNetCore.AccessTokenManagement` reference). Settings routed here by `_specs/5-WebApiBootstrap.md` §Out-of-scope: old Core `Settings\Security\` — `ClientCredentialsConfiguration.cs` (`ClientId` + `ClientSecret`, both `[Required]`), `OAuthConfiguration.cs` (a single `Scopes` string), and the shared parts of `AuthenticationConfiguration.cs` (`Authority`, `AuthenticationFlow`, `CacheKeyPrefix`, `CacheLifetimeBuffer`, `RefreshTokenCacheLifetime`, `IdentityTokenCacheLifetime`) + `AuthenticationFlow.cs` — **shared with #10; the analyst must draw the #9/#10 line explicitly**.
- **Depends on**: 1 ✅, 4 ✅ (both satisfied since 2026-08-02 — see Re-prioritisation below)
- **Migration decisions**: rebuild on **Duende.AccessTokenManagement** client-credentials per the founding spec (`_specs/Cloudstrap.md` lines 21, 84, 178 — authoritative); fills the `IAccessTokenHandlerProvider` seam shipped by #4; **no new configuration flags on the client side** — `Cloudstrap:HttpClients:{name}:AddClientAccessToken` and `:TokenRequestParameters` already exist in `Cloudstrap.Core` and are the opt-in.
- **De-NIHDI items**: `Nihdi.AspNetCore.Authentication.ClientCredentials` / `Nihdi.AspNetCore.AccessTokenManagement` → Duende ATM (**AC-A3**: zero `Nihdi.AspNetCore` references — already green suite-wide, must stay green); `AddOAuthForNihdi` → `AddCloudstrapClientCredentials`; Keycloak-realm authority fixtures and the `/protocol/openid-connect/token` path convention → neutral, explicit, overridable options (the #5 precedent: no internal IdP URLs in defaults); no company copyright headers (the source file carries a `Riziv-Inami` header).
- **Definition of done**: build/tests/format green; XML docs on all public API; package metadata complete; zero `Nihdi`/`NIHDI`/`Riziv` identifiers; **AC-A2** (token transparently renewed across calls spanning its lifetime, no 401s) covered by tests against a mocked token endpoint; **AC-A3** re-proven; ⚠️ explicit human review (auth risk area); **SUT demonstration** (standing rule) — the WASM SUT's Bff acquires a token for an outbound typed client with ≥ 1 passing E2E test.
- **Risks**: ⚠️ **Auth risk area** (human review) — and this is the first deliverable that *acquires and holds* credentials, where #5 only validated inbound tokens; ⚠️ **first Duende ATM usage** — de-risks #10 (OIDC), #12 (BlazorServer) and #17 (Proxy), which is exactly why it is scheduled ahead of them (ordering rule 3); ⚠️ **license confirmation required** — the founding spec records `Duende.AccessTokenManagement` as Apache-2.0, but Duende's other products are commercially licensed, so the analyst must confirm the current license and any usage threshold on the **exact package** before it is pinned (CLAUDE.md rule 4: OSI-approved only); ⚠️ client secrets enter the picture — `Cloudstrap.Core` deliberately models **no** client-id/secret settings type (deliverable #1, OQ-2: `AppRegistrationConfiguration` dropped outright), so the analyst must decide where the secret comes from and say so explicitly; ⚠️ **Aspire overlap: none** — token acquisition is not part of ServiceDefaults; the only adjacency is that the token handler must compose with resilience handlers a consumer may have applied via `ConfigureHttpClientDefaults` (AC-ASP3 posture, already established by #4).
- **Status**: ⬜ — next; hand-off brief below, technical-analyst not yet invoked.

#### Re-prioritisation (user-directed 2026-08-05)
#9 was pulled ahead of #6 (Mvc), #7 (Worker) and #8 (Testing). Both #9 and #10 have had **every dependency satisfied since #4 shipped on 2026-08-02** (#1 ✅, #4 ✅), so their previous position behind the rest of the hosting band was a **sequencing choice, not a graph constraint** — the roadmap's own dependency analysis already recorded this (refinement 2: "auth needs only Core, not hosting"). The user wants token acquisition next. Numbers were **not** changed; the Overview table row moved.

#### Hand-off brief for the `technical-analyst`
- **Suggested spec file**: `_specs/9-ClientCredentialsAuth.md`
- **Precedent for shape**: the #5 brief in this file and the resulting `_specs/5-WebApiBootstrap.md` (verdict table with Port / Redesign / Replace / Drop per source artefact, Open Questions the user answers before the planner starts).
- **Source material to read** — reference repo root **`D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\`** (the stale `D:\Data\gv10141\…` path was corrected in CLAUDE.md, `_specs/Cloudstrap.md` and the `project-manager` agent on 2026-08-05):
  1. `Nihdi.Core.Configuration.OAuth\Extensions\WebApplicationBuilderExtensions.cs` + `README.md` + `.csproj` — the whole package.
  2. Call sites that define the observable contract (the internal packages themselves are unreadable): `Nihdi.Core.Configuration.Common\HttpClient\ServiceCollectionExtensions.cs`, `Nihdi.Core.Configuration.Proxy\ServiceCollectionExtensions.cs`, `Nihdi.Core.Configuration.WebApi\WebApi\WebApplicationBuilderExtensions.cs` (~lines 377, 405), `Nihdi.Core.Configuration.OpenIdConnect\Extensions\WebApplicationBuilderExtensions.cs` (~line 67).
  3. Old Core `Settings\Security\` — `ClientCredentialsConfiguration.cs`, `OAuthConfiguration.cs`, `AuthenticationConfiguration.cs`, `SecurityConfiguration.cs` — plus `AuthenticationFlow.cs`.
  4. **Shipped Cloudstrap code the spec must build on, read before deciding anything**: `src/Cloudstrap.Extensions/IAccessTokenHandlerProvider.cs` and `AccessTokenHandlerWiring.cs` (the socket and how it is invoked), `src/Cloudstrap.Extensions/ServiceCollectionExtensions.cs` (`AddCloudstrapHttpServiceClient<TI,TImpl>`), `src/Cloudstrap.Core/HttpClientServiceOptions.cs` + `TokenRequestOptions.cs`, and `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AccessTokenHandlerSeamTests.cs` (the seam's asserted behavior).
- **The seam already exists and is currently wired to nothing — #9 is what fills the socket.** `Cloudstrap.Extensions` (#4, shipped) declares `IAccessTokenHandlerProvider` with `CreateUserTokenHandler` / `CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)`, and `AccessTokenHandlerWiring` already resolves it **lazily at pipeline-build time** (so `Program.cs` call order does not matter), inserts the handler **at the head of the pipeline** (ahead of the correlation handler), and today throws `InvalidOperationException` naming `Cloudstrap.Authentication.ClientCredentials` when a client sets the flag with no provider registered. `Cloudstrap.Core.HttpClientServiceOptions` already carries `AddUserAccessToken`, `AddClientAccessToken`, `HealthCheckPrefix` and `TokenRequestParameters` (`TokenRequestOptions`: `Scope`, `Resource`, `SignInScheme`, `ChallengeScheme`, `ForceRenewal`). **Success criterion**: a consumer who already has `AddClientAccessToken: true` in `Cloudstrap:HttpClients:{name}` gets tokens attached with **no consumer code change** beyond referencing the package and calling its registration. The spec should treat `IAccessTokenHandlerProvider` as a **fixed contract** and justify explicitly if it proposes changing it (allowed — see standing constraint — but it is #4's public API).
- **Scope boundary the spec must state plainly, so it is not re-litigated**: **#5 shipped inbound JWT *validation* only** (`AddCloudstrapJwtBearer` in `Cloudstrap.WebApi`, with the four D-2 hardened defaults, `Cloudstrap:JwtBearer`). **#9 is token *acquisition*** — outbound, machine-to-machine. Validation is done and out of scope here. Likewise **user/interactive tokens and the OIDC login flow are #10**; #9 implements only `CreateClientTokenHandler` unless the analyst argues otherwise, and must say what happens when a client sets `AddUserAccessToken: true` with only #9 installed (the seam permits both flags; the wiring calls user first).
- **Applicable spec decisions** (`_specs/Cloudstrap.md` authoritative): internal `Nihdi.AspNetCore.*` auth → **stock ASP.NET Core auth + Duende.AccessTokenManagement** (line 21); `Nihdi.Core.Configuration.OAuth` → `Cloudstrap.Authentication.ClientCredentials`, "rebuilt on Duende ATM client-credentials" (line 84); the named entry point is **`AddCloudstrapClientCredentials`** — "Duende ATM client-credentials token client with caching/renewal; feeds the typed `HttpClient` registration (`AddCloudstrapHttpServiceClient<TI,TImpl>`) and proxy-forwarding helpers" (line 178). Inherited facts: #1's source-generated `[OptionsValidator]` pattern with **no** `Microsoft.Extensions.Options.DataAnnotations`; #1's get-only-collection "configured values append, not replace" caveat (document it for any collection this package introduces); #4's `CentralPackageTransitivePinningEnabled` suite-wide; #5's `Cloudstrap:<Feature>` one-section-per-package convention and its "every convention has an override" posture; the #5 correlation amendments listed under §5 Delivered.
- **De-NIHDI checklist items in play**: `Nihdi.AspNetCore.*` package references (AC-A3, must stay at zero); `AddOAuthForNihdi` → `AddCloudstrapClientCredentials`; the `Riziv-Inami` copyright header on the one source file; Keycloak-realm authority examples and the `/protocol/openid-connect/token` path convention → explicit overridable options with neutral fixtures (`example.com`, `contoso`); no internal IdP hostnames anywhere, including tests and README.
- **Acceptance criteria the spec must cover**: **AC-A2** (client-credentials `HttpClient` registered; two calls an hour apart with a 5-minute token lifetime → token transparently renewed, no 401s) and **AC-A3** (zero `Nihdi.AspNetCore` references), plus this deliverable's own AC-… set, plus **AC-ASP2** (zero `Aspire.*` in the closure) as a carried criterion, plus the standing **SUT-demonstration** criterion (Bff acquires a token for an outbound typed client, ≥ 1 E2E test).
- **Open questions the analyst should put to the user** (not an exhaustive list — the analyst owns the final set):
  1. **Where does the client secret come from?** #1 dropped `AppRegistrationConfiguration` outright and ships **no** credential settings type; #4 ships `AddCloudstrapKeyVault`. Options include KeyVault-backed configuration, a `TokenCredential`/workload-identity path (client assertion), a plain option, or a hook. This is the deliverable's central design decision.
  2. **Where is the `#9`/`#10` line for the shared `Settings\Security\` material** — `Authority`, `AuthenticationFlow`, `CacheKeyPrefix`, `CacheLifetimeBuffer`, `RefreshTokenCacheLifetime`, `IdentityTokenCacheLifetime`? Which land in `Cloudstrap:ClientCredentials` now, which wait for #10, and is there a shared `Cloudstrap:Authentication` section or deliberately not? (The source's `EnableAuthentication` master flag should follow #5's precedent and be **dropped** in favour of the explicit registration call.)
  3. **Token cache**: Duende ATM's default in-memory cache vs `IDistributedCache`, and what the multi-instance/container default is. The source exposed `CacheKeyPrefix` + lifetime buffers — decide port / redesign / drop for each.
  4. **`AddUserAccessToken` with only #9 installed** — fail fast at the seam, or a clear message pointing at #10?
  5. **Does #9 need its own integration test against a real IdP?** Founding **AC-A1** parks a **Keycloak-container** test in #10. The analyst should decide whether #9 needs one of its own or stays on **locally-issued tokens / a mocked token endpoint** (the #5 precedent used locally-issued tokens; #3 and #4 precedent: document a manual verification procedure in the README for what automation cannot cover).
  6. **License**: confirm `Duende.AccessTokenManagement`'s current license and any usage threshold before pinning (CLAUDE.md rule 4).
- **⚠️ Risk areas the spec must flag**: auth risk area (human review at every gate touching it); credential handling and storage; token/secret values must never reach logs or telemetry (#2's enrichers and #5's problem-details handler are both in the request path); public API surface — `AddCloudstrapClientCredentials` and its options shape are a one-way door, and the `IAccessTokenHandlerProvider` implementation is the contract #10, #12 and #17 build on; new dependency `Duende.AccessTokenManagement` (license, and it is the suite's first auth-stack runtime dependency).
- **Aspire-overlap items**: **none** — token acquisition is outside Aspire ServiceDefaults' remit. Carry **AC-ASP2** (zero `Aspire.*` references) as a tripwire only, and note that the token handler must not disturb resilience handlers registered via `ConfigureHttpClientDefaults` (the AC-ASP3 posture #4 established).
- **Standing constraint to state in the spec**: nothing is published to nuget.org yet, so **breaking changes to already-shipped packages are allowed** until the user says otherwise. If the cleanest design needs `IAccessTokenHandlerProvider`, `HttpClientServiceOptions` or `TokenRequestOptions` to change, propose it — fix it at the source rather than working around it.

### 10. OIDC login
- **Goal**: A consumer calls `AddCloudstrapOpenIdConnect` for auth-code + PKCE login with secure cookie defaults and Duende ATM user-token refresh.
- **Source material**: `Nihdi.Core.Configuration.OpenIdConnect\` (+ old Core `Settings\Security\OpenIdConnectConfiguration.cs`, `AuthenticationConfiguration.cs`).
- **Depends on**: 1 (Duende ATM patterns de-risked by 9)
- **Migration decisions**: stock `Microsoft.AspNetCore.Authentication.OpenIdConnect` + Duende ATM user-token management replaces the `Nihdi.AspNetCore.*` suite (incl. Authentication.UI — dropped or minimal stock equivalents).
- **De-NIHDI items**: AC-A3; naming; neutral IdP fixtures.
- **Definition of done**: build/tests/format green; XML docs; AC-A1 verified against a standards-compliant IdP (Keycloak container, E2E/manual documented); human review (auth risk area); zero `Nihdi` identifiers.
- **Risks**: ⚠️ Auth risk area; ⚠️ behavior parity with the internal suite (UI/logout endpoints) needs explicit scoping in the plan.
- **Status**: ⬜

### 11. Blazor shared abstractions
- **Goal**: A consumer gets shared Blazor abstractions (ErrorHandler, Navigation, ViewModel base, Scrutor scanning) usable from Server and WASM.
- **Source material**: `Nihdi.Core.Configuration.BlazorCommon\` (standalone; `Microsoft.AspNetCore.Components` + `Scrutor`).
- **Depends on**: 0 (standalone — kept in Blazor band by choice)
- **Migration decisions**: rename only per spec.
- **De-NIHDI items**: naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: low; `Scrutor` (MIT).
- **Status**: ⬜

### 12. Blazor Server helpers (+ TestProject SUT)
- **Goal**: A consumer bootstraps a Blazor Server app with tracing, typed HttpClients with token attachment, and hardened defaults; the repo gains the Blazor Server SUT for E2E smoke tests.
- **Source material**: `Nihdi.Core.Configuration.BlazorServer\`; SUT inspiration: `Test\TestProject\src\*` (rebuild neutral, do not copy wholesale).
- **Depends on**: 4, 9, 11
- **Migration decisions**: `Nihdi.AspNetCore.AccessTokenManagement` → Duende ATM (via deliverable 9); `BlazorHubSampler` stays in Observability.
- **De-NIHDI items**: probe path `/probe.aspx` → `/healthz`; naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; TestProject SUT boots and E2E smoke test passes; zero `Nihdi` identifiers.
- **Risks**: ~~⚠️ First E2E infrastructure (Playwright) lands here~~ — resolved: the Playwright E2E harness already exists (deliverable #25); this deliverable reuses its patterns for the Blazor Server SUT.
- **Status**: ⬜

### 13. Blazor WASM helpers (+ WasmTestProject SUT)
- **Goal**: A consumer bootstraps a WASM client with cookie auth (BFF pattern), XSRF, and Refit clients; repo gains the WASM SUT.
- **Source material**: `Nihdi.Core.Configuration.BlazorWasm\` (standalone; Refit, Components.Authorization); SUT inspiration: `Test\WasmTestProject\src\*`.
- **Depends on**: 11 (band choice; package itself is standalone)
- **Scope note (2026-08-01)**: the WASM SUT already exists — built by deliverable #25 (`src/Test/WasmTestProject`: Contracts, Presentation, Host/Wasm, Host/**Bff**, E2E harness). #13 no longer scaffolds it; it adds the BlazorWasm helpers and demos them in the existing SUT (cookie/BFF auth, XSRF, Refit clients replacing the plain `HttpClient`).
- **Migration decisions**: browser-auth pattern (cookie + XSRF + `BffAuthenticationStateProvider`) ports as-is per spec; `Microsoft.Extensions.Localization` usage is stock — keep. Source's committee-named `Cfe` host → industry-standard **`Bff`** (decided at #25 Gate 1).
- **De-NIHDI items**: naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; WASM SUT boots and smoke test passes; zero `Nihdi` identifiers.
- **Risks**: `Refit` (MIT).
- **Status**: ⬜

### 14. Messaging (Wolverine)
- **Goal**: A consumer calls `AddCloudstrapMessaging(...).UseSqlServer(...)` for a Wolverine node with ASB/SQL/local transports, durable inbox/outbox, EF transactional messaging (`AddCloudstrapTransactionalMessaging<TDbContext>`), retries, and correlation.
- **Source material**: `Nihdi.Core.Configuration.NServiceBus\` (behavior reference only — implementation is new on Wolverine) + old Core `Settings\NServiceBus\*` (config shape; Bridge/ServicePlatform settings deleted).
- **Depends on**: 1, 2, 4
- **Migration decisions**: full NServiceBus → Wolverine table from the spec (transports, suffix conventions, outbox with `{WorkloadName}_` prefix, retries, `AutoProvision` default-on in Development, DLQ `{system}-error`); storage-provider seam (SQL Server v1, PostgreSQL later); **dropped**: property-level encryption, MessagingBridge, ServicePlatform connector, UniformSession.
- **De-NIHDI items**: ASB topic `nihdi-default-bundle` → Wolverine conventions (overridable); license paths gone; `Nihdi.Core.NServiceBus.Cryptography` gone; naming.
- **Definition of done**: build/tests/format green; XML docs; AC-M2 (outbox atomicity) + AC-M3 (in-memory transport, no network) covered by tests; AC-M1 verified against real ASB (documented manual/E2E); zero `Nihdi` identifiers; zero NServiceBus references.
- **Risks**: ⚠️ Biggest rewrite (not a port) — new deps `WolverineFx`, `WolverineFx.AzureServiceBus`, `WolverineFx.SqlServer`, EF integration — verify each package's license (Wolverine core is MIT; confirm transport/persistence packages at plan time); ⚠️ public API seam design (`UseSqlServer`) is a one-way door.
- **Status**: ⬜

### 15. Blob claim-check
- **Goal**: Messages over a size threshold transparently store payloads in Azure Blob and travel as references (AC-M4).
- **Source material**: old behavior via `NServiceBus.DataBus.AzureBlobStorage` usage in `Nihdi.Core.Configuration.NServiceBus\` — reimplemented as Wolverine middleware (no Wolverine built-in claim check).
- **Depends on**: 14
- **Migration decisions**: thin middleware in `Cloudstrap.Messaging.AzureBlob`; threshold + container configurable.
- **De-NIHDI items**: storage naming → explicit `BlobServiceUri` convention from deliverable 4.
- **Definition of done**: build/tests/format green; XML docs; AC-M4 covered (Azurite/emulated in tests); zero `Nihdi` identifiers.
- **Risks**: ⚠️ Custom middleware on Wolverine internals — pin Wolverine version range carefully; `Azure.Storage.Blobs` (MIT).
- **Status**: ⬜

### 16. Hangfire scheduler
- **Goal**: A consumer registers Hangfire with SQL storage and attribute/interface-based recurring-task discovery in one call.
- **Source material**: `Nihdi.Core.Configuration.Hangfire\` (+ old Core `Settings\Hangfire\*` moved here).
- **Depends on**: 1 (actual graph: Core only — not blocked by hosting or messaging)
- **Migration decisions**: free Hangfire tier only.
- **De-NIHDI items**: environment-taxonomy dashboard switches → explicit flags with environment-based defaults; naming.
- **Definition of done**: build/tests/format green; XML docs; recurring-task discovery covered by tests; **LGPL notice for Hangfire documented in package README**; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Hangfire.* is LGPL v3 — docs note required (spec decision); `Scrutor` (MIT).
- **Status**: ⬜

### 17. YARP trusted-subsystem proxy
- **Goal**: A consumer configures a YARP-based trusted-subsystem forwarder (token exchange onward) with one call.
- **Source material**: `Nihdi.Core.Configuration.Proxy\` (references Common; `Yarp.ReverseProxy`).
- **Depends on**: 4
- **Migration decisions**: token-forwarding integrates with deliverable 9's helpers where applicable.
- **De-NIHDI items**: naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Auth-adjacent (forwarded credentials) — human review; `Yarp.ReverseProxy` (MIT).
- **Status**: ⬜

### 18. Hangfire dashboard proxy
- **Goal**: A consumer exposes the Hangfire dashboard through a proxy host securely.
- **Source material**: `Nihdi.Core.Configuration.Hangfire.Proxy\` (references Hangfire + Proxy; uses `InternalsVisibleTo` from Hangfire — re-establish or design a public seam).
- **Depends on**: 16, 17
- **Migration decisions**: none beyond parents.
- **De-NIHDI items**: naming.
- **Definition of done**: build/tests/format green; XML docs; zero `Nihdi` identifiers.
- **Risks**: dashboard auth path — human review.
- **Status**: ⬜

### 19. Dashboard backend
- **Goal**: A consumer mounts the ops-dashboard API: ASB queue peek/purge/retry, diagnostics, claims viewer — minus message decryption (dropped).
- **Source material**: `Nihdi.Core.Configuration.Dashboard\Dashboard.Contracts\` + `Dashboard.Api\` (references Core, NServiceBus→Messaging, Functional→LanguageExt, Contracts; `Azure.Messaging.ServiceBus`, `Scrutor`).
- **Depends on**: 1, 14
- **Migration decisions**: dashboard settings (from old Core `Settings\Dashboard\`) live HERE (inverted dependency broken); decryption feature deleted; messaging integration re-targeted from NServiceBus to Wolverine/ASB SDK.
- **De-NIHDI items**: naming; neutral fixtures; no internal queue-naming assumptions beyond the documented workload convention.
- **Definition of done**: build/tests/format green; XML docs; queue operations covered by tests (mocked ASB); zero `Nihdi` identifiers; zero decryption code.
- **Risks**: ⚠️ Largest product-layer surface; `Azure.Messaging.ServiceBus` (MIT); claims viewer touches auth data — human review.
- **Status**: ⬜

### 20. Dashboard UI (MudBlazor)
- **Goal**: A consumer drops dashboard Blazor components (Server and WASM hosts) rendering on plain MudBlazor.
- **Source material**: `Nihdi.Core.Configuration.Dashboard\Dashboard.Components.Shared\`, `Dashboard.Components\`, `Dashboard.Components.BlazorWasm\`.
- **Depends on**: 11, 13, 19
- **Migration decisions**: `Nihdi.Common.MudBlazor8.DesignSystem` → plain **MudBlazor** (MIT); `Newtonsoft.Json` → prefer System.Text.Json (minimize deps); Functional → LanguageExt.Core.
- **De-NIHDI items**: design-system identifiers; naming; neutral fixtures.
- **Definition of done**: build/tests/format green; XML docs; components render in the SUT apps (bUnit/E2E smoke); zero `Nihdi` identifiers.
- **Risks**: ⚠️ Design-system → MudBlazor restyle is UI-visible work, hard to test automatically; MudBlazor (MIT).
- **Status**: ⬜

### 21. Cookie consent
- **Goal**: A consumer adds a CSP-friendly cookie-consent UI (bundled orejime assets, no CDN) with one component.
- **Source material**: `Nihdi.Core.Configuration.CookieConsent\` (standalone Razor lib).
- **Depends on**: 0 (standalone — kept in product band by choice)
- **Migration decisions**: verify and ship orejime license attribution (spec Package Map note).
- **De-NIHDI items**: package metadata (`Company: NIHDI`, `nihdi` tags) → Cloudstrap/MIT; naming.
- **Definition of done**: build/tests/format green; XML docs; orejime license attribution included; zero `Nihdi` identifiers.
- **Risks**: ⚠️ Bundled JS license attribution (orejime — verify current license and NOTICE requirements).
- **Status**: ⬜

### 22. Analytics abstraction + Matomo
- **Goal**: A consumer registers `IAnalyticsTracker` (consent-gated via CookieConsent) with the Matomo adapter; no tracking before consent, endpoint always required.
- **Source material**: `Nihdi.Core.Configuration.Analytics.Matomo\` (JS-interop tracker; abstraction is new).
- **Depends on**: 21
- **Migration decisions**: split into `Cloudstrap.Analytics` (abstraction) + `Cloudstrap.Analytics.Matomo` (adapter); no default endpoint URL.
- **De-NIHDI items**: internal Matomo instance default removed (endpoint required); package metadata; naming.
- **Definition of done**: build/tests/format green; XML docs; `Track_WithoutConsent_DoesNothing`-class tests pass; zero `Nihdi` identifiers.
- **Risks**: low.
- **Status**: ⬜

### 23. Google Analytics adapter
- **Goal**: A consumer swaps in GA4 (gtag) behind the same `IAnalyticsTracker`.
- **Source material**: new code (spec Analytics section); pattern from deliverable 22.
- **Depends on**: 22
- **Migration decisions**: GA4 gtag adapter; consent-gating inherited from abstraction.
- **De-NIHDI items**: none (new code).
- **Definition of done**: build/tests/format green; XML docs; adapter conformance tests pass.
- **Risks**: low.
- **Status**: ⬜

### 24. Localization setup
- **Goal**: A consumer gets one-call culture-negotiation defaults + supported-culture config section over stock ASP.NET Core localization.
- **Source material**: ⚠️ **none in the reference repo** — `Nihdi.AspNetCore.Localization` is an internal package whose source is unavailable; this is a fresh thin implementation per spec (no custom engine).
- **Depends on**: 1
- **Migration decisions**: thin setup layer only; resource conventions documented.
- **De-NIHDI items**: none beyond naming (new code).
- **Definition of done**: build/tests/format green; XML docs; culture-negotiation defaults covered by tests.
- **Risks**: ⚠️ No source reference — scope must be pinned from the spec sentence alone; keep minimal.
- **Status**: ⬜

### 25. WASM SUT + E2E demonstration harness
- **Goal**: Every delivered Cloudstrap feature is demonstrable in a running Blazor WASM app (`src/Test/WasmTestProject`) and proven by an E2E test (NUnit 4 + Microsoft.Playwright) that boots the real Bff host and drives a real browser; the demonstration becomes a standing definition-of-done rule for all deliverables.
- **Plan**: `_plans/25-WasmTestProjectSut.md` (user-directed 2026-07-30; no spec — test infrastructure + process change, not a package port).
- **Source material**: source `Test\WasmTestProject` (layout inspiration — rebuilt neutral and trimmed; the committee-named `Cfe` host renamed to industry-standard `Bff`). Not a shipped package: no NuGet output, no XML-doc requirement (`src/Test` tree).
- **Depends on**: 1, 2 (the features it demonstrates).
- **Delivered so far**: 4 SUT projects (Contracts · Presentation/MudBlazor · Host.Wasm · Host.Bff) + `Cloudstrap.WasmTestProject.E2E.Tests` (11 tests): home page boot; Core demo (`/diagnostics` server binding, client-side WASM binding badge, fail-fast startup validation); Observability demo (tagged health probes, ambient correlation id, `AddDoctor` business span asserted in captured console telemetry); CI runs the E2E suite (Playwright install step in `ci.yml`); `.vscode` launch configs; `src/Test/Directory.Build.props` made SUT-aware (NUnit/MTP wiring only for `*.Tests`); test-only CPM pins MudBlazor 9.7.0 (MIT), Microsoft.Playwright 1.61.0 (Apache-2.0), Microsoft.AspNetCore.Components.* 10.0.10 (MIT).
- **Process artefacts**: planner rule 15 + interview/self-check items, plan-template demonstration-slice block, project-manager DoD + ✅-flip verification, CLAUDE.md workflow rule 9 + commands, tests.md E2E section, this roadmap's standing rule.
- **Risks**: ~~⚠️ CI change (Playwright browser install step) — CI-green proof pending the first push~~ — resolved at the final gate (CI green verified).
- **Status**: ✅ — done 2026-08-02 (final 🛑 gate checked `[x]`: full suite + CI green, process docs reviewed). The standing SUT-demonstration rule is now institutionalized for every remaining deliverable.

## Change log

| Date | Change | Why |
|------|--------|-----|
| 2026-07-25 | Initial roadmap created (25 deliverables, 0–24), grounded in `<ProjectReference>` analysis of all source `.csproj` files. | Extraction kickoff. |
| 2026-07-25 | Reference-repo path corrected: agent briefing said `D:\Data\gv10141\Repos\Common\Nihdi-Core-Configuration` (does not exist); verified actual root `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\`. | Ground truth from filesystem. |
| 2026-07-25 | Within the auth band, ClientCredentials (9) ordered before OpenIdConnect (10), diverging from the baseline listing. | Ordering rule 3 (risk early): first Duende ATM usage, and it feeds the typed-HttpClient seam used by BlazorServer/Proxy. |
| 2026-07-25 | Noted graph refinements: auth needs only Core; Hangfire needs only Core; BlazorCommon/BlazorWasm/CookieConsent/Analytics/Dashboard.Contracts/Testing are standalone. Band order kept for focus; constraints recorded so re-prioritization stays cheap. | Actual graph beats assumed bands. |
| 2026-07-25 | Cloudstrap.Testing given a flexible slot (#8) instead of a fixed band. | Old `Nihdi.Core.Testing` depends only on Functional (→ LanguageExt); first consumer determines timing. |
| 2026-07-25 | `_specs/0-RepoScaffolding.md` approved (zero Open Questions); §0 set to 📝, planner writing `_plans/0-RepoScaffolding.md`. Gate decisions folded into §0: NUnit 4 + NUnit3TestAdapter + NUnit.Analyzers on MTP (replaces MSTest v4); no StyleCop — SDK analyzers (`AnalysisLevel=latest-recommended`) + `EnforceCodeStyleInBuild`/`.editorconfig` + `dotnet format` gate; CS1591 on in `src/`, off under `src/Test/`; Central Package Management (`src/Directory.Packages.props`); previews from `dev` pushes only → GitHub Packages; stable via tag-push `v*` → nuget.org only; scheduled cleanup keeps last 20 preview versions per package. | User gate 2026-07-25. NUnit and no-StyleCop are user-directed deviations from the founding spec/CLAUDE.md — documentation amendments pending with the user (see spec "Documentation amendments required"). |
| 2026-07-25 | Spec/plan file-naming convention adopted: roadmap-number prefix — `_specs/<N>-<Deliverable>.md` / `_plans/<N>-<Deliverable>.md`. | One-glance traceability between roadmap number, spec, and plan; applies to all future deliverables. |
| 2026-07-25 | Aspire-coexistence posture adopted (founding-spec amendment, user-directed): zero `Aspire.*` references in shipped packages (AC-ASP2); §2 gains OTel owner/contribute modes (AC-ASP1); §4 gains resilience-handler tolerance + "Cloudstrap's or Aspire's, not both" KeyVault docs (AC-ASP3); `Cloudstrap.Aspire` is post-v1 only. | Coexist with Aspire on the shared substrate (`Microsoft.Extensions.*`, OTel .NET, Azure SDK) without inheriting Aspire's platform risk — Aspire-based apps become adopters instead of conflicts. |
| 2026-07-25 | `_specs/1-CoreSettingsModel.md` approved (both Open Questions resolved); §1 set to 📝, planner is the next step. **OQ-1**: root settings type named `CloudstrapOptions` with `GetCloudstrapOptions()` accessor (repo `*Options` convention); founding-spec amendment `CloudstrapConfiguration` → `CloudstrapOptions` approved and applied to `_specs/Cloudstrap.md` (founding-spec edits are user-owned — applied by the main agent, not the analyst). **OQ-2**: `AppRegistrationConfiguration` dropped outright — no Cloudstrap type models client-id/secret credentials in appsettings; deliverable 14 builds transport auth on `TokenCredential`/`DefaultAzureCredential` + standard `AZURE_*` env vars. Analyst finding folded into §1: the source `Nihdi.Core.Functional` ProjectReference is dead code (zero usage) — `Cloudstrap.Core` takes no LanguageExt.Core dependency; the type-mapping decision defers to the first genuine consumer (likely Testing or Dashboard). | User gate 2026-07-25. |
| 2026-07-26 | **Deliverable #1 (Cloudstrap.Core) → ✅.** `_plans/1-CoreSettingsModel.md` fully executed (5 steps, 3 🛑 gates, 19/19 boxes; gates approved 2026-07-26); AC-C1…AC-C10 + AC-ASP2 signed off. §1 gains a **Delivered** entry recording the shipped public surface, the Microsoft.Extensions-only / zero-`Aspire.*` / zero-`LanguageExt.*` dependency closure, and four facts later deliverables inherit: (1) `Microsoft.Extensions.Options.DataAnnotations` deliberately NOT taken — `[OptionsValidator]` source generation emits the `[Required]` checks (deviates from the spec's dependency table; user-confirmed at the Slice-2 gate); (2) get-only collection options mean configured values **append** to defaults rather than replace them (only `CorrelationRequestOptions.HealthEndpoints` ships non-empty defaults) — every consuming package inherits and must document this; (3) a broken section-level rule is reported twice at startup (root-relative + section-relative) — user-acknowledged; (4) the LanguageExt type-mapping decision stays open, deferred to the first genuine consumer. Overview row #2 marked **next**. | Deliverable complete and verified on disk (build 0/0, 49/49 tests, format exit 0, Release nupkg+snupkg clean). Facts 1–3 are cross-package conventions, so they belong at roadmap level rather than buried in one plan. |
| 2026-07-26 | **Deliverable #0 (Repo scaffolding) → ✅.** Status went 📝 → 🔨 → ✅ within the day: the roadmap first recorded it as 🔨 because six operational boxes of the plan's final 🛑 gate were open (nuget.org prefix reservation · GitVersion probe review · PR `ci.yml` run · `dev` preview-publish no-op · `cleanup-previews.yml` dispatch · optional `v0.1.0` tag · workflow code review), which gated the first *publish* rather than local development. The user closed all of them the same day and approved the gate — `_plans/0-RepoScaffolding.md` is now 100% complete. **The `Cloudstrap.` package ID prefix is reserved on nuget.org**; together with the active Trusted Publishing policy and the `NUGET_USER` secret, the publish path is fully open and the prefix-reservation risk on §0 is resolved. | Roadmap rule: ✅ only when the plan's final gate is checked `[x]` — verified box-by-box on disk before flipping. The foundation band (0 + 1) is now closed and nothing operational stands between a finished package and a feed. |
| 2026-07-26 | §2 source material re-verified file-by-file against the reference repo and expanded (DistributedTracing / Logging / Correlation / HealthChecks inventories, plus `Dynatrace\*` marked read-and-delete). Noted that `CorrelationHttpDelegatingHandler.cs` + `IHttpClientBuilderExtensions.cs` live in `Correlation\` — confirming the Observability-before-Extensions order — and that the Logging/OpenTelemetry/Correlation/HealthChecks **options types already ship in `Cloudstrap.Core`**, so #2 consumes rather than redefines them. §2 risks gain the explicit Aspire-overlap flag and a "do not regress Core's host-agnostic closure" note. | Pre-hand-off scope check for the next deliverable (ordering rule: verify against the source repo before scheduling). Two files the earlier entry omitted (`DeferredLoggerFactory.cs`, `NihdiConsoleFormatter.cs`) surfaced; the Core options boundary changed since the entry was written. |
| 2026-07-30 | **Deliverable #2 (Cloudstrap.Observability) → ✅.** `_plans/2-ObservabilityBase.md` fully executed (12 steps, 6 🛑 gates, all boxes checked; user accepted 2026-07-30); AC-O2/O3/O4, AC-ASP1/ASP2, AC-B1…AC-B13 (+ amended AC-C6) signed off at the final gate. §2 gains a **Delivered** entry recording the shipped surface and six inherited seams/facts: the `MarkExporterContributed()` guard seam (#3), the correlation delegating-handler seam (#4), the `CloudstrapHealthCheckTags` `"live"`/`"ready"` contract (#4/5/7/12), the AC-C6 Core amendment (`OTEL_EXPORTER_OTLP_ENDPOINT` satisfies the Otlp endpoint rule on both validation paths), the suite's first `Microsoft.AspNetCore.App` framework reference (OQ-1, one-package posture), and the first non-Microsoft deps (Serilog 4.4.0 + OpenTelemetry 1.17.0 families, CPM-pinned, zero `Azure.*`/`Aspire.*`). Overview row #3 marked **next** (its only dependency, 2, is ✅); reconciliation note refreshed to 2026-07-30. | Roadmap rule: ✅ only when the plan's final gate is checked `[x]` — verified box-by-box on disk (52/52 + 91/91 tests, zero warnings, format clean, Release nupkg verified). |
| 2026-08-01 | **Deliverable #25 added (user-directed) and executed to its final gate**: WASM SUT (`src/Test/WasmTestProject`, `Bff`+`Wasm` hosts) + Playwright E2E harness + **standing SUT-demonstration rule** added to the preamble (every deliverable's DoD includes a SUT demo + E2E test). §12 risk updated (E2E infra no longer lands there); §13 re-scoped (SUT exists; adds helpers/auth/Refit demos to it; `Cfe`→`Bff` naming). Roadmap edits applied by the main agent on user instruction (project-manager not invoked). | User directive: "every time you migrate a new feature, demonstrate how it works on this test project" — institutionalized via planner rule 15, CLAUDE.md workflow rule 9, project-manager DoD, plan-template block, tests.md E2E section. |
| 2026-08-02 | **Deliverables #3 (Cloudstrap.Observability.AzureMonitor) and #25 (WASM SUT + E2E harness) → ✅.** Both final 🛑 gates user-approved 2026-08-02; verified box-by-box on disk (zero unchecked boxes in either plan) and the SUT-demonstration rule confirmed for #3 (`AzureMonitorTests.cs` in the E2E suite). §3 gains a **Delivered** entry (chained `AddAzureMonitor()`, `Cloudstrap:AzureMonitor` options, per-signal exporters + guard lift, sampling policy, Entra ID auth, AC-AM9 base amendment, AC-O1 manual procedure; first `Azure.*` deps quarantined in the leaf); §25's CI risk resolved. Tally: 5 ✅ · 0 🔨 · 21 ⬜. **#4 set to 📝** — `_specs/4-ConfigKeyVaultHttpExtensions.md` in analysis with 3 Open Questions pending user answers; #4 implementation is unblocked (one-in-flight rule satisfied) as soon as its spec and plan are approved. | Roadmap rule: ✅ only when the plan's final gate is checked `[x]` and the DoD (incl. SUT demo + E2E) holds — verified before flipping; 📝 when the technical-analyst is engaged. |
| 2026-08-02 | **Reconciliation + next-deliverable decision.** #3 (Azure Monitor exporter) corrected ⬜ → **🔨**: `_plans/3-AzureMonitorExporter.md` + `_specs/3-AzureMonitorExporter.md` exist, all 7 steps `[x]`, gates 1–3 approved — only the final 🛑 gate (behavioral verification, AC-O1 procedure review, AC walk, docs review, user approval) is open; Slice-4 SUT work (`AzureMonitorTests.cs`, Bff `AzureMonitor` wiring) is uncommitted on `0-RepoScaffold`. #25 stays 🔨 (final gate: CI-green + docs review). **#4 (Cloudstrap.Extensions) marked next — entering technical analysis**: its deps (1, 2) are ✅, #3 is not among them, and no other deliverable has unfinished *steps* (both in-flight plans await only final-gate approval, so analysis of #4 can proceed without a second implementation in flight; implementation of #4 must not start before #3's final gate closes). §4 source material re-verified file-by-file: inventory expanded (`Options\AddWebOptions/UseWebOptions`, `Serialization\DictionaryTKeyEnumTValueConverter`, `Services\ServiceCollectionExtensions`, `AssemblyVisibility.cs` InternalsVisibleTo seam) + explicit read-and-route boundary (Correlation/Tracing/Logging/HealthChecks/Dynatrace → #2 done; `Scalar\*` → #5). | Deciding-next rules: reconcile before deciding; first ⬜ deliverable with all deps ✅; verify scope against the source repo pre-hand-off. |
| 2026-08-02 | **Deliverable #4 (Cloudstrap.Extensions) → ✅.** Final 🛑 gate user-approved 2026-08-02; `_plans/4-ConfigKeyVaultHttpExtensions.md` verified box-by-box on disk (zero unchecked boxes) and the SUT-demonstration rule confirmed (`ExtensionsTests.cs`, 3 new E2E tests; the SUT's probe endpoints now come from `MapCloudstrapHealthChecks`). §4 gains a **Delivered** entry: public surface (`AddCloudstrapHttpServiceClient<TI,TImpl>`, `AddCloudstrapKeyVault`/`BlobStorage`/`DataProtection`, `MapCloudstrapHealthChecks`, the five options types, and the `IAccessTokenHandlerProvider` seam for #9/#10 — zero auth deps here); the additive Core amendment `HttpClientServiceOptions.HealthCheckPath`; three new config sections; the dependency set (first non-Microsoft **runtime** dep `AspNetCore.HealthChecks.Uris` 9.0.0, Apache-2.0, + five MIT Azure SDK packages); **`CentralPackageTransitivePinningEnabled` turned on suite-wide** to force the patched `System.Security.Cryptography.Xml` 10.0.10 (five high-severity advisories in the 10.0.7 the Azure DataProtection packages resolve; NU1510 blocks a direct reference) with the nuspec-listing consequence accepted; four executor deviations accepted at gates (`AddUrlGroup` + reconciling `IConfigureOptions<HealthCheckServiceOptions>` because `UriHealthCheck` is internal → probe client always `{client}-liveness`; marker `EndpointDataSource` for idempotence; self-sufficient typed-client entry point; AC-E7 fails at `ValidateOnStart`); 282 tests green; AC-E5 (real `AddAzureKeyVault`) covered by a documented manual procedure, not automation. Tally **6 ✅ (#0–#4, #25) · 0 in flight · 20 remaining**. **#5 (Cloudstrap.WebApi) marked next**, source inventory re-verified file-by-file and §5 re-scoped accordingly. | Roadmap rule: ✅ only when the plan's final gate is checked `[x]` and the DoD (incl. SUT demo + E2E) holds. The transitive-pinning switch and the four deviations are cross-package facts, so they belong at roadmap level rather than buried in one plan. |
| 2026-08-02 | **Next-deliverable decision: #5 WebApi bootstrap** (ordering rule 1 — bottom-up: the hosting band opens as soon as Extensions is ✅; ordering rule 3 within the band — WebApi carries the most new dependencies, the first auth surface, and the pipeline shape #6/#7 inherit, so it de-risks the band). §5 source material expanded from the reference repo: `WebApi\` (4 files), `ExceptionHandlers\` (2), `Swagger\` (2), `Correlation\CorrelationMiddleware.cs` flagged as **likely superseded by #2**, plus the moved `Common\Scalar\*` and old-Core `Settings\Swagger|Scalar\*`. Two decisions escalated to the analyst: NSwag vs .NET 10's built-in `Microsoft.AspNetCore.OpenApi` (+ `Scalar.AspNetCore` UI), and collapsing `WebApiExceptionHandlerForDevTst` into one handler with an explicit option (following #1's `IsRunningInAks()` drop precedent). | Deciding-next rules: reconcile before deciding; first ⬜ deliverable with all deps ✅; verify scope against the source repo before hand-off. |
| 2026-08-02 | **`_specs/5-WebApiBootstrap.md` approved (all 3 Open Questions resolved); §5 ⬜ → 📝, planner is the next step.** Verdict tally 4 Port · 13 Redesign · 3 Replace · 10 Drop · 6 Superseded-reuse. **OQ-1**: NSwag **dropped** — the stack is built-in `Microsoft.AspNetCore.OpenApi` + `Asp.Versioning.OpenApi` 10.0.0 + `Scalar.AspNetCore` (all MIT), because the source's NSwag path is dead code (zero references; the live path already ran the stock generator + Scalar, the reference only kept dead code compiling). ⚠️ **Founding-spec amendment authorized by the user**: Package Map WebApi row "Versioning, NSwag/Scalar" → "Versioning, OpenAPI (built-in) + Scalar" — analyst applying it to `_specs/Cloudstrap.md`. **OQ-2** (auth risk area, user sign-off): `ClockSkewSeconds = 60`, `MapInboundClaims = false`, `RequireHttpsMetadata` outside Development, require-auth fallback policy on mapped controllers with `[AllowAnonymous]` + an options opt-out — all four overridable. **OQ-3**: composite `UseCloudstrapWebApi` with four hook points + a `MapControllers` switch — **the pipeline pattern #6 and #7 inherit** (forward-noted in both entries). Folded into §5 as inherited facts: the WebApi `CorrelationMiddleware` is dropped (strict subset of #2's shipped middleware); both source exception handlers were broken by construction and collapse into one RFC 9457 problem-details handler with an explicit `IncludeDetails` option; `AddNihdiWebApiProtections` registered HSTS + CORS the pipeline never activated — the new pipeline activates what it registers and "no origins → no CORS policy" replaces the `AllowAnyOrigin` trap; `UrlHelper`/`AddLegacyIssuer`, `NormalizedQueryStringApiVersionReader` and `DictionaryTKeyEnumTValueConverter` dropped as enterprise-only/superseded-by-stock. Reconciliation tally refreshed: 6 ✅ · 1 in flight (#5, 📝) · 19 ⬜. | User gate 2026-08-02. Roadmap rule: 📝 once the analyst's spec is approved; the resolved one-way doors move from "Risks (open)" to decided so the planner does not re-litigate them. |
| 2026-08-05 | **Deliverable #5 (Cloudstrap.WebApi) → ✅.** Final 🛑 gate user-approved 2026-08-03; verified box-by-box on disk 2026-08-05 (all 11 step boxes + all four final-gate boxes `[x]`) and the SUT-demonstration rule confirmed (`WebApiTests.cs` + `ScalarPageTests.cs`, 11 new E2E tests; the 17 pre-existing E2E tests pass **unchanged** against a Bff whose whole pipeline is now one `UseCloudstrapWebApi` call). AC-W1…AC-W15 + AC-ASP2 + AC-A3 met. §5 gains a **Delivered** entry: the composite `AddCloudstrapWebApi`/`UseCloudstrapWebApi` pair (four hook points + `MapControllers` switch — the shape #6 and #7 inherit, explicitly signed off at the final gate together with every Gate 1–4 executor deviation, notably the auth-middleware predicate becoming the registered **scheme map** because MVC registers the authentication core services unconditionally); `AddCloudstrapJwtBearer` (**inbound JWT validation only**, four D-2 hardened defaults, all overridable); four owned config sections (`Cloudstrap:WebApi`, `Cloudstrap:OpenApi`, `Cloudstrap:Scalar`, `Cloudstrap:JwtBearer`); per-version OpenAPI documents + Scalar UI; RFC 9457 problem details. Verification: **408 tests solution-wide green** (109 in `Cloudstrap.WebApi.Tests`, 28 E2E), zero build warnings in **Debug and Release**, `dotnet format --verify-no-changes` exit 0. **Two additive amendments to the already-shipped `Cloudstrap.Observability` (#2)** — made under the user's standing pre-release permission to break/extend delivered packages, and recorded here because **they change #2's shipped surface**: `HttpContext.GetCloudstrapCorrelationId()` + an `HttpContext.Items` stash, and a correlation-id **response-header echo** governed by the new `Cloudstrap:Correlation:Request:EchoInResponse` (default `true`) on `CorrelationRequestOptions`. Tally: **7 ✅ · 0 in flight · 18 remaining**. ⚠️ Bookkeeping discrepancy recorded, not fixed: the plan's *Gate 4* user-approval box is still `[ ]` although the work it gates completed and the approved final gate covers it — plan files belong to the planner/executor, so the roadmap notes it rather than editing it. | Roadmap rule: ✅ only when the plan's final gate is checked `[x]` **and** the DoD (incl. the SUT demo + E2E test) holds — verified on disk before flipping. The #2 amendments are cross-package facts, so they belong at roadmap level rather than buried in #5's plan. |
| 2026-08-05 | **Re-prioritisation (user-directed): #9 (Client-credentials auth) pulled ahead of #6 (Mvc), #7 (Worker) and #8 (Testing) — it is now next.** **Numbers were NOT changed; the Overview table row moved instead.** The `#` column is an identifier referenced by `_specs/<N>-…`/`_plans/<N>-…` filenames, by shipped code and docs (the `IAccessTokenHandlerProvider` failure message names the packages of #9/#10) and by every Change log entry, so renumbering would invalidate live cross-references. The convention is now explicit in the preamble: **`#` = stable identifier, Overview row order = execution order, detail sections stay sorted by `#` as a lookup index.** Rationale: both #9 and #10 have had **every dependency satisfied since #4 shipped 2026-08-02** (#1 ✅, #4 ✅), so their position behind the hosting band was a sequencing choice, not a graph constraint — the dependency analysis already recorded this (refinement 2, "auth needs only Core, not hosting"). The user wants token acquisition next. §6/§7/§8 marked deferred (dependencies unchanged, all still ✅); §10's dependency cell updated to note the Duende ATM patterns #9 de-risks. §9 re-scoped with a verified source inventory and a full **technical-analyst hand-off brief**: the OAuth package is **one 45-line file** whose real logic sits in two unreadable internal packages, so #9 is a **rebuild against an observed contract** reconstructed from in-repo call sites (Common HttpClient, Proxy, WebApi, OpenIdConnect, BlazorServer), not a textual port; the `IAccessTokenHandlerProvider` + `AccessTokenHandlerWiring` seam shipped by #4 is the socket to fill, and `HttpClientServiceOptions.AddClientAccessToken`/`TokenRequestParameters` already exist in #1 — so a consumer already setting the flag must get tokens with **no code change**; the **#5-shipped JWT surface is validation only**, stated plainly so the analyst does not re-litigate it; six Open Questions surfaced, chief among them **where the client secret comes from** (#1 dropped `AppRegistrationConfiguration` outright, so no Cloudstrap type models credentials), the **#9/#10 line through the shared `Settings\Security\` material**, and whether #9 needs its own IdP integration test or stays on locally-issued tokens (founding **AC-A1** parks the Keycloak-container test in #10). | User decision 2026-08-05. Ordering rule 3 (risk early) independently supports it: #9 is the first Duende ATM usage and de-risks #10, #12 and #17, and it is the first deliverable that *acquires and holds* credentials rather than only validating inbound tokens. |
| 2026-07-25 | Hosting posture adopted (user-directed): supported matrix is **Azure Web Apps + containers/Kubernetes** (cloud-native) — on-prem IIS/VM hosting added to the founding spec's Non-Goals + Decisions Made. `_specs/1-CoreSettingsModel.md` amended post-approval: `IsRunningInAks()` verdict Redesign → **Drop** (no `CloudstrapEnvironment` helper ships); later packages expose explicit options wherever the source branched on the environment. technical-analyst instructions gain the matching drop-heuristic for legacy-hosting accommodations. | The source's K8s check was a cloud-AKS-vs-on-prem proxy that mis-classifies Azure Web Apps (no `KUBERNETES_SERVICE_HOST`); explicit, overridable options beat un-overridable environment sniffing. |
