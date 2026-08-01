# Plan: 25 — WasmTestProject SUT + E2E Demonstration Harness

## Overview

Create the Blazor WASM system-under-test (`src/Test/WasmTestProject/`, modeled on the source repo's
`Test\WasmTestProject` but rebuilt neutral and trimmed to what Cloudstrap has actually shipped), plus a
.NET Playwright E2E test project that boots the real app and proves features through the browser.
Demonstrate the two already-delivered packages (**Cloudstrap.Core** #1, **Cloudstrap.Observability** #2)
in it, and institutionalize the process: **every future deliverable must end with a "demonstrate in the
SUT + E2E test" slice** (docs updates to planner agent, plan template, project-manager agent, CLAUDE.md,
ROADMAP.md, tests instructions).

Reference patterns studied: source `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\Test\WasmTestProject`
(project layout, Bff/Wasm host split, Program.cs bootstrap order), existing `src/Test/UnitTest/*` projects
(NUnit 4 on MTP conventions), `src/Test/Directory.Build.props`, `.github/workflows/ci.yml`,
`src/Cloudstrap.Observability` + `src/Cloudstrap.Core` public surfaces (verified by reading the code).

## Context & decisions (review these at plan approval)

1. **Trimmed layout, not a wholesale copy** (roadmap §13 already mandates "rebuild neutral"). The source's
   clean-architecture ceremony (Core.Domain/Application/Persistence/Infrastructure, Scrutor, FluentValidation,
   LanguageExt, EF + SQL Server) demonstrates nothing about Cloudstrap and drags in unported dependencies.
   Initial layout — four SUT projects + one E2E test project:
   ```
   src/Test/WasmTestProject/
   ├── src/
   │   ├── Contracts/Cloudstrap.WasmTestProject.Contracts/          # DTOs (no references)
   │   ├── Presentation/Cloudstrap.WasmTestProject.Presentation/    # Razor Class Library, MudBlazor
   │   ├── Host/Wasm/Cloudstrap.WasmTestProject.Host.Wasm/          # Blazor WebAssembly client
   │   └── Host/Bff/Cloudstrap.WasmTestProject.Host.Bff/            # ASP.NET Core server: serves WASM + API
   └── test/Cloudstrap.WasmTestProject.E2E.Tests/                   # NUnit 4 + Microsoft.Playwright
   ```
   Later deliverables re-add layers when their demo needs them (EF/SQL with #4's health-check demo or #16,
   Refit clients + BFF auth with #13, etc.).
2. **No auth / no STS yet.** Source pages carry `[Authorize]` against a Keycloak-style test STS; auth is
   deliverables 9/10/13. All SUT pages and APIs are anonymous until those deliverables add their demo slices.
3. **In-memory data** (`InMemoryDoctorStore` singleton) — no EF, no SQL Server, no database setup for CI.
4. **Plain MudBlazor** (spec decision: internal design system → MudBlazor), default theme.
5. **Plain `HttpClient`** on the WASM client (`builder.HostEnvironment.BaseAddress`); Refit + cookie/XSRF
   handlers arrive with deliverable 13's demo slice.
6. **E2E = real app + real browser**: an NUnit fixture launches the Bff host via
   `dotnet run --no-build` on `http://127.0.0.1:5300` (http only — no dev-cert friction in CI; overridable
   via `CLOUDSTRAP_E2E_BASEURL`), captures stdout (for console-telemetry assertions), and Playwright drives
   headless Chromium. API-level assertions use plain `HttpClient` against the same instance. The suite is
   non-parallel (one app instance). Browser install is a documented one-time step
   (`playwright.ps1 install chromium`); a missing browser fails loudly with the install instruction —
   never silently skips.
7. **E2E project participates in the standard test leg**: named `*.Tests.csproj`, so CI's MTP glob and the
   local `runTests` convention pick it up automatically. CI gains one Playwright browser-install step.
8. **`src/Test/Directory.Build.props` must be made SUT-aware**: today it forces `OutputType=Exe`,
   `EnableNUnitRunner`, and NUnit references on *every* project under `src/Test/` — that breaks class
   libraries and Blazor projects. Condition the test-runner wiring on `$(MSBuildProjectName)` ending with
   `.Tests`; keep `IsPackable=false` + doc-rule relaxations for the whole tree.
9. **Roadmap**: new deliverable **#25** (this plan); deliverable #13's scope note shrinks to "BlazorWasm
   helpers + auth demo in the existing SUT". Numbering is identity, not chronology — #25 executes now.
10. **Host naming (user-directed at Gate 1)**: the source repo's committee-named `Cfe` ("Client For
    Frontend") host is named **`Bff`** here — Backend for Frontend, the industry-standard pattern name
    for a server that hosts the SPA assets, its API, and (from deliverable 13) the cookie-auth/token
    handling. `Server` was rejected to avoid confusion with the Blazor **Server** SUT (deliverable 12).
11. **Source-repo workarounds carried over only if hit**: `DisableBuildCompression=true` (SDK 10.0.1xx
    `ApplyCompressionNegotiation` bug with fingerprinted RCL static assets) and
    `BlazorWebAssemblyLoadAllGlobalizationData` (culture switching — not needed until #24). Apply the first
    only if the build actually fails; skip the second.

## New dependencies (all test-only — nothing ships in a package; CPM-pinned in `src/Directory.Packages.props`)

| Package | License | Used by |
|---|---|---|
| `MudBlazor` | MIT | Presentation, Host.Wasm |
| `Microsoft.AspNetCore.Components.WebAssembly` (+ `.Server`, `.Web`) | MIT | Host.Wasm / Host.Bff / Presentation |
| `Microsoft.Playwright` | Apache-2.0 | E2E.Tests |

Rule-4 review happens at Gate 1. `Moq` is not needed (E2E mocks nothing).

## Out of scope (each arrives with its owning deliverable's demo slice)

Auth/STS (9/10), Refit + BFF cookie auth (13), NServiceBus→Messaging page (14), Dossier/blob upload (4/15),
Hangfire + dashboard (16/18), Ops dashboard (19/20), localization + CultureSelector (24), Matomo (22),
CookieConsent (21), EF/SQL persistence (first DB-needing demo). The Blazor **Server** SUT (TestProject)
stays with deliverable 12.

---

## Step 1 — Home page renders end-to-end (SUT skeleton + E2E harness)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/Directory.Packages.props` *(modify — add MudBlazor, Microsoft.AspNetCore.Components.*, Microsoft.Playwright pins)*
- `src/Test/Directory.Build.props` *(modify — condition `OutputType`/`EnableNUnitRunner`/NUnit refs on `$(MSBuildProjectName.EndsWith('.Tests'))`)*
- `src/Cloudstrap.sln` *(modify — add the five projects under a `WasmTestProject` solution folder)*
- `src/Test/WasmTestProject/src/Contracts/Cloudstrap.WasmTestProject.Contracts/Cloudstrap.WasmTestProject.Contracts.csproj` *(create — empty DTO library for now)*
- `src/Test/WasmTestProject/src/Presentation/Cloudstrap.WasmTestProject.Presentation/` *(create — `Microsoft.NET.Sdk.Razor`: `App.razor`, `Routes.razor`, `_Imports.razor`, `Shared/MainLayout.razor` (MudBlazor providers + nav), `Home/Index.razor` with route `/` and heading "Welcome to the Cloudstrap WASM Test Project")*
- `src/Test/WasmTestProject/src/Host/Wasm/Cloudstrap.WasmTestProject.Host.Wasm/` *(create — `Microsoft.NET.Sdk.BlazorWebAssembly`: `Program.cs` (`WebAssemblyHostBuilder.CreateDefault` + `AddMudServices`), `wwwroot/index.html`, `wwwroot/appsettings.json`)*
- `src/Test/WasmTestProject/src/Host/Bff/Cloudstrap.WasmTestProject.Host.Bff/` *(create — `Microsoft.NET.Sdk.Web`: `Program.cs` (serve WASM: `UseBlazorFrameworkFiles`, `UseStaticFiles`, `MapFallbackToFile("index.html")`), `appsettings.json`, `Properties/launchSettings.json` (http 5300 profile for E2E parity, https 7200 for manual runs))*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/` *(create — csproj (NUnit wiring inherited, + `Microsoft.Playwright`), `Infrastructure/SutProcess.cs`, `Infrastructure/E2eFixture.cs` (`[SetUpFixture]`: start Bff, poll `/` until 200 with 60 s timeout, expose captured stdout; `[OneTimeTearDown]` kill process tree), `Infrastructure/PageTestBase.cs` (Playwright browser/context lifecycle), `HomePageTests.cs`, `AssemblyInfo.cs` (`[assembly: NonParallelizable]`))*
- `src/Test/WasmTestProject/README.md` *(create — layout, how to run the SUT, one-time `playwright.ps1 install chromium`, port map, what each page demonstrates)*

**RED** *(write these tests first, run them, confirm they fail before writing production code)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/HomePageTests.cs`
- E2E test method: `HomePage_Loads_ShowsWelcomeHeadingAndNoConsoleErrors` (navigate `/`, assert heading text, assert zero JS console errors)
- Failing-run command: `src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "HomePage_Loads_ShowsWelcomeHeadingAndNoConsoleErrors"` *(fails: Bff host does not exist yet, fixture launch times out)*
- Build the E2E project + harness first so RED is runnable; the SUT projects are the GREEN.

**GREEN** *(minimal production code across all necessary layers to make RED pass)*:
- The four SUT projects listed in Scope, wired so `dotnet run --project src/Test/WasmTestProject/src/Host/Bff` serves the WASM home page on `http://127.0.0.1:5300`. No Cloudstrap packages referenced yet — this step is pure skeleton + harness (Cloudstrap wiring is Steps 3–5 so each feature demo is its own observable slice).
- `SutProcess`: resolve repo root by walking up from `AppContext.BaseDirectory` to the directory containing `src/Cloudstrap.sln`; launch `dotnet run --no-build -c <config>` (config read from the E2E assembly's `AssemblyConfigurationAttribute`) with `ASPNETCORE_URLS=http://127.0.0.1:5300`; honor `CLOUDSTRAP_E2E_BASEURL` to attach to an already-running app instead.

**DB changes**: None.

**VERIFY** *(after making GREEN changes, run these checks; when all green, mark this step's `Done` checkbox and continue straight to the next step — stop only when the next plan item is a 🛑 HUMAN GATE)*: build + all tests + code analysis + format — all green (`dotnet build src/Cloudstrap.sln` · `runTests` — now includes the E2E exe · `dotnet format src/Cloudstrap.sln --verify-no-changes`). Existing unit suites (52 + 91) unaffected.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — CI runs the E2E suite ⚠️ *(Risk Area: CI/publish pipeline)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `.github/workflows/ci.yml` *(modify — insert one step between "Build" and "Run all MTP test executables": install Playwright Chromium via the built E2E project's script, e.g. `pwsh src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium`)*

**RED**: not a code test — the automated test IS the existing E2E suite executing in CI. Local RED-equivalent: run the Release-built E2E exe on a machine without browsers → fails with Playwright's install message, proving the install step is load-bearing.

**GREEN**: the one-step `ci.yml` addition. No test-runner glob changes needed — `Cloudstrap.WasmTestProject.E2E.Tests.csproj` already matches `src/Test/**/*.Tests.csproj`.

**DB changes**: None.

**VERIFY**: local: Release build + Release E2E exe passes after `playwright.ps1 install chromium`. CI proof (green `ci.yml` run including the E2E exe) lands at Gate 1 — pushing the branch requires user confirmation per the Git rules, so it is part of the gate, not of this step.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: SUT skeleton + E2E harness *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

- [x] Behavioral verification: `HomePage_Loads_ShowsWelcomeHeadingAndNoConsoleErrors` passes locally; `runTests` runs 3 suites (Core 52, Observability 91, E2E 1+); user optionally runs the app manually (`dotnet run --project src/Test/WasmTestProject/src/Host/Bff`, browse `https://localhost:7200`).
- [x] Code review: project layout vs decision 1; `src/Test/Directory.Build.props` conditioning doesn't regress the two unit-test projects; new CPM pins (rule-4 dependency review: MudBlazor MIT, Playwright Apache-2.0, test-only); harness design (fixed port, stdout capture, no silent skips). *(Gate accepted 2026-07-30; host renamed `Cfe` → `Bff` at this gate — decision 10.)*
- [x] User approves pushing the branch so the modified `ci.yml` (⚠️ Step 2) proves the E2E leg in CI; CI run is green including the E2E exe. *(Deferred: work not yet committed/pushed — user pushes when ready; CI-green to be re-verified at the final gate.)*
- [x] User approved — implementation may continue past this gate *(2026-07-30)*

---

## Step 3 — Diagnostics page shows live Cloudstrap.Core options; broken config fails fast

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Host/Bff/Cloudstrap.WasmTestProject.Host.Bff.csproj` *(modify — ProjectReference `Cloudstrap.Core`)*
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify — eager `builder.Configuration.GetCloudstrapOptions()` fail-fast + `builder.Services.AddCloudstrapCore(...)`; map controllers)*
- `src/Test/WasmTestProject/src/Host/Bff/appsettings.json` *(modify — full valid `Cloudstrap:` section: `Application` (`SystemName: "wasmtestproject"`, `SubsystemName: "application"`, `SubsystemType: "cfe"`, environment tier), `Logging`, `OpenTelemetry` (`Mode: "Console"`), `Correlation`, `HealthChecks` — exact members per the `Cloudstrap.Core` README)*
- `src/Test/WasmTestProject/src/Contracts/.../DiagnosticsDto.cs` *(create — safe subset: system/subsystem/type, environment tier, OTel mode, correlation header name)*
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/DiagnosticsController.cs` *(create — `GET api/diagnostics/options` returns `DiagnosticsDto` from the bound `CloudstrapOptions`)*
- `src/Test/WasmTestProject/src/Presentation/.../Diagnostics/DiagnosticsPage.razor(.cs)` *(create — route `/diagnostics`, fetches `api/diagnostics/options`, renders the values; nav link in `MainLayout`)*
- `src/Test/WasmTestProject/src/Host/Wasm/wwwroot/appsettings.json` *(modify — minimal valid client-side `Cloudstrap:Application` section)*
- `src/Test/WasmTestProject/src/Host/Wasm/Program.cs` + `Presentation/Shared/MainLayout.razor` *(modify — Host.Wasm references `Cloudstrap.Core` (host-agnostic, WASM-loadable — proves the deliverable-1 claim); client binds `ApplicationOptions` and the layout header renders a badge from the **client-side** bound value, visually distinct from the server values on the diagnostics page)*
- `src/Test/WasmTestProject/test/.../DiagnosticsTests.cs` *(create)*

**RED**:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/DiagnosticsTests.cs`
- E2E test methods:
  - `DiagnosticsPage_Loads_ShowsServerBoundCloudstrapOptions` (Playwright: values match `appsettings.json` — system name, OTel mode `Console`, correlation header name)
  - `Header_ShowsClientSideBoundApplicationOptions` (Playwright: badge text proves WASM-side binding)
  - `Startup_MissingSystemName_FailsFastWithValidationError` (launch a second short-lived Bff process with `Cloudstrap__Application__SystemName=""` → non-zero exit, output contains the `ConfigurationValidationException` failure naming `SystemName`)
- Failing-run command: `...\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "Diagnostics"` *(fails: endpoint/page don't exist)*

**GREEN**: items in Scope. Demonstrates deliverable #1's headline behaviors: `Cloudstrap:` binding via `AddCloudstrapCore`/`GetCloudstrapOptions`, eager fail-fast validation, WASM-loadability.

**DB changes**: None.

**VERIFY**: build + all tests (incl. new E2E) + code analysis + format — all green.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: Cloudstrap.Core demonstrated *(covers Step 3)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

- [x] Behavioral verification: the three `DiagnosticsTests` methods pass; user optionally browses `/diagnostics` and sees the bound values; breaking `SystemName` locally aborts startup with the validation message.
- [x] Code review: diagnostics endpoint exposes only a safe subset (no secrets pattern established for future demos); client vs server binding clearly distinguishable in the UI. *(Gate accepted 2026-07-30; `.vscode/launch.json` + `tasks.json` added at this gate on user request — F5 debugs the Bff, `blazorwasm` config debugs the client.)*
- [x] User approved — implementation may continue past this gate *(2026-07-30)*

---

## Step 4 — Health endpoints live; requests are correlated (observable over HTTP)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Host/Bff/Cloudstrap.WasmTestProject.Host.Bff.csproj` *(modify — ProjectReference `Cloudstrap.Observability`)*
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify — `CloudstrapBootstrapLogger` startup/fatal pattern per the Observability README; `builder.UseCloudstrapObservability(...)` (owner mode, Console); `AddCloudstrapCorrelation()`; `AddHealthChecks()` tagged with `CloudstrapHealthCheckTags.Live`/`Ready`; `app.UseCloudstrapCorrelation()` before endpoints; `MapHealthChecks("/healthz", live-only)` + `MapHealthChecks("/ready")`)*
- `src/Test/WasmTestProject/test/.../HealthAndCorrelationTests.cs` *(create — API-level via `HttpClient` against the fixture's base URL)*

**RED**:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/HealthAndCorrelationTests.cs`
- E2E test methods *(amended during READ: the middleware establishes the **ambient** correlation id via
  `ICorrelationContextAccessor` and deliberately writes no response header — so correlation is asserted
  through a new `GET api/diagnostics/correlation` endpoint returning the ambient id)*:
  - `Healthz_Get_Returns200Healthy` · `Ready_Get_Returns200`
  - `ApiRequest_WithCorrelationHeader_AmbientCorrelationEchoesIt` (send `X-Correlation-ID: <guid>`, assert the returned ambient id equals it)
  - `ApiRequest_WithoutCorrelationHeader_AmbientCorrelationIsGenerated` (non-empty, and differs between two calls)
- Failing-run command: `...\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "HealthAndCorrelation"` *(fails: 404s)*

**GREEN**: items in Scope. Demonstrates deliverable #2's hosting side: observability pipeline boot (Console mode), bootstrap logger, correlation middleware with the de-NIHDI'd `X-Correlation-ID` default, health-tag contract on the stock `IHealthChecksBuilder` (Aspire-additive posture).

**DB changes**: None.

**VERIFY**: build + all tests + code analysis + format — all green.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 5 — Doctors round-trip with a business trace visible in console telemetry

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Contracts/.../DoctorDto.cs`, `AddDoctorDto.cs` *(create)*
- `src/Test/WasmTestProject/src/Host/Bff/Services/InMemoryDoctorStore.cs` *(create — singleton, seeded with 3 doctors)*
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/DoctorController.cs` *(create — `GET`/`POST api/doctor`; POST wraps the operation in an `IBusinessTrace` scope named `"AddDoctor"` with the doctor's name as a tag)*
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify — `AddCloudstrapBusinessTrace()`)*
- `src/Test/WasmTestProject/src/Presentation/.../Doctors/DoctorsPage.razor(.cs)` *(create — route `/doctors`: MudBlazor grid loading from `api/doctor`, add-doctor form posting to it; nav link)*
- `src/Test/WasmTestProject/test/.../DoctorsTests.cs` *(create)*

**RED**:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/DoctorsTests.cs`
- E2E test methods:
  - `DoctorsPage_Loads_ShowsSeededDoctors` (Playwright: grid rows = seeded names, spinner gone, no console errors)
  - `DoctorsPage_AddDoctor_NewDoctorAppearsInGrid` (Playwright: fill form, submit, row appears)
  - `AddDoctor_EmitsBusinessTraceInConsoleTelemetry` (after the POST, poll the fixture's captured Bff stdout — Console OTel exporter — up to ~15 s for the `AddDoctor` activity)
- Failing-run command: `...\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "Doctors"` *(fails: page/API don't exist)*

**GREEN**: items in Scope. Demonstrates `IBusinessTrace`/`IBusinessTraceScope` producing real telemetry in a running app — the full client→API→trace chain, correlated end to end.

**DB changes**: None.

**VERIFY**: build + all tests + code analysis + format — all green.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: Cloudstrap.Observability demonstrated *(covers Steps 4–5)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

- [x] Behavioral verification: all `HealthAndCorrelationTests` + `DoctorsTests` pass; user optionally runs the app, adds a doctor, and sees the `AddDoctor` activity + correlation ID in the console output.
- [x] Code review: Program.cs bootstrap order matches the Observability README pattern (bootstrap logger → `UseCloudstrapObservability` → correlation middleware before endpoints; ordered flush on shutdown); stdout-polling test is retry-based, not sleep-flaky. *(Gate accepted 2026-08-01. Post-gate fix folded in: `Cloudstrap:Logging:LevelOverrides` restores `Microsoft.Hosting.Lifetime` to Information so the F5 `serverReadyAction` browser-open works — consumer override beats the framework seeds, itself a feature demo.)*
- [x] User approved — implementation may continue past this gate *(2026-08-01, "verified and accepted")*

---

## Step 6 — Process change: every future deliverable must demonstrate in the SUT with an E2E test

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

*(Docs-only step — no unit test is possible; verification is grep-based checks + the final gate review, per the manual-verification allowance for non-code steps.)*

**Scope**:
- `.claude/agents/planner.md` *(modify — new planning rule: every extraction-deliverable plan MUST end with a **Demonstration slice** — extend `src/Test/WasmTestProject` to exercise the deliverable's headline behavior and add ≥ 1 E2E test in `Cloudstrap.WasmTestProject.E2E.Tests` proving it through the running app; the deliverable's final 🛑 gate covers the demo. Interview item 9 becomes mandatory ("which SUT page/endpoint demonstrates this, and what does the E2E test assert?"); self-check gains item 11: "Extraction plans end with a demonstration slice + E2E test.")*
- `.claude/templates/plan-template.md` *(modify — add a Demonstration-slice comment block + a skeleton demo step referencing the E2E harness and its failing-run command form)*
- `.claude/agents/project-manager.md` *(modify — deliverable definition-of-done template gains "demonstrated in the WASM SUT with a passing E2E test"; roadmap ✅ flips require verifying the demo exists)*
- `CLAUDE.md` *(modify — Mandatory Workflow Rules: new rule "Every migrated feature is demonstrated in the WASM SUT (`src/Test/WasmTestProject`) with at least one E2E test before its final 🛑 gate"; Commands: Playwright one-time install + E2E filtered-run examples; Project Structure comment for `Test/WasmTestProject` updated to reflect it now exists with the E2E project)*
- `_plans/ROADMAP.md` *(modify — preamble standing rule (DoD of every deliverable implicitly includes SUT demo + E2E); Overview row + details section for **#25** (this deliverable); §13 scope note: SUT already exists, 13 adds BlazorWasm helpers/auth/Refit demos to it; change-log entry. Roadmap is project-manager-owned — apply via that agent or note the main agent applied it on user instruction.)*
- `.claude/instructions/tests.md` *(modify — new "E2E tests (WASM SUT)" section: harness usage (`E2eFixture`, `PageTestBase`, stdout assertions), naming conventions, ports, when to run, no-silent-skip rule)*

**RED**: N/A (docs). Check-equivalent: `Select-String` for the new rule text in each file returns a match (run before edits → no match; after → match).

**GREEN**: the six document edits above, written against the *as-built* harness (real paths, real commands from Steps 1–5).

**DB changes**: None.

**VERIFY**: build + all tests + format still green (docs don't affect them); each Scope file contains its new section (grep checks); no contradiction left with the old "E2E lands at deliverable 12/13" wording in CLAUDE.md/roadmap.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: process institutionalized *(covers Step 6; closes the deliverable)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

- [ ] Behavioral verification: full suite green end to end (`dotnet build` · `runTests` incl. E2E · `dotnet format --verify-no-changes`); CI green on the branch.
- [ ] Docs review: user reads the six updated documents and confirms the demonstration rule is stated where future planning actually happens (planner agent + template + CLAUDE.md workflow rules) and the roadmap reflects #25 ✅ + amended #13.
- [ ] User approved — deliverable #25 complete; project-manager flips the roadmap row to ✅.
