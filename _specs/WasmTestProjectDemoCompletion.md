# Spec: WasmTestProjectDemoCompletion — the WASM SUT as the complete auth/authz demonstration app

> **⚠️ Reconciliation note (deliverable #27, 2026-08-20)** — `_specs/27-DemoAppsRestructure.md`
> restructured the SUT into demo apps under `src/demo` (`Cloudstrap.Demo.*` names; E2E suite at
> `src/Test/E2E/Cloudstrap.Demo.E2E.Tests`). Three consequences for this spec:
> 1. **AC-D11's diff-scope pin (`src/Test/WasmTestProject/**`) is superseded** for the moved paths —
>    the equivalent scope is now `src/demo/**` + `src/Test/E2E/**`.
> 2. **Increment 3 is realized by #27**: the downstream JWT host exists as `Cloudstrap.Demo.Api`
>    on port **5330** (not the 5320 planned here — #6's MVC host took 5320 in the meantime),
>    `UserApi` is retargeted, and AC-D7/AC-D8/AC-D9 are covered by `ApiHostTests` +
>    `UserCall_SignedIn_ProvesTheApiHostValidatedTheUsersToken`.
> 3. **Increment 2 (the authorization demo) remains open** and runs as its own follow-up plan after
>    #27's gate (D-E). This spec's remaining Open Questions — including ⚠️ **OQ-2** (the shipped
>    `Cloudstrap.WebApi` claim-type change) — stay owned by this spec's own gate, untouched by #27.
> Identifier renames since: `wasmtestproject-*` → `demo-*`, seed user unchanged.

> **Subject**: completing `src/Test/WasmTestProject` as the demonstration app for the Cloudstrap
> suite, with emphasis on authentication and authorization: the target end-state and the increments
> to get there. This is SUT-local work (a test asset, not a shipped package), so the founding spec's
> Port Decision Table is adapted: verdicts are **keep-as-is / extend / replace-when-#13-lands /
> defer**. Increment 1 is the already-approved `_plans/SecureDoctorsAndDemoIdp.md` (user-amended
> 2026-08-09) — this spec builds on its end-state and does not re-litigate it.
>
> **Sources read** (all verified in code this session): the SUT (`README.md`, `Host/Bff/Program.cs`,
> `appsettings.json`, all four controllers, `Presentation/*`, `Host/Wasm/Program.cs`, `Contracts/*`),
> the E2E suite (`E2eFixture.cs`, `OpenIdConnectTests.cs`, `DoctorsTests.cs` context via the plan),
> `_plans/SecureDoctorsAndDemoIdp.md`, `_plans/ROADMAP.md` (#10 delivered, #13, #17, #26),
> `_specs/Cloudstrap.md`, `src/Cloudstrap.Authentication.OpenIdConnect/ServiceCollectionExtensions.cs`,
> `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs`, the test IdP
> (`EndpointRouteBuilderExtensions.cs`, `TestIdentityProviderOptions.cs`), and the source reference
> `Nihdi.Core.Configuration.BlazorWasm\Authentication\*` (what #13 will bring).

---

## User Story

**As a** developer evaluating Cloudstrap for a secure SPA on Azure,
**I want to** run one demo app that shows the whole mainstream BFF architecture — WASM SPA,
auth-code + PKCE at a separate IdP, hardened cookie session, server-side tokens, machine-to-machine
calls, **and role-based authorization** — each piece traceable to one Cloudstrap package call,
**So that** I can see every authentication *and authorization* posture working together in a real
browser before adopting the suite.

---

## Context: what the SUT proves after increment 1, and the verified gaps

After `_plans/SecureDoctorsAndDemoIdp.md` executes, the SUT demonstrates the mainstream secure-SPA
architecture and the main **authentication** flows: interactive login (auto-triggered on `/doctors`),
RP-initiated logout, client-credentials M2M with caching, user-token forwarding
(`UserController.GetCall`), inbound JWT validation, and cookie/bearer scheme coexistence. Verified
gaps this spec closes:

1. **Authorization is not demonstrated at all.** Verified: no `Roles =`, no policy, no
   `RequireRole` anywhere under `src/Test/WasmTestProject/**`. Yet the seeded user already carries
   `role: tester` (`E2eFixture.cs` lines 65–74), the test IdP destines user claims to **both** the
   id_token and the access token (`EndpointRouteBuilderExtensions.GetInteractiveDestinations` —
   user claims are in neither client-specific set, so both destinations apply), and the OIDC
   package already sets `MapInboundClaims = false` + `TokenValidationParameters.RoleClaimType =
   "role"` (`ServiceCollectionExtensions.cs` lines 191–193, documented in the package README's
   Claims section). **Conclusion: role claims flow into the cookie principal today — the cookie
   surface needs zero shipped-package change for a role demo.** (The JWT surface does not — see
   Finding F-1.)
2. **The hand-rolled WASM auth-state code is a placeholder** (increment 1's `UserStateDto`,
   `GET api/v1/user/state`, the `forceLoad` login redirect, the challenge-shaping hook — all marked
   #13-replaced in the plan). Deliverable #13 (`Cloudstrap.BlazorWasm`) ships the real thing: the
   source's `BffAuthenticationStateProvider` fetches user info **including claims** from a BFF
   endpoint, captures the XSRF token, and feeds `AuthenticationStateProvider` /
   `AuthorizeView` (verified in
   `Nihdi.Core.Configuration.BlazorWasm\Authentication\BffAuthenticationStateProvider.cs`).
3. **The self-call topology is artificial**: `SelfApi` and `UserApi` both point back at the Bff
   itself (`appsettings.json` `HttpClients`), so one process is simultaneously the cookie-secured
   BFF and the JWT-secured downstream API — the demo blurs which scheme protects which surface.
4. **SUT vs showcase**: the SUT accumulates every deliverable's E2E demo and will never read as a
   clean consumer sample. The founding spec already resolves the tension: public samples are a
   separate docs-phase artifact ("sample app replacing TestProject/WasmTestProject as public
   samples", `_specs/Cloudstrap.md` → Repository & Delivery). The SUT stays the exhaustive test bed.

---

## Target End-State (the architecture picture)

Four processes, each launchable individually, via the VS Code compound configuration, or by the E2E
fixture. All ports are launch-profile / configuration defaults — every one overridable via
`ASPNETCORE_URLS` or `Cloudstrap:HttpClients:*:BaseAddress`.

```
┌─────────────────────────────┐          ┌──────────────────────────────────────────┐
│ Host/IdentityProvider        │          │ Browser (Chromium / E2E)                  │
│ http://127.0.0.1:5310        │◄─ login ─┤  WASM SPA (Host/Wasm, served by the Bff)  │
│ OpenIddict test IdP          │  form    │  __Host-Cloudstrap cookie                 │
│  · client-credentials grant  │          └───────────────┬──────────────────────────┘
│  · auth-code + PKCE, refresh │                          │ cookie (SameSite=Lax)
│  · users: wasmtestproject    │                          ▼
│    .user (role: tester)      │          ┌──────────────────────────────────────────┐
│    .guest (no role)          │◄─ token ─┤ Host/Bff  http://127.0.0.1:5300           │
│  DEMO-ONLY, placeholder      │  reqs    │ Cookie surface (default scheme):          │
│  credentials                 │          │  · /account/login|logout  (#10)           │
└─────────────────────────────┘          │  · GET/POST api/doctor    [Authorize]      │
                                          │    POST additionally [Authorize(Policy=   │
                                          │    "DoctorEditor")]  → roles demo         │
                                          │  · api/v1/user/whoami|call [Authorize]    │
                                          │  · api/v1/user/state  (anonymous probe)   │
                                          │ Bearer pin (coexistence, #9/#10):         │
                                          │  · api/v1/machine/status [Authorize(      │
                                          │    AuthenticationSchemes = Bearer)]       │
                                          │ Anonymous: /, /diagnostics, status,       │
                                          │  probes, OpenAPI/Scalar                   │
                                          │ Typed clients:                            │
                                          │  · SelfApi  → self (5300)  [M2M token]    │
                                          │  · UserApi  → downstream   [user token]   │
                                          └───────────────┬──────────────────────────┘
                                                          │ Authorization: Bearer (user token)
                                                          ▼
                                          ┌──────────────────────────────────────────┐
                                          │ Host/DownstreamApi http://127.0.0.1:5320  │
                                          │ Pure JWT surface (#5 defaults):           │
                                          │  · RequireAuthenticatedEndpoints = true   │
                                          │    (the hardened default, finally         │
                                          │    demonstrated) — fallback policy        │
                                          │  · GET api/v1/downstream/whoami echoes    │
                                          │    validated claims + host marker         │
                                          │  · /healthz anonymous (probe carve-out)   │
                                          └──────────────────────────────────────────┘
```

**Which `[Authorize]` variant protects which surface** (the teaching matrix):

| Variant | Where | Demonstrates |
|---|---|---|
| `[Authorize]` (default = cookie scheme) | `DoctorController` (class), `UserController` | #10 cookie session; interactive BFF surface |
| `[Authorize(Policy = "DoctorEditor")]` | `DoctorController.Add` | **Role/policy authorization on the cookie principal** (new) |
| `[Authorize(AuthenticationSchemes = Bearer)]` | Bff `MachineController.GetStatus` | Per-endpoint scheme pin inside a mixed host (#10 AC-OIDC9) |
| `RequireAuthenticatedEndpoints = true` fallback policy | entire `Host/DownstreamApi` | #5's hardened whole-app default on a pure API host (new) |
| Anonymous by opt-out (`RequireAuthenticatedEndpoints = false` + no attribute) | Bff home/diagnostics/status/probes | #5/#10 documented whole-app opt-out |

**Which Cloudstrap package each demo exercises**: unchanged for #1–#5/#9/#10 (see the SUT README
table); the new pieces exercise no new package — the authorization demo runs on shipped #10 claim
plumbing plus stock `AddAuthorization`, and the downstream host runs on shipped #1/#2/#5. **This
spec introduces zero new NuGet dependencies.** Deliverable #13 later swaps the placeholder client
code for `Cloudstrap.BlazorWasm`.

**Interim posture, stated honestly**: until #13, cookie-authenticated mutations rely on
`SameSite=Lax` + `__Host-` (no explicit antiforgery token). XSRF (server validation + WASM token
store) is #13 headline scope — it is deliberately **not** hand-rolled here, because that would
build a second placeholder #13 must then remove.

---

## Verdict Table (every current SUT feature area, adapted verdicts)

| # | Feature area (where) | Verdict | Justification |
|---|---|---|---|
| V-1 | Home page, anonymous skeleton (`Home/Index.razor`) | **Keep-as-is** | The one anonymous page (increment-1 user decision); `HomePageTests` baseline. |
| V-2 | Diagnostics / health / correlation / outbound demos (#1–#4 rows) | **Keep-as-is** | Green, package-traceable, untouched by auth work. |
| V-3 | `SelfApi` self-call (`Cloudstrap:HttpClients:SelfApi` → 5300) | **Keep-as-is** | It exists to demo #4's correlation hop + dependency readiness, and self-loop is the cheapest live peer for that; the *scheme-blur* complaint is fixed by V-9/V-10, not by moving this. Documented as a deliberate loop. |
| V-4 | Doctors feature (page + `api/doctor`) | **Extend** | Increment 1 makes it the authenticated feature; increment 2 role-gates the write path — the natural home for the authorization demo (no new feature surface invented). |
| V-5 | `MachineController` (`status` bearer-pinned + `call` relay) | **Keep-as-is** | It is the per-endpoint-scheme-pin demo (AC-OIDC9) and the #9 E2E contract; moving it would churn #9/#10 test files for no teaching gain once V-10 provides the pure JWT surface. |
| V-6 | `UserController.whoami` / `call` (#10 demo pair) | **Extend** | `call` retargets through `UserApi` to the downstream host (V-10) — user-token forwarding becomes a real cross-process trusted-subsystem hop; `whoami` unchanged. |
| V-7 | Increment-1 placeholders: `UserStateDto`, `GET api/v1/user/state`, `forceLoad` login redirect, challenge-shaping hook | **Extend now, replace-when-#13-lands** | Increment 2 adds `Roles` to the state contract and 403 shaping (minimum viable authz UI); all of it stays marked #13-replaced. The handover inventory is listed under Increment 4. |
| V-8 | Plain `HttpClient` in `Host/Wasm/Program.cs` | **Replace-when-#13-lands** | #13 brings cookie-handler/XSRF/Refit clients (roadmap §13 scope note); no interim change — churn without teaching value. |
| V-9 | Bff-as-its-own-downstream for **user** tokens (`UserApi` → 5300) | **Extend (retarget)** | The pedagogy bug named in the assessment: the user-token forwarding demo should terminate on a separate JWT-only host. Fixed by V-10 with minimal blast radius (only `UserApi` moves; `SelfApi` stays per V-3). |
| V-10 | *(new)* `Host/DownstreamApi` — pure JWT API host | **Extend (new host)** | Completes the trusted-subsystem picture, finally demonstrates #5's `RequireAuthenticatedEndpoints = true` default (the Bff structurally cannot), and is the topology #17 (YARP proxy) needs anyway. Timing is OQ-3. |
| V-11 | Test IdP host + `TestIdentityProviderSeed` (increment 1) | **Extend** | Second seeded user without the role (`wasmtestproject.guest`) enables the 403 path; seed stays the single source of truth for fixture and demo host. |
| V-12 | E2E harness (`E2eFixture`, `SutProcess`, port map) | **Extend** | Gains the downstream host boot (fixture-owned, like the IdP) and its ports; everything else untouched. |
| V-13 | VS Code launch story (increment-1 compound) | **Extend** | Compound grows to IdP + DownstreamApi + Bff; still one F5. |
| V-14 | Distilled consumer-facing auth sample | **Defer (post-v1 docs phase)** | Founding spec already plans public samples as separate artifacts (Repository & Delivery); the SUT stays the exhaustive test bed. Confirmation is OQ-4. |
| V-15 | E2E token-refresh demonstration | **Defer (not scheduled)** | AC-A1's refresh half was verified at #10's gates (unit + lifecycle tests; IdP refresh grant + counters exist). An E2E re-proof would add fixture lifetime-manipulation complexity for coverage that exists — gold-plating. |

---

## Findings that need a user decision before implementation

**F-1 — JWT-bearer role-claim asymmetry (shipped-package finding, not specced around).**
`AddCloudstrapOpenIdConnect` sets `TokenValidationParameters.NameClaimType = "name"` and
`RoleClaimType = "role"` (`Cloudstrap.Authentication.OpenIdConnect/ServiceCollectionExtensions.cs`
lines 192–193). `AddCloudstrapJwtBearer` sets **neither**
(`Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` lines 213–229): with its
`MapInboundClaims = false` default, a raw `role` claim survives into the principal but
`ClaimsIdentity.RoleClaimType` stays the legacy `ClaimTypes.Role` URI — so `[Authorize(Roles=…)]`
and `User.IsInRole` silently never match on the JWT surface, and `User.Identity.Name` is null. The
package's own README says "`sub` stays `sub`" — the no-remapping default is half of one decision
whose other half (matching claim-type expectations) was never set. This will bite real consumers,
not just the SUT. The consumer-side workaround exists (the documented `configure` hook), so the
demo *could* proceed without a package change — but per this repo's rules a shipped-package gap is
surfaced, not silently patched locally. → **OQ-2.**
*Note: the increment-2 role demo does not depend on this finding either way — it runs on the cookie
surface, which is already correct.*

**F-2 — cookie access-denied path defaults to a redirect, not 403.** A cookie-authenticated user
failing a policy triggers the cookie handler's `OnRedirectToAccessDenied` → 302 to
`/Account/AccessDenied` (a page the SUT does not have). Same class of problem increment 1 solved
for the 401 path with its `Accept: text/html` challenge-shaping hook. Resolution is SUT-local via
the documented `CloudstrapOpenIdConnectConfigurator.Cookie` hook (no package change): non-HTML
callers get **403 + handled response**; the WASM UI never triggers it in the happy path because the
role-aware form is hidden. No open question — this is the only design that keeps API semantics
honest; recorded here so the planner does not rediscover it.

---

## Increment Sequence

Each increment is one `_plans/<X>.md` (planner-owned), each small enough for one plan, each ending
with the E2E demonstration workflow rule 9 requires. Increments run strictly one at a time and
respect the one-port-deliverable-in-flight rule (they are SUT-local, slotted between roadmap ports
with user approval).

### Increment 1 — `SecureDoctorsAndDemoIdp` *(already approved; recorded for the sequence, not re-specced)*

The doctors feature behind the cookie session with auto-triggered login (home = only anonymous
page; anonymous API calls get 401 via the SUT-local challenge-shaping hook); separate IdP host
project + VS Code compound launch. Plan: `_plans/SecureDoctorsAndDemoIdp.md`. Its E2E additions:
anonymous 401 pair, `user/state`, auto-trigger headline test, separate-IdP full-login test.

### Increment 2 — Authorization demonstrated: a role-gated write path, a guest who gets 403, role-aware UI

**Scope** (all under `src/Test/WasmTestProject/**`): 

- **Seed**: add `wasmtestproject.guest` / placeholder password, claims `name: Wasm Guest User`,
  **no `role` claim**, to `TestIdentityProviderSeed` (single source of truth per increment 1).
  Existing `wasmtestproject.user` (role `tester`) unchanged — zero churn to existing tests.
- **Policy**: the Bff registers one named policy `DoctorEditor` = `RequireRole("tester")` (stock
  `AddAuthorizationBuilder`; comment explaining the `role`-claim chain). `DoctorController.Add`
  gains `[Authorize(Policy = "DoctorEditor")]` on top of the class-level `[Authorize]`; `Get`
  stays authentication-only.
- **403 shaping** (F-2): extend the increment-1 configurator usage with a
  `Cookie.Events.OnRedirectToAccessDenied` override — non-HTML `Accept` → 403 + `HandleResponse()`;
  marked SUT-local, #13-replaced alongside the 401 hook.
- **Auth-state contract**: `UserStateDto` gains `IReadOnlyList<string> Roles`;
  `UserController.GetState` fills it from the cookie principal's `role` claims. Still marked
  #13-replaced.
- **Role-aware UI**: `DoctorsPage` shows the add form only when the state probe reports the
  `tester` role; a guest sees the grid plus a read-only notice (`data-testid="doctors-readonly"`).
  No 403 is ever provoked by the happy-path UI (zero console errors preserved).
- **Identity affordance** *(pending OQ-5)*: the top bar (`MainLayout`) shows "Signed in as {Name}"
  + a sign-out button (navigates `/account/logout`, `forceLoad`) when `user/state` reports
  signed-in — completing the interactive lifecycle in the UI instead of URL-only logout.
- **E2E** (`DoctorsTests` / new `AuthorizationTests`): guest sign-in → grid visible, form absent,
  zero console errors; guest in-page `fetch` POST `api/doctor` → **403**; tester behavior
  unchanged (existing tests stay green); `user/state` exposes `roles` for the tester and `[]` for
  the guest; (if OQ-5 approved) UI sign-out ends both sessions — the AC-OIDC5 path driven from a
  button.

**Sequencing** is OQ-1: recommended **now** (before #13) — see the question for the trade-off.

### Increment 3 — The trusted-subsystem topology: a separate downstream JWT API host

**Scope**:

- New project `src/Test/WasmTestProject/src/Host/DownstreamApi/` (test asset, no packaging):
  `AddCloudstrapCore`-bound config + `UseCloudstrapObservability` (Console mode — its telemetry is
  fixture-capturable) + `AddCloudstrapWebApi` + `AddCloudstrapJwtBearer` with
  **`RequireAuthenticatedEndpoints` left at its `true` default** — the first live demo of #5's
  hardened whole-app posture. Authority 5310, audience `wasmtestproject-selfapi` (the audience the
  seed already stamps — no seed churn). Default port `http://127.0.0.1:5320` (launchSettings;
  overridable like every host).
- One endpoint: `GET api/v1/downstream/whoami` echoing the validated `sub`, `client_id`, `scope`
  and a constant `host: downstream-api` marker (the cross-process proof — the same-shaped Bff echo
  cannot fake it).
- Bff: `Cloudstrap:HttpClients:UserApi:BaseAddress` → `http://127.0.0.1:5320/`;
  `UserApiClient` path → the downstream endpoint; optionally `EnableHealthCheck: true` so Bff
  readiness genuinely depends on its downstream (same documented consequence as `SelfApi` today:
  standalone Bff runs must override or expect `/ready` unhealthy). `SelfApi` and `MachineController`
  untouched (V-3, V-5).
- E2E: fixture boots the downstream host before the Bff (like the IdP); port map gains 5320
  (fixture-owned) + one reserve for second-instance scenarios; new test: signed-in browser →
  `api/v1/user/call` → response carries `subject = wasmtestproject.user`,
  `clientId = wasmtestproject-web`, `host = downstream-api`; direct anonymous call to the
  downstream host → 401 while its `/healthz` answers 200 anonymously (proving #5's probe carve-out
  coexists with the fallback policy); the increment-1 `SeparateIdpHost` full-demo test gets its
  `UserApi` override reconciled.
- VS Code: the compound configuration grows the downstream host; README architecture picture and
  port map updated.

**Timing** is OQ-3 (standalone now vs folded into #17's demonstration slice).

### Increment 4 — #13 handover *(owned by deliverable #13's spec/plan — recorded here as the handover inventory, not scheduled by this spec)*

When `Cloudstrap.BlazorWasm` lands, the following SUT placeholders **must disappear**, replaced by
package surface (this list is the completeness check for #13's demonstration slice):

1. `UserStateDto` + `GET api/v1/user/state` → the package's BFF user-info contract (source
   precedent: `UserInfo` + claims fetched by `BffAuthenticationStateProvider`).
2. The `DoctorsPage` state-probe + `forceLoad` redirect logic → `AuthenticationStateProvider`
   integration (`AuthorizeView` / `CascadingAuthenticationState`), including the increment-2
   role-aware form visibility → `<AuthorizeView Policy="DoctorEditor">` or equivalent.
3. The SUT-local challenge/access-denied shaping hooks (401/403 by `Accept`) → whatever
   server-side BFF companion #13 ships (its analyst decides the package boundary; if #13 ships
   none, the hooks stay SUT-local and this inventory line is struck at #13's spec gate).
4. Plain `HttpClient` usage in `Host/Wasm` and `DoctorsPage` → cookie-credentialed handler + XSRF
   token store + Refit clients; antiforgery validation lands Bff-side with it.

After #13, every remaining hand-rolled line in the SUT is demo plumbing, not auth code.

### Post-v1 — distilled consumer-facing auth sample

Out of scope for the SUT (V-14, founding-spec posture). Confirmed or re-timed by OQ-4.

---

## Acceptance Criteria

Carried tripwires (must stay green through every increment): **AC-A1**, **AC-A3** (zero
`Nihdi.AspNetCore` references), **AC-ASP2** (zero `Aspire.*` anywhere in the SUT's closure — it is
not a shipped package, but the posture allows Aspire only in the dedicated future sample), the
increment-1 contracts (anonymous doctors API → 401; home anonymous; auto-trigger login), the #9 E2E
contract (machine 401 / cached token), and the #10 contracts (AC-OIDC5 logout, AC-OIDC9
coexistence). Plus workflow rule 9: every increment lands ≥ 1 passing E2E test.

| # | Given | When | Then |
|---|-------|------|------|
| AC-D1 | The tester user (`role: tester`) signed in through the browser | They open `/doctors` and add a doctor | Form visible, add succeeds, grid updates, `AddDoctor` business span still lands in captured telemetry (increments 1+2 compose). |
| AC-D2 | The guest user (no `role` claim) signed in through the browser | They open `/doctors` | Grid renders (authenticated read allowed), add form absent, read-only notice visible, zero console errors. |
| AC-D3 | The guest's cookie session | An in-page `fetch` POSTs to `api/doctor` | **403** — not a redirect, not 401, not 302-to-a-404. |
| AC-D4 | An anonymous API caller | POST `api/doctor` | **401** (increment-1 contract intact — the 403 shaping did not widen). |
| AC-D5 | Tester and guest sessions | `GET api/v1/user/state` | `roles` contains `tester` for the tester and is empty for the guest; anonymous callers still get 200 `signedIn: false`. |
| AC-D6 | *(if OQ-5 approved)* A signed-in user | They click the top-bar sign-out button | Both sessions end (cookie gone, IdP re-challenges) — AC-OIDC5 driven from the UI. |
| AC-D7 | The downstream host running with #5 defaults | An anonymous `GET api/v1/downstream/whoami` | **401** from the fallback policy — no per-endpoint attribute needed. |
| AC-D8 | The downstream host running | `GET /healthz` anonymously | 200 — the probe carve-out coexists with `RequireAuthenticatedEndpoints = true`. |
| AC-D9 | A signed-in browser session on the Bff | `GET api/v1/user/call` | The echo proves the **downstream host** validated the **user's** token: `subject = wasmtestproject.user`, `clientId = wasmtestproject-web`, `host = downstream-api`. |
| AC-D10 | A clean checkout | The VS Code compound configuration (or the documented `dotnet run` sequence) is launched | IdP + DownstreamApi + Bff all boot; browsing `/doctors` auto-triggers login; both seeded users demonstrate their paths manually. |
| AC-D11 | Any increment's diff | `git diff --stat` at its final gate | Changes confined to `src/Test/WasmTestProject/**`, `.vscode/tasks.json`, `.vscode/launch.json`, `src/Cloudstrap.sln` — **unless** a shipped-package change was explicitly user-approved (OQ-2), which then carries its own unit tests and ⚠️ auth-risk review. |
| AC-D12 | The full E2E suite after each increment | It runs | All pre-existing tests pass unchanged (fixture-owned IdP on 5310, second-instance ports, token counters, sign-in flows undisturbed). |

---

## API Endpoint Deltas

### `POST /api/doctor` *(increment 2 — modified)*
**Auth**: cookie session **and** policy `DoctorEditor` (`RequireRole("tester")`).
**Errors**: 401 anonymous (increment-1 shaping) · **403** authenticated-without-role (F-2 shaping) · body/response otherwise unchanged.

### `GET /api/v1/user/state` *(increment 2 — modified, #13-replaced)*
**Auth**: anonymous, always 200.
**Response**: `{ "signedIn": bool, "name": string, "roles": string[] }` — `roles` from the cookie principal's `role` claims, empty when signed out.

### `GET /api/v1/downstream/whoami` *(increment 3 — new, on `Host/DownstreamApi`)*
**Auth**: bearer JWT via the host-wide fallback policy (no attribute — that is the demo).
**Response**: `{ "subject": string, "clientId": string, "scope": string, "host": "downstream-api" }`.
**Errors**: 401 missing/invalid token (never a redirect — pure JWT surface).

### `GET /api/v1/user/call` *(increment 3 — retargeted)*
Unchanged contract, but the relay now terminates on the downstream host and relays its `host`
marker (response DTO gains the field).

---

## Behaviors & Conventions (defaults and their overrides)

| Opinionated default | Override |
|---|---|
| Ports: 5300 Bff · 5310 IdP · 5320 DownstreamApi (5301–5304, 5311 second-instance/demo variants; 59999 dead port) | `ASPNETCORE_URLS` / launchSettings per host; `Cloudstrap:HttpClients:*:BaseAddress` for the clients; `WasmTestProject:ApplicationBaseAddresses` for IdP redirect URIs |
| Role gate: policy `DoctorEditor` ⇒ role `tester` | Demo constant, changed in one place (Bff `Program.cs` policy registration + seed) |
| Seeded identities: `wasmtestproject.user` (tester) / `wasmtestproject.guest` (no role), placeholder passwords | `TestIdentityProviderSeed` — the single source for fixture and demo hosts; placeholder-only rule carried from increments past (never anything resembling a real secret) |
| API callers get status codes (401/403), browsers get redirects/pages — decided by the `Accept: text/html` heuristic | SUT-local hooks via `CloudstrapOpenIdConnectConfigurator`; replaced per the Increment-4 inventory |
| Downstream host: `RequireAuthenticatedEndpoints` **left at default `true`** | The #5-documented `Cloudstrap:JwtBearer:RequireAuthenticatedEndpoints` key — deliberately *not* set, unlike the Bff |
| `UserApi` readiness coupling to the downstream host (`EnableHealthCheck: true`) | The same config flag, off; consequence for standalone Bff runs documented in the README (mirrors today's `SelfApi` note) |

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| Guest navigates to `/doctors` anonymously | Increment-1 auto-trigger → IdP form → guest signs in → grid without form (never a 403 experience). |
| Signed-in tester carries a bearer header to a cookie endpoint | Unchanged coexistence: the request forwards to the JWT scheme (`BearerCoexistence`), never served from the cookie session. |
| Downstream host not running | Bff boots and `/healthz` stays 200; `api/v1/user/call` fails loudly naming the peer; `/ready` reports the `UserApi` dependency unhealthy (if the health flag ships). |
| Browser *navigates* (HTML `Accept`) to a role-gated API URL | Outside the demo path; the cookie default (redirect to `AccessDeniedPath`) applies — documented, not shaped, because no UI flow produces it. |
| Both users signed in sequentially in one E2E run | Fresh browser context per test (`PageTestBase`) — no cross-test session bleed. |
| `wasmtestproject.guest` attempts the machine or downstream endpoint directly with no token | 401 — role never enters it; authentication precedes authorization. |

---

## Deliberate Behavior Changes (vs the current SUT)

1. `POST api/doctor` becomes role-gated on top of increment 1's authentication gate (AC-D3/AC-D4).
2. `api/v1/user/call` terminates on a separate process; its response DTO gains the `host` marker.
3. `UserStateDto` gains `Roles` (placeholder contract, #13-replaced).
4. *(if OQ-5 approved)* The top bar gains a signed-in identity + sign-out affordance; increment 1's
   "no sign-*in* button" decision is untouched.

None of these change any shipped package. The only candidate shipped-package change is OQ-2, which
ships nothing without explicit approval.

---

## Out of Scope

- Everything Dropped/Deferred in the verdict table: E2E token-refresh re-proof (V-15), the
  distilled consumer sample (V-14 — post-v1 docs phase per the founding spec).
- Deliverable #13's package content (BlazorWasm helpers, XSRF, Refit, auth-state provider) and its
  SUT demonstration slice — owned by #13; this spec only fixes its handover inventory.
- Deliverable #17's proxy host and any YARP wiring — the downstream host is reusable by it, not a
  pre-implementation of it.
- Hand-rolled interim XSRF/antiforgery (stated interim posture; #13 scope).
- Any change to shipped `Cloudstrap.*` packages beyond the explicitly gated OQ-2 candidate.
- Message encryption, MessagingBridge, Dynatrace, ServicePlatform, `Cloudstrap.Functional` —
  founding-spec out-of-scope, restated per checklist.
- Blazor Server SUT (`src/Test/TestProject`) — arrives with #12.
- Multi-role/claims-transformation/resource-based authorization demos — one policy on one write
  path is the demo; more is gold-plating until a package ships an authorization feature.

---

## Open Questions

- [ ] **OQ-1 — When does the authorization demo (increment 2) run: now on the placeholder, or after #13?**
  *Found*: the role chain works today on the cookie surface with zero package changes (Context §1);
  the placeholder delta is small (`Roles` on `UserStateDto`, form-visibility check) and already
  sits in code marked #13-replaced; #13 is several deliverables away (after #6/#7/#8/#11/#12 in the
  roadmap's execution order). *Why it matters*: building now means #13 replaces slightly more
  placeholder; waiting means the suite's flagship demo app demonstrates zero authorization for
  months. *Options*: (a) now, on the placeholder (extending the increment-1 stand-ins); (b) wait
  for #13 and build it once on `AuthorizeView`. **Recommendation: (a)** — server-side enforcement
  (403 vs 200) is the load-bearing security demo and is placeholder-independent; the UI part is a
  few lines #13 was always going to replace anyway (the increment-1 gate already accepted exactly
  this trade-off for authentication).
- [ ] **OQ-2 — Fix the JWT-bearer role/name claim-type asymmetry in shipped `Cloudstrap.WebApi`?** (Finding F-1 — ⚠️ auth risk area, shipped public behavior.)
  *Found*: `AddCloudstrapJwtBearer` leaves `TokenValidationParameters.RoleClaimType`/`NameClaimType`
  at the legacy `ClaimTypes.*` URIs while defaulting `MapInboundClaims = false`, so
  `[Authorize(Roles=…)]`, `IsInRole` and `Identity.Name` silently fail against standard `role`/`name`
  claims on every JWT surface — the OIDC package already sets both to `"role"`/`"name"`.
  *Why it matters*: real consumers of the pure-API package hit this before the SUT does; the
  downstream host (increment 3) is where a demo would first expose it. *Options*: (a) set
  `NameClaimType = "name"` / `RoleClaimType = "role"` as `Cloudstrap.WebApi` defaults, overridable
  via the existing `configure` hook — package change + unit tests + explicit auth review (breaking
  changes allowed pre-release); (b) leave the package, use the documented `configure` hook in the
  SUT when/if the downstream host needs role checks; (c) do nothing — increment 3 demos
  authentication only on the JWT surface. **Recommendation: (a)** — the two defaults are halves of
  one decision ("keep claims as issued" only makes sense if the identity looks for them there);
  consistency across Cloudstrap's two token surfaces is exactly the suite's promise. Independent of
  increment 2 either way.
- [ ] **OQ-3 — Downstream API host timing: standalone increment now, or folded into #17's demonstration slice?**
  *Found*: the self-call topology is functional but pedagogically blurred (Context §3); #17 (YARP
  trusted-subsystem proxy, `⬜`, after #14–#16 bands) will need a downstream API host in the SUT for
  its own workflow-rule-9 demo. *Why it matters*: build-once vs demo-quality-now. *Options*:
  (a) increment 3 as specced, soon after increment 2 — #17 later reuses the host; (b) defer the
  whole topology to #17's plan and accept the blurred self-call until then. **Recommendation: (a)**
  — it is small (one minimal host + one retargeted client), needs zero new dependencies, is the
  only way AC-D7/AC-D8 (the #5 hardened default) ever gets a live demo before #17, and #17 is far
  down the execution order.
- [ ] **OQ-4 — Confirm: the distilled consumer-facing auth sample stays a post-v1 docs-phase artifact?**
  *Found*: the founding spec's Repository & Delivery section plans "sample app replacing
  TestProject/WasmTestProject as public samples" alongside the docs site — i.e., samples ≠ SUT, and
  no v1 deliverable covers them. *Why it matters*: if the user wants a clean showcase earlier
  (e.g. for adoption/marketing), it must become a scheduled deliverable with the project-manager,
  not an SUT mutation. *Options*: (a) confirm post-v1 (this spec's position — the SUT stays the
  exhaustive test bed); (b) schedule an early sample deliverable. **Recommendation: (a)**; nothing
  in this spec blocks (b) later.
- [ ] **OQ-5 — Ship the top-bar identity + sign-out affordance in increment 2?**
  *Found*: logout is E2E-proven but URL-only (`/account/logout` navigation); the UI has no
  signed-in indicator or sign-out control; the user personally amended the *login* affordance
  decision at increment 1 (auto-trigger, no button), making UI-affordance shape a user taste call.
  *Why it matters*: a demo app whose logout is only reachable by typing a URL under-sells the
  shipped RP-initiated logout; conversely it is strictly more UI than any acceptance criterion
  demands. *Options*: (a) include (top-bar "Signed in as {Name}" + sign-out button, one E2E test —
  AC-D6); (b) keep logout URL-only and drop AC-D6. **Recommendation: (a)** — trivial cost,
  completes the interactive lifecycle in the place a demo audience looks first.
