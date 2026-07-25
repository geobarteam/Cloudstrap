# Claude Code Instructions — Cloudstrap

You are the coding assistant for the Cloudstrap library suite. Read and investigate relevant files before answering questions or making changes — never speculate about code you have not opened.

## Context

**Cloudstrap** is an MIT-licensed, opinionated open-source .NET library suite that simplifies bootstrapping ASP.NET Core applications for deployment on Azure: configuration + KeyVault, observability (OpenTelemetry + Application Insights), auth (OIDC / client credentials), messaging (Wolverine), background jobs (Hangfire), Blazor server and WebAssembly helpers, health checks, YARP trusted-subsystem proxy, ops dashboard, cookie consent, and analytics.

- Repo: https://github.com/geobarteam/Cloudstrap · packages published to nuget.org under the `Cloudstrap.*` prefix.
- .NET 10 · NUnit 4 · Moq · `Microsoft.Testing.Platform` · .NET SDK analyzers (`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`) — no StyleCop.
- **Founding spec: [`_specs/Cloudstrap.md`](_specs/Cloudstrap.md)** — package map, migration decisions, acceptance criteria. Read it before any extraction work.

## Extraction Phase (current)

Cloudstrap is being extracted from a private enterprise library (`Nihdi.Core.Configuration`). Until the extraction is complete:

- **Source reference repo (read-only)**: `D:\Data\gv10141\Repos\Common\Nihdi-Core-Configuration` — read it to port code, never modify it, and never copy it wholesale.
- Apply the **De-NIHDI-fication Checklist** in the spec to everything you port: no hard-coded enterprise KeyVault/storage naming, no internal hostnames/URLs/feeds, no `Nihdi`/`NIHDI`/`Riziv` identifiers, no company copyright headers.
- Replacements decided in the spec: Dynatrace → Application Insights (Azure Monitor OTel exporter; keep OTLP/Console modes) · NServiceBus → Wolverine (SQL Server durability, provider seam for PostgreSQL) · internal `Nihdi.AspNetCore.*` auth → stock ASP.NET Core auth + Duende.AccessTokenManagement · property-level message encryption → dropped · internal design system → plain MudBlazor · `Nihdi.Core.Functional` → **LanguageExt.Core** NuGet (MIT), not ported.
- Extraction proceeds bottom-up through the dependency graph (see the spec's package map): Core → Observability → Extensions/hosting → auth → Messaging → Hangfire/Proxy → Dashboard/Analytics/Localization.
- The **`project-manager`** subagent owns `_plans/ROADMAP.md` — the ordered list of port deliverables and their status. Consult it to decide what to port next; it produces the hand-off brief the **`technical-analyst`** turns into a specification `_specs/<Deliverable>.md` (critical analysis of the source code: port / redesign / replace-with-library / drop, with open questions for the user), which the `planner` then turns into a detailed `_plans/<Deliverable>.md`. Workflow: `project-manager` (what/order) → `technical-analyst` (what exactly & why) → `planner` (how) → `build-feature` (implementation).

## Aspire Coexistence (design rule)

Cloudstrap coexists with Aspire **without depending on it** — full posture + AC-ASP1–AC-ASP3 acceptance criteria in the founding spec's **Aspire Coexistence** section. In short:

- **Zero `Aspire.*` package references** in any shipped package. Aspire may appear only in docs and sample projects (the "Cloudstrap in an Aspire app" sample AppHost). The only sanctioned home for a reference is a future optional `Cloudstrap.Aspire` leaf — post-v1, user-approved.
- Build on the shared substrate instead: `Microsoft.Extensions.*`, OpenTelemetry .NET, the Azure SDK.
- **Composability is a spec-level requirement** wherever Cloudstrap overlaps Aspire ServiceDefaults: observability supports pipeline-**owner** (default) and **contribute**-only modes (enrich an existing OTel pipeline with samplers/noise filters/enrichment/`IBusinessTrace` — no duplicate exporters); typed `HttpClient` registration tolerates resilience handlers already applied via `ConfigureHttpClientDefaults`; KeyVault config is documented "Cloudstrap's or Aspire's, not both".
- **Speak platform conventions**: standard `ConnectionStrings:` names and well-known env vars (`APPLICATIONINSIGHTS_CONNECTION_STRING`) where sensible; health checks registered additively via the stock `IHealthChecksBuilder`.

---

## Critical Rules

1. After every code change, run all three:

    ```powershell
    dotnet build src/Cloudstrap.sln
    runTests                                            # dotnet test CLI is NOT compatible with .NET 10 + Microsoft.Testing.Platform
    dotnet format src/Cloudstrap.sln --verify-no-changes
    ```

2. All public types and members require XML documentation comments (`<summary>`, `<param>`, `<returns>`, `<exception>`).

3. Versioning is handled by GitVersion + `git tag` on `main` — never set versions manually in code (see the `git` subagent).

4. Minimize external dependencies — review every new `PackageReference`. Wrap external deps behind abstractions. **Only OSI-approved-licensed dependencies** (this is an MIT project; note Hangfire's LGPL in docs).

5. `internal` by default, `public` only for the intended API surface. Seal classes unless designed for inheritance.

6. Match existing patterns in the same namespace/feature before writing new code. `TreatWarningsAsErrors` is non-negotiable — zero warnings.

7. Follow the Mandatory Workflow Rules below — one RGR cycle per step, RED before GREEN, stop only at every 🛑 HUMAN GATE (placed at slice boundaries, not after every step).

---

## Scope & Boundaries

- **Changes under `src/` only** (plus `.github/workflows/` for CI, `_plans/`, `_specs/`, docs). One issue per change.
- **Features require `_plans/<FeatureName>.md` + user approval first.**
- **Risk areas** (require human review): public API surface changes · breaking changes · shared contracts · dependency updates · auth code.
- **Analyzer rules** in `Directory.Build.props` are **fixed** — do not modify.

---

## Project Structure (target)

```
_plans/                                  # Feature/extraction plans (approve before implementing)
_specs/                                  # Specifications — Cloudstrap.md is the founding spec
src/
├── Cloudstrap.Core/                     # CloudstrapOptions settings model + validation
├── Cloudstrap.Extensions/               # KeyVault config, typed HttpClients, hosting helpers
├── Cloudstrap.Observability/            # Serilog bootstrap, OTel traces/metrics/logs, correlation
├── Cloudstrap.Observability.AzureMonitor/ # Application Insights exporter wiring
├── Cloudstrap.WebApi/                   # WebApi bootstrap: versioning, Swagger/Scalar, middleware
├── Cloudstrap.Mvc/                      # MVC middleware, session hardening
├── Cloudstrap.Worker/                   # Worker-service bootstrap, health listener
├── Cloudstrap.Authentication.OpenIdConnect/   # OIDC login (stock handler + Duende ATM)
├── Cloudstrap.Authentication.ClientCredentials/ # Client-credentials tokens (Duende ATM)
├── Cloudstrap.BlazorServer/             # Blazor Server helpers (tracing, typed HttpClient)
├── Cloudstrap.BlazorWasm/               # WebAssembly client helpers: cookie auth, XSRF, Refit
├── Cloudstrap.BlazorCommon/             # Shared Blazor abstractions (ErrorHandler, Navigation, ViewModel)
├── Cloudstrap.Messaging/                # Wolverine: transports, outbox, conventions
├── Cloudstrap.Messaging.AzureBlob/      # Blob claim-check middleware
├── Cloudstrap.Hangfire/                 # Hangfire scheduler + recurring-task discovery
├── Cloudstrap.Hangfire.Proxy/           # Dashboard proxying through a proxy host
├── Cloudstrap.Proxy/                    # YARP trusted-subsystem forwarder
├── Cloudstrap.CookieConsent/            # Cookie consent UI components
├── Cloudstrap.Analytics/                # IAnalyticsTracker abstraction (consent-gated)
├── Cloudstrap.Analytics.Matomo/         # Matomo adapter (default)
├── Cloudstrap.Analytics.GoogleAnalytics/ # GA4 adapter
├── Cloudstrap.Dashboard.*/              # Ops dashboard (contracts, API, components — MudBlazor)
├── Cloudstrap.Localization/             # Thin setup over ASP.NET Core localization
├── Cloudstrap.Testing/                  # Test helper utilities
└── Test/
    ├── UnitTest/                        # Unit tests (mirror source structure, one project per package)
    ├── TestProject/                     # Blazor Server SUT — E2E smoke tests
    └── WasmTestProject/                 # Blazor WASM SUT — E2E smoke tests
```

---

## Naming Conventions

| Type | Pattern | Example |
|------|---------|---------|
| Public interface | `I<Name>` | `IAnalyticsTracker` |
| Implementation | `<Name>` | `MatomoAnalyticsTracker` |
| Extension class | `<Target>Extensions` | `ServiceCollectionExtensions` |
| Public entry point | `AddCloudstrap<Feature>` / `UseCloudstrap<Feature>` | `AddCloudstrapMessaging` |
| Options class | `<Feature>Options` | `MessagingOptions` |
| Exception | `<Name>Exception` | `ConfigurationValidationException` |
| Test class | `<ClassUnderTest>Tests` | `MatomoAnalyticsTrackerTests` |
| Test method | `<Method>_<Scenario>_<Expected>` | `Track_WithoutConsent_DoesNothing` |

Configuration lives under the `Cloudstrap:` section, one subsection per package (e.g. `Cloudstrap:OpenTelemetry:Mode`).

---

## Public API Design

- `internal` by default, `public` only for the intended API surface.
- Seal classes unless designed for inheritance. Mark `virtual` only what is intended to be overridden.
- Return interfaces or abstract types from public API for extensibility.
- Nullable reference types enabled — no `null` returns without explicit `T?` return type.
- Guard clauses on public method parameters (`ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`).
- All async public methods must accept `CancellationToken` as the last parameter.
- Use `[Obsolete("message")]` before removing public API — never remove without a deprecation cycle.
- Use `EditorBrowsable(EditorBrowsableState.Never)` to hide infrastructure types from IntelliSense.
- **Every convention has an override**: opinionated defaults (naming, paths, headers) must be configurable.

---

## NuGet Packaging

- Packages are produced by MSBuild — the `.csproj` enables this with two minimal properties:
  ```xml
  <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  ```
- No `dotnet pack` step — `GeneratePackageOnBuild` handles it automatically in Release builds.
- SemVer 2.0.0 via GitVersion — versions derived from git tags on `main`. Never set `<PackageVersion>` manually.
- `dev` branch builds get `-preview.N` prerelease suffixes automatically.
- Every package: MIT `<PackageLicenseExpression>`, embedded `<PackageIcon>`, `<PackageReadmeFile>`, repository URL, SourceLink. Target framework: `net10.0` (no multi-targeting).
- Publishing runs in GitHub Actions: previews from `dev`, stable from tags on `main`.

---

## DI Registration

Extension methods for consumers:

```csharp
namespace Cloudstrap.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds &lt;Feature&gt; services to the dependency injection container.
    /// </summary>
    public static IServiceCollection AddCloudstrapFeature(
        this IServiceCollection services,
        Action<FeatureOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);

        return services;
    }
}
```

---

## Test Conventions

- AAA (Arrange/Act/Assert), NUnit 4, `[TestFixture]` / `[Test]`; assertions use the `Assert.That` constraint model.
- Mock at boundary (interfaces only, Moq). No real external services in unit tests (no live Azure, no live Application Insights).
- Integration tests: verify DI registration, service resolution, and cross-cutting concerns.
- Messaging tests run on Wolverine's in-memory/local transport — no network.
- E2E smoke tests: run against the TestProject app that references the library as SUT.

---

## Commands

```powershell
dotnet build src/Cloudstrap.sln                              # Build
dotnet restore src/Cloudstrap.sln                            # Restore
runTests                                                     # Run tests (Microsoft.Testing.Platform)
{{TestExePath}} --filter "<TestMethod>"                      # Filtered test
dotnet format src/Cloudstrap.sln --verify-no-changes         # Format check
```

`dotnet test` is **not supported** — use the test `.exe` directly (`Microsoft.Testing.Platform` + .NET 10 SDK).

---

## Development Process

### Planning Gate

> **Before any tool call**: does this change need a plan? If yes → create `_plans/<FeatureName>.md` (use the `planner` subagent or follow the template in `.claude/templates/plan-template.md`) → **STOP and WAIT for approval**.

| Situation | Plan required? |
|-----------|---------------|
| New feature / package extraction slice | **Yes** |
| Change touching ≥ 3 files | **Yes** |
| Risk area change (auth, public API surface, shared contracts) | **Yes** |
| ≤ 2 file bugfix | No — but still RED-first |
| Config correction, simple refactor | No |

## Implementation

> After plan approval → use the **`build-feature`** skill for step-by-step implementation with Red-Green-Refactor cycles.

## Mandatory Workflow Rules

1. **Show plan before coding.**
2. **Steps must be verifiable and testable** — every step must produce observable behavior (a test passes, an API responds, a UI renders) and at least one automated test. Merge code-review-only steps into an adjacent step.
3. **One RGR cycle per step, steps run back-to-back** — never blend two steps into one cycle; between 🛑 HUMAN GATEs, continue from step to step without waiting for user approval.
4. **RED before GREEN** — write a failing test, confirm it fails, then write production code.
5. **Every bugfix gets a regression test first.**
6. **Code analysis every step** — after REFACTOR, fix all violations, then run the full test suite.
7. **🛑 STOP at HUMAN GATE** — plans place gates at slice boundaries, not after every step; do not proceed past one until the user confirms.
8. **Mark done** — check a step's `Done` box when its VERIFY passes; check a gate's boxes only after user approval. First unchecked `[ ]` in `_plans/<FeatureName>.md` = where to resume.

---

## Red-Green-Refactor-Proof Loop

Each implementation step follows this cycle:

```
1. READ    — read the plan step, understand scope and files
2. RED     — write the failing test FIRST
3. RUN     — {{TestExePath}} --filter "<TestMethod>" → confirm FAIL
4. GREEN   — write minimal production code to pass
5. RUN     — {{TestExePath}} --filter "<TestMethod>" → confirm PASS
6. REFACTOR — cleanup if needed
7. CODE ANALYSIS — fix all violations before proving:
   a. dotnet build src/Cloudstrap.sln 2>&1 | Select-String -Pattern ": (warning|error) (CS|CA|IDE|NUnit)\d+" | Sort-Object | Get-Unique
   b. dotnet format src/Cloudstrap.sln  (auto-fix formatting)
   c. Fix any remaining violations manually (skip disabled rules)
   d. Repeat a–c until the Select-String output is empty
8. PROVE:
   - dotnet build src/Cloudstrap.sln → succeeded (zero warnings/errors)
   - {{TestExePath}} → all pass
   - dotnet format src/Cloudstrap.sln --verify-no-changes → exit 0
9. MARK DONE — update _plans/<FeatureName>.md: change [ ] → [x] on this step's Done checkbox
10. NEXT — if the next plan item is a step, start its cycle immediately (no user approval
    between steps); if it is a 🛑 HUMAN GATE, STOP: present the results of all steps since
    the previous gate, wait for user approval, then check the gate's boxes
```

**Never blend steps into one cycle. Never skip RED. Never skip CODE ANALYSIS. Never proceed past a 🛑 HUMAN GATE without user confirmation.**

---

## Human Review Gates

The agent **stops and waits for user confirmation** at every gate. This is non-negotiable:

- After plan creation → user approves before any code is written.
- At each 🛑 HUMAN GATE (end of each vertical slice, plus any extra gates the plan places) → user reviews all steps completed since the previous gate before the next slice starts. Steps between gates do not require user review.
- Before any Git push → user confirms the branch/commit/PR.
- Risk area changes (auth, public API surface, shared contracts, dependency updates) → user explicitly reviews.

---

## Claude Code Artefacts Catalog

> Load the relevant artefact with `Read` before starting work on any matching task. The `subagent_type` column for agents indicates the value to pass to the Task tool when delegating.

### Subagents — `.claude/agents/`

| File | `subagent_type` | Purpose |
|------|------|---------|
| [bugfix.md](.claude/agents/bugfix.md) | `bugfix` | Fix a bug with a regression test first (RED-first, ≤ 2 files) |
| [code-analysis.md](.claude/agents/code-analysis.md) | `code-analysis` | Fix compiler / SDK analyzer diagnostics (CS/CA/IDE/NUnit); full code-quality sweep |
| [explore.md](.claude/agents/explore.md) | `explore` | Read-only codebase exploration and Q&A |
| [git.md](.claude/agents/git.md) | `git` | Git workflow: branches, GitHub PRs, tags, hotfixes, GitVersion |
| [planner.md](.claude/agents/planner.md) | `planner` | Create `_plans/<FeatureName>.md` before implementing |
| [project-manager.md](.claude/agents/project-manager.md) | `project-manager` | Own the extraction roadmap (`_plans/ROADMAP.md`): define port deliverables + order, decide the next port, produce the technical-analyst hand-off brief |
| [technical-analyst.md](.claude/agents/technical-analyst.md) | `technical-analyst` | Produce `_specs/<Deliverable>.md` from a roadmap deliverable: critical analysis of the source code (port / redesign / replace-with-library / drop), library alternatives, open questions for the user |

### Skills — `.claude/skills/`

> Load a skill's `SKILL.md` with `Read` **before** generating any code for that domain.

| Folder | When to use |
|--------|-------------|
| `add-public-api` | Adding a new public type, interface, method, or options class |
| `build-feature` | Implementing an approved `_plans/<FeatureName>.md` step by step |
| `configure-hangfire` | Setting up / extending Cloudstrap.Hangfire (topology, recurring jobs, dashboard auth) |
| `fix-violations` | Fixing CS*/CA*/IDE*/NUnit* violations, `dotnet format` issues |
| `refit` | Creating or modifying Refit HTTP service clients |
| `webapp-testing` | Writing .NET Playwright tests for the running web app |
| `pdf` / `pptx` / `xlsx` | Working with PDF / PowerPoint / Excel files |

### Per-Area Instructions — `.claude/instructions/`

> Read the matching one before editing files under its `applyTo` glob. These contain rules not duplicated in this CLAUDE.md.

| File | `applyTo` pattern | Content |
|------|------------------|---------|
| [blazor.md](.claude/instructions/blazor.md) | `src/Cloudstrap.Blazor*/**` | browser-auth patterns, HTTP client registration, distributed tracing, Scrutor scanning |
| [nuget-packaging.md](.claude/instructions/nuget-packaging.md) | `**/*.csproj` | MSBuild packaging, GitVersion SemVer, SourceLink |
| [public-api.md](.claude/instructions/public-api.md) | `src/Cloudstrap*/**` | XML docs, sealed-by-default, guard clauses, `CancellationToken`, `ObsoleteAttribute` |
| [tests.md](.claude/instructions/tests.md) | `src/Test/**` | MSTest conventions, AAA, Moq, integration test factory pattern |
| [webapi.md](.claude/instructions/webapi.md) | `src/Cloudstrap.WebApi/**` | Exception handling, correlation, API versioning, Swagger/NSwag |

### Slash Commands — `.claude/commands/`

| Command | Purpose |
|---------|---------|
| `/new-feature` | Scaffold a new feature end-to-end: planner subagent → build-feature skill, one approved step at a time |

### Templates — `.claude/templates/`

| File | Purpose |
|------|---------|
| [plan-template.md](.claude/templates/plan-template.md) | Template for `_plans/<FeatureName>.md` feature plans |
| [spec-template.md](.claude/templates/spec-template.md) | Template for `_specs/<FeatureName>.md` feature specifications |

### Pending artefacts (to author during extraction)

- `configure-wolverine` skill — replaces the old NServiceBus skill once `Cloudstrap.Messaging` exists.
- `observability` instructions — OTel + Application Insights conventions once `Cloudstrap.Observability` exists.
- `functional` instructions — LanguageExt.Core usage conventions (success/failure type mapping, `Option<T>`, `Unit`) once the first consuming package is planned. Replaces the removed `Cloudstrap.Functional` instructions — that package is not ported.
- GitHub Actions workflows (`.github/workflows/ci.yml`, `release.yml`).
- `GitVersion.yml`, `Directory.Build.props` (`TreatWarningsAsErrors` + SDK analyzer settings — no StyleCop, no internal build-props package), `src/Cloudstrap.sln`.

> Migrated artefacts were adapted from the source repo by mechanical rename; when you first use one during extraction, sanity-check examples/paths against the actual Cloudstrap code and fix drift.

---

## Conventions

- Commit format: `<type>(<scope>): <desc>`.
- Build + test + format before push.
- `protected Program() {}` in TestProject host (if applicable).
