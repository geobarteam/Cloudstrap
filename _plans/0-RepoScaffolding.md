# Plan: 0-RepoScaffolding — Repo builds, tests, formats, versions, and publishes before any package exists

## Overview

Deliverable #0 of the extraction roadmap: an empty-but-fully-wired repository — a contributor clones Cloudstrap and immediately gets a green `dotnet build`, a runnable NUnit/MTP test executable, a passing `dotnet format` check, GitVersion-computed versions, and CI that enforces all of it and publishes previews/stables. **Binding spec: `_specs/0-RepoScaffolding.md`** (all decisions final — NUnit 4, no StyleCop, `AnalysisLevel=latest-recommended`, CPM, two workflows + preview cleanup). Reference pattern: the source repo's build conventions, read and redesigned per the spec's Port Decision Table — `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\Directory.Build.props` (keep `TreatWarningsAsErrors` + NU190x carve-out, drop all StyleCop), `src\.editorconfig` (port formatting/style/naming, drop SA block + RIZIV identifiers + CS1591 downgrade), and the `Test\UnitTest\*.Tests.csproj` MTP shape (framework swapped to NUnit).

This is an infrastructure deliverable: there is no UI and no API. "Vertical slice" here means an end-to-end **capability of the repository** (clone→verify locally; version→publish via CI). Several steps are pure configuration with no automatable test — for those, the observable VERIFY commands (build / test-exe / format / gitversion exit codes and outputs) stand in for RED/GREEN, recorded explicitly per step.

**AC coverage map** (from `_specs/0-RepoScaffolding.md`): AC-R1/R3/R7/R10/R11 → Step 1 · AC-R2 + AC-R11(test side) → Step 2 · AC-R9 → Step 3 · AC-R4/R5 → Step 4 + Gate 2 · AC-R6 → Step 5 + Gate 2 · AC-R8 → Step 5 VERIFY.

**Assumptions to confirm at Gate 1** (spec is silent; defaults chosen, not TBD): `Authors=Cloudstrap` *(decided 2026-07-25: the nuget.org owner is the `Cloudstrap` organization — package author metadata matches the reserved-prefix owner)*; `Nullable=enable` + `ImplicitUsings=enable` repo-wide in `src/Directory.Build.props` (CLAUDE.md mandates nullable reference types); base `PackageTags=cloudstrap;azure;aspnetcore`. **At Gate 2**: cleanup schedule = weekly (`0 3 * * 0`).

---

## Slice 1 — Clone → build, test, and format green locally

---

## Step 1 — Clean clone restores from nuget.org only, builds, and format-checks under full strictness

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `global.json` *(create)* — SDK `10.0.x` (current installed 10.0 feature band), `"rollForward": "latestFeature"`.
- `nuget.config` *(create, repo root)* — `<packageSources><clear /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>`. Nothing else (no apikeys/credentials sections — spec drops that cruft).
- `src/Cloudstrap.sln` *(create)* — near-empty classic `.sln`, AnyCPU only, with empty solution folders `Test` → `UnitTest` (Step 2 adds the first project).
- `src/Directory.Build.props` *(create)* — see GREEN.
- `src/Directory.Packages.props` *(create)* — `<Project><ItemGroup></ItemGroup></Project>` shell; first `PackageVersion` items arrive in Step 2 (versions live **only** here from now on).
- `src/.editorconfig` *(create)* — see GREEN.
- `assets/icon.png` *(create)* — placeholder package icon (simple generated PNG; final artwork swapped in-place later per spec).

**RED** *(recorded explicitly: pure-configuration step — no test project exists until Step 2, so no automated test is possible; the behavioral strictness probes in VERIFY stand in for RED/GREEN)*:
- The "failing state" is observable before this step: `dotnet build src/Cloudstrap.sln` fails with MSB1009 (no solution). After GREEN it succeeds — that transition plus the probes below is the observable behavior change.

**GREEN**:
- `src/Directory.Build.props` (redesign of the source props — StyleCop lists/package **dropped**, per spec):
  - Strictness: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `<AnalysisLevel>latest-recommended</AnalysisLevel>`, `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`, `<WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>` (audit-warning carve-out ported from source line 26; the SA/SX lists and `NoWarn` block are **not** ported).
  - Language defaults: `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>` *(assumption — confirm at Gate 1)*.
  - CPM switch: `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`.
  - Packaging metadata defaults (apply to future packable projects; harmless now): `Authors=geobarteam` *(assumption)*, `PackageLicenseExpression=MIT`, `PackageProjectUrl`/`RepositoryUrl=https://github.com/geobarteam/Cloudstrap`, `RepositoryType=git`, base `PackageTags`, `PackageIcon=icon.png` + `<None Include="$(MSBuildThisFileDirectory)../assets/icon.png" Pack="true" PackagePath="/" Condition="'$(IsPackable)' != 'false'" />`, `PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`, `IncludeSymbols=true`, `SymbolPackageFormat=snupkg` (SourceLink via SDK built-in — **no** package reference). `ContinuousIntegrationBuild` is **not** set here — CI passes `-p:ContinuousIntegrationBuild=true` (Step 4).
  - `GenerateDocumentationFile`/`GeneratePackageOnBuild` stay per-project (CLAUDE.md packaging rule); `TargetFramework` stays per-project (`net10.0`).
- `src/.editorconfig` (single style authority — port of the source file with deletions per spec): add `root = true`; port the formatting sections (indent/newline/spacing/wrapping), C# and .NET code-style preferences, and the naming-rule block. **Promote all naming rules to `warning`** (interface `I`-prefix, types/non-field-members PascalCase — `suggestion` in source lines 150–160; private `_camelCase` fields already `warning` at line 196). **Delete**: the `# Style rules for RIZIV-INAMI` header, the entire `dotnet_diagnostic.SA*`/`SX*` block (lines 237–425), `stylecop.documentation.companyName` (line 427), and the `dotnet_diagnostic.CS1591.severity = suggestion` downgrade (line 108) — CS1591 becomes build-breaking in libraries.

**DB changes**: none — this repository has no database.

**VERIFY** *(all observable; when green, mark Done and continue straight to Step 2)*:
1. `dotnet nuget list source --configfile nuget.config` → exactly one enabled source: nuget.org (AC-R10 feed part).
2. `dotnet restore src/Cloudstrap.sln` → succeeds. `dotnet build src/Cloudstrap.sln` → succeeds, zero warnings/errors (AC-R1). `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0 (AC-R3).
3. **Transient strictness probes** (throwaway project `src/ScaffoldProbe/` — built directly, never added to the solution, **deleted afterwards, never committed**; props/.editorconfig apply by directory inheritance). Create `dotnet new classlib -o src/ScaffoldProbe`, set `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (mimicking a real package), then one probe at a time via `dotnet build src/ScaffoldProbe/ScaffoldProbe.csproj`:
   - Probe A — undocumented `public` class/member → build **fails** with `error CS1591` (AC-R11 library side).
   - Probe B — private field named `BadName` (violates `_camelCase` naming rule) → build **fails** with `error IDE1006` (AC-R7 style side).
   - Probe C — an SDK code-quality violation, e.g. a public type declared outside any namespace → build **fails** with `error CA1050` (AC-R7 quality side; executor may substitute any rule that reliably fires at `latest-recommended`).
   - Probe D — `<PackageReference Include="NUnit" Version="4.6.1" />` (versioned, despite CPM) → `dotnet restore` **fails** with `NU1008` (AC-R10 CPM part).
   - Delete `src/ScaffoldProbe/` entirely; re-run build + format → green again.
4. Capture the four probe failure outputs for presentation at Gate 1.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Test leg proves itself: a failing NUnit test fails the run, the passing suite goes green (MTP executable, no `dotnet test`)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/Directory.Build.props` *(create)* — test-layer defaults, layered on the root props.
- `src/Directory.Packages.props` *(modify)* — add `PackageVersion` items: `NUnit` 4.6.1, `NUnit3TestAdapter` 6.2.0, `NUnit.Analyzers` 4.14.0 (minimum versions per spec Dependencies table; pin the latest available in each line).
- `src/Test/UnitTest/Cloudstrap.Scaffolding.Tests/Cloudstrap.Scaffolding.Tests.csproj` *(create)* — ~10-line placeholder test project (the living NUnit-conventions template until deliverable 1 absorbs it).
- `src/Test/UnitTest/Cloudstrap.Scaffolding.Tests/ScaffoldingSanityTests.cs` *(create)* — sanity fixture.
- `src/Cloudstrap.sln` *(modify)* — add the project under solution folders `Test\UnitTest`.

**RED** *(a genuine failing-first cycle — it proves the runner surfaces failures, without which the whole test leg is worthless)*:
- Test file: `src/Test/UnitTest/Cloudstrap.Scaffolding.Tests/ScaffoldingSanityTests.cs`
- Write the full wiring (csproj + layer props + `PackageVersion` items) with fixture `ScaffoldingSanityTests` containing a single temporary method `TestLeg_RedPhase_SurfacesFailure` whose body is `Assert.Fail("RED: proves the MTP runner detects and reports failures");`
- Failing-run command *(this repo forbids `dotnet test` — run the MTP executable directly)*:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Scaffolding.Tests\bin\Debug\net10.0\Cloudstrap.Scaffolding.Tests.exe
  ```
  → confirm: 1 failed test reported, **exit code non-zero**.

**GREEN**:
- `src/Test/Directory.Build.props`: first line imports the parent chain — `<Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)..'))" />`; then `IsPackable=false`, `GenerateDocumentationFile=false`, `<NoWarn>$(NoWarn);CS1591</NoWarn>` (doc rules off in the test layer — AC-R11), `OutputType=Exe`, `EnableNUnitRunner=true`, and shared version-less `PackageReference` items for `NUnit`, `NUnit3TestAdapter`, `NUnit.Analyzers` (`PrivateAssets=all` on the analyzer). `TestingPlatformDotnetTestSupport` is **not** set (source csproj line 8 is deliberately not ported — `dotnet test` stays unsupported; no `dotnet.config`). *(Note for deliverable 12+: future SUT web-app projects under `src/Test/` will need an opt-out from the NUnit wiring — their problem, documented here so it isn't a surprise.)*
- `Cloudstrap.Scaffolding.Tests.csproj`: `<Project Sdk="Microsoft.NET.Sdk">` + `TargetFramework=net10.0` only — everything else comes from the layer props.
- Replace the temporary failing method with the real sanity test (AAA, constraint model, naming convention `<Method>_<Scenario>_<Expected>`):
  - `RuntimeVersion_OnPinnedSdk_IsNet10` — Arrange `int expectedMajor = 10;` / Act `int actualMajor = Environment.Version.Major;` / Assert `Assert.That(actualMajor, Is.EqualTo(expectedMajor));`
- Rebuild, rerun the exe → 1 passed, exit code 0.

**DB changes**: none.

**VERIFY**:
1. `src\Test\UnitTest\Cloudstrap.Scaffolding.Tests\bin\Debug\net10.0\Cloudstrap.Scaffolding.Tests.exe` → all tests pass, exit 0, no network access (AC-R2).
2. `dotnet build src/Cloudstrap.sln` → zero warnings — note the fixture is a `public` undocumented class, so a clean build **is** the observable proof that CS1591 is off under `src/Test/` (AC-R11 test side).
3. `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
4. `dotnet build src/Cloudstrap.sln -c Release` → succeeds and produces **no** `*.nupkg` anywhere under `src/Test/` (`IsPackable=false`).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: local rails locked in *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **This gate freezes the analyzer ruleset.** Per CLAUDE.md, the rules in `Directory.Build.props` are immutable after this deliverable — loosening is impossible later, only per-rule tightening via `.editorconfig`. Review accordingly.

- [x] Behavioral verification: the three local gates are green on a clean working tree (`dotnet build`, test exe run direct → pass/exit 0, `dotnet format --verify-no-changes` → exit 0); the four Step 1 probe outputs show CS1591, IDE1006, CA-rule, and NU1008 each **failing the build/restore as errors**; the Step 2 RED run output shows the deliberately failing test reported with non-zero exit.
- [x] Code review — `src/Directory.Build.props` (⚠️ frozen after approval): `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`, NU1901–NU1904 carve-out, CPM switch, packaging metadata defaults; **zero StyleCop remnants**. *(Approved with executor deviation: `dotnet_diagnostic.IDE1006.severity = warning` added to `src/.editorconfig` — naming-rule severities are IDE-only; this line makes naming build-breaking per AC-R7.)*
- [x] Code review — `src/.editorconfig` as the single style authority: naming rules at `warning`, no SA block, no company identifiers, no CS1591 downgrade.
- [x] Confirm the flagged assumptions: `Authors=Cloudstrap` *(settled 2026-07-25 — matches the nuget.org `Cloudstrap` organization that owns the reserved prefix)* · `Nullable`+`ImplicitUsings` enabled repo-wide · base `PackageTags` · placeholder `assets/icon.png` acceptable until final artwork.
- [x] ⚠️ Dependency review (risk area): NUnit 4.6.1 / NUnit3TestAdapter 6.2.0 / NUnit.Analyzers 4.14.0 — all MIT, versions pinned only in `src/Directory.Packages.props`. *(Approved with executor deviation: `CA1707` added to the test-layer `NoWarn` — the `<Method>_<Scenario>_<Expected>` convention requires underscores; suppressed under `src/Test/` only.)*
- [x] User approved — implementation may continue past this gate *(approved 2026-07-25)*

---

## Slice 2 — Versions computed from git; CI enforces the gates and publishes

---

## Step 3 — GitVersion computes `-preview.N` on dev and exact `X.Y.Z` from tags on main

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `GitVersion.yml` *(create, repo root)* — GitVersion 6.x configuration.

**RED** *(recorded explicitly: pure-configuration step — no automated test is possible for a versioning config; the local `dotnet-gitversion` probes in VERIFY stand in for RED/GREEN)*:
- Failing state observable up front: `dotnet-gitversion` with no `GitVersion.yml` yields GitVersion's defaults, not the spec's contract (no `preview` label on `dev`).

**GREEN**:
- `GitVersion.yml` implementing the spec's versioning contract exactly (executor consults GitVersion 6.x docs for exact syntax):
  - Tag prefix accepts `v` (`vX.Y.Z` tags on `main` are the **only** source of stable versions → that commit versions exactly `X.Y.Z`).
  - `dev` branch → `X.Y.Z-preview.N`, `N` incrementing per commit since the last version anchor.
  - Untagged `main` commits → a next-patch pre-release version (never published — enforced by workflow triggers in Steps 4–5, not here).
  - `hotfix/*` → GitVersion's standard branch handling (patch pre-releases).

**DB changes**: none.

**VERIFY** *(local throwaway refs only — nothing is pushed, all probe refs deleted afterwards)*:
0. One-time: `dotnet tool install --global GitVersion.Tool` (VERIFY prerequisite on the executor machine — deliberately **not** a repo file; the spec's inventory has no tool manifest, CI uses `gittools/actions`).
1. Config sanity: `dotnet-gitversion /showvariable FullSemVer` on the current branch → runs clean (config parses), SemVer output.
2. Stable probe: `git tag v0.9.9` on HEAD → `dotnet-gitversion /showvariable FullSemVer` → exactly `0.9.9` → `git tag -d v0.9.9` (AC-R9 stable side).
3. Preview probe: `git checkout -b dev` → `git commit --allow-empty -m "gitversion probe 1"` → `dotnet-gitversion /showvariable FullSemVer` → matches `*-preview.N` → second empty commit → `N` increments by 1 (AC-R9 preview side) → return to the working branch, `git branch -D dev`.
4. Standard gates still green: build + test exe + format.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — CI enforces build + test + format on every PR/push; `dev` pushes publish previews to GitHub Packages

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `.github/workflows/ci.yml` *(create)*.

**RED** *(recorded explicitly: pure-configuration step — workflow behavior is only fully observable on GitHub after a push, which requires user approval; local VERIFY validates what it can, the behavioral proof lives at Gate 2)*.

**GREEN** — `ci.yml` per the spec's CI/CD shape:
- Triggers: `pull_request` + `push` to `dev` and `main`. Top-level `permissions: contents: read` with `packages: write` scoped to the publish job/step.
- Steps (single job, `ubuntu-latest`):
  1. `actions/checkout` with `fetch-depth: 0` (GitVersion needs full history).
  2. `actions/setup-dotnet` with `global-json-file: global.json`.
  3. `gittools/actions/gitversion/setup@v4` (versionSpec `6.x`) + `gittools/actions/gitversion/execute@v4`.
  4. `dotnet build src/Cloudstrap.sln -c Release -p:ContinuousIntegrationBuild=true -p:Version=<gitversion semVer output>` — analyzers enforce strictness by failing the build.
  5. Run **every** built MTP test executable: loop over `src/Test/**/ *.Tests.csproj`, execute the matching `bin/Release/net10.0/<name>` binary; any non-zero exit fails the job; **zero executables found also fails the job** (the test leg must never silently vanish).
  6. `dotnet format src/Cloudstrap.sln --verify-no-changes`.
  7. Preview publish — guarded `if: github.event_name == 'push' && github.ref == 'refs/heads/dev'`: `dotnet nuget push` all `src/**/bin/Release/*.nupkg` to `https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json` with `--api-key ${{ secrets.GITHUB_TOKEN }} --skip-duplicate`; shell-guarded to **no-op green when zero `.nupkg` files exist** (AC-R5 while the solution is empty). PRs and `main` pushes publish nothing (AC-R4).
- Pin all action references to major version tags (`@v4`/`@v5`); no third-party actions beyond `gittools/actions`.

**DB changes**: none.

**VERIFY** *(local — the CI-behavioral part is deferred to Gate 2, recorded explicitly)*:
1. YAML validity: run `actionlint` if available on the machine, otherwise a YAML parse (any available parser); zero syntax errors.
2. Static checklist against the spec: no publish step reachable from `pull_request` events; publish condition references `refs/heads/dev` exactly; `fetch-depth: 0` present; `global.json` drives `setup-dotnet`; no plaintext secrets.
3. `Select-String -Path .github/workflows/ci.yml -Pattern '(?i)(nihdi|riziv)'` → zero matches.
4. Standard local gates still green: build + test exe + format.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 5 — Pushing a `v*` tag releases stables to nuget.org; a scheduled job trims the preview feed to the last 20 versions

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `.github/workflows/release.yml` *(create)*.
- `.github/workflows/cleanup-previews.yml` *(create)*.

**RED** *(recorded explicitly: pure-configuration step — same deferral as Step 4; behavioral proof at Gate 2)*.

**GREEN**:
- `release.yml` — trigger: `push` of tags `v*` (on `main`). Repeats the full verification gates from `ci.yml` (checkout full-history → setup-dotnet → GitVersion → Release build with `-p:ContinuousIntegrationBuild=true` and the stable GitVersion version → run all MTP test exes → format check — a release never ships what CI didn't verify), then authenticates via **nuget.org Trusted Publishing** (decided 2026-07-25 — no long-lived key): job `permissions: contents: read` + `id-token: write`, and a `NuGet/login@v1` step (`user: ${{ secrets.NUGET_USER }}`, `id: login`) placed **after** the verification gates and immediately before the push (the issued key lives 1 hour and is single-use), then pushes `.nupkg` + `.snupkg` to `https://api.nuget.org/v3/index.json` with `--api-key ${{ steps.login.outputs.NUGET_API_KEY }} --skip-duplicate`; shell-guarded to **no-op green with zero packable projects** (AC-R6). No GitHub Packages mirror. `ci.yml` never publishes stables.
- `cleanup-previews.yml` — triggers: `schedule` (weekly, `cron: '0 3 * * 0'` — *assumption, confirm at Gate 2*) + `workflow_dispatch`; `permissions: packages: write`. Enumerates the owner's NuGet packages dynamically (GitHub API via `gh api`/`actions/github-script` — an empty list makes the job a **green no-op** today), then for each package runs `actions/delete-package-versions` with `min-versions-to-keep: 20` and `delete-only-pre-release-versions: true` (stables live on nuget.org only and are never touched).

**DB changes**: none.

**VERIFY** *(local — CI behavior deferred to Gate 2, recorded explicitly)*:
1. YAML validity for both files (actionlint or YAML parse) → zero errors.
2. Static checklist: `release.yml` triggers **only** on `v*` tags; nuget.org is the only publish target; the publish job declares `id-token: write` and takes its key from `NuGet/login@v1` (no long-lived key anywhere; `NUGET_USER` referenced only via `secrets.`); the workflow file name stays `release.yml` (the Trusted Publishing policy matches on it); cleanup keeps 20 and deletes pre-release versions only.
3. **Final De-NIHDI sweep (AC-R8)** across the entire deliverable:
   ```powershell
   Get-ChildItem -Recurse -File -Path src, .github, assets, global.json, nuget.config, GitVersion.yml |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv)'
   ```
   → zero matches.
4. Standard local gates still green: build + test exe + format.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: CI proven on GitHub, deliverable #0 complete *(covers Steps 3–5)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step. Every push below requires the user's explicit go-ahead (CLAUDE.md: no Git push without confirmation).*

**Manual prerequisites (user, on nuget.org / GitHub — from the spec's operational prerequisites):**
- [x] Reserve the `Cloudstrap.` package ID prefix on nuget.org (verified free 2026-07-24; required before the first *real* stable publish). **Reserved — confirmed by the user 2026-07-26.**
- [x] Trusted Publishing policy on nuget.org — `Cloudstrap-GitHubActions-Release`: owner `Cloudstrap`, repo `geobarteam/Cloudstrap`, workflow `release.yml`, no environment. **Active 2026-07-25** (public repo → no 7-day pending window). Replaces the previously planned `NUGET_API_KEY` secret.
- [x] Create the `NUGET_USER` repository secret — **set to `Cloudstrap` on 2026-07-25**; consumed by `NuGet/login@v1`. Fall back to `geobarteam` if the token exchange reports no matching policy — the docs do not state which one applies to organization-owned policies, so this is settled at the first real push).

**Behavioral verification on GitHub (user + executor together):**
- [x] GitVersion probes reviewed: Step 3 outputs show tag `v0.9.9` → exactly `0.9.9` and local `dev` → `-preview.N` incrementing per commit (AC-R9).
- [x] Push the working branch and open a PR → `ci.yml` runs: build, test, and format checks all execute and pass; the preview-publish step does **not** run on the PR (AC-R4).
- [x] After merge: create/push the `dev` branch → `ci.yml` completes green and the preview-publish step runs as a **graceful no-op** (zero packable projects) (AC-R5).
- [x] Manually dispatch `cleanup-previews.yml` → completes green as a no-op; confirm the weekly schedule (assumption: Sundays 03:00 UTC) is acceptable.
- [x] Recommended (optional now, required before deliverable 1 publishes anything): push tag `v0.1.0` on `main` → `release.yml` runs all gates green and no-ops the nuget.org push (AC-R6), anchoring the stable version baseline for future previews.
- [x] Code review across Steps 3–5: workflow permissions are minimal; secrets never echoed; action versions pinned; `release.yml` is the only stable path and nuget.org its only target; De-NIHDI sweep output (AC-R8) is empty.
- [x] User approved — implementation may continue past this gate *(deliverable #0 done — user confirmed complete 2026-07-26; ROADMAP status update belongs to the project-manager, not the executor)*
