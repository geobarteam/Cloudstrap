# Spec: 27 — Demo Applications Restructure (`src/demo`)

> **Subject**: relocating and splitting the WASM SUT (`src/Test/WasmTestProject`) into consumer-facing
> demo applications under `src/demo` — **BlazorWasm, BlazorServer, Mvc, Api** plus the shared demo
> IdP host — that serve as coding examples for the suite's main features (OIDC login,
> OAuth/client-credentials tokens, JWT-protected APIs, observability) **and** remain the maintainer's
> Playwright E2E test bed. Nothing ships to NuGet; this is a restructure + one new host, not a
> package port. The founding spec's Port Decision Table is therefore adapted: verdicts are
> **Move / Move+extend / New / Keep-in-place / Not-replicated**.
>
> **Sources read this session**: `_plans/ROADMAP.md` §27 + hand-off brief + ninth-pass notes,
> `_specs/WasmTestProjectDemoCompletion.md` (in full — increments 2–3 and OQ-1…OQ-5 reconciled below),
> `src/Test/WasmTestProject/README.md`, `src/Test/Directory.Build.props`, `src/Cloudstrap.sln`
> (project entries verified), `.claude/templates/spec-template.md`. Current-state facts marked
> "verified" in the §27 brief (breakage checklist, project inventory, Nihdi test-app precedent
> mapping) are trusted per the brief and not re-derived.

---

## User Story

**As a** developer evaluating Cloudstrap for an ASP.NET Core app on Azure,
**I want to** clone the repo and find runnable demo applications under `src/demo` — one per hosting
style I might use (WASM/BFF, Blazor Server, MVC, pure API) — each showing which one Cloudstrap call
produces which behavior,
**So that** I have coding examples I can copy into my own app;
**and as the** Cloudstrap maintainer,
**I want** those same demo apps to stay the end-to-end test bed (Playwright suite + CI),
**So that** every example is permanently proven to work and every future deliverable demonstrates
its feature in a consumer-shaped app.

---

## Context

- The only SUT today is `src/Test/WasmTestProject` (verified: `src/Test/TestProject` does not
  exist). Its Bff host **aggregates** every demonstrated surface: WASM static hosting, cookie OIDC
  (#10), JWT bearer (#5/#9), M2M typed clients (#4/#9) — the pedagogy blur
  `_specs/WasmTestProjectDemoCompletion.md` already diagnosed (its Context §3).
- The user directive (2026-08-20, verbatim in the roadmap) supersedes that spec's **OQ-4**: the
  consumer-facing demo is no longer a post-v1 docs artifact — the SUT restructures into demo apps
  now. Its increment 3 (a separate DownstreamApi JWT host) **is** the requested **Api** demo app
  (confirmed below); increment 2 (authorization demo) is sequenced below; its ⚠️ **OQ-2**
  (`RoleClaimType`/`NameClaimType` change to shipped `Cloudstrap.WebApi`) is **explicitly not
  decided by this spec** — it keeps its own user gate.
- Nihdi test-apps precedent (layout/feature inspiration only — nothing ports textually): complete
  miniature solutions under one root, one shared local STS serving all stacks, shared Contracts,
  per-app README feature matrices, VS Code compound launches with sequenced prereqs, observability
  modes deliberately varied per app. Anti-patterns that must **not** be replicated: committed
  secrets + real PII in test claims, cloud Service Bus dependency, `WebApplication1` naming,
  committed Azurite state.

---

## Verdict Table

| # | Item (current location) | Verdict | Target | Justification |
|---|---|---|---|---|
| V-1 | `Cloudstrap.WasmTestProject.Contracts` | **Move** | `src/demo/Shared/Contracts` → `Cloudstrap.Demo.Contracts` (D-A) | Shared DTOs; the Api demo app will share the echo/doctor contracts, so it belongs in `Shared/`, not inside the BlazorWasm app. |
| V-2 | `Cloudstrap.WasmTestProject.Presentation` (MudBlazor RCL) | **Move** | `src/demo/BlazorWasm/Presentation` | Consumed only by the WASM client — it is the BlazorWasm app's UI, not shared infrastructure; keeping it inside that app keeps each demo self-explaining. |
| V-3 | `Cloudstrap.WasmTestProject.Host.Wasm` | **Move** | `src/demo/BlazorWasm/Client` | The WASM SPA half of the BFF pattern; unchanged behavior. |
| V-4 | `Cloudstrap.WasmTestProject.Host.Bff` | **Move+extend** | `src/demo/BlazorWasm/Bff` | Becomes the BlazorWasm demo's server. One behavior change: `UserApi` retargets from the self-loop to the new Api host (V-8) — the increment-3 fix for the scheme-blur. `SelfApi` self-loop stays (prior spec V-3 verdict: it demos #4's correlation hop + dependency readiness, and a self-loop is the cheapest live peer). `MachineController` bearer-pin stays (prior V-5: the AC-OIDC9 coexistence demo + #9 E2E contract). |
| V-5 | `Cloudstrap.WasmTestProject.Host.IdentityProvider` | **Move** | `src/demo/Shared/IdentityProvider` | The Nihdi precedent's "one shared STS serving all stacks", already built; all demo apps authenticate against it. Demo-only posture (placeholder credentials, no counters) unchanged. |
| V-6 | `Cloudstrap.WasmTestProject.Host.Mvc` | **Move** | `src/demo/Mvc` | Becomes the Mvc demo app. Stays the deliberately minimal "`Cloudstrap.Mvc` README consumer example, live" (#6 D-3): anonymous, no IdP dependency. Gains no OIDC (D-F). |
| V-7 | `Cloudstrap.WasmTestProject.E2E.Tests` (16 files) | **Move** | `src/Test/E2E/Cloudstrap.Demo.E2E.Tests` (D-C) | Stays a test project under `src/Test` — the CI glob (`src/Test/**/*.Tests.csproj`) and the load-bearing MTP wiring in `src/Test/Directory.Build.props` keep working with zero CI-logic change; `src/demo` stays purely "examples consumers read". |
| V-8 | *(new)* Api demo app | **New** | `src/demo/Api` → `Cloudstrap.Demo.Api` | **This is `_specs/WasmTestProjectDemoCompletion.md` increment 3's `Host/DownstreamApi`, realized directly in its final home** (confirmed, per the §27 brief's OQ-d): pure JWT surface with #5's `RequireAuthenticatedEndpoints = true` default finally demonstrated live, `GET api/v1/downstream/whoami` echo, Console observability mode, health probes. Building it under `src/Test` first and moving it later would be waste. |
| V-9 | *(new)* BlazorServer demo app | **New** | `src/demo/BlazorServer` → `Cloudstrap.Demo.BlazorServer` | The user's directive names it; everything it demonstrates is shippable today with **shipped packages only** (stock Blazor Server + `AddCloudstrapOpenIdConnect` + user/M2M typed client to the Api + observability OTLP mode — the Nihdi `Host.Wfe` precedent). Scaffolded now, deliberately minimal (D-B); #12 later extends it with the `Cloudstrap.BlazorServer` helpers instead of scaffolding a SUT (roadmap §12 scope note written to tolerate this). |
| V-10 | `Cloudstrap.TestIdentityProvider` library (`src/Test/TestIdentityProvider/`) | **Keep-in-place (D-D)** | unchanged | Dual audience: the demo IdP host **and** three unit-test projects reference it; it is also deferred #26's promotion source. Moving it under `src/demo` would point unit tests into the demo tree (wrong dependency direction) and complicate the #26 path. Only the *host* moves (V-5). |
| V-11 | `src/Test/Directory.Build.props` (NoWarn + MTP wiring) | **Keep-in-place + sibling** | new `src/demo/Directory.Build.props` | Load-bearing: demo projects leaving `src/Test` lose `IsPackable=false` + NoWarn unless an equivalent lands under `src/demo`. The demo variant carries the app-relevant subset only (no MTP block — no `.Tests` projects live under `src/demo`). |
| V-12 | Increment 2 of `_specs/WasmTestProjectDemoCompletion.md` (authorization demo: `DoctorEditor` policy, guest user, 403 shaping) | **Not folded in — sequenced after (D-E)** | its own follow-up plan, immediately after #27's final gate | Orthogonal to the restructure (cookie-surface only, no file moves); folding it in grows an already-broad deliverable. Its own OQ-1/OQ-5 get answered at that plan's gate; the demo apps are its home either way. |
| V-13 | Nihdi precedent: per-app README feature matrices, compound launch with sequenced prereqs, observability modes varied per app | **Adopt** | see Behaviors | These are exactly what makes demo apps documentation-by-example; near-zero code cost. |
| V-14 | Nihdi precedent: two-phase bootstrap-logger boilerplate per Program.cs | **Not-replicated** | — | `UseCloudstrapObservability` already owns startup logging; hand-rolled bootstrap ceremony in demos would teach an anti-pattern. |
| V-15 | Nihdi anti-patterns: committed secrets/PII in claims, cloud Service Bus dependency, `WebApplication1` naming, committed emulator state | **Not-replicated** | — | De-NIHDI checklist items; placeholder-only credentials rule already established (#9/#10 harness notes). |

---

## Target Structure

```
src/demo/
├── Directory.Build.props            IsPackable=false · GenerateDocumentationFile=false
│                                    NoWarn CS1591;CA1848;CA1873 (no MTP block)
├── README.md                        suite overview: architecture diagram, port map, run matrix
├── Shared/
│   ├── Contracts/                   Cloudstrap.Demo.Contracts
│   └── IdentityProvider/            Cloudstrap.Demo.IdentityProvider   (host; references the
│                                    Cloudstrap.TestIdentityProvider library, which stays in src/Test)
├── Api/                             Cloudstrap.Demo.Api                (new — pure JWT API)
├── Mvc/                             Cloudstrap.Demo.Mvc
├── BlazorServer/                    Cloudstrap.Demo.BlazorServer       (D-B: scaffolded now, minimal)
└── BlazorWasm/
    ├── Bff/                         Cloudstrap.Demo.BlazorWasm.Bff
    ├── Client/                      Cloudstrap.Demo.BlazorWasm.Client
    └── Presentation/                Cloudstrap.Demo.BlazorWasm.Presentation

src/Test/
├── E2E/Cloudstrap.Demo.E2E.Tests/   the relocated Playwright suite (D-C)
├── TestIdentityProvider/…           unchanged (D-D)
└── UnitTest/…                       unchanged
```

Solution folders mirror the tree (`demo` → `Shared`/`Api`/`Mvc`/`BlazorWasm`/`BlazorServer`); the
known **duplicate `Host.Mvc` NestedProjects entry** in `src/Cloudstrap.sln` is removed as part of
the sln rewrite.

**Naming (D-A — `Cloudstrap.Demo.*`)**: demo apps are documentation-by-example — consumers copy
namespaces out of them. `Cloudstrap.WasmTestProject.Host.Mvc` is wrong twice over in a demo folder
(it is neither a WASM project nor a test), and freezing the old names makes the confusion
permanent. The rename cost is mechanical namespace churn confined to demo + E2E projects (nothing
published, no shipped package touched), paid once — and paid in a separate commit from the move so
git rename detection keeps history legible (see Migration).

---

## Per-App Feature Matrix (what each demo app demonstrates, with shipped packages only)

| Demo app | Port(s) | Packages demonstrated (the one-call surface) | Observability mode | E2E coverage |
|---|---|---|---|---|
| **Api** (new) | **5330** | Core binding · WebApi composite pipeline, versioning, OpenAPI/Scalar, problem details (#5) · `AddCloudstrapJwtBearer` with **`RequireAuthenticatedEndpoints` left at its `true` default** — the hardened whole-app posture's first live demo · `MapCloudstrapHealthChecks` probes (#4) | **Console** (fixture-capturable) | new tests: anonymous `whoami` → 401 (fallback policy, no attribute); `/healthz` → 200 anonymously (probe carve-out); cross-process token echo (see Bff row) |
| **BlazorWasm** (Bff + Client + Presentation) | 5300 (+5301–5304 second-instance) | Everything it demos today, unchanged: Core (#1), Observability + correlation + business spans (#2), AzureMonitor mode (#3), typed clients + KeyVault-style probes (#4), WebApi pipeline (#5), client credentials M2M via `SelfApi` (#9), cookie OIDC + `MapCloudstrapAuthenticationEndpoints` + `UserApi` user-token forwarding (#10) — **`UserApi` retargeted to the Api host**, so user-token forwarding becomes a real cross-process trusted-subsystem hop (`host: demo-api` marker in the echo) | **AzureMonitor** (unreachable connection string; Console default on — unchanged E2E posture) | the entire existing suite, unchanged, plus the retargeted `api/v1/user/call` assertion (subject = seeded user, clientId = web client, host = the Api process) |
| **Mvc** | 5320 | `AddCloudstrapMvc` + `UseCloudstrapMvc` and nothing else (#6): hardened session cookie, content-negotiated error handling, the under-ten-lines consumer example — anonymous by design (D-F) | Console (via the Mvc composite's defaults) | existing `MvcHostTests`, path constants updated |
| **BlazorServer** (D-B) | **5340** | Stock Blazor Server + `AddCloudstrapOpenIdConnect` (interactive login on a server-rendered app — the second consumer of #10) · one page calling the Api via a user-flagged typed client (#4/#9/#10) · `UseCloudstrapObservability` | **OTLP** (endpoint defaulting to the conventional localhost collector; README states no collector is required to boot) | new: sign-in at the shared IdP + authenticated page render + zero console errors |
| **Shared/IdentityProvider** | 5310 | not a Cloudstrap demo — demo-only OpenIddict STS serving all apps; placeholder credentials; `TestIdentityProviderSeed` stays the single source of truth (fixture + host cannot drift) | — | existing `SelfHostedIdentityProviderTests` (5311/5304), path constants updated |

Together: **OIDC login** (BlazorWasm and BlazorServer), **client credentials / OAuth M2M**
(Bff `SelfApi`), **JWT-protected API called cross-process** (Bff → Api), **observability with
three modes exercised** (Console on Api, OTLP on BlazorServer, AzureMonitor on the Bff) —
satisfying the roadmap DoD's "≥ Console and OTLP both exercised somewhere".

**Port map after #27**: 5300 Bff · 5301–5304 second-instance tests · 5310 shared IdP (fixture-owned
in E2E) · 5311 IdP-host test instance · 5320 Mvc · **5330 Api** · **5340 BlazorServer** · 59999
dead-port test. ⚠️ This **deliberately diverges** from `_specs/WasmTestProjectDemoCompletion.md`
increment 3, which allocated **5320** to the DownstreamApi host — that allocation predates #6's MVC
host shipping on 5320 and is stale; 5330 is the reconciliation (recorded in Deliberate Behavior
Changes).

---

## Acceptance Criteria

Carried tripwires that must stay green throughout: **AC-A3** (zero `Nihdi.AspNetCore` references),
**AC-ASP2 posture** (zero `Aspire.*` anywhere in the demo/test closure), the #9 E2E contract
(machine 401 / cached token), the #10 contracts (AC-OIDC5 logout, AC-OIDC9 coexistence), the
increment-1 contracts (anonymous doctors API → 401, auto-trigger login), and the #6 MVC contracts.
From `_specs/WasmTestProjectDemoCompletion.md`, **AC-D7, AC-D8, AC-D9** carry over verbatim as the
Api demo app's acceptance tests (its increment 3 is realized here); its AC-D1…AC-D6 belong to the
authorization follow-up (V-12), not to #27.

| # | Given | When | Then |
|---|-------|------|------|
| AC-DR1 | The completed restructure | `dotnet build src/Cloudstrap.sln` + every unit-test exe + the E2E exe + `dotnet format --verify-no-changes` run | All green; every pre-existing E2E and unit test passes with unchanged *behavioral* assertions (only paths/namespaces/ports differ). |
| AC-DR2 | The updated `.github/workflows/ci.yml` | CI runs on the restructure branch | Green: the test-project glob still discovers **all** test projects including the relocated E2E suite; the `playwright.ps1` install path resolves. |
| AC-DR3 | A clean checkout | The developer lists `src/demo` | The four demo apps exist (Api, Mvc, BlazorWasm, BlazorServer — D-B), each with its own README, each bootable standalone via `dotnet run` (booting without its peers degrades loudly-but-gracefully, per the existing lazy-metadata precedent). |
| AC-DR4 | VS Code on a clean checkout | The compound launch configuration is started | All demo hosts boot in dependency order (IdP first); browsing the Bff's `/doctors` auto-triggers login at the shared IdP; stopping the session stops all of them. |
| AC-DR5 | The four demo apps running | The feature walk is performed (manual or E2E) | OIDC login, client-credentials M2M, and a JWT-protected **cross-process** API call (Bff → Api, `host` marker proves the peer) all demonstrably work; ≥ 2 observability modes (Console + OTLP) are exercised across the apps, AzureMonitor mode remains exercised by the E2E run. |
| AC-DR6 | The new Api host with #5 defaults | Anonymous `GET api/v1/downstream/whoami` / anonymous `GET /healthz` | **401** from the fallback policy / **200** from the probe carve-out (= carried AC-D7/AC-D8). |
| AC-DR7 | A signed-in browser session on the Bff | `GET api/v1/user/call` | The echo proves the **Api host** validated the **user's** token: seeded-user subject, web clientId, Api host marker (= carried AC-D9). |
| AC-DR8 | Each demo app's README | Compared against the app's `Program.cs` + config + the E2E suite | The feature matrix is accurate: every row names the package, the one Cloudstrap call, and the proving E2E test; the suite-level `src/demo/README.md` carries the architecture diagram + port map. |
| AC-DR9 | The full diff of the deliverable | Searched for secrets and enterprise identifiers | Zero committed secrets (placeholder-only credentials, all visibly fake); zero `Nihdi`/`NIHDI`/`Riziv` identifiers; zero real PII in seeded claims; zero `Aspire.*` references. |
| AC-DR10 | Any file moved by the restructure | `git log --follow <new path>` | History traces back across the move (move and rename land in separate commits — see Migration). |
| AC-DR11 | The breakage checklist (table below) | Walked item-by-item at the final gate | Every item verified closed; no stale `src/Test/WasmTestProject` path remains anywhere in the repo (`grep` sweep), except in historical documents (ROADMAP change log, delivered plans/specs' historical narrative). |
| AC-DR12 | The standing-rule doc set | Reviewed at the final gate | Workflow rule 9 / planner rule 15 / project-manager DoD / tests.md / plan-template / SUT README successors all carry the new canonical wording (below); no doc instructs extending `src/Test/WasmTestProject`. |
| AC-DR13 | The demo apps' project references | Inspected | Shipped Cloudstrap packages via `ProjectReference` only (nothing published yet); **no** `Cloudstrap.BlazorServer`/`Cloudstrap.BlazorWasm` helper code pre-implemented (#12/#13 scope guard); no new NuGet dependencies beyond the already-pinned test/demo set. |
| AC-DR14 | `src/demo/Directory.Build.props` | A demo project builds | `IsPackable=false`, no XML-doc requirement, the demo NoWarn set applies; no demo project produces a package in Release. |

---

## Behaviors & Conventions (defaults and their overrides)

| Opinionated default | Override |
|---|---|
| Ports: 5300/5310/5320/5330/5340 per the port map | `ASPNETCORE_URLS` / launchSettings per host; `Cloudstrap:HttpClients:*:BaseAddress`; IdP redirect URIs via the existing `ApplicationBaseAddresses`-style config key (renamed with the projects, D-A) |
| Observability mode varied per app (Console/OTLP/AzureMonitor) | Each app's `Cloudstrap:OpenTelemetry:Mode` in `appsettings.json` — one key, per-app README says so |
| Demo apps reference packages by `ProjectReference` | Consumers copying the example use the equivalent `PackageReference` — each README states the mapping |
| `Cloudstrap.Demo.*` naming + `src/demo` layout (D-A) | n/a — repository convention, not runtime behavior |
| Credentials: placeholder-only, visibly fake, local loopback IdP | Real deployments: KeyVault / env vars / user-secrets — restated in every README that shows a `ClientSecret` |
| E2E fixture boots IdP → Api → Bff; Mvc/IdP-host tests boot their own instances | `CLOUDSTRAP_E2E_BASEURL` attach mode unchanged |
| `UserApi` targets the Api host (readiness note: standalone Bff runs see the dependency unhealthy, same as today's `SelfApi` note) | `Cloudstrap:HttpClients:UserApi:BaseAddress`; `EnableHealthCheck` flag |

**Standing-rule canonical rewording** (AC-DR12; final wording user-approved at the gate):

> **Demonstrate every migrated feature in the demo apps** — each deliverable's plan ends with a
> demonstration slice extending the appropriate app under `src/demo` (API/hosting features →
> `Cloudstrap.Demo.Api` · interactive/BFF/browser features → the BlazorWasm app · MVC features →
> `Cloudstrap.Demo.Mvc` · Blazor Server features → `Cloudstrap.Demo.BlazorServer` · worker/messaging
> features → the demo app their deliverable designates) plus ≥ 1 E2E test in
> `Cloudstrap.Demo.E2E.Tests` proving the behavior through the running app, before its final 🛑 gate.

Doc files carrying it (the update set): `CLAUDE.md` (workflow rule 9, project-structure tree,
commands section, test-conventions E2E line), `.claude/agents/planner.md`,
`.claude/agents/project-manager.md`, `.claude/instructions/tests.md`,
`.claude/templates/plan-template.md`, the roadmap preamble forward note (project-manager applies at
✅-flip), the relocated suite README(s), and a reconciliation note in
`_specs/WasmTestProjectDemoCompletion.md` (AC-D11's diff-scope pin + increment 3 marked
realized-by-#27 + the 5320→5330 port change — user-approved edit at the gate, since that spec's
open questions belong to its own gate).

---

## Dependencies

**No new NuGet packages.** The demo apps and E2E suite use only the already-CPM-pinned set
(MudBlazor, Microsoft.Playwright, Microsoft.AspNetCore.Components.*, OpenIddict via the unchanged
`Cloudstrap.TestIdentityProvider` library) plus `ProjectReference`s to shipped Cloudstrap packages.
The BlazorServer app (D-B) uses only framework `Microsoft.AspNetCore.Components.Server`
(part of the shared framework) + existing pins.

---

## Migration Strategy & Breakage Verification

Strategy decisions (the planner turns these into steps; recorded here because they are one-way
choices the user should see):

1. **`git mv` with move and rename separated**: commit A relocates the tree with original project
   names **and** simultaneously fixes every path reference (sln, props, ci.yml, SutProcess,
   PageTestBase message, `.vscode`) so the commit is CI-green; commit B (or a slice) renames
   projects/namespaces to `Cloudstrap.Demo.*` (D-A). Separation keeps git rename
   detection above the similarity threshold — AC-DR10.
2. **Incremental slices, E2E net never dark**: relocate+fix → rename → Api app + `UserApi`
   retarget → BlazorServer (D-B) → READMEs + process docs. Every slice ends with the
   full E2E suite green; the 16-file suite is the regression net for the whole deliverable.
3. **The Api app is built in place** under `src/demo/Api` (never under `src/Test` first).

Breakage checklist as the verification table (every row closed at the final gate — AC-DR11; file
references verified by the project-manager 2026-08-20):

| Item | What breaks | Verification |
|---|---|---|
| `src/Cloudstrap.sln` | 14 project paths, solution folders, duplicate `Host.Mvc` NestedProjects entry | sln loads; duplicate entry gone; folders mirror `src/demo` |
| `src/Test/Directory.Build.props` | demo projects lose `IsPackable=false`/NoWarn when leaving `src/Test`; the `.Tests`-suffix MTP condition must keep covering the relocated E2E project | new `src/demo/Directory.Build.props` in effect (AC-DR14); E2E exe still an MTP executable |
| `.github/workflows/ci.yml` (≈ lines 44/46/51/62) | `playwright.ps1` path, test glob `src/Test/**/*.Tests.csproj`, error-message path | CI green on the branch (AC-DR2) |
| `SutProcess.cs:15` | hardcoded Bff csproj path (runtime boot) | E2E fixture boots the relocated hosts |
| `PageTestBase.cs:37` | install-instruction string with old path | failure message names the new path |
| `.vscode/tasks.json` (2 paths) + `launch.json` (3 configs + compound) | build/launch paths | AC-DR4 compound launch |
| `src/Directory.Packages.props` comments (lines 33/100) | stale path references in comments | comments updated |
| SUT README | whole document describes the old layout | superseded by `src/demo/README.md` + per-app READMEs (AC-DR8) |
| `_specs/WasmTestProjectDemoCompletion.md` AC-D11 | pins diff scope to `src/Test/WasmTestProject/**` — in direct tension with the move | reconciliation note added (user-approved, see doc-update set) |
| Standing-rule doc set (CLAUDE.md rule 9 + tree + test line, planner.md, project-manager.md, tests.md, plan-template.md, roadmap preamble) | all embed the old SUT path/wording | AC-DR12 |
| Repo-wide sweep | any other stale `src/Test/WasmTestProject` string | grep sweep clean (historical docs exempt) |

---

## Deliberate Behavior Changes

1. **`UserApi` retargets** from the Bff self-loop to the Api host; `api/v1/user/call`'s response
   gains the `host` marker (the cross-process proof). Adopted from
   `_specs/WasmTestProjectDemoCompletion.md` increment 3 (its V-6/V-9/V-10), realized here.
2. **Api host port is 5330, not the 5320 that spec allocated** — 5320 was taken by the #6 MVC host
   after that spec was written; BlazorServer takes 5340.
3. **Project IDs/namespaces rename** to `Cloudstrap.Demo.*` (D-A) — pure naming, no
   runtime behavior change, nothing published.
4. **The E2E suite relocates to `src/Test/E2E/Cloudstrap.Demo.E2E.Tests`** (D-C); its behavioral
   assertions are unchanged.
5. **No shipped `Cloudstrap.*` package changes.** If the restructure exposes a package defect, it
   routes as a separate RED-first bugfix (roadmap standing constraint), never inside #27.

---

## Out of Scope

- **#12/#13 package content** — no `Cloudstrap.BlazorServer`/`Cloudstrap.BlazorWasm` helper code,
  no XSRF/Refit/auth-state-provider pre-implementation; the BlazorServer demo (D-B)
  uses stock Blazor Server + shipped packages only. The increment-4 handover inventory of
  `_specs/WasmTestProjectDemoCompletion.md` transfers to the new paths unchanged.
- **The authorization demo** (that spec's increment 2: `DoctorEditor` policy, guest user, 403
  shaping) — sequenced as its own follow-up plan after #27's final gate (V-12, D-E), not folded
  into the restructure.
- **That spec's ⚠️ OQ-2** (`RoleClaimType`/`NameClaimType` defaults in shipped `Cloudstrap.WebApi`)
  — explicitly *not decided here*; it keeps its own user gate (auth risk area).
- **NuGet packaging of demo apps** — nothing under `src/demo` is packable, ever.
- **`Cloudstrap.TestIdentityProvider` promotion** — deferred #26 stays deferred; this deliverable
  must not complicate its path (V-10).
- **Messaging/Hangfire/proxy/dashboard demos** — arrive with #14+ and extend these apps then.
- **A "Cloudstrap in an Aspire app" sample** — separate docs/samples concern (founding-spec
  posture); no Aspire anywhere here.
- Founding-spec out-of-scope items restated per checklist: message encryption, MessagingBridge,
  Dynatrace, ServicePlatform, `Cloudstrap.Functional`.

---

## Decision Log (gate answers, 2026-08-20 — zero Open Questions remain; spec is planner-ready)

All six gate questions were answered by the user on 2026-08-20; each accepted this spec's
recommendation as-is. The full findings/options/rationale for each question live in this repo's
git history of this file (the pre-gate draft); the decided outcomes are:

| # | Question | Answer (user, 2026-08-20) |
|---|----------|---------------------------|
| **D-A** | Naming scheme: rename to `Cloudstrap.Demo.*` or keep `Cloudstrap.WasmTestProject.*` under `src/demo`? | **Rename to `Cloudstrap.Demo.*`** (`…Demo.Api`, `…Demo.Mvc`, `…Demo.BlazorWasm.{Bff,Client,Presentation}`, `…Demo.BlazorServer`, `…Demo.Contracts`, `…Demo.IdentityProvider`, `…Demo.E2E.Tests`) — in a **separate commit after the move commit**, per the AC-DR10 history-preservation mechanics. Basis: nothing is published (breaking is free pre-release), demo namespaces are what consumers copy, and "WasmTestProject" as the permanent name of the Mvc/Api demos is the `WebApplication1`-class confusion the De-NIHDI precedent forbids. |
| **D-B** | BlazorServer demo app: scaffold now with shipped packages, or defer to #12? | **Scaffold NOW, deliberately minimal** — stock Blazor Server + **shipped packages only**: `AddCloudstrapOpenIdConnect` interactive login, a typed HttpClient call to the Api demo app, OTLP observability mode. No `Cloudstrap.BlazorServer` helper code pre-implemented; #12 later **extends** this app (roadmap §12 scope note). Basis: directly answers the user's four-app directive, demonstrates OIDC on the second host style today, and gives #12 a consumer-shaped app instead of a greenfield scaffold mid-deliverable. |
| **D-C** | Where does the E2E suite live? | **`src/Test/E2E/Cloudstrap.Demo.E2E.Tests`** (name per D-A). Basis: zero CI-glob change (`src/Test/**/*.Tests.csproj` still matches), the load-bearing `.Tests`-suffix MTP wiring in `src/Test/Directory.Build.props` keeps applying, and `src/demo` stays purely "examples consumers read" — the smallest breakage surface on the two load-bearing files. |
| **D-D** | Where does the `Cloudstrap.TestIdentityProvider` library live? | **Stays at `src/Test/TestIdentityProvider/`; only the IdP demo *host* moves to `src/demo/Shared/IdentityProvider`.** Basis: three unit-test projects reference the library — dependency direction must stay demo → test-infrastructure, never test → demo — and deferred #26's promotion path stays exactly as recorded. |
| **D-E** | How do `_specs/WasmTestProjectDemoCompletion.md` increments 2–3 fold in? | **Increment 3 (DownstreamApi JWT host) folds into #27 as the Api demo app** (confirmed structurally identical; its OQ-3 thereby resolved "now-as-part-of-#27"); **increment 2 (authorization demo) becomes its own follow-up plan immediately after #27's final gate** (its OQ-1/OQ-5 answered at that plan's approval); **that spec's ⚠️ OQ-2** (`RoleClaimType`/`NameClaimType` change to shipped `Cloudstrap.WebApi`) **keeps its own user gate — untouched by #27**. |
| **D-F** | Does the Mvc demo app gain OIDC login? | **No — the Mvc demo stays minimal and anonymous**: `AddCloudstrapMvc` + `UseCloudstrapMvc` and nothing else, preserving the "#6 README consumer example, live" teaching point. OIDC stays demonstrated by the BlazorWasm and BlazorServer apps; the suite-level README's feature matrix states where each feature lives. |
