# Plan: SecureDoctorsAndDemoIdp — The doctors feature requires signing in (auto-triggered on navigation), and a separate identity-provider host makes the full app launchable for demos

## Overview

Two SUT-local behaviors for the WASM test project, amended per user review 2026-08-09: (1) the
**whole doctors feature** — page and API, `GET` included — requires an authenticated user on the #10
cookie session; only the home page stays anonymous, and navigating to `/doctors` **auto-triggers
login** (no sign-in button); anonymous API callers get **401, never a login redirect**; (2) a
**separate identity-provider host project** (`Cloudstrap.WasmTestProject.Host.IdentityProvider`)
serves the seeded test IdP on `http://127.0.0.1:5310`, wired into VS Code as a launch configuration
plus a **compound** "full app + IdP" configuration, so a demo run is one F5 (or two `dotnet run`
commands).

**Reference patterns, all read before planning**: `UserController.cs` (the cookie-scheme `[Authorize]`
pattern moving onto `DoctorController`, and the `whoami` shape the auth-state endpoint mirrors) ·
`MachineController.cs` (the bearer-pin counter-example — deliberately *not* used, browsers sign in
with cookies) · `OpenIdConnectTests.cs` (`SignInThroughBrowserAsync`, the IdP login form's
`data-testid` selectors `username`/`password`/`submit`, the coexistence assertions that must stay
green) · `E2eFixture.cs` + `Infrastructure/SutProcess.cs` (IdP on 5310 booted before the Bff; env/args
passing; port map 5300–5303 + 59999) · `src/Cloudstrap.Authentication.OpenIdConnect/`
(`EndpointRouteBuilderExtensions.cs` login/logout endpoints, `ServiceCollectionExtensions.cs`
configurator hooks, `BearerCoexistence.cs`) · `TestIdentityProviderHost.cs` (`StartLoopback` and the
`ConfigureIdentityProviderHost` pipeline the new IdP host mirrors) / `TestIdentityProviderOptions.cs` /
`ServiceCollectionExtensions.cs` (`AddCloudstrapTestIdentityProvider` + `MapCloudstrapTestIdentityProvider`)
· `.vscode/tasks.json` + `.vscode/launch.json` (the existing `build-wasmtestproject` task and the two
Bff launch configs this plan extends) · `src/Cloudstrap.sln` (the flat `WasmTestProject` solution
folder holding the four SUT projects).

**Hard constraint**: changes only under `src/Test/WasmTestProject/**` **plus** the user-sanctioned
`.vscode/tasks.json`, `.vscode/launch.json` and `src/Cloudstrap.sln`. **No shipped `Cloudstrap.*`
package changes** — every mechanism uses documented extension points
(`CloudstrapOpenIdConnectConfigurator`, `[Authorize]`, configuration, the TestIdentityProvider's
public `Add`/`Map` surface). No new public API in any Cloudstrap package. The hand-rolled auth-state
code is deliberately minimal and marked as the placeholder deliverable **#13** replaces.

**Why no stub phase in these slices**: the backend the UI needs already exists (#10's cookie session,
login endpoints, and `UserController`) — a stubbed auth-state service would fake an endpoint that is
already real and would force churn in the three existing `DoctorsTests`. Each slice still ends in
user-visible, E2E-proven behavior; slices run strictly one at a time.

**Test vehicle**: this SUT has no unit-test or `CustomWebApplicationFactory` integration project — its
documented equivalent (precedent: `_plans/25-WasmTestProjectSut.md`, `_plans/10-OidcLogin.md`) is the
E2E suite `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/`, which boots the real
Bff and the real IdP. Every step's RED is a failing E2E test run against the real running app. The
endpoint-integration template block maps onto E2E tests accordingly. `runTests` is not on the agent
PATH — steps run the built test executables directly.

## Decisions

### User decisions (2026-08-09 review) — reversals of the first draft

- **Separate IdP host project is the chosen design; Bff self-hosting is rejected.** The first draft's
  `WasmTestProject:SelfHostIdentityProvider` flag, the Bff → `Cloudstrap.TestIdentityProvider` project
  reference, the `appsettings.Development.json` flag file and the `SutProcess` env-var force-off are
  **all dropped** — no port clash exists when the IdP is its own process. The demo launch story is a
  VS Code **compound** configuration (IdP host + Bff server) or two `dotnet run` commands.
- **The whole doctors feature requires authentication — `GET api/doctor` included.** The first
  draft's "GET stays anonymous so the page stays viewable" posture is rejected: only the **home page**
  stays anonymous. Consequence accepted: the #2 observability demo on `/doctors` now runs behind
  sign-in (the `AddDoctor` span assertions still work — the E2E tests sign in first); the anonymous
  posture demos elsewhere (`/`, `/diagnostics`, status endpoints, probes) are untouched.
- **Login is auto-triggered on navigation, not offered as a button.** `/doctors` immediately redirects
  an anonymous visitor into `/account/login?returnUrl=/doctors`; the signed-in page always shows grid
  + add form.
- Still rejected (unchanged from the brief): **waiting for deliverable #13** — the demo must be
  secured now; the SUT-local auth-state endpoint and page logic are the minimum viable stand-in that
  #13 replaces.

### Plan-level picks (verified in code, reviewable at the named gates)

1. **Anonymous API calls answer 401 via a SUT-local challenge-shaping hook (Gate A).** Verified:
   `BearerCoexistence.SelectForwardScheme` only forwards requests *carrying* an `Authorization: Bearer`
   header to the JWT scheme; a bare anonymous request challenges the OIDC scheme → **302 to the IdP,
   not 401** (exactly what `OpenIdConnectTests.AnonymousBrowser_IsChallengedWhileTheMachineEndpointStill401s`
   asserts for a browser navigation). Pinning to the Bearer scheme (the `MachineController` pattern)
   is wrong here — the signed-in *browser* must reach the API with its cookie. So the SUT installs,
   through the documented `CloudstrapOpenIdConnectConfigurator.OpenIdConnect` hook, an
   `OnRedirectToIdentityProvider` event that answers **401 + `context.HandleResponse()` when the
   request's `Accept` header does not contain `text/html`** (API/XHR callers), and leaves browser
   navigations redirecting — which is now also what powers the auto-trigger, and keeps the existing
   OIDC coexistence test green. No package change.
2. **Auth state via a tiny new anonymous endpoint, not `whoami` (Gate A).** `GET api/v1/user/whoami`
   is `[Authorize]` — probing it anonymously yields a 401 resource load that headless Chromium reports
   as a console **error**, tripping the suite's `ConsoleErrors, Is.Empty` assertions. The brief allows
   "a tiny new Bff endpoint that returns auth state": `GET api/v1/user/state` (anonymous, always 200)
   returns `UserStateDto(bool SignedIn, string Name)` from the cookie principal. **Ordering matters on
   the page**: the state probe runs *first*, and the (now-`[Authorize]`) doctors fetch happens only
   when it reports signed-in — an anonymous visit performs exactly one anonymous 200 fetch, then a
   full-page redirect: zero console errors. SUT application code only; #13 brings the real BFF
   user-info contract.
3. **The auto-trigger navigation must bypass the Blazor router.** `/account/login` is a server
   endpoint with no `@page` route — the redirect uses
   `NavigationManager.NavigateTo("account/login?returnUrl=/doctors", forceLoad: true)`.
4. **IdP host shape: a minimal `WebApplication`, not `StartLoopback` held open (Gate B).** Both were
   weighed. `TestIdentityProviderHost.StartLoopback` builds a private `IHost` and would need artificial
   blocking, ignores `ASPNETCORE_URLS`/launchSettings (port is a method parameter), and gives VS Code
   nothing to debug cleanly. The chosen shape mirrors `TestIdentityProviderHost.ConfigureIdentityProviderHost`
   on a stock `WebApplication`: `AddRouting()` + `AddCloudstrapTestIdentityProvider(seed)` →
   `UseAuthentication()` → `MapCloudstrapTestIdentityProvider()` (WebApplication inserts routing
   automatically), Kestrel port from `ASPNETCORE_URLS`/launchSettings (default `http://127.0.0.1:5310`),
   standard host lifetime (Ctrl+C/SIGTERM). The token-counter middleware is deliberately **not**
   replicated — counters are a fixture concern (`E2eFixture.IdentityProviderTokenRequestCount` keeps
   using the fixture's own `StartLoopback` host).
5. **One seed, two hosts (Gate B).** The IdP seed (clients `wasmtestproject-bff` +
   `wasmtestproject-web`, user `wasmtestproject.user`) becomes a public static helper **in the IdP
   host project**; the E2E test project references the IdP host project and `E2eFixture` calls the
   same helper for its in-process 5310 IdP — no drift. The helper takes the *application* base
   address(es) so redirect URIs follow the Bff instance (`{base}signin-oidc`,
   `{base}signout-callback-oidc`); the IdP host reads them from
   `WasmTestProject:ApplicationBaseAddresses` (default: `http://127.0.0.1:5300/` **and**
   `https://localhost:7200/`, so both existing VS Code/launch profiles work).
6. **E2E process plumbing (Gate B).** `SutProcess.Start` is generalized with an optional
   repo-root-relative project path (default: the Bff csproj — all existing call sites unchanged). New
   E2E ports: **5311** (the IdP host instance under test — 5310 stays fixture-owned) and **5304** (the
   Bff instance pointed at it — 5301–5303 are taken). IdP-host readiness is polled on
   `/.well-known/openid-configuration`.
7. **The business-trace test becomes UI-driven (Gate A).** `AddDoctor_EmitsBusinessTraceInConsoleTelemetry`
   currently POSTs anonymously — after Step 1 that gets 401. It is reworked to sign in through the real
   browser and add the doctor through the form (keeping its attach-mode `Assert.Inconclusive` guard),
   which still lands the `AddDoctor` span in `E2eFixture.CapturedSutOutput`. This avoids hand-carrying
   the `__Host-` cookie into an `HttpClient` (the `__Host-` prefix does not survive `CookieContainer`
   round-tripping over plain-http loopback reliably; the browser is the honest vehicle).

### Verified blast radius of securing `GET api/doctor`

- **Home page stays green anonymously**: `Home/Index.razor` is static text; `MainLayout.razor` renders
  the `client-workload` badge from options bound *inside* the WASM client — no server fetch. `HomePageTests`
  unaffected.
- **`WebApiTests`** touch `/api/doctor` only as a **path listed in the OpenAPI v1 document**
  (`OpenApiDocuments_AreServedPerVersion`) — `[Authorize]` does not remove paths from the document;
  no test calls the endpoint anonymously outside `DoctorsTests`.
- **`DoctorsTests`** is the only fixture navigating `/doctors` or calling `api/doctor` — every test in
  it is reworked in Slice A.

---

## Step 1 — The doctors API requires sign-in: anonymous GET and POST get 401, signed-in browsers keep the round-trip and the `AddDoctor` span ⚠️ *(auth code — risk area)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/DoctorController.cs` *(modify — `[Authorize]` on the **class** (both `Get` and `Add`), `using Microsoft.AspNetCore.Authorization;`, XML doc updated: default (cookie) scheme like `UserController`, home page is the only anonymous page now)*
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify — `AddCloudstrapOpenIdConnect(configure => configure.OpenIdConnect = ...)` installing the pick-1 challenge shaping, with a comment marking it SUT-local, #13-replaced)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/Infrastructure/BrowserSignIn.cs` *(create — shared static helper, extracted from `OpenIdConnectTests.SignInThroughBrowserAsync`: `Task SignInAsync(IPage page, string baseUrl, string returnUrl = "/")` filling `[data-testid='username']`/`password`/`submit` at the IdP and waiting for the `form_post` return; credentials constants `wasmtestproject.user` / `local-e2e-placeholder-password`)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/OpenIdConnectTests.cs` *(modify — delegate its private helper to `BrowserSignIn`; zero behavior change)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/DoctorsTests.cs` *(modify — two new tests, three reworked tests; see RED)*

**RED** *(write these tests first, run them, confirm they fail before writing production code)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/DoctorsTests.cs`
- E2E test methods *(the SUT's endpoint-integration equivalent — happy path + error case per endpoint action)*:
  - `GetDoctors_AnonymousApiGet_Returns401` *(new — plain `HttpClient` GET `api/doctor`, no cookie/bearer; assert `HttpStatusCode.Unauthorized`. Fails today with 200 — RED signal)*
  - `AddDoctor_AnonymousApiPost_Returns401` *(new — plain `HttpClient` POST of `{ name, specialty }`, no cookie/bearer; assert `HttpStatusCode.Unauthorized`. Fails today with 200 — RED signal)*
  - `DoctorsPage_Loads_ShowsSeededDoctors` *(rework — `BrowserSignIn.SignInAsync(Page, BaseUrl, "/doctors")` first; keep the three seeded-doctor assertions and `ConsoleErrors, Is.Empty`)*
  - `DoctorsPage_AddDoctor_NewDoctorAppearsInGrid` *(rework — sign in first, then use the form as before)*
  - `AddDoctor_EmitsBusinessTraceInConsoleTelemetry` *(rework per pick 7 — keep the attach-mode `Assert.Inconclusive` guard, sign in through the browser, add "Dr. Telemetry Probe"/"Observability" through the form, poll `E2eFixture.CapturedSutOutput` for `AddDoctor`)*
- Failing-run command *(build the solution first — the fixture launches with `--no-build`)*:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "GetDoctors_AnonymousApiGet_Returns401"
  ```

**GREEN** *(minimal production code across all necessary layers to make RED pass)*:
- `DoctorController` gets class-level `[Authorize]` (default cookie scheme — the `UserController`
  pattern). The `IBusinessTrace` span code is untouched; the span now records under an authenticated
  request.
- `Program.cs`: pass a configurator to the existing `AddCloudstrapOpenIdConnect()` call —
  `configure.OpenIdConnect = oidc => oidc.Events.OnRedirectToIdentityProvider = context => { if (!context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase)) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; context.HandleResponse(); } return Task.CompletedTask; }`.
  Assign only the one event property — never replace the `Events` object (Cloudstrap's own
  `OnRedirectToIdentityProviderForSignOut` wiring must survive; the configurator hook runs last by design).
- **Known interim state, closed by Step 2 in this same slice**: an *anonymous browser navigation* to
  `/doctors` is broken after this step (the page's doctors fetch gets 401) — no test asserts it, the
  executor continues straight into Step 2, and the covering gate sits after Step 2.

**DB changes**: None — the SUT is deliberately database-free (`InMemoryDoctorStore`).

**VERIFY** *(after making GREEN changes, run these checks; when all green, mark this step's `Done` checkbox and continue straight to the next step — stop only when the next plan item is a 🛑 HUMAN GATE)*: build + all tests + code analysis + format — all green (exact commands in copilot-instructions.md; run test executables directly, `runTests` is not on the agent PATH). Specifically:
- `GetDoctors_AnonymousApiGet_Returns401` and `AddDoctor_AnonymousApiPost_Returns401` pass (401, not a
  redirect-followed 200 login page).
- The full E2E suite passes — in particular `OpenIdConnectTests.AnonymousBrowser_IsChallengedWhileTheMachineEndpointStill401s`
  (browser navigations still redirect — the Accept heuristic did not over-reach), `ClientCredentialsTests`
  (bearer path untouched), `HomePageTests` (home stays anonymous), `WebApiTests.OpenApiDocuments_AreServedPerVersion`
  (`/api/doctor` still listed in the v1 document), and all three reworked `DoctorsTests`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — Navigating to `/doctors` anonymously auto-triggers login and returns to a working, signed-in doctors page

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/Test/WasmTestProject/src/Contracts/UserStateDto.cs` *(create — `public sealed record UserStateDto(bool SignedIn, string Name);` with XML docs marking it SUT demo code replaced by #13's BFF user-info contract)*
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/UserController.cs` *(modify — new anonymous action `[HttpGet("state")] public ActionResult<UserStateDto> GetState()` returning `User.Identity?.IsAuthenticated == true` + `User.Identity?.Name ?? string.Empty`; no `[Authorize]` — the cookie middleware still populates the principal when a session exists)*
- `src/Test/WasmTestProject/src/Presentation/Doctors/DoctorsPage.razor` *(modify — signed-in view always shows grid + add form plus a `data-testid="doctors-user"` "Signed in as {Name}" line; while the state probe or redirect is pending, only the existing `MudProgressCircular` shows; **no sign-in button**)*
- `src/Test/WasmTestProject/src/Presentation/Doctors/DoctorsPage.razor.cs` *(modify — `[Inject] NavigationManager`; `OnInitializedAsync` order per pick 2: GET `api/v1/user/state` **first**; if signed out → `Navigation.NavigateTo("account/login?returnUrl=/doctors", forceLoad: true)` and **return without fetching doctors**; only when signed in → `ReloadAsync()`)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/DoctorsTests.cs` *(modify — two new tests; see RED)*

**RED** *(write these tests first, run them, confirm they fail before writing production code)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/DoctorsTests.cs`
- E2E test methods *(endpoint-integration equivalent for the new `state` action: anonymous case + the signed-in case exercised through the headline flow)*:
  - `UserState_AnonymousApiGet_Returns200SignedOut` *(new — plain `HttpClient` GET `api/v1/user/state` → 200, `signedIn: false`. Fails today with 404 — RED signal for the endpoint)*
  - `DoctorsPage_AnonymousNavigation_AutoTriggersLoginAndShowsDoctors` *(new — **the headline test**: goto `/doctors` anonymously → the browser lands on the IdP login form (`[data-testid='username']` visible on `http://127.0.0.1:5310/**`) with **no click** → fill the form via the `BrowserSignIn` selectors → land back on `/doctors` → `doctors-user` shows "Wasm Test User", `doctors-grid` shows the seeded doctors, `add-doctor-submit` visible, `ConsoleErrors` empty — the pick-2 ordering proof. Fails today: no auto-redirect happens)*
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "DoctorsPage_AnonymousNavigation_AutoTriggersLoginAndShowsDoctors"
  ```

**GREEN** *(minimal production code across all necessary layers to make RED pass)*:
- Contracts: `UserStateDto` as scoped above.
- `UserController.GetState` as scoped above (route: `api/v1/user/state` under the existing versioned
  route attribute; anonymous by omission of `[Authorize]`).
- `DoctorsPage`: state-probe-first initialization, redirect-or-load per pick 2/3, "Signed in as" line.
  The existing add/reload logic is unchanged.

**DB changes**: None.

**VERIFY** *(after making GREEN changes, run these checks; when all green, mark this step's `Done` checkbox and continue straight to the next step — stop only when the next plan item is a 🛑 HUMAN GATE)*: build + all tests + code analysis + format — all green (test executables run directly). Specifically: both new tests pass; every Step 1 test still passes (the signed-in flows now go probe → load); `HomePageTests` still passes anonymously with zero console errors.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice A: the doctors feature requires sign-in *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

- [x] Behavioral verification: `GetDoctors_AnonymousApiGet_Returns401`, `AddDoctor_AnonymousApiPost_Returns401`,
      `UserState_AnonymousApiGet_Returns200SignedOut` and `DoctorsPage_AnonymousNavigation_AutoTriggersLoginAndShowsDoctors`
      green; the three reworked `DoctorsTests` green (the `AddDoctor` business span still reaches the
      console telemetry — the #2 demo survives behind sign-in); the entire pre-existing E2E suite
      green, especially the four `OpenIdConnectTests` and `HomePageTests` (home is the only anonymous
      page and stays clean).
- [x] UI/UX sign-off: the auto-trigger experience is acceptable — anonymous `/doctors` shows only the
      brief loading indicator before the IdP form; the signed-in page shows "Signed in as", grid, form.
- [x] Code review *(⚠️ auth risk area)*: `[Authorize]` sits on the `DoctorController` class using the
      default cookie scheme (not a Bearer pin); the challenge-shaping hook lives in SUT code via the
      documented configurator and its `Accept: text/html` heuristic is acceptable as the #13
      placeholder; the state probe runs before any `[Authorize]`'d fetch (no 401 console noise);
      `UserStateDto` + `state` are clearly marked SUT-local/#13-replaced; no shipped `Cloudstrap.*`
      file touched — confirm with `git diff --stat`.
- [x] User approved — implementation may continue past this gate *(2026-08-09: user ran the Bff
      standalone, hit the expected no-IdP discovery failure, and directed continuation into Step 3)*

---

## Step 3 — The full app launches for demos: a separate IdP host process on 5310, one VS Code compound launch (or two `dotnet run` commands) *(demonstration slice — workflow rule 9)* ⚠️ *(auth configuration — risk area)*

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(all layers touched by this slice)*:
- `src/Test/WasmTestProject/src/Host/IdentityProvider/Cloudstrap.WasmTestProject.Host.IdentityProvider.csproj` *(create — `Microsoft.NET.Sdk.Web`, `net10.0`, single `ProjectReference` to `..\..\..\..\TestIdentityProvider\Cloudstrap.TestIdentityProvider\Cloudstrap.TestIdentityProvider.csproj`; a test asset like its siblings — no packaging properties)*
- `src/Test/WasmTestProject/src/Host/IdentityProvider/Program.cs` *(create — pick-4 shape: `WebApplication.CreateBuilder(args)`; read `WasmTestProject:ApplicationBaseAddresses` (string array, fallback `["http://127.0.0.1:5300/", "https://localhost:7200/"]`); `AddRouting()` + `AddCloudstrapTestIdentityProvider(options => TestIdentityProviderSeed.Configure(options, addresses))`; `app.UseAuthentication(); app.MapCloudstrapTestIdentityProvider(); await app.RunAsync();` — port comes from `ASPNETCORE_URLS`/launchSettings, default 5310)*
- `src/Test/WasmTestProject/src/Host/IdentityProvider/TestIdentityProviderSeed.cs` *(create — `public static class TestIdentityProviderSeed` with `public static void Configure(TestIdentityProviderOptions options, IReadOnlyCollection<Uri> applicationBaseAddresses)`: client `wasmtestproject-bff` (secret `local-e2e-placeholder-secret`, scope `selfapi`, audience `wasmtestproject-selfapi`), client `wasmtestproject-web` (secret `local-e2e-placeholder-secret-web`, same scope/audience, per base address `new Uri(base, "signin-oidc")` redirect + `new Uri(base, "signout-callback-oidc")` post-logout URIs), user `wasmtestproject.user` / `local-e2e-placeholder-password` with claims `name: Wasm Test User`, `role: tester` — byte-for-byte the values `E2eFixture` seeds today)*
- `src/Test/WasmTestProject/src/Host/IdentityProvider/appsettings.json` *(create — the `WasmTestProject:ApplicationBaseAddresses` defaults plus minimal logging config)*
- `src/Test/WasmTestProject/src/Host/IdentityProvider/Properties/launchSettings.json` *(create — `http` profile, `applicationUrl: http://127.0.0.1:5310`, `ASPNETCORE_ENVIRONMENT: Development`, `launchBrowser: false` — so `dotnet run --project .../Host/IdentityProvider` works standalone)*
- `src/Cloudstrap.sln` *(modify — add the new project to the existing `WasmTestProject` solution folder next to `Cloudstrap.WasmTestProject.Host.Bff`; use `dotnet sln src/Cloudstrap.sln add --solution-folder` matching the existing entries)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/Cloudstrap.WasmTestProject.E2E.Tests.csproj` *(modify — add `ProjectReference` to the IdP host project so the fixture reuses the seed; the `Cloudstrap.TestIdentityProvider` reference stays)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/E2eFixture.cs` *(modify — replace the inline `options.Clients.Add(...)`/`options.Users.Add(...)` block with `TestIdentityProviderSeed.Configure(options, [new Uri(DefaultBaseUrl)])`; the fixture keeps its own `StartLoopback(5310)` host for `IdentityProviderTokenRequestCount`)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/Infrastructure/SutProcess.cs` *(modify — pick 6: optional repo-root-relative project path parameter on `Start`, defaulting to the Bff csproj; existing call sites unchanged)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/SelfHostedIdentityProviderTests.cs` *(create — see RED; name kept file-descriptive: the separately hosted IdP demo)*
- `.vscode/tasks.json` *(modify — new task `build-wasmtestproject-idp` building the IdP host csproj, same shape as `build-wasmtestproject`)*
- `.vscode/launch.json` *(modify — new config `"WASM Test Project (IdP host)"`: `coreclr`, `preLaunchTask: build-wasmtestproject-idp`, program = the IdP host dll under `bin/Debug/net10.0/`, cwd = the IdP host project dir, env `ASPNETCORE_ENVIRONMENT: Development` + `ASPNETCORE_URLS: http://127.0.0.1:5310`, no `serverReadyAction`; plus a `"compounds"` entry `"WASM Test Project (full app + IdP)"` launching `["WASM Test Project (IdP host)", "WASM Test Project (Bff server)"]` with `"stopAll": true`)*
- `src/Test/WasmTestProject/README.md` *(modify — layout tree gains `Host/IdentityProvider`; "Running the app manually" becomes the compound-launch / two-`dotnet run` story with the IdP host command first; the doctors row of the demo table gains the secured `GET/POST` + auto-trigger login and this feature's new tests; port map gains 5304/5311; harness notes: the IdP host is **demo-only** test infrastructure (never a real IdP), the seed is shared with `E2eFixture`, and the "SUT stays effectively anonymous" note is amended — home is the anonymous page, doctors opts in via class-level `[Authorize]`)*

**RED** *(write these tests first, run them, confirm they fail before writing production code)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/SelfHostedIdentityProviderTests.cs` *(create — inherits `PageTestBase`; readiness polls: `/.well-known/openid-configuration` for the IdP host, `/` for the Bff, modeled on `WebApiTests.WaitUntilReadyAsync`)*
- E2E test method: `SeparateIdpHost_FullBrowserLogin_AddsDoctorThroughTheUi` — starts the **IdP host
  executable** via the generalized `SutProcess.Start("http://127.0.0.1:5311", ["--WasmTestProject:ApplicationBaseAddresses:0=http://127.0.0.1:5304/"], idpHostProjectPath)`,
  then a Bff instance via `SutProcess.Start("http://127.0.0.1:5304", [...])` with
  `--Cloudstrap:OpenIdConnect:Authority=http://127.0.0.1:5311`,
  `--Cloudstrap:JwtBearer:Authority=http://127.0.0.1:5311`,
  `--Cloudstrap:ClientCredentials:TokenEndpoint=http://127.0.0.1:5311/connect/token`,
  `--Cloudstrap:HttpClients:SelfApi:BaseAddress=http://127.0.0.1:5304/`,
  `--Cloudstrap:HttpClients:UserApi:BaseAddress=http://127.0.0.1:5304/`; then in the browser: goto
  `http://127.0.0.1:5304/doctors` → the Step 2 **auto-trigger** lands on the login form at
  `http://127.0.0.1:5311/**` → fill it (`BrowserSignIn` selectors/credentials) → back on `/doctors` →
  grid + form visible → add a doctor → it appears in the grid. **This proves the separately hosted IdP
  served the login (redirect URIs seeded for base 5304 through configuration), while the fixture's
  5310 IdP keeps serving the main instance.** Fails today: the IdP host project does not exist —
  compilation of the test's project path helper and the process start both fail.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "SeparateIdpHost_FullBrowserLogin_AddsDoctorThroughTheUi"
  ```

**GREEN** *(minimal production code across all necessary layers to make RED pass)*:
- Everything scoped above, in this order: IdP host project (csproj, `Program.cs`, seed,
  `appsettings.json`, `launchSettings.json`) → solution entry → `SutProcess` generalization →
  `E2eFixture` seed reuse + E2E csproj reference → the new E2E test passing → `.vscode` task/launch/compound
  wiring → README. The `.vscode` files have no automated test — they are verified manually at the gate.

**DB changes**: None — the TestIdentityProvider uses its own per-process in-memory SQLite; nothing persists.

**VERIFY** *(after making GREEN changes, run these checks; when all green, mark this step's `Done` checkbox and continue straight to the next step — stop only when the next plan item is a 🛑 HUMAN GATE)*: build + all tests + code analysis + format — all green (test executables run directly). Specifically:
- `SeparateIdpHost_FullBrowserLogin_AddsDoctorThroughTheUi` passes.
- The **entire** E2E suite passes — proof the fixture-owned 5310 IdP and every second-instance test
  (`DiagnosticsTests` 5301, `AzureMonitorTests` 5301/5302, `ExtensionsTests` 5302, `WebApiTests` 5303,
  dead port 59999) are undisturbed, and the seed relocation changed no observable fixture behavior
  (`ClientCredentialsTests` token counters, `OpenIdConnectTests` sign-in).
- All other test executables under `src/Test/UnitTest/` still pass (nothing shipped changed — a
  tripwire, not an expectation of new coverage).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice B: full-app demo launch *(covers Step 3 — final gate)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

- [ ] Behavioral verification: `SeparateIdpHost_FullBrowserLogin_AddsDoctorThroughTheUi` green; full
      E2E suite green (fixture-owned 5310 IdP and all second-instance tests undisturbed; seed
      relocation behavior-neutral).
- [ ] **Manual demo** (the headline behavior, run by the user): VS Code → run the compound
      configuration **"WASM Test Project (full app + IdP)"** (or, terminal:
      `dotnet run --project src/Test/WasmTestProject/src/Host/IdentityProvider` then
      `dotnet run --project src/Test/WasmTestProject/src/Host/Bff`) → browse
      `http://127.0.0.1:5300/doctors` → login is **auto-triggered** at the self-hosted IdP → sign in
      as `wasmtestproject.user` / `local-e2e-placeholder-password` → add a doctor. Stopping the
      compound session stops both processes.
- [ ] Code review *(⚠️ auth configuration risk area)*: the IdP host project is documented **demo-only**
      test infrastructure in the README (never a real IdP; placeholder credentials only — nothing
      resembling a real secret); the Bff gained **no** reference to `Cloudstrap.TestIdentityProvider`;
      `TestIdentityProviderSeed` is the single source of truth (fixture inline seed deleted); the
      `SutProcess` generalization left all existing call sites' behavior unchanged; changes are
      confined to `src/Test/WasmTestProject/**`, `.vscode/tasks.json`, `.vscode/launch.json` and
      `src/Cloudstrap.sln` — no shipped `Cloudstrap.*` package touched anywhere in this plan
      (`git diff --stat` confirms).
- [ ] Solution/launch review: the sln entry sits in the `WasmTestProject` solution folder; the new
      launch config and compound work from a clean checkout (`build-wasmtestproject-idp` task builds
      the IdP host first).
- [ ] README review: layout tree, launch story, demo table row, port map (5304/5311), harness notes.
- [ ] User approved — the plan is complete
