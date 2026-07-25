# Spec: Repo Scaffolding (Roadmap Deliverable #0)

> Sources: `_plans/ROADMAP.md` §0 · `_specs/Cloudstrap.md` "Repository & Delivery" + Decisions Made + De-NIHDI-fication Checklist · source reference repo (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\`.
> This deliverable is mostly fresh authorship: the source repo contributes its *build conventions* (Directory.Build.props, .editorconfig, stylecop.json, solution layout), not code. The Port Decision Table therefore covers **artifacts**, not public types.
> All Open Questions were answered by the user at the 2026-07-25 gate — see the Decision Log at the end. Two answers are user-directed deviations from the founding spec / CLAUDE.md (NUnit instead of MSTest; **no StyleCop at all**) and require documentation amendments by the user (listed under Deliberate Behavior Changes).

## User Story

**As a** contributor (human or agent) to the Cloudstrap open-source library suite,
**I want to** clone the repository and immediately build, test, and format-check an empty-but-fully-wired solution, with CI enforcing all three and publishing preview packages automatically,
**So that** every subsequent package deliverable (1–24) lands on rails — same analyzers, same versioning, same publishing pipeline — with zero per-package build-infrastructure work.

---

## Acceptance Criteria

> The founding spec's "Repository & Delivery" section has **no AC-numbered criteria** — its bullet requirements (GitVersion + tags, GitHub Actions build/test/format/pack/publish, SourceLink, nuget.org feed, `Cloudstrap.` prefix reservation) are formalized here as AC-R1…AC-R11. Its "StyleCop + `TreatWarningsAsErrors` carried over" bullet is superseded by the user's gate decision (no StyleCop — see Deliberate Behavior Changes #2); `TreatWarningsAsErrors` itself is retained. No founding-spec AC numbers exist for this area, so none are carried over.

| # | Given | When | Then |
|---|-------|------|------|
| AC-R1 | A clean clone and the .NET 10 SDK selected by `global.json` | `dotnet build src/Cloudstrap.sln` (Release) | Build succeeds with **zero warnings and zero errors**. |
| AC-R2 | The solution is built | Each produced test executable (Microsoft.Testing.Platform runner) is run directly | All tests pass (placeholder suite contains at least one NUnit test); no network access needed; `dotnet test` is **not** used. |
| AC-R3 | A clean clone | `dotnet format src/Cloudstrap.sln --verify-no-changes` | Exit code 0. |
| AC-R4 | A pull request to `dev` or `main` | CI (`ci.yml`) runs | Build, test, and format checks all execute; failure of any one fails the PR check. No packages are published from PR builds. |
| AC-R5 | A commit is pushed to `dev` | CI (`ci.yml`) completes green | Version `X.Y.Z-preview.N` is computed by GitVersion and all packable project outputs are pushed to the GitHub Packages NuGet feed of the repository owner; the publish step no-ops gracefully while zero packable projects exist. Pushes to `main` (untagged) and PR builds publish **nothing**. |
| AC-R6 | Tag `vX.Y.Z` is pushed on `main` | The dedicated release workflow (`release.yml`) runs | Stable packages (`.nupkg` + `.snupkg` symbols) are published to **nuget.org only** (no GitHub Packages mirror); the step no-ops gracefully while zero packable projects exist. The routine `ci.yml` never publishes stable packages. |
| AC-R7 | A C# file violating an enabled analyzer rule — an SDK code-quality rule at `latest-recommended` level, or a code-style rule configured `warning` in `.editorconfig` (e.g. IDE1006 naming) | `dotnet build src/Cloudstrap.sln` | The build **fails** (`TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` surface it as an error). |
| AC-R8 | The scaffolding files (`src/`, `.github/`, root build/config files) | Searched case-insensitively for `Nihdi`, `NIHDI`, `Riziv` | Zero occurrences. |
| AC-R9 | GitVersion runs locally (`dotnet-gitversion`) | On a `dev` commit vs. on a `main` commit tagged `vX.Y.Z` | `dev` yields `*-preview.N` (N increments per commit); the tagged `main` commit yields exactly `X.Y.Z`. |
| AC-R10 | A clean clone with no user-level feed configuration interference from the repo | `dotnet restore src/Cloudstrap.sln` | All packages restore from nuget.org only — the repo `nuget.config` clears inherited sources and declares nuget.org as the single feed. Every `PackageReference` version resolves from the central `Directory.Packages.props` (CPM); a versioned `PackageReference` in a `.csproj` is a restore error (NU1008). |
| AC-R11 | A public type or member without an XML documentation comment | Added to a library project under `src/` / added to a test project under `src/Test/` | Library: build **fails** (CS1591 enforced). Test project: no error (documentation rules off in the test layer). |

---

## Port Decision Table

One row per source **artifact** (this deliverable ports build conventions, not code). Verdicts: Port / Redesign / Replace / Drop.

| Source artifact (relative to source `src\`) | Verdict | Target | Justification |
|---|---|---|---|
| `Nihdi.Core.Configuration.sln` — solution layout (packages at `src` root, `Test\UnitTest\*` folder tree, solution folders) | **Redesign** | `src/Cloudstrap.sln`, initially near-empty, with solution folders mirroring the CLAUDE.md target structure | The layout convention (flat package projects + `Test/UnitTest` tree) is good and survives; the file itself carries 40+ projects including permanently dropped ones (Functional, Bridge, STS, Dashboard test hosts) and dead x64/x86 solution platforms (all mapped to AnyCPU) — nothing to copy, only the shape. |
| `Directory.Build.props` (repo `src\` root) — `TreatWarningsAsErrors`, `EnabledCodeStyleAnalysisRuleIds`/`DisabledStyleCopRuleIds` lists, `WarningsNotAsErrors` carve-out, `NoWarn`, `StyleCop.Analyzers.Unstable 1.2.0.556` reference | **Redesign** | `src/Directory.Build.props` | Kept: `TreatWarningsAsErrors=true` and the NU190x audit-warning carve-out. **Dropped: everything StyleCop** — the SA/SX rule lists, the `WarningsNotAsErrors` carve-out for them, and the analyzer package reference — per the user's gate decision that Cloudstrap does not use StyleCop. Strictness is instead carried by the .NET SDK's built-in analyzers (`AnalysisLevel=latest-recommended`) plus `EnforceCodeStyleInBuild` against `.editorconfig` (see Behaviors). New: shared packaging metadata defaults and the CPM switch (`ManagePackageVersionsCentrally`). |
| `Nihdi.StyleCop.MsBuildProperties 1.8.2` package reference (in every `.csproj`, e.g. `Nihdi.Core.Configuration\Nihdi.Core.Configuration.csproj` line 21) | **Drop** | — | Internal, source-unavailable package whose entire job was wiring StyleCop (the rule lists + `StyleCop.Analyzers.Unstable` reference, visible inlined in the source `Directory.Build.props`). With StyleCop itself dropped by user decision there is **no effect left to reproduce** — the roadmap's original "reproduce its settings inline" instruction is superseded by that decision. |
| Per-project `stylecop.json` (8 copies, all identical: `documentationRules.companyName = "Riziv-Inami"`) | **Drop** | — | StyleCop is not used in Cloudstrap (user decision), so its settings file has no consumer; the `Riziv-Inami` company identifier had to go regardless (De-NIHDI). |
| `.editorconfig` (source `src\` root) — formatting/style/naming prefs **plus** a large `dotnet_diagnostic.SA*` severity block, header comment "Style rules for RIZIV-INAMI", `stylecop.documentation.companyName` | **Redesign** | `src/.editorconfig` | **Promoted role**: with StyleCop gone this file is the repo's *single style authority* — its IDE code-style and naming rules (build-enforced via `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors`) and formatting conventions (enforced by `dotnet format --verify-no-changes`) are the only style enforcement Cloudstrap has. The formatting, code-style, and naming sections port (they encode the house style); naming rules keep `warning` severity (build-breaking), other style prefs stay advisory unless deliberately promoted. **Deleted**: the entire `dotnet_diagnostic.SA*` severity block and `stylecop.documentation.companyName` line (dead config without StyleCop — and it contradicted the old props anyway, e.g. SA1600/SA1633 `warning` here but `NoWarn`'d there), the RIZIV header comment (De-NIHDI), and the CS1591 `suggestion` downgrade (CS1591 becomes enforced in libraries per gate decision). |
| `Test\TestProject\src\nuget.config` — `<clear/>` + single internal feed `pkgs.dev.azure.com/riziv-inami/...` | **Redesign** | root `nuget.config` | The *pattern* (`<clear/>` + exactly one explicit feed) is correct supply-chain hygiene and ports; the feed becomes `https://api.nuget.org/v3/index.json` (De-NIHDI checklist item). Moves from a nested test folder to the repo root so it governs the whole repo. Commented-out cruft and empty `<apikeys>`/credential sections dropped. |
| `azure-pipeline-dev.yml` / `azure-pipeline-main.yml` / `azure-pipeline-pr.yml` (repo root) | **Drop** | fresh `.github/workflows/ci.yml` + `release.yml` (+ `cleanup-previews.yml`) | All three are thin wrappers extending private templates (`Common/devops` repo, "Nihdi Build Agents" pool) — zero portable content. Only two facts survive as requirements: MTP-based test execution (`enableMTP: true`) and the dev/main/PR trigger split, which maps onto the two-workflow shape decided by the user. |
| Test-project shape: `MSTest.Sdk/4.2.3`, `TestingPlatformDotnetTestSupport=true` (e.g. `Test\UnitTest\Nihdi.Core.Configuration.Tests\*.csproj`) | **Replace** | NUnit 4 + `NUnit3TestAdapter` v6 in Microsoft.Testing.Platform runner mode | **User decision at this gate: NUnit, not MSTest.** The MTP execution model (self-contained test executables, no `dotnet test`) is preserved; only the framework changes. See Deliberate Behavior Changes and Dependencies. |
| Test-project extras: `coverlet.collector`, `Microsoft.Testing.Extensions.CodeCoverage`, `Microsoft.Testing.Extensions.TrxReport` | **Drop** | — | Not required by the deliverable's definition of done (build/test/format in CI). Dropping them avoids carrying *two* overlapping coverage stacks and sidesteps the non-OSI license of `Microsoft.Testing.Extensions.CodeCoverage` (closed-source Microsoft license). Coverage/reporting tooling can be added later as its own reviewed decision. |
| Manual versioning: `<Version>0.1-prerelease</Version>` hard-coded per `.csproj` | **Replace** | GitVersion 6 (CI tool + `GitVersion.yml`) | Founding-spec decision: SemVer from git tags on `main`, `-preview.N` on `dev`. Manual versions are exactly the drift GitVersion removes; CLAUDE.md forbids setting versions in code. |
| Package metadata scattered per `.csproj` (`<Company>NIHDI</Company>`, `<Authors>Platform Team</Authors>`, `nihdi` tags in CookieConsent/Matomo projects; absent elsewhere) | **Redesign** | shared metadata defaults in `src/Directory.Build.props` | Source metadata is inconsistent and NIHDI-branded. Centralized defaults (MIT license expression, repository URL, authors, icon, SourceLink-related props) guarantee every future package is complete and neutral; per-package `.csproj` keeps only description/tags/readme. |
| Per-`.csproj` `PackageReference` versions (no central management in source) | **Redesign** | `src/Directory.Packages.props` (NuGet Central Package Management) | **Accepted at gate (former OQ-6).** ~25 packages with heavily shared dependencies (OTel, Azure SDKs, Serilog) are coming; one central version file prevents the same dependency restoring at three versions across the suite and gives the "review every new PackageReference" rule a single diff surface. |
| SourceLink (absent in source) | **Replace** (with framework feature) | .NET SDK built-in Source Link | Required by the founding spec; since .NET 8 the SDK includes Source Link for GitHub by default — **zero package references needed** ([Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/source-link)). Only `ContinuousIntegrationBuild=true` in CI and symbol-package properties are wired. |
| `global.json` (absent in source) | — (new) | root `global.json` | Pins the .NET 10 SDK feature band (`rollForward: latestFeature`) so contributors and CI build with the same SDK line — cheap insurance for an analyzer-strict repo where SDK drift changes diagnostics. |

---

## Repository File Inventory (the "API surface" of this deliverable)

```
global.json                                  # SDK pin: 10.0.x, rollForward latestFeature
nuget.config                                 # <clear/> + nuget.org only
GitVersion.yml                               # versioning contract (below)
assets/icon.png                              # package icon (placeholder until final artwork supplied)
.github/workflows/ci.yml                     # PR + dev/main pushes: build, test, format; preview publish (dev only)
.github/workflows/release.yml                # dedicated stable build: tag vX.Y.Z on main → nuget.org only
.github/workflows/cleanup-previews.yml       # scheduled: keep last 20 preview versions per package on GitHub Packages
src/Cloudstrap.sln                           # near-empty solution, Test/UnitTest solution folders
src/Directory.Build.props                    # strictness + SDK analyzers + shared packaging metadata + CPM switch
src/Directory.Packages.props                 # central package versions (CPM) — the only place versions live
src/.editorconfig                            # single style authority: formatting/naming/code-style rules + severities
src/Test/Directory.Build.props               # test-project layer: IsPackable=false, doc-rules off, NUnit/MTP wiring
src/Test/UnitTest/Cloudstrap.Scaffolding.Tests/   # placeholder NUnit project proving the test leg end-to-end
```

Notes:
- `LICENSE` (MIT) and `README.md` already exist and are unchanged.
- The placeholder test project exists because the deliverable's goal explicitly requires CI to enforce **tests** before any package exists — an empty test leg is unverifiable. It contains a minimal sanity fixture, doubles as the living template for NUnit conventions, and is removed (or absorbed) when deliverable 1 brings the first real test project.
- No `dotnet.config`: the .NET 10 `dotnet test` MTP mode is deliberately not enabled — the repo convention is running the test executables directly.
- No `stylecop.json` anywhere and no third-party style analyzer package — see Behaviors.

---

## Behaviors & Conventions

### Build strictness (⚠️ analyzer ruleset is immutable after this deliverable)

**No StyleCop.** Per the user's gate decision, Cloudstrap uses no third-party style analyzer. Enforcement is native tooling only: compiler warnings, .NET SDK built-in analyzers, `.editorconfig`-driven code-style analysis, and `dotnet format`. This also matches the repo's own code-analysis guidance (prefer native compiler/SDK analyzers over StyleCop-centric workflows) and removes a low-activity third-party dependency (StyleCop.Analyzers' last release dates from 2023).

| Behavior | Default | Override |
|---|---|---|
| `TreatWarningsAsErrors` | `true`, repo-wide | None — non-negotiable (CLAUDE.md). |
| .NET SDK code-quality analyzers (CA rules) | `AnalysisLevel=latest-recommended` — **decided by recommendation** (the ruleset is frozen afterwards): `latest-recommended` enables Microsoft's curated rule set as build-breaking (via `TreatWarningsAsErrors`) without the false-positive noise of `latest-all`, which would freeze known-noisy rules as permanent build breakers. Tightening later (raising individual rule severities) stays possible; loosening does not. | Per-rule severity via `.editorconfig` `dotnet_diagnostic.CAxxxx.severity` — tightening only. |
| Code-style analysis in build | `EnforceCodeStyleInBuild=true` — IDE code-style rules (IDExxxx) at `warning`+ severity in `.editorconfig` fail the build; naming rules (e.g. `_camelCase` private fields, `I`-prefixed interfaces) are `warning` | Style rule severities live only in `src/.editorconfig`; promote a rule by raising its severity there. |
| Formatting | `dotnet format src/Cloudstrap.sln --verify-no-changes` is the CI style gate (whitespace + `.editorconfig` conventions) | House style itself changes only by editing `.editorconfig` (a reviewed, repo-wide decision). |
| NuGet audit warnings NU1901–NU1904 | Remain warnings (`WarningsNotAsErrors`) — a new vulnerability advisory published overnight must not brick every build; CI surfaces them in logs | Tighten per project by removing from `WarningsNotAsErrors`. |
| XML documentation | `GenerateDocumentationFile=true` for library projects; **CS1591 enforced (build-breaking) in `src/`** (accepted at gate), suppressed under `src/Test/` | Test layer suppresses via `src/Test/Directory.Build.props`. |
| Central package versions | `ManagePackageVersionsCentrally=true`; all versions in `src/Directory.Packages.props`; a versioned `PackageReference` in a `.csproj` fails restore (NU1008) | `VersionOverride` per reference for a documented exceptional case. |
| File headers | None required; licensing carried by `LICENSE` + `PackageLicenseExpression` — no per-file MIT/SPDX headers, and no company headers (De-NIHDI) | A future decision may introduce SPDX headers via an `.editorconfig` `file_header_template`. |

### Packaging defaults (in `src/Directory.Build.props`, applied to packable projects)

- `PackageLicenseExpression=MIT`, `Authors`, `PackageProjectUrl`/`RepositoryUrl=https://github.com/geobarteam/Cloudstrap`, `RepositoryType=git`, `PackageIcon` (from `assets/icon.png`, packed automatically), base `PackageTags`.
- SourceLink via SDK (no package): `PublishRepositoryUrl=true`, `EmbedUntrackedSources=true`; `ContinuousIntegrationBuild=true` set by CI only (deterministic release builds without breaking local incremental builds).
- Symbols: `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`. Symbol packages are published **only** with stable releases to nuget.org — GitHub Packages has no symbol server, so preview packages rely on SourceLink metadata alone.
- Per-package `.csproj` responsibility stays minimal per CLAUDE.md: `GeneratePackageOnBuild`, `GenerateDocumentationFile`, description, tags, `PackageReadmeFile`.
- Target framework: `net10.0` only, everywhere. No multi-targeting (founding spec Out of Scope).
- Placeholder `assets/icon.png` ships now so `PackageIcon` wiring is real; final artwork is a user-supplied asset swapped in-place later.

### Test conventions (NUnit — user-directed change)

- Test projects: `NUnit` (≥ 4.6.1) + `NUnit3TestAdapter` (≥ 6.2.0) + `NUnit.Analyzers` (≥ 4.14.0), `OutputType=Exe`, `EnableNUnitRunner=true` → each test project builds a **Microsoft.Testing.Platform executable** run directly (the `runTests` convention). `dotnet test` remains unsupported; the .NET 10 `dotnet.config` MTP mode for `dotnet test` is not enabled. Adapter v6 targets MTP 2.x and postdates the fix for the .NET 10 MTP-mode incompatibility (adapter issue #1267, fixed in 5.1).
- Attributes: `[TestFixture]` / `[Test]` replace `[TestClass]` / `[TestMethod]`. Naming conventions are unchanged: `<ClassUnderTest>Tests` classes, `<Method>_<Scenario>_<Expected>` methods, AAA structure.
- Moq stays as the mocking library (framework-agnostic — no NUnit incompatibility; BSD-3-Clause, OSI-approved). Not referenced until a test needs it.
- `src/Test/Directory.Build.props` layers on the root props: `IsPackable=false`, CS1591/documentation rules off, shared NUnit/MTP package wiring — so each new test csproj stays ~10 lines.

### Versioning contract (`GitVersion.yml`, GitVersion 6.x)

- Tag `vX.Y.Z` on `main` → that commit versions exactly `X.Y.Z` (stable). Tags are the only source of stable versions; nobody edits versions in code.
- Commits on `dev` → `X.Y.Z-preview.N`, `N` incrementing per commit since the last version anchor.
- Untagged commits on `main` compute a next-patch pre-version but are **never published** (gate decision).
- `hotfix/*` branches follow GitVersion's standard branch handling (patch pre-releases); stable still only via tag on `main`.

### CI/CD shape (user decisions: two workflows + preview cleanup)

**`ci.yml`** — the routine build. Triggers: pull requests, pushes to `dev` and `main`.
1. Checkout (full history for GitVersion) → setup .NET per `global.json` → GitVersion (via `gittools/actions` v4).
2. `dotnet build src/Cloudstrap.sln` (Release) — analyzers enforce strictness by failing the build.
3. Run every built MTP test executable; nonzero exit fails the job.
4. `dotnet format src/Cloudstrap.sln --verify-no-changes`.
5. **Preview publish — pushes to `dev` only** (never PRs, never `main`; gate decision): push all produced `.nupkg` (version `X.Y.Z-preview.N`) to the GitHub Packages NuGet feed (`https://nuget.pkg.github.com/<owner>/index.json`) using the workflow `GITHUB_TOKEN` (`permissions: packages: write`). No-ops when no packable projects exist.

**`release.yml`** — the dedicated stable build. Trigger: push of tag `v*` on `main` (gate decision).
1. Same build + test + format gates (a release never ships what CI didn't verify).
2. Push `.nupkg` + `.snupkg` at the stable GitVersion version to **nuget.org only** (gate decision — no GitHub Packages mirror) using the `NUGET_API_KEY` repository secret.

**`cleanup-previews.yml`** — scheduled feed hygiene (gate decision). Runs on a schedule (plus manual dispatch); deletes preview versions from the GitHub Packages feed, keeping the **most recent 20 preview versions per package** (via GitHub's maintained `actions/delete-package-versions`). Stable packages are never touched (they live on nuget.org only). Full verification is deferred until packable projects exist; until then the workflow must run green as a no-op.

Documented caveat: **GitHub Packages requires authentication even for public NuGet packages** — preview consumers need a PAT with `read:packages` ([GitHub Docs](https://docs.github.com/en/packages/learn-github-packages/about-permissions-for-github-packages)). This is acceptable for a preview channel (previews target the team and early adopters), and is why stable releases go to nuget.org.

Operational prerequisites (user, manual, before first stable publish):
- Reserve the `Cloudstrap.` ID prefix on nuget.org (founding-spec decision; verified free 2026-07-24).
- Create the `NUGET_API_KEY` repository secret.

---

## Dependencies

Everything below is build/test-time only — **nothing in this deliverable ships as a package dependency**. Notably, the repo carries **zero third-party style-analyzer packages**: strictness comes from the compiler and the .NET SDK's built-in analyzers (user gate decision).

| Dependency | Kind | License | Justification |
|---|---|---|---|
| NUnit ≥ 4.6.1 | Test framework | MIT ([nuget.org](https://www.nuget.org/packages/nunit/), latest 4.6.1, May 2026) | User decision at this gate (replaces MSTest v4). Actively maintained. |
| NUnit3TestAdapter ≥ 6.2.0 | MTP runner/adapter | MIT ([nuget.org](https://www.nuget.org/packages/NUnit3TestAdapter), v6 line supports MTP 2.x; .NET 10 MTP-mode issue #1267 fixed since 5.1) | Required for NUnit on Microsoft.Testing.Platform (`EnableNUnitRunner`), preserving the repo's test-exe execution model. |
| NUnit.Analyzers ≥ 4.14.0 | Roslyn analyzer (`PrivateAssets=all`, test projects only) | MIT ([nuget.org](https://www.nuget.org/packages/NUnit.Analyzers), 4.14.0, June 2026) | Compile-time enforcement of correct NUnit usage — replaces the MSTEST* analyzer coverage the workflow's code-analysis step relied on. |
| GitVersion 6.x via `gittools/actions` v4 | CI tool (not a package ref) | MIT (both tool and actions; GitVersion 6.7+/6.8 current, actions v4.x current) | Founding-spec versioning model; no manual versions. |
| `actions/checkout`, `actions/setup-dotnet`, `actions/delete-package-versions` | GitHub Actions | MIT (GitHub-maintained) | Standard CI plumbing; `delete-package-versions` implements the accepted preview-retention policy (keep last 20). Exact pins are planner detail. |
| ~~StyleCop.Analyzers~~ | — | — | **Not added** (user gate decision): no StyleCop in Cloudstrap; SDK analyzers + `.editorconfig` + `dotnet format` replace it. Also avoids a dependency whose last release was 2023. |
| ~~Microsoft.SourceLink.GitHub~~ | — | — | **Not added**: Source Link is built into the .NET SDK since 8.0 — one less dependency. |
| ~~Microsoft.Testing.Extensions.CodeCoverage / TrxReport, coverlet~~ | — | — | **Not added** (dropped from source shape): not required by DoD; CodeCoverage extension is not OSI-licensed. |

---

## Deliberate Behavior Changes (vs. the source repo and, where flagged, the founding spec)

1. **MSTest v4 → NUnit 4** (user directive at this gate; deviates from founding spec + CLAUDE.md — amendments listed below).
2. **StyleCop removed entirely** (user directive at this gate; deviates from the founding spec's "StyleCop + `TreatWarningsAsErrors` carried over" and from CLAUDE.md — amendments listed below). The source enforced style via `StyleCop.Analyzers` wired by `Nihdi.StyleCop.MsBuildProperties`; Cloudstrap uses .NET SDK built-in analyzers (`AnalysisLevel=latest-recommended`), `EnforceCodeStyleInBuild=true` with `.editorconfig` severities, `TreatWarningsAsErrors`, and `dotnet format --verify-no-changes` as the style gate. Per-project `stylecop.json` (with `companyName=Riziv-Inami`) and the `.editorconfig` SA-severity block go with it.
3. **XML-doc enforcement turned on** for library projects — CS1591 build-breaking in `src/`, off under `src/Test/` (accepted at gate; the source silenced CS1591 to `suggestion`).
4. **Publishing model** (all gate-decided): previews `X.Y.Z-preview.N` publish from `dev` pushes only, to GitHub Packages; stable publishes from a dedicated tag-triggered (`v*`) release workflow to nuget.org **only**; a scheduled cleanup keeps the last 20 preview versions per package. The source published via private Azure DevOps templates to an internal feed with no comparable preview/stable split.
5. **GitVersion replaces manual `<Version>` properties.**
6. **Single root `nuget.config` (nuget.org only)** replaces the internal-feed config.
7. **Central Package Management adopted** (`src/Directory.Packages.props`) — the source versioned every `PackageReference` per `.csproj`.

### Documentation amendments required (user/maintainer — this spec must not edit these files)

The two user-directed deviations (#1 NUnit, #2 no StyleCop) contradict existing project documents, which only the user amends:

- **`_specs/Cloudstrap.md`** (founding spec, Repository & Delivery): "GitHub Actions: build, test (**MSTest v4** / Microsoft.Testing.Platform), format check…" and "**StyleCop** + `TreatWarningsAsErrors` carried over (build props inlined, no internal package)".
- **`CLAUDE.md`**: context line ".NET 10 · **MSTest v4** · Moq · `Microsoft.Testing.Platform` · **StyleCop** (`TreatWarningsAsErrors`)"; Test Conventions section ("MSTest v4, `[TestClass]` / `[TestMethod]`"); RGR loop step 7a grep pattern `": (warning|error) (SA|SX|CA|CS|MSTEST)\d+"` (drop `SA|SX`; `MSTEST` → `NUNIT`); Artefacts Catalog rows for `code-analysis` ("Fix **StyleCop** / Roslyn / CA warnings") and `fix-violations` ("SA*/CA*/CS* violations, …, **StyleCop** warnings"); Pending-artefacts note "`Directory.Build.props` (**StyleCop settings inlined** — no internal build-props package)".
- **`_plans/ROADMAP.md` §0** (project-manager's file): Overview row and details "`Directory.Build.props` (**StyleCop inlined**)"; migration decision "**MSTest v4** + Microsoft.Testing.Platform"; De-NIHDI item "`Nihdi.StyleCop.MsBuildProperties` → **inlined** `Directory.Build.props`" (now: dropped outright, replaced by SDK analyzers); DoD "`Directory.Build.props` (StyleCop, `TreatWarningsAsErrors`, …)".
- **`.claude/skills/fix-violations/SKILL.md`** and **`.claude/agents/code-analysis.md`**: StyleCop-centric wording; **`.claude/instructions/tests.md`**: MSTest conventions.

---

## Out of Scope

- Any `Cloudstrap.*` package project (deliverables 1–24) — the solution ships (near-)empty.
- Docs site (docfx), per-package READMEs, sample apps — later deliverables per founding spec.
- Code coverage collection, thresholds, TRX reporting, test-result dashboards (dropped from the source test-project shape; reintroduce as an explicit decision when wanted).
- E2E/Playwright infrastructure (arrives with deliverable 12).
- GitHub Release note automation, changelog generation.
- Branch protection rules, environments, required reviewers — GitHub *settings*, configured manually by the owner, not repo files.
- Community files (CODEOWNERS, issue/PR templates, CONTRIBUTING) — not in the deliverable's definition of done.
- `.slnx` solution format (classic `.sln` keeps every documented command and tool path valid), multi-targeting, `dotnet test`/`dotnet.config` MTP mode.
- **StyleCop and any third-party style analyzer** (user gate decision — the planner must not reintroduce them).
- Everything in the founding spec's global Out of Scope (message encryption, MessagingBridge, Dynatrace, ServicePlatform, `Cloudstrap.Functional`).
- Dropped artifacts from the Port Decision Table: Azure DevOps pipeline wrappers, `Nihdi.StyleCop.MsBuildProperties`, per-project `stylecop.json`, coverage/TRX extensions, x64/x86 solution platforms.

---

## Decision Log (gate answers, 2026-07-25 — zero Open Questions remain; spec is planner-ready)

| Decision | Answer |
|---|---|
| Stable release destination | nuget.org **only** (no GitHub Packages mirror). |
| Preview channel | `dev` pushes only; untagged `main` builds and PRs publish nothing. |
| Stable release trigger | Pushing tag `v*` on `main` fires `release.yml`. |
| Preview retention | Scheduled cleanup keeps the last **20** preview versions per package on GitHub Packages. |
| Style/analyzer stack | **No StyleCop at all** (supersedes both parts of the former analyzer-strictness question): SDK built-in analyzers + `.editorconfig` + `dotnet format`. `AnalysisLevel=latest-recommended` decided by recommendation (curated build-breaking set; `latest-all` rejected — it would freeze known-noisy rules into the immutable ruleset). XML-doc enforcement (CS1591) **accepted**: build-breaking in `src/`, off under `src/Test/`. |
| Central Package Management | Accepted — `src/Directory.Packages.props` is the only place package versions live. |
| Test framework (prior gate decision) | NUnit 4 on Microsoft.Testing.Platform replaces MSTest v4. |
