# Claude Code Instructions — Cloudstrap

You are the coding assistant for the Cloudstrap library suite. Read and investigate relevant files before answering questions or making changes — never speculate about code you have not opened.

The development workflow is the spec-driven kit's, in `.claude/rules/spec-driven.md`; its values for this repository are in **§ Project settings** at the end of this file.

## Context

**Cloudstrap** is an MIT-licensed, opinionated open-source .NET library suite that simplifies bootstrapping ASP.NET Core applications for deployment on Azure: configuration + KeyVault, observability (OpenTelemetry + Application Insights), auth (OIDC / client credentials), messaging (Wolverine), background jobs (Hangfire), Blazor server and WebAssembly helpers, health checks, YARP trusted-subsystem proxy, ops dashboard, cookie consent, and analytics.

- Repo: https://github.com/geobarteam/Cloudstrap · packages published to nuget.org under the `Cloudstrap.*` prefix.
- .NET 10 · NUnit 4 · Moq · `Microsoft.Testing.Platform` · .NET SDK analyzers (`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`) — no StyleCop.
- **Founding spec: [`_specs/Cloudstrap.md`](_specs/Cloudstrap.md)** — package map, migration decisions, acceptance criteria. Read it before any extraction work.

## Extraction Phase (current)

Cloudstrap is being extracted from a private enterprise library (`Nihdi.Core.Configuration`). Until the extraction is complete:

- **Source reference repo (read-only)**: `D:\source\Nihdi-Core-Configuration` — sources live under `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\`. Read it to port code, never modify it, and never copy it wholesale.
- Apply the **De-NIHDI-fication Checklist** in the spec to everything you port: no hard-coded enterprise KeyVault/storage naming, no internal hostnames/URLs/feeds, no `Nihdi`/`NIHDI`/`Riziv` identifiers, no company copyright headers. It applies to terminology too (e.g. the committee name `Cfe` became the industry term `Bff`).
- Replacements decided in the spec: Dynatrace → Application Insights (Azure Monitor OTel exporter; keep OTLP/Console modes) · NServiceBus → Wolverine (SQL Server durability, provider seam for PostgreSQL) · internal `Nihdi.AspNetCore.*` auth → stock ASP.NET Core auth + Duende.AccessTokenManagement · property-level message encryption → dropped · internal design system → plain MudBlazor · `Nihdi.Core.Functional` → **LanguageExt.Core** NuGet (MIT), not ported.
- Extraction proceeds bottom-up through the dependency graph (see the spec's package map). **What is done, what remains and in which order, and the porting rules** (Port / Redesign / Replace / Drop per source feature, cloud-native Azure hosting only, definition of done) are in [`docs/extraction-backlog.md`](docs/extraction-backlog.md). Each remaining deliverable is a feature of the spec-driven workflow: `/new-feature`, spec `_specs/<N>-<Deliverable>/spec.md`.
- Nothing is published yet: breaking changes to already-extracted packages are allowed; fix an earlier package at its source rather than working around it.
- **Every extracted feature is demonstrated in the demo apps** under `src/demo`, with ≥ 1 E2E test in `Cloudstrap.Demo.E2E.Tests` proving it through the running app (in the final review of the feature). The demo app per feature type: API/hosting features → `Cloudstrap.Demo.Api` · interactive/BFF/browser features → the BlazorWasm app · MVC features → `Cloudstrap.Demo.Mvc` · Blazor Server features → `Cloudstrap.Demo.BlazorServer` · worker / headless-hosting features → `Cloudstrap.Demo.Worker` · messaging features → the demo app their spec designates.

## Aspire Coexistence (design rule)

Cloudstrap coexists with Aspire **without depending on it** — full posture + AC-ASP1–AC-ASP3 acceptance criteria in the founding spec's **Aspire Coexistence** section. In short:

- **Zero `Aspire.*` package references** in any shipped package. Aspire may appear only in docs and sample projects (the "Cloudstrap in an Aspire app" sample AppHost). The only sanctioned home for a reference is a future optional `Cloudstrap.Aspire` leaf — post-v1, user-approved.
- Build on the shared substrate instead: `Microsoft.Extensions.*`, OpenTelemetry .NET, the Azure SDK.
- **Composability is a spec-level requirement** wherever Cloudstrap overlaps Aspire ServiceDefaults: observability supports pipeline-**owner** (default) and **contribute**-only modes (enrich an existing OTel pipeline with samplers/noise filters/enrichment/`IBusinessTrace` — no duplicate exporters); typed `HttpClient` registration tolerates resilience handlers already applied via `ConfigureHttpClientDefaults`; KeyVault config is documented "Cloudstrap's or Aspire's, not both".
- **Speak platform conventions**: standard `ConnectionStrings:` names and well-known env vars (`APPLICATIONINSIGHTS_CONNECTION_STRING`) where sensible; health checks registered additively via the stock `IHealthChecksBuilder`.

---

## Critical Rules

1. Verification runs through `node .claude/hooks/verify.mjs` (the commands are in `.claude/spec-driven.json`; see § Project settings § Verification Commands). `dotnet test` is **not** supported with .NET 10 + Microsoft.Testing.Platform — run the test `.exe` directly.
2. All public types and members require XML documentation comments (`<summary>`, `<param>`, `<returns>`, `<exception>`).
3. Versioning is handled by GitVersion + `git tag` on `main` — never set versions manually in code (see the `git` subagent).
4. Minimize external dependencies — review every new `PackageReference`. Wrap external deps behind abstractions. **Only OSI-approved-licensed dependencies** (this is an MIT project; note Hangfire's LGPL in docs).
5. `internal` by default, `public` only for the intended API surface. Seal classes unless designed for inheritance.
6. Match existing patterns in the same namespace/feature before writing new code. `TreatWarningsAsErrors` is non-negotiable — zero warnings.

---

## Scope & Boundaries

- **Changes under `src/` only** (plus `.github/workflows/` for CI, `_specs/`, docs). One issue per change.
- **Analyzer rules** in `Directory.Build.props` are **fixed** — do not modify.

---

## Project Structure (target)

```
_specs/                                  # Specs — Cloudstrap.md is the founding spec; <FeatureName>/spec.md per feature
docs/                                    # extraction-backlog.md, upstream advisory notes
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
├── Cloudstrap.BlazorCommon/             # Shared Blazor abstractions (ErrorHandler, ViewModel, convention scan)
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
├── demo/                                # Demo applications — consumer examples + E2E test bed
│   ├── Api/                             #   Cloudstrap.Demo.Api — pure JWT host, hardened by default (5330)
│   ├── BlazorServer/                    #   Cloudstrap.Demo.BlazorServer — OIDC + user-token client (5340)
│   ├── BlazorWasm/                      #   Bff (5300) + Client + Presentation — the WASM/BFF demo
│   ├── Mvc/                             #   Cloudstrap.Demo.Mvc — two-call MVC example (5320)
│   ├── Worker/                          #   Cloudstrap.Demo.Worker — headless worker + health listener (5350)
│   └── Shared/                          #   Contracts (shared DTOs) + IdentityProvider (demo IdP host, 5310)
└── Test/
    ├── UnitTest/                        # One project per package: unit tests + host-level integration tests
    ├── E2E/                             # Cloudstrap.Demo.E2E.Tests (NUnit 4 + Playwright) — drives the demo apps
    ├── TestIdentityProvider/            # Cloudstrap.TestIdentityProvider — shared OpenIddict test IdP library
    └── TestInfrastructure/              # Cloudstrap.TestInfrastructure — shared test helpers (SQL test DB, recorder, host); test-only, never packed
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
- Use `[Obsolete("message")]` before removing public API — never remove without a deprecation cycle (once packages are published).
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
namespace Cloudstrap.Extensions
{
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
}
```

Namespaces are block-scoped, with `using` directives inside the namespace (`src/.editorconfig`).

---

## Test Conventions

- AAA (Arrange/Act/Assert), NUnit 4, `[TestFixture]` / `[Test]`; assertions use the `Assert.That` constraint model.
- Mock at boundary (interfaces only, Moq). No real external services in tests (no live Azure, no live Application Insights).
- Host-level integration tests boot the package through its public entry points (§ Project settings § Test levels); SQL Server / Azurite through `Cloudstrap.TestInfrastructure`.
- Messaging tests run on Wolverine's in-memory/local transport — no network.
- E2E tests: NUnit 4 + Microsoft.Playwright in `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/` — the fixture boots the demo IdP, the Api host and the Bff and drives headless Chromium against the demo apps (see `src/demo/README.md`; one-time `playwright.ps1 install chromium`).
- Details: `.claude/instructions/tests.md`; the order of the tests and the rules for agents: `.claude/rules/testing-for-agents.md`.

---

## Commands

```bash
dotnet restore src/Cloudstrap.sln                            # Restore
dotnet build src/Cloudstrap.sln -v q -nologo                 # Build
node .claude/hooks/verify.mjs --slice <IntegrationTestClass> # Slice gate: build + every test executable, no format check
node .claude/hooks/verify.mjs --feature                      # Feature gate: every command, format check included
src/Test/UnitTest/Cloudstrap.WebApi.Tests/bin/Debug/net10.0/Cloudstrap.WebApi.Tests.exe --filter "FullyQualifiedName~WebApiDefaultsTests"  # Filtered test
dotnet format src/Cloudstrap.sln --verify-no-changes         # Format check
dotnet format src/Cloudstrap.sln                             # Formatter

# E2E — one-time browser install (solution must be built first), then run like any MTP suite
pwsh src/Test/E2E/Cloudstrap.Demo.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
src/Test/E2E/Cloudstrap.Demo.E2E.Tests/bin/Debug/net10.0/Cloudstrap.Demo.E2E.Tests.exe
```

`dotnet test` is **not supported** — use the test `.exe` directly (`Microsoft.Testing.Platform` + .NET 10 SDK).

---

## Claude Code Artefacts Catalog

> Load the relevant artefact with `Read` before starting work on any matching task. The workflow's own skills, agents, hooks and templates (`new-feature`, `investigate`, `write-spec`, `build-slice`, `amend-spec`, `bugfix`, `spec-critic`, `spec-reviewer`) are listed in `.claude/rules/spec-driven.md` § Tooling.

### Subagents — `.claude/agents/`

| File | `subagent_type` | Purpose |
|------|------|---------|
| [code-analysis.md](.claude/agents/code-analysis.md) | `code-analysis` | Fix compiler / SDK analyzer diagnostics (CS/CA/IDE/NUnit) — the **Fix analysis** of the final review |
| [explore.md](.claude/agents/explore.md) | `explore` | Read-only codebase exploration and Q&A |
| [git.md](.claude/agents/git.md) | `git` | Git workflow: branches, GitHub PRs, tags, hotfixes, GitVersion |

### Skills — `.claude/skills/`

> Load a skill's `SKILL.md` with `Read` **before** generating any code for that domain.

| Folder | When to use |
|--------|-------------|
| `configure-hangfire` | Setting up / extending Cloudstrap.Hangfire (topology, recurring jobs, dashboard auth) |
| `fix-violations` | Fixing CS*/CA*/IDE*/NUnit* violations, `dotnet format` issues |
| `refit` | Creating or modifying Refit HTTP service clients |
| `webapp-testing` | Writing .NET Playwright tests for the running web app |

### Per-Area Instructions — `.claude/instructions/`

> Read the matching one before editing files under its `applyTo` glob. These contain rules not duplicated in this CLAUDE.md.

| File | `applyTo` pattern | Content |
|------|------------------|---------|
| [blazor.md](.claude/instructions/blazor.md) | `src/Cloudstrap.Blazor*/**` | browser-auth patterns, HTTP client registration, distributed tracing, Scrutor scanning |
| [nuget-packaging.md](.claude/instructions/nuget-packaging.md) | `**/*.csproj` | MSBuild packaging, GitVersion SemVer, SourceLink |
| [public-api.md](.claude/instructions/public-api.md) | `src/Cloudstrap*/**` | XML docs, sealed-by-default, guard clauses, `CancellationToken`, `ObsoleteAttribute` |
| [tests.md](.claude/instructions/tests.md) | `src/Test/**` | NUnit 4 conventions, AAA, Moq, host-level test pattern, E2E harness |
| [webapi.md](.claude/instructions/webapi.md) | `src/Cloudstrap.WebApi/**` | Exception handling, correlation, API versioning, Swagger/NSwag |

### Pending artefacts (to author during extraction)

- `configure-wolverine` skill — Wolverine conventions for `Cloudstrap.Messaging` consumers.
- `observability` instructions — OTel + Application Insights conventions for `Cloudstrap.Observability`.
- `functional` instructions — LanguageExt.Core usage conventions (success/failure type mapping, `Option<T>`, `Unit`) once the first consuming package is specified.

> Some artefacts were adapted from the source repo by mechanical rename; when you first use one, sanity-check examples/paths against the actual Cloudstrap code and fix drift.

---

## Conventions

- Commit format: `<type>(<scope>): <desc>`.
- `protected Program() {}` in demo host projects (if applicable).

---

## Project settings

The development workflow is in `.claude/rules/spec-driven.md` (spec-driven kit 3.0.0, level 3, manual skills, pack `dotnet-shared`). The skills and agents read the values below.

In this repository that workflow leads, for every feature, every extraction deliverable and every bugfix. Do not use `superpowers:brainstorming`, `superpowers:writing-plans`, `superpowers:executing-plans`, `superpowers:subagent-driven-development` or `superpowers:test-driven-development` to plan or build here, nor any skill that writes plan files or builds from a task list of its own, also when their own instructions say they are mandatory.

- **Reference feature**: `Cloudstrap.WebApi` with its host-level tests (`src/Test/UnitTest/Cloudstrap.WebApi.Tests/`, `Infrastructure/WebApiTestHost.cs`) and its demonstration in `Cloudstrap.Demo.Api`; for an extraction deliverable, also the source package in the reference repo (§ Extraction Phase)
- **Risk areas** (a human go before code): auth code, public API surface, shared contracts, dependency updates (new or updated `PackageReference`)
- **Specs folder**: `_specs/` (the founding spec `_specs/Cloudstrap.md`, the kit's `_specs/<FeatureName>/spec.md`, and the historical extraction specs `_specs/<N>-<Deliverable>.md` — see `_specs/README.md`)
- **Protected branches** (never build on them): `main`, `dev`
- **Spec branch**: `feature/<short-name>` from `dev`; one per spec, created by Claude when the spec starts, merged through a pull request
- **Commit format**: `<type>(<scope>): <desc>`
- **Guidelines**: testing (`.claude/rules/testing-for-agents.md`: the order of the tests and R-1 to R-7; frameworks, naming and structure stay in § Test Conventions and `.claude/instructions/tests.md`)
- **Models** (recommended since kit 2.6.5): `build-slice` on Sonnet 5.5; spec writing (`investigate`, `write-spec`, `amend-spec`) and review (`spec-critic`, `spec-reviewer`, the final review) on Opus 5.5

### Test levels

- **Outer test** (the integration test first in every slice, seen failing for the right reason before any production code or unit test; at least one per slice): the host-level integration test of the package the slice changes
- **Integration test** (every slice's outer test; mocked view models do not count): an NUnit 4 test that boots a real ASP.NET Core / generic host in process (TestServer, the package's `Infrastructure/*TestHost`), registers the package through its public `AddCloudstrap*` / `UseCloudstrap*` entry points on the real DI container and asserts through HTTP or the resolved services; only true externals (IdP, Azure, network) are faked; SQL Server / Azurite through `Cloudstrap.TestInfrastructure`
- **Integration test location**: the package's own test project, `src/Test/UnitTest/Cloudstrap.<Package>.Tests/` (the repository keeps unit and host-level tests in one project per package)
- **End-to-end browser test**: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/` (NUnit 4 + Playwright, driving the demo apps); at most once per feature, in the final review, never a slice's outer test. For an extraction deliverable it is required, not optional (§ Extraction Phase): the demo-app change belongs to the slice whose behavior it shows (that slice's outer test stays an integration test), and the ≥ 1 E2E test proving it through the running demo app is written in the final review
- **UI test**: bUnit in `Cloudstrap.BlazorCommon.Tests` / `Cloudstrap.BlazorServer.Tests` / `Cloudstrap.BlazorWasm.Tests`, only for what the integration test cannot observe; never a slice's outer test
- **Unit test**: pure logic of a package (options validation, mapping, guards), Moq on interfaces at the boundary only
- **Human check**: run a demo app from `src/demo` (e.g. `dotnet run --project src/demo/Api/Cloudstrap.Demo.Api.csproj`, http://localhost:5330; the BFF on 5300, IdP on 5310 — see `src/demo/README.md`)
- **Browser tool**: the Playwright MCP server, when available in the session; otherwise page checks are reported as not carried out
- **Criterion ids on tests**: the AC id in the test's XML doc `<summary>` (the existing convention, e.g. `AC-W1`), or `[Property("AC", "AC-3")]` where a filterable trait is wanted

### Code analysis

- **Analysis** (run once per feature, in the final review, after the feature gate, on the feature's changed code; never per slice): `dotnet build src/Cloudstrap.sln -v q -nologo` with the SDK analyzers of `Directory.Build.props` (`TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild`), then `dotnet format src/Cloudstrap.sln --verify-no-changes`. Plain .NET practice, no server and no account; it runs on the machine only and sends no code anywhere.
- **Fix analysis**: `dotnet format src/Cloudstrap.sln` for style and the analyzers' code fixes, by hand for the rest (the `code-analysis` agent / `fix-violations` skill)
- **Formatter**: `dotnet format src/Cloudstrap.sln`
- **Warnings in the build**: every slice leaves no new warning in the files it touched (compiler and analyzers); a rule is never switched off in `.editorconfig`, `Directory.Build.props` or with a suppression to get a build through. The NuGet audit warnings NU1902/NU1903 already present are not code warnings and are left to the owner.

### Pack values

- **Test projects**: `src/Test/UnitTest/Cloudstrap.Core.Tests/Cloudstrap.Core.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Extensions.Tests/Cloudstrap.Extensions.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Observability.Tests/Cloudstrap.Observability.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Observability.AzureMonitor.Tests/Cloudstrap.Observability.AzureMonitor.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.WebApi.Tests/Cloudstrap.WebApi.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Cloudstrap.Mvc.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Worker.Tests/Cloudstrap.Worker.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Cloudstrap.Authentication.OpenIdConnect.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/Cloudstrap.Authentication.ClientCredentials.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.BlazorCommon.Tests/Cloudstrap.BlazorCommon.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.BlazorServer.Tests/Cloudstrap.BlazorServer.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.BlazorWasm.Tests/Cloudstrap.BlazorWasm.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Messaging.Tests/Cloudstrap.Messaging.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Messaging.AzureBlob.Tests/Cloudstrap.Messaging.AzureBlob.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Hangfire.Tests/Cloudstrap.Hangfire.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.Proxy.Tests/Cloudstrap.Proxy.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.TestInfrastructure.Tests/Cloudstrap.TestInfrastructure.Tests.csproj`, `src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests/Cloudstrap.TestIdentityProvider.Tests.csproj`, `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/Cloudstrap.Demo.E2E.Tests.csproj`
- **Mutation threshold**: `80`

### Verification Commands

The commands are in `.claude/spec-driven.json` under `verify.commands`, in the order build, the test executables (Microsoft.Testing.Platform, `--no-ansi --no-progress`; `dotnet test` is not supported), the format check that changes nothing. That file is the only place they are listed; a hook reads it too; they run in Git Bash (`verify.shell: bash`). The format check runs only in the feature gate, never in the slice gate. `verify.integration`: not set — the host-level integration tests live in each package's test project, so there is no single integration command to filter; the slice gate then runs every test executable.

Two gates run them:

- **Slice gate**, at the end of every slice: `node .claude/hooks/verify.mjs --slice <the slice's integration test class>`: the build and every test executable; no format check.
- **Mutation check** (pack dotnet-shared), after the slice gate: `node scripts/mutation-check.mjs --slice <n> --base <the slice's base commit>`, Stryker.NET (`dotnet-stryker` 5.1.0, pinned in `.config/dotnet-tools.json`; run `dotnet tool restore` once) on the lines the slice changed in its production files, with every project of Pack values **Test projects**; evidence in `_specs/<FeatureName>/mutation-<n>.md`, committed with the slice; red below Pack values **Mutation threshold**.
- **Feature gate**, once in the final review: `node .claude/hooks/verify.mjs --feature`: every command.

- Not established yet: a filterable integration command (`verify.integration`)
- Single test or class: `src/Test/UnitTest/Cloudstrap.WebApi.Tests/bin/Debug/net10.0/Cloudstrap.WebApi.Tests.exe --filter "FullyQualifiedName~WebApiDefaultsTests"`
- Commands are written with forward slashes (`src/Test/...`), never with backslashes.
- A step that changes no source file (docs, specs) does not need this gate; run that step's own check instead.
- After a dependency or analyzer change, run a clean build for the proof: an incremental build can hide warnings.
