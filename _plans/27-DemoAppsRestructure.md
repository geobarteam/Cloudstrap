# Plan: 27 — Demo Applications Restructure (`src/demo`)

## Overview

Execute `_specs/27-DemoAppsRestructure.md` (approved, Decision Log D-A…D-F, zero Open Questions):
move/split the WASM SUT (`src/Test/WasmTestProject`) into consumer-facing demo applications under
`src/demo` (BlazorWasm Bff/Client/Presentation, Mvc, shared IdP host, shared Contracts), build the
**new** `Cloudstrap.Demo.Api` host (port 5330, the folded-in increment-3 DownstreamApi) and the
**new** minimal `Cloudstrap.Demo.BlazorServer` app (port 5340, D-B: stock Blazor Server + shipped
packages only), relocate the E2E suite to `src/Test/E2E/Cloudstrap.Demo.E2E.Tests` (D-C), rename
everything to `Cloudstrap.Demo.*` (D-A — **move and rename in separate commits** for AC-DR10
history preservation), and update the standing-rule process docs (AC-DR12). Nothing ships to NuGet;
no shipped `Cloudstrap.*` package changes (defects route as separate RED-first bugfixes).

**Reference patterns studied (read this session)**: the SUT itself across all layers
(`Host/Bff/Program.cs` + controllers + `appsettings.json`, `Host/IdentityProvider/Program.cs` +
`TestIdentityProviderSeed.cs`, `Host/Mvc`, Contracts DTOs), the E2E harness (`E2eFixture.cs`,
`Infrastructure/SutProcess.cs` incl. the `projectRelativePath` boot-by-path seam,
`PageTestBase.cs`, `MvcHostTests.cs` / `SelfHostedIdentityProviderTests.cs` as the
boot-a-second-host precedent), `src/Cloudstrap.sln` (verified duplicate `Host.Mvc` NestedProjects
entry, lines 403+408), `src/Test/Directory.Build.props`, `.github/workflows/ci.yml`,
`.vscode/tasks.json` + `launch.json`, `src/Directory.Packages.props` (comments lines 33/100), and
`_plans/25-WasmTestProjectSut.md` (plan precedent).

## Context & decisions (review these at plan approval)

1. **The E2E suite is the regression net and never goes dark**: every step ends with the full
   suite (9 unit exes + the E2E exe) green from the then-current locations; every gate re-confirms
   it. The spec's breakage checklist items are distributed as VERIFY items across Steps 1–2 and
   re-walked wholesale at the final gate (AC-DR11).
2. **Move ≠ rename (AC-DR10)**: Step 1 is commit A — `git mv` with **original project names** plus
   every path fix needed for green; Step 2 is commit B — the `Cloudstrap.Demo.*` rename. Gates 1
   and 2 are the commit checkpoints; the user commits (or approves the commit) at each gate so the
   two commits stay separate and rename detection stays above the similarity threshold.
3. **Mechanical steps have no invented RED** (user-directed): for Steps 1–2 the "test" is the
   existing 60-test E2E suite + 9 unit suites passing from the new locations/names — stated
   explicitly in those steps. RED-first applies where behavior is created: the Api host (Step 3),
   the retargeted `UserApi` cross-process hop (Step 4), the BlazorServer app (Step 5).
4. **Identifier hygiene rides with the rename (D-A)**: Step 2 also renames the demo-local strings
   `wasmtestproject-bff/-web/-selfapi`, `SystemName: wasmtestproject`, and the
   `WasmTestProject:ApplicationBaseAddresses` config key to `demo-*` / `demo` /
   `Demo:ApplicationBaseAddresses`. These live only in the IdP seed, demo appsettings, and E2E
   assertions — nothing shipped — but the seed is auth-adjacent, so Gate 2 reviews the seed diff.
5. **New IdP surface is gated (⚠️ auth risk area)**: Step 3 adds the `demo-api` audience to the
   web client's tokens; Step 5 adds the new interactive client `demo-blazorserver`. Gates 3 and 4
   carry the explicit auth review.
6. **Fixture boot order becomes IdP → Api → Bff** (spec Behaviors): the Api host is fixture-owned
   like the IdP, booted in attach mode too, readiness-polled on `/healthz` before the Bff starts
   (the Bff's `/ready` will depend on it once `UserApi:EnableHealthCheck` is on).
7. **Out of scope** (restated from the spec): increment 2 authorization demo (D-E — own follow-up
   plan), that spec's ⚠️ OQ-2 (`RoleClaimType`/`NameClaimType` — keeps its own user gate), #12/#13
   helper code (demos use shipped packages only, AC-DR13), `Cloudstrap.TestIdentityProvider` stays
   at `src/Test/TestIdentityProvider/` (D-D), the ROADMAP preamble update (project-manager applies
   it at ✅-flip), NuGet packaging of anything under `src/demo`.
8. **No new NuGet dependencies** — the already-CPM-pinned set only; the BlazorServer app uses the
   shared framework + existing pins (spec Dependencies).
9. **Test commands**: `runTests` is not on the agent PATH — VERIFY invokes each test exe directly.
   The **full-suite check** referenced by every step means: `dotnet build src/Cloudstrap.sln`, then
   the 9 unit exes under `src/Test/UnitTest/<Name>.Tests/bin/Debug/net10.0/<Name>.Tests.exe`
   (Core, Observability, Observability.AzureMonitor, Extensions, WebApi, Mvc,
   TestIdentityProvider, Authentication.ClientCredentials, Authentication.OpenIdConnect), then the
   E2E exe at its then-current path (Step 1:
   `src\Test\E2E\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe`;
   Step 2 onward:
   `src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe`), then
   `dotnet format src/Cloudstrap.sln --verify-no-changes`.

## Port map after this plan

5300 Bff (+5301–5304 second-instance tests) · 5310 shared IdP (fixture-owned in E2E) · 5311
IdP-host test instance · 5320 Mvc · **5330 Api (new)** · **5340 BlazorServer (new)** · 59999
dead-port test. *(Deliberate divergence from the older increment-3 spec, which allocated 5320 —
taken by #6's MVC host since; recorded in the spec's Deliberate Behavior Changes.)*

---

## Step 1 — Demo tree runs from `src/demo` (move commit A, original project names) ⚠️ *(Risk Area: broad breakage surface — sln, build props, CI, hardcoded paths)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `git mv` (history-preserving; original project/file names kept):
  - `src/Test/WasmTestProject/src/Contracts` → `src/demo/Shared/Contracts`
  - `src/Test/WasmTestProject/src/Host/IdentityProvider` → `src/demo/Shared/IdentityProvider`
  - `src/Test/WasmTestProject/src/Host/Bff` → `src/demo/BlazorWasm/Bff`
  - `src/Test/WasmTestProject/src/Host/Wasm` → `src/demo/BlazorWasm/Client`
  - `src/Test/WasmTestProject/src/Presentation` → `src/demo/BlazorWasm/Presentation`
  - `src/Test/WasmTestProject/src/Host/Mvc` → `src/demo/Mvc`
  - `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests` → `src/Test/E2E/Cloudstrap.WasmTestProject.E2E.Tests` (D-C)
  - `src/Test/WasmTestProject/README.md` → `src/demo/README.md` *(interim carry — rewritten in Step 6)*
- `src/demo/Directory.Build.props` *(create — V-11: import the parent props via
  `GetPathOfFileAbove` like `src/Test/Directory.Build.props` does; `IsPackable=false`,
  `GenerateDocumentationFile=false`, `NoWarn CS1591;CA1848;CA1873`; **no** MTP block — no `.Tests`
  projects live under `src/demo`)*
- Every moved `.csproj` *(modify — repair `ProjectReference` relative paths for the new tree
  depths: Bff → Contracts/Presentation/Client + shipped packages; Client/Presentation → Contracts;
  E2E → Bff/IdentityProvider/TestIdentityProvider/Contracts as currently referenced)*
- `src/Cloudstrap.sln` *(modify — rewrite the 7 moved project paths; replace the `WasmTestProject`
  solution-folder chain (`WasmTestProject`/`src`/`Host`/`Mvc` folders `{8C09616C…}`,
  `{685D38E4…}`, `{24B55F2A…}`, `{953232A8…}`) with a `demo` folder tree mirroring
  `src/demo` (`Shared`, `Mvc`, `BlazorWasm`) plus an `E2E` folder under `Test`; **remove the
  duplicate `Host.Mvc` NestedProjects entry** (`{DED7BF91-4989-4FC5-B917-B742644533AF}` is nested
  twice today, lines 403 + 408))*
- `.github/workflows/ci.yml` *(modify — line 47 `playwright.ps1` path →
  `src/Test/E2E/Cloudstrap.WasmTestProject.E2E.Tests/bin/Release/net10.0/playwright.ps1`; line 44
  comment path; the glob `src/Test/**/*.Tests.csproj` (line 54) and the no-tests guard (line 66)
  still hold — verify, don't change)*
- `src/Test/E2E/Cloudstrap.WasmTestProject.E2E.Tests/Infrastructure/SutProcess.cs` *(modify —
  `_bffProjectPath` (line 15) → `src/demo/BlazorWasm/Bff/Cloudstrap.WasmTestProject.Host.Bff.csproj`)*
- `.../Infrastructure/PageTestBase.cs` *(modify — install-instruction string (line 37) → the new
  `src/Test/E2E/...` path)*
- `.../MvcHostTests.cs` *(modify — project path constant (line 21) →
  `src/demo/Mvc/Cloudstrap.WasmTestProject.Host.Mvc.csproj`)*
- `.../SelfHostedIdentityProviderTests.cs` *(modify — project path constant (line 21) →
  `src/demo/Shared/IdentityProvider/Cloudstrap.WasmTestProject.Host.IdentityProvider.csproj`)*
- `.vscode/tasks.json` *(modify — both build-task csproj paths)* · `.vscode/launch.json` *(modify —
  the 3 configurations' `program`/`cwd` paths; the compound is unchanged for now)*
- `src/Directory.Packages.props` *(modify — line 100 comment "SUT + E2E only
  (src/Test/WasmTestProject)" → the demo/E2E wording; line 33 comment re-verified accurate —
  the TestIdentityProvider library does **not** move (D-D))*

**RED** *(mechanical move — no new behavior, no invented failing test; user-directed)*:
- The regression net **is** the RED/GREEN signal: before the path fixes the solution does not even
  load; the step is GREEN when the **entire pre-existing suite** (9 unit exes + the 60-test E2E
  suite) passes from the new locations with **unchanged behavioral assertions** (AC-DR1).
- First-run command (expected to fail until all Scope fixes land): `dotnet build src/Cloudstrap.sln`

**GREEN** *(minimal changes to make the suite pass from the new tree)*:
- The moves + path fixes listed in Scope — nothing else. No namespace, project-name, port,
  config-key, or behavior change in this step (that is Step 2 / Slices 3–4).

**DB changes**: None.

**VERIFY** *(after making GREEN changes, run these checks; when all green, mark this step's `Done` checkbox and continue straight to the next step — stop only when the next plan item is a 🛑 HUMAN GATE)*:
- Full-suite check (Context §9) — all green; E2E exe runs from `src/Test/E2E/...` (the
  `.Tests`-suffix MTP wiring in `src/Test/Directory.Build.props` still applies — breakage-checklist row 2).
- `src/demo/Directory.Build.props` in effect: `dotnet build` a demo project in Release produces
  **no** `.nupkg` (AC-DR14).
- Stale-path sweep of the build-critical set:
  `Select-String -Path src/Cloudstrap.sln,.github/workflows/ci.yml,.vscode/*.json,src/Directory.Packages.props -Pattern 'Test[/\\]WasmTestProject'`
  returns nothing.
- sln loads with the `demo` folder tree and **no** duplicate NestedProjects entry.
- `dotnet run --project src/demo/Mvc` and `--project src/demo/Shared/IdentityProvider` boot
  standalone (AC-DR3 spot check — loud-but-graceful without peers, per the existing lazy-metadata precedent).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RGR cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the move commit *(covers Step 1)*

*Executor: STOP here. Present the results of Step 1 and WAIT for user approval — do not start the rename.*

- [x] Behavioral verification: full suite green from the new locations (9 unit exes + E2E exe run output shown); the stale-path sweep of the build-critical set is empty; Release build packs nothing under `src/demo`.
- [x] ⚠️ Risk review (broad breakage surface): user reviews the sln rewrite (folder tree, duplicate-nesting removal), `src/demo/Directory.Build.props`, and the ci.yml diff.
- [x] **Commit A lands here** (move + path fixes, original names) — kept strictly separate from the rename so `git log --follow` traces history (AC-DR10). User confirms the commit (and optionally pushes to see CI green — AC-DR2 is re-verified at the final gate either way).
- [x] User approved — implementation may continue past this gate (2026-08-20)

---

## Step 2 — Demo apps carry the `Cloudstrap.Demo.*` identity (rename commit B, D-A)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- Project renames (csproj filename = assembly name = root namespace; `git mv` per file):
  - `Cloudstrap.WasmTestProject.Contracts` → **`Cloudstrap.Demo.Contracts`**
  - `Cloudstrap.WasmTestProject.Presentation` → **`Cloudstrap.Demo.BlazorWasm.Presentation`**
  - `Cloudstrap.WasmTestProject.Host.Wasm` → **`Cloudstrap.Demo.BlazorWasm.Client`**
  - `Cloudstrap.WasmTestProject.Host.Bff` → **`Cloudstrap.Demo.BlazorWasm.Bff`**
  - `Cloudstrap.WasmTestProject.Host.IdentityProvider` → **`Cloudstrap.Demo.IdentityProvider`**
  - `Cloudstrap.WasmTestProject.Host.Mvc` → **`Cloudstrap.Demo.Mvc`**
  - `Cloudstrap.WasmTestProject.E2E.Tests` → **`Cloudstrap.Demo.E2E.Tests`** (folder + csproj)
- Namespace sweep across all moved `.cs`/`.razor`/`_Imports.razor` files
  (`Cloudstrap.WasmTestProject.*` → `Cloudstrap.Demo.*`) incl. the E2E suite's `namespace` +
  `using` lines and `Cloudstrap.Demo.E2E.Tests.Infrastructure`.
- Demo-local identifier hygiene (Context §4 — reviewed at Gate 2):
  - `src/demo/Shared/IdentityProvider/TestIdentityProviderSeed.cs`: clients `wasmtestproject-bff` →
    `demo-bff`, `wasmtestproject-web` → `demo-web`; audience `wasmtestproject-selfapi` →
    `demo-selfapi`; placeholder secrets keep their visibly-fake shape.
  - `src/demo/BlazorWasm/Bff/appsettings.json`: `SystemName` `wasmtestproject` → `demo`,
    `JwtBearer:Audience` → `demo-selfapi`, `ClientCredentials:ClientId` → `demo-bff`,
    `OpenIdConnect:ClientId` → `demo-web`, `OpenApi:Title` → "Cloudstrap Demo Bff API".
  - `src/demo/Shared/IdentityProvider/Program.cs` + `SelfHostedIdentityProviderTests.cs`: config
    key `WasmTestProject:ApplicationBaseAddresses` → `Demo:ApplicationBaseAddresses`.
  - E2E assertions naming any of the renamed strings (clientId/audience/issuer/system-name
    assertions in `ClientCredentialsTests`, `OpenIdConnectTests`, `DiagnosticsTests`,
    `AzureMonitorTests` — mechanical value updates only, assertion logic unchanged).
- `src/Cloudstrap.sln` *(modify — the 7 renamed project names/paths)*
- `.github/workflows/ci.yml` *(modify — line 47 playwright path now
  `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/bin/Release/net10.0/playwright.ps1`)*
- `SutProcess.cs` / `PageTestBase.cs` / `MvcHostTests.cs` / `SelfHostedIdentityProviderTests.cs`
  *(modify — path constants + message now carry the `Cloudstrap.Demo.*` csproj/exe names)*
- `.vscode/tasks.json` + `launch.json` *(modify — renamed dll/csproj names; launch config display
  names become "Demo …")*

**RED** *(mechanical rename — no new behavior, no invented failing test; user-directed)*:
- The regression net is the signal: GREEN = the entire suite passes under the new names with
  unchanged behavioral assertions (AC-DR1); the E2E executable is now
  `Cloudstrap.Demo.E2E.Tests.exe` and is still discovered by the CI glob (still under
  `src/Test/**`, still `.Tests`-suffixed).

**GREEN**:
- The renames listed in Scope — no port, endpoint, pipeline, or dependency change.

**DB changes**: None.

**VERIFY**: Full-suite check (Context §9, E2E exe at its new `Cloudstrap.Demo.E2E.Tests` path) — all
green; repo sweep `Select-String -Pattern 'WasmTestProject'` over `src/demo/**`, `src/Test/E2E/**`,
`src/Cloudstrap.sln`, `.vscode/**`, `.github/**` returns nothing; `dotnet format` clean.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RGR cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: the rename commit *(covers Step 2)*

*Executor: STOP here. Present the results of Step 2 and WAIT for user approval.*

- [x] Behavioral verification: full suite green under `Cloudstrap.Demo.*`; the `WasmTestProject` identifier sweep over the live trees is empty.
- [x] Code review: user reviews the **IdP seed diff** (auth-adjacent identifier renames `demo-bff`/`demo-web`/`demo-selfapi` — names only, no flow/secret/URI change) and spot-checks namespaces.
- [x] AC-DR10 spot check: `git log --follow src/demo/BlazorWasm/Bff/Cloudstrap.Demo.BlazorWasm.Bff.csproj` (and one E2E file) traces history back across commit B **and** commit A.
- [x] **Commit B lands here** (rename only), separate from commit A.
- [x] User approved — implementation may continue past this gate (2026-08-20)

---

## Step 3 — The Api demo app enforces authenticated-by-default (new host, port 5330)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/demo/Api/Cloudstrap.Demo.Api.csproj` *(create — `Microsoft.NET.Sdk.Web`; ProjectReferences:
  `Cloudstrap.Core`, `Cloudstrap.Observability`, `Cloudstrap.WebApi`, `Cloudstrap.Demo.Contracts`;
  built in place under `src/demo` — never under `src/Test` first (spec Migration §3))*
- `src/demo/Api/Program.cs` *(create — the Bff `Program.cs` pattern, minimized:
  `GetCloudstrapOptions()` fail-fast → `CloudstrapBootstrapLogger` → `UseCloudstrapObservability()`
  (**Console mode** — fixture-capturable) → `AddCloudstrapWebApi()` → `AddCloudstrapJwtBearer()`
  with **`RequireAuthenticatedEndpoints` left at its `true` default** (the key deliberately absent
  from appsettings — the hardened posture's first live demo, AC-DR6) → a `self` health check →
  `UseCloudstrapWebApi()` with **no** hooks (pure API — no static files, no SPA fallback))*
- `src/demo/Api/Controllers/DownstreamController.cs` *(create — `[ApiController]`,
  `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/downstream")]`;
  `GET whoami` → `DownstreamWhoAmIDto` echoing the validated `sub`, `client_id`, `scope` claims +
  the constant `Host = "demo-api"` marker; **no `[Authorize]` attribute** — the fallback policy is
  the demo (AC-DR6/carried AC-D7))*
- `src/demo/Api/appsettings.json` *(create — `SystemName: demo`, `SubsystemType: api`,
  `OpenTelemetry:Mode: Console`, `JwtBearer` `Authority: http://127.0.0.1:5310` +
  `Audience: demo-api` (no `RequireAuthenticatedEndpoints` key), `OpenApi:Title`)*
- `src/demo/Api/Properties/launchSettings.json` *(create — http profile `http://127.0.0.1:5330`)*
- `src/demo/Shared/Contracts/DownstreamWhoAmIDto.cs` *(create —
  `public sealed record DownstreamWhoAmIDto(string Subject, string ClientId, string Scope, string Host);`)*
- `src/demo/Shared/IdentityProvider/TestIdentityProviderSeed.cs` *(modify — ⚠️ auth surface: the
  `demo-web` client's `Audiences` gains `demo-api`, so the signed-in user's token is valid at both
  the Bff (`demo-selfapi`) and the Api host; `demo-bff` unchanged — the machine loop stays on the Bff)*
- `src/Cloudstrap.sln` *(modify — add the project under a `demo/Api` solution folder)*
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/E2eFixture.cs` *(modify — new
  `ApiBaseUrl = "http://127.0.0.1:5330"` + a second `SutProcess` for
  `src/demo/Api/Cloudstrap.Demo.Api.csproj`, booted **after the IdP, before the Bff**, in launch
  **and attach** mode, readiness-polled on `GET /healthz`; disposed after the Bff)*
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/ApiHostTests.cs` *(create — see RED)*

**RED** *(write these tests first, run them, confirm they fail before writing production code)*:
- E2E test file: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/ApiHostTests.cs` (plain `HttpClient`
  against `E2eFixture.ApiBaseUrl` — the `WebApiTests` style):
  - `ApiHost_AnonymousWhoAmI_Returns401` — `GET api/v1/downstream/whoami` with no token → **401**
    from the fallback policy, no attribute involved (AC-DR6 / carried AC-D7).
  - `ApiHost_AnonymousHealthz_Returns200` — `GET /healthz` → **200** (probe carve-out coexists with
    the hardened default; AC-DR6 / carried AC-D8).
- Failing-run command:
  `src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "ApiHost_AnonymousWhoAmI_Returns401"`
  *(fails first: the Api project does not exist, the fixture's Api boot times out)*
- Write the fixture extension + tests first so RED is runnable; the Api host is the GREEN.

**GREEN** *(minimal production code across all necessary layers to make RED pass)*:
- The Api host as specced in Scope: two shipped-package calls (`AddCloudstrapWebApi` +
  `AddCloudstrapJwtBearer`) give it versioning, OpenAPI/Scalar, problem details, security headers,
  probes, and the whole-app auth fallback — the teaching point is how little code the file holds.
- The seed's `demo-api` audience addition (without it, no positive-path test can ever pass in Step 4).

**DB changes**: None.

**VERIFY**: the two new E2E tests pass; **the entire pre-existing suite still green** (the fixture
now boots three processes — no pre-existing assertion changes); full-suite check (Context §9);
`dotnet run --project src/demo/Api` boots standalone with `/healthz` 200 while token validation
fails loudly until the IdP listens (the lazy-metadata precedent, AC-DR3).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RGR cycles, only after the feature slice is fully working and verified.

---

## Step 4 — User-token forwarding becomes a real cross-process hop (Bff → Api)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/demo/BlazorWasm/Bff/appsettings.json` *(modify — `HttpClients:UserApi:BaseAddress` →
  `http://127.0.0.1:5330/`; add `EnableHealthCheck: true` so `/ready` proves a live dependency on
  the Api peer (spec Behaviors row; the standalone-Bff readiness consequence is a Step 6 README note))*
- `src/demo/BlazorWasm/Bff/Services/IUserApiClient.cs` + `UserApiClient.cs` *(modify — the client
  now calls `GET api/v1/downstream/whoami` on the Api host and returns `DownstreamWhoAmIDto`;
  method rename to `GetWhoAmIAsync(CancellationToken)`)*
- `src/demo/BlazorWasm/Bff/Controllers/UserController.cs` *(modify — `GetCall` relays
  `DownstreamWhoAmIDto` (incl. the `Host` marker) instead of `MachineStatusDto`; XML docs updated —
  the relay now terminates on a separate process)*
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/OpenIdConnectTests.cs` *(modify — the signed-in
  `api/v1/user/call` assertion becomes the cross-process proof; any user-call shape assertions
  updated to the new DTO)*
- `MachineStatusDto` / `SelfApi` / `MachineController`: **untouched** (spec V-4 — the #9 machine
  loop and the AC-OIDC9 bearer pin stay as-is).

**RED** *(write these tests first, run them, confirm they fail before writing production code)*:
- E2E test (in `OpenIdConnectTests.cs`, using the existing `BrowserSignIn` sign-in flow):
  `UserCall_SignedIn_ProvesTheApiHostValidatedTheUsersToken` — after a real Chromium sign-in,
  `GET api/v1/user/call` returns `subject` = the seeded user (`TestIdentityProviderSeed.Username`),
  `clientId = demo-web`, **`host = "demo-api"`** (AC-DR7 / carried AC-D9).
- Failing-run command:
  `src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "UserCall_SignedIn_ProvesTheApiHostValidatedTheUsersToken"`
  *(fails first: `UserApi` still self-loops into the Bff's machine endpoint — no `host` marker in
  the response)*

**GREEN** *(minimal production code across all necessary layers to make RED pass)*:
- The retarget listed in Scope. The user token reaches the Api because Step 3 added `demo-api` to
  the web client's audiences; the client-credentials fallback token (machine, `demo-selfapi`
  audience only) is deliberately **not** made Api-valid — `user/call` is `[Authorize]`, so the
  user-first AC-CC13 ordering is what the wire carries.

**DB changes**: None.

**VERIFY**: the new E2E test passes; the whole suite green — in particular the untouched #9
contract (`ClientCredentialsTests`: machine 401 / cached token) and the `ExtensionsTests`
second-instance readiness scenarios still pass with `UserApi:EnableHealthCheck: true` (if a
second-instance `/ready` expectation trips, the fix is the documented config override in that
test's boot arguments — never a shipped-package change); full-suite check (Context §9).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RGR cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: the Api demo app ⚠️ *(Risk Area: auth surface — covers Steps 3–4)*

*Executor: STOP here. Present the results of Steps 3–4 and WAIT for user approval.*

- [x] Behavioral verification: `ApiHost_AnonymousWhoAmI_Returns401`, `ApiHost_AnonymousHealthz_Returns200`, and `UserCall_SignedIn_ProvesTheApiHostValidatedTheUsersToken` pass (AC-DR6 + AC-DR7); full suite green with the three-process fixture.
- [x] ⚠️ Auth review: the seed diff (`demo-api` audience on the `demo-web` client — token blast radius reviewed), the Api host's hardened default left in place (no `RequireAuthenticatedEndpoints` key anywhere in `src/demo/Api`), placeholder-only credentials (AC-DR9 spot check on the diff).
- [x] Code review: Api `Program.cs` stays a minimal consumer example (shipped packages only — AC-DR13); `SelfApi`/`MachineController`/`MachineStatusDto` untouched.
- [x] User approved — implementation may continue past this gate (2026-08-20)

---

## Step 5 — A Blazor Server app signs in at the shared IdP and calls the Api (D-B, port 5340)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/demo/BlazorServer/Cloudstrap.Demo.BlazorServer.csproj` *(create — `Microsoft.NET.Sdk.Web`;
  ProjectReferences: `Cloudstrap.Core`, `Cloudstrap.Observability`, `Cloudstrap.Extensions`,
  `Cloudstrap.Authentication.OpenIdConnect`, `Cloudstrap.Demo.Contracts`; framework-provided Blazor
  Server components only — **no new NuGet pins, no `Cloudstrap.BlazorServer` helper code** (D-B /
  AC-DR13; #12 extends this app later))*
- `src/demo/BlazorServer/Program.cs` *(create — stock Blazor Server (interactive server render
  mode) + the shipped one-liners: `GetCloudstrapOptions()` fail-fast, `UseCloudstrapObservability()`
  (**Otlp mode**), `AddCloudstrapOpenIdConnect()` with `RequireAuthenticatedEndpoints: false` (the
  Bff precedent — home stays anonymous so the app boots gracefully without peers, AC-DR3),
  `AddCloudstrapHttpServiceClient<IDemoApiClient, DemoApiClient>("DemoApi")`,
  `MapCloudstrapAuthenticationEndpoints()` mapped before the component endpoints)*
- `src/demo/BlazorServer/Services/IDemoApiClient.cs` + `DemoApiClient.cs` *(create — typed client
  returning `DownstreamWhoAmIDto` from `GET api/v1/downstream/whoami`)*
- `src/demo/BlazorServer/Components/` *(create — `App.razor`, `Routes.razor`, `_Imports.razor`,
  `Layout/MainLayout.razor`; `Pages/Home.razor` (anonymous, names the app + login link);
  `Pages/WhoAmI.razor` at `/whoami`, `[Authorize]` — renders the signed-in user's name and the
  relayed Api echo (`Host = demo-api`) via `IDemoApiClient`)*
- `src/demo/BlazorServer/appsettings.json` *(create — `SystemName: demo`,
  `SubsystemType: blazorserver`, `OpenTelemetry:Mode: Otlp` (endpoint left at the conventional
  localhost default — README states no collector is required to boot),
  `OpenIdConnect` `Authority: http://127.0.0.1:5310` + `ClientId: demo-blazorserver` + visibly-fake
  placeholder secret + `RequireAuthenticatedEndpoints: false`,
  `HttpClients:DemoApi` `BaseAddress: http://127.0.0.1:5330/` + `AddUserAccessToken: true`)*
- `src/demo/BlazorServer/Properties/launchSettings.json` *(create — http profile `http://127.0.0.1:5340`)*
- `src/demo/Shared/IdentityProvider/TestIdentityProviderSeed.cs` *(modify — ⚠️ auth surface: new
  interactive client `demo-blazorserver` (placeholder secret, `Audiences { demo-api }`, scopes
  matching the web client) with redirect/post-logout URIs derived from a BlazorServer base-address
  parameter defaulting to `http://127.0.0.1:5340/` — the seed stays the single source of truth for
  fixture and host (no drift))*
- `src/Cloudstrap.sln` *(modify — add the project under a `demo/BlazorServer` solution folder)*
- `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/BlazorServerTests.cs` *(create — see RED; boots the host
  itself by project path on 5340, the `MvcHostTests` precedent; the fixture-owned IdP (5310) and
  Api (5330) are already running)*

**RED** *(write these tests first, run them, confirm they fail before writing production code)*:
- E2E test file: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/BlazorServerTests.cs` (inherits
  `PageTestBase`, boots `src/demo/BlazorServer/Cloudstrap.Demo.BlazorServer.csproj` on
  `http://127.0.0.1:5340` via `SutProcess.Start`):
  - `BlazorServer_SignInAndWhoAmI_RendersUserAndApiEcho_NoConsoleErrors` — navigate `/whoami`
    anonymously → redirected to the shared IdP login form → sign in via `BrowserSignIn` → page
    renders the seeded user's display name **and** the Api echo with `demo-api`, with zero browser
    console errors (spec matrix row; contributes to AC-DR5).
- Failing-run command:
  `src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "BlazorServer_SignInAndWhoAmI_RendersUserAndApiEcho_NoConsoleErrors"`
  *(fails first: the BlazorServer project does not exist)*

**GREEN** *(minimal production code across all necessary layers to make RED pass)*:
- The BlazorServer app + seed client as specced in Scope. Deliberately minimal: the OIDC login, the
  user-flagged typed client, and OTLP-mode observability are the entire feature set — every
  Cloudstrap call in `Program.cs` is one a consumer would copy.

**DB changes**: None.

**VERIFY**: the new E2E test passes; whole suite green; `dotnet run --project src/demo/BlazorServer`
boots standalone (home page anonymous, `/whoami` fails loudly naming the authority until the IdP
listens — AC-DR3); Otlp mode boots with no collector running (spec matrix note); full-suite check
(Context §9).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RGR cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 4: the BlazorServer demo app ⚠️ *(Risk Area: auth surface — covers Step 5)*

*Executor: STOP here. Present the results of Step 5 and WAIT for user approval.*

- [x] Behavioral verification: `BlazorServer_SignInAndWhoAmI_RendersUserAndApiEcho_NoConsoleErrors` passes; full suite green. With this, OIDC login runs on two host styles, M2M on the Bff, the JWT API cross-process, and all three observability modes are exercised (Console/Api, Otlp/BlazorServer, AzureMonitor/Bff) — AC-DR5's feature walk is complete in E2E form.
- [x] ⚠️ Auth review: the new `demo-blazorserver` IdP client (redirect URIs, audiences, placeholder secret — AC-DR9 spot check).
- [x] Code review: no #12/#13 helper pre-implementation (AC-DR13 scope guard) — stock Blazor Server + shipped packages only.
- [x] User approved — implementation may continue past this gate (2026-08-20)

---

## Step 6 — The demo suite is self-documenting and boots with one F5 (READMEs + compound launch)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/demo/README.md` *(rewrite the interim carry — suite overview: architecture diagram (five
  hosts + who talks to whom), the port map (Context: 5300/5301–5304/5310/5311/5320/5330/5340/59999),
  run matrix (standalone `dotnet run` per app + the compound launch + `CLOUDSTRAP_E2E_BASEURL`
  attach mode), the ProjectReference→PackageReference mapping note, the placeholder-credentials
  rule, the E2E harness notes carried over from the old SUT README (fixture boot order IdP → Api →
  Bff, captured console telemetry, standalone-Bff readiness note for the retargeted `UserApi`))*
- Per-app READMEs *(create — each with the spec's feature-matrix shape: package · the one
  Cloudstrap call · the proving E2E test (AC-DR8))*:
  - `src/demo/Api/README.md` (hardened-by-default posture, Console mode, probe carve-out)
  - `src/demo/BlazorWasm/README.md` (Bff/Client/Presentation — everything it demos today + the cross-process `UserApi` hop)
  - `src/demo/Mvc/README.md` (the under-ten-lines `Cloudstrap.Mvc` consumer example — anonymous by design, D-F)
  - `src/demo/BlazorServer/README.md` (OIDC on server-rendered Blazor, user-flagged typed client, Otlp mode — states no collector is required to boot)
  - `src/demo/Shared/IdentityProvider/README.md` (demo-only STS, never a real IdP, seed = single source of truth)
- `.vscode/tasks.json` *(modify — build tasks for Api, BlazorServer)* · `.vscode/launch.json`
  *(modify — configs for Api (5330), BlazorServer (5340), Mvc (5320); the compound becomes
  **"Demo apps (all hosts + IdP)"** listing IdP first, then Api, Mvc, BlazorServer, Bff, with
  `stopAll: true` — AC-DR4)*

**RED** *(documentation/launch-config step — no new runtime behavior, no invented failing test)*:
- The observable assertions are in VERIFY (grep-based accuracy checks + the unchanged suite); the
  AC-DR4 compound-launch walk is the user's manual check at the final gate — automation of a VS
  Code F5 is impractical (workflow rule 2's documented exception for presentation-only steps).

**GREEN**: the documents and launch configs in Scope. Every feature-matrix row must name a real E2E
test method that exists in `src/Test/E2E/Cloudstrap.Demo.E2E.Tests` (AC-DR8 is checkable by grep).

**DB changes**: None.

**VERIFY**: full-suite check (Context §9) still green (no code touched — regression guard);
`Select-String -Pattern 'Test[/\\]WasmTestProject' -Path src/demo/**/*.md,.vscode/*.json` returns
nothing; every E2E test name cited in a README resolves to a method in the E2E project
(spot-check by grep); `dotnet format` clean.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RGR cycles, only after the feature slice is fully working and verified.

---

## Step 7 — The process docs point every future deliverable at the demo apps (AC-DR12) + final sweep

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(process docs outside `src/` — human-reviewed at the final gate; the spec's canonical
standing-rule wording is the text to install, final wording user-approved at the gate)*:
- `CLAUDE.md` *(modify — workflow rule 9 → the spec's canonical "Demonstrate every migrated feature
  in the demo apps" wording incl. the per-feature-type vehicle table; Project Structure tree →
  `src/demo` + `src/Test/E2E`; Commands section E2E paths (`playwright.ps1` + exe) → the
  `Cloudstrap.Demo.E2E.Tests` paths; Test Conventions E2E line → new suite path/wording)*
- `.claude/agents/planner.md` *(modify — rule 15, interview item 9, and the demonstration-slice
  references → demo apps + `Cloudstrap.Demo.E2E.Tests`; precedent reference gains this plan)*
- `.claude/agents/project-manager.md` *(modify — DoD / ✅-flip verification wording → demo apps)*
- `.claude/instructions/tests.md` *(modify — E2E section paths and wording)*
- `.claude/templates/plan-template.md` *(modify — the demonstration-slice comment block: new paths,
  new example failing-run command, README-table instruction → the `src/demo` READMEs)*
- `_specs/WasmTestProjectDemoCompletion.md` *(modify — the reconciliation note the spec's doc-update
  set defines: AC-D11's diff-scope pin recorded as superseded for the moved paths, increment 3
  marked realized-by-#27, the 5320→5330 port change; **user-approved edit at the final gate** —
  that spec's remaining open questions (incl. ⚠️ OQ-2) stay owned by its own gate, untouched)*
- **Not in scope**: `_plans/ROADMAP.md` preamble — the project-manager applies the standing-rule
  path change at ✅-flip (spec doc-update set).
- Repo-wide final sweep *(verification, not files)*: no stale `src/Test/WasmTestProject` string
  anywhere except historical documents (ROADMAP change log, delivered plans/specs' narrative) — AC-DR11.

**RED** *(documentation step — no runtime behavior, no invented failing test)*:
- The observable assertions are the VERIFY sweeps; the full suite is the regression guard.

**GREEN**: the doc edits in Scope, using the spec's canonical wording verbatim as the draft the
user approves or amends at the gate.

**DB changes**: None.

**VERIFY**: full-suite check (Context §9) green;
`Get-ChildItem -Recurse -File | Select-String -Pattern 'Test[/\\]WasmTestProject'` returns hits
**only** in historical documents (`_plans/ROADMAP.md` history, delivered `_plans/*`/`_specs/*`
narrative) — list presented at the gate; no doc outside that set instructs extending
`src/Test/WasmTestProject` (AC-DR12); `dotnet format` clean.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RGR cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — FINAL: deliverable #27 done *(covers Steps 6–7 + the whole-deliverable acceptance walk)* ⚠️ *(Risk Areas: process-doc edits outside `src/`; auth demo surface reviewed across gates 3–4)*

*Executor: STOP here. Present the results of Steps 6–7 plus the full acceptance walk and WAIT for user approval.*

- [ ] Behavioral verification (AC-DR1): `dotnet build src/Cloudstrap.sln` + all 9 unit exes + `Cloudstrap.Demo.E2E.Tests.exe` (full run — pre-existing behavioral assertions unchanged, plus the new `ApiHostTests`, the cross-process `UserCall` proof, and `BlazorServerTests`) + `dotnet format --verify-no-changes` — all green, output presented.
- [ ] AC-DR2: CI green on the branch — glob discovers every test project incl. the relocated E2E suite; `playwright.ps1` path resolves (user pushes / reviews the run).
- [ ] AC-DR4 (user-run, manual): the VS Code compound launch boots all demo hosts in dependency order (IdP first); browsing the Bff's `/doctors` auto-triggers login at the shared IdP; stopping the session stops all hosts.
- [ ] AC-DR5 feature walk confirmed (E2E + optionally manual): OIDC login (BlazorWasm + BlazorServer), client-credentials M2M (Bff `SelfApi`), JWT cross-process hop (Bff → Api, `host: demo-api`), observability Console + Otlp + AzureMonitor all exercised.
- [ ] AC-DR11: the spec's breakage-checklist table walked item-by-item (sln incl. duplicate-nesting removal · `src/Test/Directory.Build.props` + `src/demo/Directory.Build.props` · ci.yml · `SutProcess.cs` · `PageTestBase.cs` · `.vscode` · `Directory.Packages.props` comments · READMEs · AC-D11 reconciliation · standing-rule doc set) — every row closed; the Step 7 grep-sweep hit list reviewed (historical docs only).
- [ ] AC-DR8 + AC-DR12 doc review: per-app feature matrices accurate against `Program.cs`/config/E2E; the canonical standing-rule wording approved (or amended) across CLAUDE.md / planner.md / project-manager.md / tests.md / plan-template.md; the `_specs/WasmTestProjectDemoCompletion.md` reconciliation note approved.
- [ ] AC-DR9 + AC-DR13 + AC-DR14: full-diff sweep — zero secrets (placeholder-only, visibly fake), zero `Nihdi`/`NIHDI`/`Riziv`, zero `Aspire.*`, zero real PII; demo apps use ProjectReferences to shipped packages only, no #12/#13 helper code; nothing under `src/demo` packs in Release.
- [ ] AC-DR10: `git log --follow` spot checks across move + rename commits confirmed.
- [ ] User approved — deliverable #27 done (project-manager then flips §27 ✅ and applies the roadmap-preamble standing-rule update; the D-E authorization-demo follow-up plan may be scheduled).
