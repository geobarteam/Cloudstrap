# Plan: 6-MvcBootstrap — A consumer calls `AddCloudstrapMvc` + `UseCloudstrapMvc` and gets a server-rendered MVC app with hardened session state, browser-appropriate error handling, correlation, health probes and edge hardening — in under ten lines of `Program.cs`

> ✅ **Plan approved by the user on 2026-08-16.**
>
> ⚠️ **Start constraint (roadmap, seventh pass)**: implementation of this plan **must not start before the
> `_plans/SecureDoctorsAndDemoIdp.md` final 🛑 gate closes** (or the user explicitly parks it). Planning and
> plan approval may proceed; the executor checks that gate is closed before beginning Step 1.

## Overview

Deliverable #6 of the extraction roadmap: the new `Cloudstrap.Mvc` package — the suite's eighth.
**Binding spec: `_specs/6-MvcBootstrap.md`** (approved 2026-08-16, zero Open Questions). Its Port Decision
Table (**1 Port · 7 Redesign · 3 Replace · 7 Drop · 3 Superseded-reuse**), Public API Sketch, Behaviors &
Conventions table, Dependencies table, Deliberate Behavior Changes, Edge Cases, Out of Scope list and
Decision Log (**D-1** stock session with hardened defaults · **D-2** content-negotiating handler +
`/error` re-execution · **D-3** test-only MVC SUT host · **D-4** two inline header constants, zero new
dependencies · **D-5** stock `IDistributedCache` posture) are authoritative and are not re-litigated here.
Nothing the spec marked Drop appears in this plan: no session-middleware fork and no `NihdiCookieProtection`
(the package ships **zero** session code, AC-MVC4), no `CorrelationSourceMiddleware`/`UseNihdiCorrelation`,
no static-web-assets loader, no reflection `AddExceptionHandler<T>` loop, no
`BootstrapConfiguration`/`AddWebOptions` remnants, no `UseForwardedHeaders` by default, no `NWebsec.*` or
`NetEscapades.*` dependency, no localization (#24), no forced `RequireAuthorization()`, no shipped default
error page, no Redis/SQL session-store opinion, no authentication of any kind (#5/#9/#10).

Reference patterns, all read in full before planning:

- **Primary reference: shipped `Cloudstrap.WebApi` (#5)** — this package inherits its D-3 composite shape
  verbatim (the #5 final gate approved that shape *explicitly for #6 and #7 to copy*, including every
  Gate 1–4 executor deviation). Read on disk: `WebApplicationBuilderExtensions.cs` (eager options read,
  hook-runs-last idiom, `ConfigureCors` no-origins→nothing), `WebApplicationExtensions.cs` (canonical
  order, run-once marker, **scheme-map predicate** + the two minimal-hosting middleware-marker claims —
  the #5 Gate-4 discoveries this plan replicates rather than rediscovers),
  `CloudstrapWebApiExceptionHandler.cs` (three-source correlation resolution, depth-5 inner chain),
  `SecurityHeadersMiddleware.cs`, `EnvironmentDefault.cs`, `HstsSettings.cs`/`CorsSettings.cs` (shapes
  mirrored here as package-local types), `WebApiPipelineOptions.cs`, `Cloudstrap.WebApi.csproj`, plus
  `_plans/5-WebApiBootstrap.md` (slice/step/gate granularity, mechanics, Gate 1 decisions — path base
  stays stock `UsePathBase`; **correlation before authentication**; eager configuration read at
  registration time; all carried forward here as settled).
- **Shipped seams this package consumes (read in the shipped code, never rebuilt)**: Core
  `ApplicationOptions` (`PathBase` normalized to `/x`, **`ExceptionHandlerPath` default `/error` — left
  unconsumed by #5 by design, its consumer arrives here**), `AddCloudstrapCore()`; Observability
  `AddCloudstrapCorrelation()`/`UseCloudstrapCorrelation()` (read-or-generate, `EchoInResponse`,
  `HttpContext.GetCloudstrapCorrelationId()`), `ICorrelationContextAccessor`, `CorrelationOptions`;
  Extensions `MapCloudstrapHealthChecks()` (marker-idempotent, `/healthz` + `/ready`),
  `AddCloudstrapDataProtection` (referenced by the multi-instance session recipe only — never called by
  this package).
- **Pairing surface (test-only reference)**: `Cloudstrap.Authentication.OpenIdConnect` —
  `AddCloudstrapOpenIdConnect(this IServiceCollection, Action<CloudstrapOpenIdConnectConfigurator>?)`
  with the `OpenIdConnect : Action<OpenIdConnectOptions>?` hook (used to pre-seed metadata in the
  AC-MVC10 pairing test, no network).
- **Demonstration harness (verified on disk)**: `E2eFixture` (Bff on 5300, fixture IdP on 5310, attach
  mode, `CapturedSutOutput`), `SutProcess.Start(baseUrl, applicationArguments, projectRelativePath)`
  (forces `ASPNETCORE_ENVIRONMENT=Development` — see mechanic (j)), `PageTestBase`,
  `SelfHostedIdentityProviderTests` (the boot-another-host-by-project-path precedent Step 8 follows),
  `src/Test/WasmTestProject/src/Host/IdentityProvider/` (the minimal-extra-host precedent D-3 names:
  `Microsoft.NET.Sdk.Web`, `net10.0`, no NUnit wiring because the project name does not end in `.Tests`,
  `IsPackable=false` inherited from `src/Test/Directory.Build.props`), `src/Test/WasmTestProject/README.md`
  (port map + demo table + per-deliverable harness notes).

This is a library deliverable with no database and no UI of its own: the plan-template's
endpoint-integration block does not apply literally. Its equivalent here is that **every step's tests boot
a real ASP.NET Core pipeline in-process on `Microsoft.AspNetCore.TestHost` (already CPM-pinned, test-only)
and assert over real HTTP** — status codes, `Set-Cookie` attributes, HTML and `application/problem+json`
bodies — plus the mandatory E2E demonstration slice (Step 8, the D-3 MVC host driven by Playwright).

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-MVC1 (conventional route renders, static files, probes per #4, no health code of our own) | 1 (route + static files) · 2 (probes) · 8 (live, incl. a real `.cshtml` view) |
| AC-MVC2 (one hardened session cookie, exact attributes, no-store headers, round-trip) | 3 (+ live in 8) |
| AC-MVC3 (every session override honored; hook runs last; `Enabled=false` removes it) | 4 |
| AC-MVC4 (zero session middleware/store/cookie-protection types in the assembly) | 3 (sweep) + 7 (permanent guard) |
| AC-MVC5 (HTML → `/error` re-execution; JSON → RFC 9457; `IncludeDetails` JSON-only; logged once) | 5 (+ live in 8, both branches) |
| AC-MVC6 (Development developer page; selection overridable) | 5 |
| AC-MVC7 (correlation exactly per #2; no correlation type of our own) | 2 |
| AC-MVC8 (nosniff + no-referrer, never overwriting; HSTS outside Development; no `NWebsec.*`) | 6 (+ closure guard in 7) |
| AC-MVC9 (no origins → no CORS at all; configured origins → credentialed + wildcard subdomains) | 6 |
| AC-MVC10 (scheme-map predicate; OIDC pairing; no auth → anonymous; double-call throws; hooks + switches) | 1 (switches) · 2 (everything else) |
| AC-MVC11 (consumer `IDistributedCache` wins; TryAdd fallback; README multi-instance recipe) | 4 (behavior) + 7 (recipe) |
| AC-MVC12 (build/tests/format, XML docs, metadata, closure + identifier sweeps) | 7 |
| AC-MVC13 (MVC SUT host; ≥ 1 E2E proving AC-MVC2 + AC-MVC5a through a real browser; existing E2E green) | 8 |
| AC-ASP2 (zero `Aspire.*` in the closure) | 7 |
| AC-A3 (zero `Nihdi.AspNetCore` references) | 7 (permanent guard; this package references no auth package at all) |

### New CPM entries: **none**

The spec's headline dependency fact, carried into the plan: **zero new external NuGet packages, zero new
CPM pins**. `Cloudstrap.Mvc.csproj` has *no* `PackageReference` at all — three project references plus the
suite's **fourth** `Microsoft.AspNetCore.App` framework reference (after #2, #4, #5). The test project uses
only already-pinned packages (`Microsoft.AspNetCore.TestHost`, `Microsoft.Extensions.Configuration`(+`.Binder`),
`Microsoft.Extensions.Hosting`) plus two project references (`Cloudstrap.Mvc`, and — test-only, for the
AC-MVC10 pairing proof — `Cloudstrap.Authentication.OpenIdConnect`).

### ⚠️ Risk areas (spec header; reviewed at the gates named)

- **Session/cookie security defaults (auth-adjacent)** — the D-1 *defaults* are signed off at the spec
  gate; the *implementation* (the exact `SessionOptions` values, the cookie path derivation, the hook
  ordering, the `Enabled=false` shape) gets explicit human review at **Gate 2**, per the spec's standing
  rule that session/cookie changes remain a human-review area at every plan gate that touches them.
  Steps 3–4 and the Step 8 E2E also touch them; Gate 2 and the final gate carry the review.
- **Public API one-way doors** — `AddCloudstrapMvc`/`UseCloudstrapMvc`, `CloudstrapMvcConfigurator`,
  `MvcPipelineOptions` and the `Cloudstrap:Mvc` section (subsections `Session`, `Hsts`, `Cors`,
  `ExceptionHandling`) are permanent surface: **Gates 1–3** each review the pieces they froze.
- **Fourth `Microsoft.AspNetCore.App` framework reference** in the suite — **Gate 1**, where the csproj
  is created (runtime-image consequence goes in the README at Step 7).
- **Copied-stock-code provenance** — resolved by construction: D-1 means Cloudstrap owns zero session
  middleware; the AC-MVC4 reflection sweep (Step 3) and the permanent guard (Step 7) prove it stays true.
- **Aspire overlap: none** — session/MVC pipeline/security headers are outside ServiceDefaults' remit;
  health and correlation route through the already-composable #2/#4 seams; zero `Aspire.*` (AC-ASP2,
  guarded at Step 7). **Final gate** confirms.

### Planner mechanics decided here (no spec conflict; each flagged for review at the named gate)

**(a) One hand-written validator.** `CloudstrapMvcOptionsValidator` (`internal sealed :
IValidateOptions<CloudstrapMvcOptions>`, the #5 `WebApiOptionsValidator` precedent) carries the two
conditional rules: `Session.IdleTimeoutMinutes > 0` while `Session.Enabled`, and `Hsts.MaxAgeDays > 0`
while `Hsts.Enabled` — each failure naming the full `Cloudstrap:Mvc:...` key. The spec sketch's generic
"`[OptionsValidator]`" note yields to the suite's established split (source-generated only for pure
attribute rules — none exist here; both rules are conditional). *(Gates 1–2 confirm.)*

**(b) Internal helpers are re-expressed package-locally, never shared.** `EnvironmentDefault` (~10 lines)
and the ~15-line `SecurityHeadersMiddleware` are deliberate near-duplicates of #5's internals: D-2
explicitly rejects a `Cloudstrap.WebApi` reference (it would drag `Asp.Versioning.*`/OpenAPI/Scalar into
every MVC closure), and no cross-package `InternalsVisibleTo` exists in the suite. *(Gate 1.)*

**(c) Run-once + minimal-hosting markers, replicated not rediscovered.** `UseCloudstrapMvc` guards with
its own `app.Properties` marker (`Cloudstrap.Mvc.Pipeline` — distinct from WebApi's, per the spec edge
case on hosts calling both composites) and throws `InvalidOperationException` naming the method on a
second call. It claims `__AuthenticationMiddlewareSet`/`__AuthorizationMiddlewareSet` before placing auth
middleware itself after routing, and wires that middleware on the **scheme-map predicate**
(`IOptions<AuthenticationOptions>.Value.SchemeMap.Count > 0`) — both verbatim from #5's Gate-4 executor
report, already user-approved as the shape #6 inherits. *(Gate 1.)*

**(d) The content-negotiation rule, fixed at plan time (spec edge case requires exactly this).** A request
is **HTML-preferring iff its `Accept` header contains the `text/html` or `application/xhtml+xml` media
type** (browsers always send `text/html` on navigation). Everything else — `application/json`, `*/*`
alone, an absent or unparsable `Accept` header — is JSON-preferring: API clients and curl get
machine-readable RFC 9457. The Step 5 tests pin all four cases. *(Gate 3.)*

**(e) View *rendering* is proven in the SUT host, not the unit suite.** The unit tests drive routing,
switches, session, errors and headers through content-returning test controllers: the behavior this
package owns is registration + pipeline order, while `.cshtml` compilation is the framework's. Compiling
Razor views inside an MTP test executable would force a fragile `Microsoft.NET.Sdk.Razor`/`OutputType=Exe`
hybrid for zero package coverage. AC-MVC1's "the view renders" lands live in Step 8, where a real
`Views/Home/Index.cshtml` renders in a real Chromium. *(Gate 1 — confirm or direct a Razor test project.)*

**(f) AC-MVC10's "OIDC login round-trips" is proven to the challenge redirect, offline.** Step 2 registers
the real `AddCloudstrapOpenIdConnect` (test-only project reference) with valid `Cloudstrap:OpenIdConnect`
configuration and a **pre-seeded `OpenIdConnectConfiguration`** injected through the package's documented
`configurator.OpenIdConnect` hook (the #5 pre-seeded-metadata precedent — the authority is never
contacted): an anonymous browser-shaped request to an `[Authorize]` page must 302 to the seeded
authorization endpoint through middleware `UseCloudstrapMvc` placed after routing. The full
code-for-cookie round-trip is #10's own E2E-proven behavior and is not re-proven per pipeline. A
self-contained consumer-cookie-scheme test proves the predicate without any Cloudstrap auth package.
*(Gate 1 — confirm this reading, or direct adding an OIDC login to the Step 8 SUT host instead.)*

**(g) Test strategy = #5's mechanic (g).** Every step boots a real pipeline: `WebApplication.CreateBuilder`
+ `builder.WebHost.UseTestServer()` + in-memory `Cloudstrap:` configuration (valid `Application` values) +
`app.GetTestClient()`. Test controllers live in the test assembly and are discovered through the
documented `configurator.Mvc` hook (`mvc => mvc.AddApplicationPart(...)`) — which doubles as that hook's
own proof. Fixtures are neutral (`Contoso`, `Widgets`, `Catalog`, `example.com`). The Step 1 host helper
also provisions a temp web root carrying a `site.css` asset so `UseStaticFiles` is testable for real.

**(h) `InternalsVisibleTo` to `Cloudstrap.Mvc.Tests` only** (suite precedent) — the internal handler,
middleware, validator and helpers are directly testable; no cross-package IVT.

**(i) Library-behavior confirmations the executor makes during RED and reports at the covering gate.**
The plan states the outcome to hit, not a guessed detail, for three surfaces not verifiable offline now:
  1. **Antiforgery guard status** — `UseAntiforgery` (placed per the spec sketch, after the authorization
     slot) must reject a token-less form POST to a minimal-API form endpoint mapped through
     `ConfigureEndpoints`; the expected status is **400**, the executor confirms the exact stock code and
     that `AddControllersWithViews` registers the antiforgery services the middleware requires. *(Gate 1.)*
  2. **Logged exactly once, on both branches** — JSON path: the Cloudstrap handler logs (the #5 pattern,
     whose exactly-once test is green on net10). HTML path: the handler returns `false` **without
     logging**, and the framework's `ExceptionHandlerMiddleware` logs once on re-execution. The executor
     confirms the categories/levels in RED. *(Gate 3.)*
  3. **Developer-page interplay** — minimal hosting auto-inserts the developer page in `Development` as
     outer middleware; Cloudstrap's `UseExceptionHandler` sits inner, so when
     `UseDeveloperExceptionPage` resolves `false` in Development the inner handler catches first and the
     developer page never renders; when it resolves `true` in Production, `app.UseDeveloperExceptionPage()`
     is added explicitly. Confirmed by the Step 5 override tests. *(Gate 3.)*

**(j) The SUT MVC host pins both environment-defaulted switches off in `appsettings.json`.**
`SutProcess` forces `ASPNETCORE_ENVIRONMENT=Development`, where the unset defaults would select the
developer page and detail-bearing JSON. The host therefore ships
`Cloudstrap:Mvc:ExceptionHandling: { UseDeveloperExceptionPage: false, IncludeDetails: false }` — the
documented overrides (AC-MVC6's override clause), making the *hardened* browser error page and generic
problem details the shapes the E2E asserts (the #5 `IncludeDetails` SUT precedent). A comment in the file
says exactly that. *(Final gate.)*

**(k) SUT host port and boot.** The MVC host takes **`http://127.0.0.1:5320`** (clear of 5300 Bff,
5301–5304 second instances, 5310/5311 IdP, 59999 dead port; README port map updated). `MvcHostTests`
boots it itself in `[OneTimeSetUp]` via
`SutProcess.Start("http://127.0.0.1:5320", null, "src/Test/WasmTestProject/src/Host/Mvc/Cloudstrap.WasmTestProject.Host.Mvc.csproj")`
— the `SelfHostedIdentityProviderTests` precedent — polling **`/healthz`** to readiness (which is itself
AC-MVC1's probe clause, live). The host needs no IdP and stays anonymous. The hardened `Secure` session
cookie works in Chromium over plain `http://127.0.0.1` because loopback is a trustworthy origin — the
recorded #10 harness precedent. *(Final gate.)*

**(l) Eager options read at registration time** (#5 Gate-1 decision 3 carried): `AddCloudstrapMvc` reads
`Cloudstrap:Mvc` (and `Cloudstrap:Application` for the cookie path) from `builder.Configuration` eagerly
and uses that instance for registration decisions (session on/off, HSTS/CORS registration);
`AddOptions<CloudstrapMvcOptions>().BindConfiguration(...).ValidateOnStart()` still provides DI resolution
and startup validation. Consequence for the README: configuration sources added *after* `AddCloudstrapMvc`
(e.g. `AddCloudstrapKeyVault`) do not affect those decisions — call KeyVault first.

**Canonical middleware order** (spec Public API Sketch with #5's approved correlation-before-authentication
ordering baked in; established across Steps 1–2 and never re-ordered — later steps only fill their
reserved slot):

```
developer exception page (only when UseDeveloperExceptionPage resolves true; in Development the
framework's auto-inserted page is left in charge, in Production it is added explicitly) |
UseExceptionHandler(ExceptionHandlerPath) otherwise: consumer IExceptionHandlers → Cloudstrap
negotiating handler (JSON, terminal) → re-execution at Cloudstrap:Application:ExceptionHandlerPath (HTML)
→ UseHsts (non-Development, when enabled) → security-header middleware → UsePathBase (when
ApplicationOptions.PathBase is non-empty) → UseStaticFiles (switch, default on) → hooks.BeforeRouting →
UseRouting → UseCors (only when origins are configured) → UseCloudstrapCorrelation (#2) →
UseAuthentication (scheme-map predicate) → hooks.BeforeAuthorization → UseAuthorization (same predicate)
→ UseSession (when Session.Enabled) → UseAntiforgery → hooks.BeforeEndpoints →
MapDefaultControllerRoute (switch, default on; attribute routes included) → MapCloudstrapHealthChecks (#4)
→ hooks.ConfigureEndpoints
```

---

## Slice 1 — One `Add`/`Use` pair serves a server-rendered MVC app that composes the shipped Cloudstrap seams and the consumer's own middleware

---

## Step 1 — Two calls in `Program.cs` and a browser gets an MVC page: the conventional default route answers at `/`, attribute routes work, `wwwroot` is served, and both pipeline switches compose (AC-MVC1 routing/static half, AC-MVC10 switch clause)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Mvc/Cloudstrap.Mvc.csproj` *(create)* — Sdk project, `TargetFramework=net10.0`,
  `GeneratePackageOnBuild=true`, `GenerateDocumentationFile=true`;
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (the suite's **fourth**);
  `<ProjectReference>` to `..\Cloudstrap.Core\`, `..\Cloudstrap.Observability\`,
  `..\Cloudstrap.Extensions\`; `<InternalsVisibleTo Include="Cloudstrap.Mvc.Tests" />` (mechanic (h));
  **zero `PackageReference`**. Description/tags/README metadata land in Step 7 (the #5 precedent —
  packable from day one).
- `src/Cloudstrap.Mvc/CloudstrapMvcConfigurator.cs` *(create)* — `public sealed`; this step:
  `Mvc : Action<IMvcBuilder>?` (`Session` arrives in Step 4).
- `src/Cloudstrap.Mvc/MvcPipelineOptions.cs` *(create)* — the full D-3 shape, declared here in one go
  (it is the frozen pattern): `BeforeRouting`/`BeforeAuthorization`/`BeforeEndpoints`
  (`Action<IApplicationBuilder>?`), `ConfigureEndpoints` (`Action<IEndpointRouteBuilder>?`),
  `MapDefaultControllerRoute : bool = true`, `UseStaticFiles : bool = true`. Step 1 honors the two
  switches and `ConfigureEndpoints`; the three middleware hooks are honored from Step 2.
- `src/Cloudstrap.Mvc/WebApplicationBuilderExtensions.cs` *(create)* —
  `public static WebApplicationBuilder AddCloudstrapMvc(this WebApplicationBuilder builder, Action<CloudstrapMvcConfigurator>? configure = null)`:
  guards; `services.AddCloudstrapCore()` + `AddCloudstrapCorrelation()` (idempotent, #1/#2);
  `AddHttpContextAccessor()`; stock `AddHealthChecks()` (additive, Aspire posture);
  `IMvcBuilder mvc = services.AddControllersWithViews()` (the spec's one **Port** row);
  `configurator.Mvc` invoked last. Registers **no** authentication (pairing is #5/#10's business).
- `src/Cloudstrap.Mvc/WebApplicationExtensions.cs` *(create)* —
  `public static WebApplication UseCloudstrapMvc(this WebApplication app, Action<MvcPipelineOptions>? configure = null)`:
  guards; this step wires `UseStaticFiles()` (when the switch is on) → `UseRouting()` →
  `MapDefaultControllerRoute()` (when the switch is on) → `hooks.ConfigureEndpoints`. The XML docs
  already state the full canonical order from the Overview so later steps fill slots without drift.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Cloudstrap.Mvc.Tests.csproj` *(create)* — mirror of
  `Cloudstrap.WebApi.Tests.csproj`: `net10.0`, `<ProjectReference>` to the package, version-less
  `<PackageReference>`s `Microsoft.AspNetCore.TestHost`, `Microsoft.Extensions.Configuration`,
  `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.Hosting` (all already CPM-pinned;
  NUnit wiring inherited from `src/Test/Directory.Build.props`).
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Infrastructure/MvcTestHost.cs` *(create)* — mechanic (g)
  fixture helper: builds a `TestServer`-hosted app from an in-memory `Cloudstrap:` dictionary (valid
  `Application` values), an optional environment name, optional configurator/pipeline actions, and a
  temp web root containing `site.css`; returns the `HttpClient` (+ access to the app for
  resolved-options assertions).
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Infrastructure/TestControllers.cs` *(create)* — neutral,
  content-returning fixtures (mechanic (e)): `HomeController` (`Index` returns a marker),
  `WidgetsController` (`Details(int id)` echoes the id), `[Route("api/catalog")] CatalogController`,
  plus their DTOs as needed.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/MvcRoutingTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under the `Test\UnitTest`
  solution folder (same nesting pattern as the existing eight).

**RED** *(write these tests first; for a brand-new project the honest first failure is the test project
failing to compile against missing types — the plan-4/plan-5 precedent — followed by real red runs once
the types exist)*:
- Unit test file: `MvcRoutingTests.cs`
  - `AddAndUse_HomeControllerIndex_AnswersAtTheRoot` — `GET /` → 200 with the Home marker: the
    conventional default route `{controller=Home}/{action=Index}/{id?}` is live (AC-MVC1; Deliberate
    Behavior Change 5 — the source mapped attribute routes only).
  - `AddAndUse_ConventionalRoute_BindsControllerActionAndId` — `GET /widgets/details/5` → body carries `5`.
  - `AddAndUse_AttributeRoutedController_AlsoAnswers` — `GET /api/catalog` → 200 (attribute routes
    included in the same mapping).
  - `Use_WithMapDefaultControllerRouteFalse_MapsNoControllerEndpoints` — `/` and `/widgets/details/5`
    404 while an endpoint mapped through `ConfigureEndpoints` answers 200 (AC-MVC10 switch clause; spec
    edge case: the consumer's explicit choice, not an error).
  - `Use_ServesStaticFilesFromTheWebRootByDefault` — `GET /site.css` → 200 `text/css` (AC-MVC1).
  - `Use_WithUseStaticFilesFalse_ServesNoStaticFiles` — `GET /site.css` → 404 (the documented
    `MapStaticAssets` adopters' switch).
  - `AddCloudstrapMvc_MvcHook_RunsAndCanAddApplicationParts` — asserted implicitly by every fixture
    (test controllers are only discoverable through it) and explicitly by a controller added through a
    second application part.
  - `AddCloudstrapMvc_OnNullBuilder_ThrowsArgumentNullException` /
    `UseCloudstrapMvc_OnNullApp_ThrowsArgumentNullException` (guard clauses).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = the new test project fails to compile against missing types
  src\Test\UnitTest\Cloudstrap.Mvc.Tests\bin\Debug\net10.0\Cloudstrap.Mvc.Tests.exe --filter "MvcRoutingTests"
  ```

**GREEN**: the Scope items. Full XML docs on every public member — the two entry points name the exact
configuration section (`Cloudstrap:Mvc`), the canonical order, and that the granular framework pieces
stay independently callable.

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to the next step)*:
1. Test exe → all pass: an app with two Cloudstrap calls now serves controllers over the conventional
   default route and attribute routes, serves `wwwroot`, and both switches compose with
   `ConfigureEndpoints` — none of which existed before.
2. `dotnet build src/Cloudstrap.sln` → zero warnings/errors; full `runTests` green (all existing suites
   untouched); `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Mvc.*.nupkg` appears under
   `src/Cloudstrap.Mvc/bin/Release/`.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — The one pipeline call composes everything else: probes and correlation from the shipped packages, the configured path base, antiforgery, the four hook points, the run-once guard — and auth middleware appears exactly when a scheme is registered, OIDC pairing included ⚠️ *(Risk Area: the inherited D-3 shape, frozen here for this package — AC-MVC1 probes, AC-MVC7, AC-MVC10)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Mvc/WebApplicationExtensions.cs` *(modify)* — fill the pipeline to the canonical order
  minus the slots owned by Steps 3–6: run-once marker + double-call throw and the two minimal-hosting
  middleware-marker claims (mechanic (c)) → `UsePathBase` (only when `ApplicationOptions.PathBase` is
  non-empty; stock `UsePathBase`, the #5 Gate-1 decision) → `UseStaticFiles` (switch, Step 1) →
  `hooks.BeforeRouting` → `UseRouting` → *(CORS slot reserved for Step 6)* → `UseCloudstrapCorrelation()`
  → `UseAuthentication` (scheme-map predicate) → `hooks.BeforeAuthorization` → `UseAuthorization` (same
  predicate) → *(session slot reserved for Step 3)* → `UseAntiforgery()` → `hooks.BeforeEndpoints` →
  `MapDefaultControllerRoute` (switch) → `MapCloudstrapHealthChecks()` → `hooks.ConfigureEndpoints`.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/PipelineCompositionTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Infrastructure/TestControllers.cs` *(modify)* — add an action
  echoing `ICorrelationContextAccessor.CorrelationId`, an `[Authorize]` page action, and a
  `[CorrelationRequired]` action.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Cloudstrap.Mvc.Tests.csproj` *(modify)* — add the **test-only**
  `<ProjectReference>` to `..\..\..\Cloudstrap.Authentication.OpenIdConnect\` (mechanic (f); the package
  itself gains no reference — AC-A3 posture untouched).

**RED** *(write these tests first, run them, confirm they fail — all assertions are real HTTP through
`app.GetTestClient()`; hook ordering is observed by having each hook insert a middleware appending its
name to a request-scoped list that a test endpoint echoes — the #5 idiom)*:
- Unit test file: `PipelineCompositionTests.cs`
  - `Use_ServesLivenessAndReadinessProbes` — `/healthz` 200 with a healthy `live`-tagged check while an
    unhealthy `ready`-tagged check keeps `/ready` at 503 — #4's mapping and #2's tag contract live in
    this pipeline, and this package maps no health code of its own (AC-MVC1).
  - `Use_WithExplicitMapCloudstrapHealthChecksCall_DoesNotDuplicateEndpoints` — idempotence clause.
  - `Use_FlowsInboundCorrelationId` — request with the configured header → the echo action returns
    exactly that id (AC-MVC7).
  - `Use_GeneratesAndEchoesACorrelationIdWhenAbsent` — non-empty generated id, echoed in the response
    header per `EchoInResponse` default (AC-MVC7).
  - `Use_OnCorrelationRequiredEndpointWithoutHeader_Returns400ProblemDetails` — #2's enforcement,
    produced by the shipped middleware.
  - `MvcAssembly_DeclaresNoCorrelationMiddlewareOfItsOwn` — reflection over the package assembly: no
    type whose name contains `Correlation` (AC-MVC7's second half; the source's
    `CorrelationSourceMiddleware` Drop made observable).
  - `Use_WithConfiguredPathBase_ServesUnderIt` — `Cloudstrap:Application:PathBase=contoso` →
    `/contoso/` 200 and generated links carry the prefix; with no path base nothing is prefixed (no
    env-var/workload magic — Deliberate Behavior Change 6).
  - `Use_Hooks_RunInTheDocumentedOrder` — the echoed list is exactly `BeforeRouting`,
    `BeforeAuthorization`, `BeforeEndpoints`; an endpoint mapped through `ConfigureEndpoints` answers.
  - `Use_CalledTwice_ThrowsInvalidOperationException` — message names `UseCloudstrapMvc` (AC-MVC10).
  - `Use_WithoutAnyAuthenticationScheme_AddsNoAuthMiddlewareAndEverythingIsAnonymous` — no scheme
    registered: the page answers 200, the scheme map is empty, no auth middleware failure anywhere
    (AC-MVC10's "no auth registered" half).
  - `Use_WithAConsumerCookieScheme_ChallengesAfterRouting` — `AddAuthentication().AddCookie(...)`
    registered by the "consumer": an anonymous request to the `[Authorize]` action → 302 to the cookie
    login path — the scheme-map predicate wires the middleware, placed after routing so endpoint
    metadata is visible (AC-MVC10; mechanic (c)).
  - `Use_WithCloudstrapOpenIdConnectRegistered_ChallengeRedirectsToTheSeededAuthority` — the real
    `AddCloudstrapOpenIdConnect` (test-only reference) with valid `Cloudstrap:OpenIdConnect`
    configuration and metadata pre-seeded through its `configurator.OpenIdConnect` hook (mechanic (f),
    no network): an anonymous `Accept: text/html` request to the `[Authorize]` page → 302 whose
    `Location` starts with the seeded authorization endpoint (AC-MVC10's pairing half).
  - `Use_AntiforgeryMiddleware_RejectsATokenlessFormPost` — a minimal-API form endpoint mapped through
    `ConfigureEndpoints`; a form POST without a token is rejected (expected 400 — mechanic (i.1), the
    executor confirms the exact stock status in RED); MVC controller actions are unaffected.
  - `Use_WithMapDefaultControllerRouteFalseAndNoEndpointsHook_StillServesStaticFilesAndProbes` — the
    spec edge case: static files + probes only, no error.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Mvc.Tests\bin\Debug\net10.0\Cloudstrap.Mvc.Tests.exe --filter "PipelineCompositionTests"
  ```

**GREEN**: the pipeline per Scope, in the canonical order, with the run-once and middleware markers. XML
docs on `UseCloudstrapMvc` spell out the full order (naming the reserved exception/HSTS/headers/CORS/session
slots), the scheme-map predicate ("no forced `RequireAuthorization` — endpoint protection belongs to the
auth package's fallback policy or your attributes", Deliberate Behavior Change 8), the forwarded-headers
posture (platform env var or `hooks.BeforeRouting` — never a silent library default), and the escape
hatch: consumers composing their own pipeline call the stock pieces (`UseSession`, `UseStaticFiles`,
`UseCloudstrapCorrelation`, `MapCloudstrapHealthChecks`) themselves.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: one call now yields the whole request pipeline — standard probes, correlation
   exactly per #2, the configured path base, antiforgery, four working hook points, the run-once guard,
   and authentication middleware that appears exactly when a scheme is registered, proven with a plain
   cookie scheme and with #10's real OIDC registration (AC-MVC1, AC-MVC7, AC-MVC10).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green;
   `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: the entry-point pair and the pipeline shape are frozen *(covers Steps 1–2)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **public API one-way door** — `AddCloudstrapMvc`, `UseCloudstrapMvc`,
`CloudstrapMvcConfigurator` and `MvcPipelineOptions` against the spec's Public API Sketch verbatim, and
the middleware order against this plan's canonical list (D-3 conformance: this is the #5-approved shape,
inherited — any deviation needs naming) · the **fourth `Microsoft.AspNetCore.App` framework reference**
(csproj created in Step 1; runtime-image consequence documented at Step 7) · **zero `PackageReference`**
in the package csproj (D-4/the spec's headline dependency fact) · mechanic (c)'s replicated run-once +
scheme-map + middleware-marker shape · mechanic (e)'s view-rendering placement (unit = routing with
content controllers, SUT = real `.cshtml`) — confirm or direct a Razor test project · mechanic (f)'s
challenge-redirect reading of AC-MVC10's "OIDC login round-trips" — confirm, or direct an OIDC login
in the Step 8 SUT host · mechanic (i.1)'s confirmed antiforgery status code.

- [x] Behavioral verification: test exe output shows — the conventional route at `/` with
  controller/action/id binding, attribute routes, both switches incl. the no-endpoints edge case, static
  files on/off, the application-part hook and the guard clauses (Step 1); probes with the live/ready tag
  contract and idempotence, inbound/generated/echoed/enforced correlation with no correlation type of our
  own, path base on/off, hook order, the double-call throw, the no-scheme anonymous baseline, the
  cookie-scheme and seeded-OIDC challenge redirects, and the antiforgery rejection (Step 2).
- [x] Code review: entry-point/configurator/pipeline-options signatures vs the spec sketch, verbatim;
  `internal` by default + sealed + full XML docs; `dotnet list src/Cloudstrap.Mvc/Cloudstrap.Mvc.csproj package`
  → **zero package references**, three project references; the test-only OIDC reference lives in the
  test csproj only.
- [x] User approved — implementation may continue past this gate *(2026-08-19; mechanics (e)/(f)
  confirmed as planned, antiforgery status confirmed 400 — mechanic (i.1))*

---

## Slice 2 — Session state is on by default and hardened by default, with zero session code of our own ⚠️ SESSION / COOKIE-SECURITY RISK AREA (D-1)

---

## Step 3 — An action writes to `ISession` and the browser gets exactly one hardened session cookie that round-trips — flowing entirely through stock `Microsoft.AspNetCore.Session` (AC-MVC2, AC-MVC4) ⚠️ *(Risk Area: session/cookie security defaults — auth-adjacent)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Mvc/SessionSettings.cs` *(create)* — `public sealed`, bound from
  `Cloudstrap:Mvc:Session`; the D-1 signed-off defaults, each a settable property:
  `Enabled : bool = true`, `CookieName : string = ".Cloudstrap.Session"`,
  `CookieSecurePolicy : CookieSecurePolicy = CookieSecurePolicy.Always`,
  `IdleTimeoutMinutes : int = 20`, `IsEssential : bool = false`. XML docs state that `HttpOnly = true`
  and `SameSite = Lax` are stock defaults asserted by tests, not modeled; that the cookie path follows
  `Cloudstrap:Application:PathBase` (else `/`); the plain-HTTP edge case (`Secure` cookie is not
  returned — dev over HTTPS or an explicit `SameAsRequest` override, never a silent downgrade); and the
  `IsEssential`/CookieConsent (#21) interplay.
- `src/Cloudstrap.Mvc/CloudstrapMvcOptions.cs` *(create)* — `public sealed`,
  `const string SectionName = "Cloudstrap:Mvc"`; this step's member: `Session : SessionSettings`
  (`ExceptionHandling` arrives in Step 5, `Hsts`/`Cors` in Step 6).
- `src/Cloudstrap.Mvc/CloudstrapMvcOptionsValidator.cs` *(create)* — mechanic (a): `internal sealed :
  IValidateOptions<CloudstrapMvcOptions>`; this step's rule: `Session.IdleTimeoutMinutes > 0` while
  `Session.Enabled`, failure naming `Cloudstrap:Mvc:Session:IdleTimeoutMinutes`.
- `src/Cloudstrap.Mvc/WebApplicationBuilderExtensions.cs` *(modify)* — bind + `ValidateOnStart`
  `CloudstrapMvcOptions` with the validator (`TryAddEnumerable`); eager read (mechanic (l)); when
  `Session.Enabled`: stock `services.AddDistributedMemoryCache()` (its own `TryAdd` semantics are the
  D-5 fallback — a consumer-registered `IDistributedCache` wins by construction) +
  `services.AddSession(session => …)` applying exactly the hardening delta: `Cookie.Name`,
  `Cookie.SecurePolicy`, `Cookie.IsEssential`, `Cookie.Path` = normalized
  `ApplicationOptions.PathBase` when non-empty else `/`, `IdleTimeout` from `IdleTimeoutMinutes` —
  `HttpOnly`/`SameSite` deliberately untouched (stock). When `Session.Enabled = false`: neither call.
- `src/Cloudstrap.Mvc/WebApplicationExtensions.cs` *(modify)* — fill the reserved session slot: stock
  `app.UseSession()` between the authorization slot and `UseAntiforgery`, only when `Session.Enabled`.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Infrastructure/TestControllers.cs` *(modify)* — a
  `SessionController`: `write` (stores a marker value via `HttpContext.Session`), `read` (returns it or
  404).
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/SessionDefaultsTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — cookie attributes are asserted on the
raw `Set-Cookie` response header; the round-trip resends the cookie manually on the TestServer client)*:
- Unit test file: `SessionDefaultsTests.cs`
  - `SessionWrite_EstablishesExactlyOneHardenedCookie` — the establishing response carries exactly one
    session cookie: name `.Cloudstrap.Session`, `secure`, `httponly`, `samesite=lax`, `path=/`
    (AC-MVC2, all six attributes pinned — the fork's whole delta now expressed as startup options).
  - `SessionCookie_PathFollowsTheConfiguredPathBase` — `Cloudstrap:Application:PathBase=contoso` →
    `path=/contoso`.
  - `SessionWrite_ResponseCarriesTheNoStoreHeaders` — `Cache-Control: no-cache,no-store` +
    `Pragma: no-cache` + `Expires: -1` (stock behavior, asserted because AC-MVC2 names it — spec
    finding 2: this was never fork-added value).
  - `SessionRoundTrip_ReadsTheStoredValueBack` — a follow-up request presenting the cookie returns the
    stored marker (AC-MVC2's round-trip clause).
  - `SessionCookieValue_IsOpaque` — the cookie value does not contain the stored marker text
    (DataProtection-protected, stock).
  - `SessionRequest_WithoutAWrite_IssuesNoCookie` — a plain page response carries no `Set-Cookie`
    (stock establish-on-write semantics preserved — nothing eager was added).
  - `MvcAssembly_ContainsNoSessionCodeOfItsOwn` — reflection over the package assembly: no type
    implements `ISession` or `ISessionStore`, and no type name contains `SessionMiddleware` or
    `CookieProtection` (`SessionSettings` is the allowed options type) — AC-MVC4, D-1 made structural.
  - `AddCloudstrapMvc_IdleTimeoutZero_FailsStartupNamingTheKey` — message contains
    `Cloudstrap:Mvc:Session:IdleTimeoutMinutes`.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Mvc.Tests\bin\Debug\net10.0\Cloudstrap.Mvc.Tests.exe --filter "SessionDefaultsTests"
  ```

**GREEN**: the Scope items — the entire hardening delta as startup-time `SessionOptions`, zero forked
middleware (the fork's per-request shared-options mutation defect disappears by construction — spec
finding 3). XML docs name every default, its `Cloudstrap:Mvc:Session` key, and the stock cookie
compatibility (same DataProtection purpose string).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: writing to `ISession` now establishes exactly one `.Cloudstrap.Session` cookie —
   `Secure` always, `HttpOnly`, `SameSite=Lax`, path-base-scoped, opaque — that round-trips, with the
   no-store headers on the establishing response, an idle-timeout misconfiguration failing startup by
   key name, and a reflection proof that this package ships no session code at all (AC-MVC2, AC-MVC4).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green;
   `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — Every session convention is overridable, off means off, and a consumer's distributed cache wins (AC-MVC3, AC-MVC11) ⚠️ *(Risk Area: session/cookie security defaults — auth-adjacent)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Mvc/CloudstrapMvcConfigurator.cs` *(modify)* — add
  `Session : Action<SessionOptions>?` (the `Microsoft.AspNetCore.Builder.SessionOptions` type), XML docs:
  runs **after** the Cloudstrap defaults, full `SessionOptions` access, final say.
- `src/Cloudstrap.Mvc/WebApplicationBuilderExtensions.cs` *(modify)* — invoke `configurator.Session`
  last inside the `AddSession` delegate.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/SessionOverrideTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Infrastructure/RecordingDistributedCache.cs` *(create)* — a
  test-owned `IDistributedCache` wrapping `MemoryDistributedCache` and recording `Set`/`Get` calls.

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `SessionOverrideTests.cs`
  - `SessionCookieName_ConfiguredOverride_Wins` — `Cloudstrap:Mvc:Session:CookieName=.Contoso.Session`
    → the establishing cookie carries that name (AC-MVC3).
  - `SessionSecurePolicy_SameAsRequestOverride_DropsSecureOverHttp` — the configured
    `CookieSecurePolicy=SameAsRequest` produces a cookie without the `secure` attribute on an HTTP
    request (the documented local-dev override; behavioral, not options-inspection).
  - `SessionIdleTimeoutAndIsEssential_ConfiguredOverrides_LandOnTheResolvedOptions` — asserted on the
    resolved `IOptions<SessionOptions>` (`IdleTimeout`, `Cookie.IsEssential`) — the #5
    resolved-options precedent for settings whose behavior needs a 20-minute wait.
  - `SessionHook_RunsAfterTheCloudstrapDefaultsAndWins` — `configurator.Session` sets a cookie name
    different from both the default and the configured value → the hook's name is on the wire (AC-MVC3's
    "runs after and wins").
  - `SessionDisabled_WiresNoSessionServicesAndIssuesNoCookie` — `Cloudstrap:Mvc:Session:Enabled=false`:
    no response ever carries a session `Set-Cookie`, and the session slot adds no middleware; accessing
    `HttpContext.Session` surfaces the stock `InvalidOperationException` (no Cloudstrap masking — spec
    edge case; the executor confirms the exact stock surfacing through TestServer in RED).
  - `ConsumerRegisteredDistributedCache_IsTheOneSessionUses` — a `RecordingDistributedCache` registered
    **before** `AddCloudstrapMvc` records the session write/read; the TryAdd fallback never displaces
    it (AC-MVC11, D-5).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Mvc.Tests\bin\Debug\net10.0\Cloudstrap.Mvc.Tests.exe --filter "SessionOverrideTests"
  ```

**GREEN**: the Scope items. XML docs on `SessionSettings`/the hook restate the override ladder
(defaults → `Cloudstrap:Mvc:Session` → `configurator.Session`) and point multi-instance deployments at
the README recipe (distributed `IDistributedCache` + #4's `AddCloudstrapDataProtection` — written in
Step 7).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: every signed-off session default is now provably overridable through
   configuration and through the hook that runs last; disabling session removes it entirely with stock
   failure semantics; and any consumer-registered distributed cache is the one session state actually
   uses (AC-MVC3, AC-MVC11).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green;
   `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: ⚠️ SESSION SIGN-OFF — the auth-adjacent surface, as built *(covers Steps 3–4)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start
the next step. This gate is a mandatory human review under CLAUDE.md's risk-area rule and the spec's D-1
standing rule: the session/cookie **defaults** were signed off at the spec gate, the **implementation**
is signed off here.*

⚠️ **Risk areas at this gate**: **session/cookie security, end to end** — the `Set-Cookie` attributes on
the wire against the D-1 sign-off verbatim (`.Cloudstrap.Session`, `Secure` always, `HttpOnly`,
`SameSite=Lax`, path-base-scoped, `IsEssential=false`, 20-minute idle), the override ladder, and the
structural proof that Cloudstrap owns **zero** session middleware/store/cookie-protection code
(AC-MVC4 — the copied-stock-code provenance risk closed by construction) · **public API one-way door** —
`SessionSettings` and the `Cloudstrap:Mvc:Session` keys are permanent surface · the D-5
consumer-cache-wins posture and the stock `AddDistributedMemoryCache` TryAdd fallback · mechanic (a)'s
validator placement of the idle-timeout rule.

- [x] Behavioral verification: test exe output shows — the exactly-one hardened cookie with all six
  attributes, the path-base-scoped path, the no-store headers, the opaque value, the round-trip, the
  no-write-no-cookie proof, the assembly sweep and the idle-timeout fail-fast (Step 3); the name/policy
  overrides on the wire, the resolved-options overrides, the hook-wins proof, the fully-removed disabled
  mode with stock failure semantics, and the consumer-cache-wins recording (Step 4).
- [x] Code review (session): the `AddSession` delegate against D-1 line by line — exactly the hardening
  delta (name, `SecurePolicy`, `IsEssential`, path, idle timeout), `HttpOnly`/`SameSite` untouched
  stock, hook invoked last, nothing registered when disabled; no session type anywhere in the package.
- [x] User approved — implementation may continue past this gate *(2026-08-19)*

---

## Slice 3 — The app is safe at the edge: browsers get an error page and JSON clients get problem details, every response carries the right hardening headers

---

## Step 5 — An action throws: browsers get the consumer's `/error` page, JSON clients get RFC 9457 problem details, `Development` keeps the developer page — every selection overridable, logged exactly once (AC-MVC5, AC-MVC6)

- [x] Done *(executor note: the Step 4 disabled-session test was adapted — the Step 5 error head now
  answers the stock `InvalidOperationException` as a 500 problem details instead of letting it propagate
  raw through TestServer; the assertion pins the stock exception type/message in the detail payload.)* *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Mvc/ExceptionHandlingSettings.cs` *(create)* — `IncludeDetails : bool? = null`
  (null → details in `Development` only; **JSON path only** — D-2's confirmed sub-question),
  `UseDeveloperExceptionPage : bool? = null` (null → `Development`).
- `src/Cloudstrap.Mvc/CloudstrapMvcOptions.cs` *(modify)* — add `ExceptionHandling`.
- `src/Cloudstrap.Mvc/EnvironmentDefault.cs` *(create)* — `internal static`, mechanic (b): the
  re-expressed `Resolve(bool? explicitValue, bool environmentDefault)` with this package's two rules
  documented in one place.
- `src/Cloudstrap.Mvc/MvcExceptionHandler.cs` *(create)* — `internal sealed : IExceptionHandler`, the
  D-2 content-negotiating terminal handler: for **HTML-preferring** requests (mechanic (d) rule) it
  returns `false` without writing or logging, so stock `UseExceptionHandler` re-executes
  `Cloudstrap:Application:ExceptionHandlerPath`; for everything else it logs the exception once
  (`MvcLog`), writes `500` `application/problem+json` through `IProblemDetailsService` with a generic
  title, the `correlationId` extension (three-source resolution:
  `HttpContext.GetCloudstrapCorrelationId()` → `ICorrelationContextAccessor` → inbound header — the #5
  shape), and — only when `IncludeDetails` resolves true — the exception type, message, stack trace and
  a depth-5-bounded inner chain; returns `true`. Re-expressed here, **no `Cloudstrap.WebApi` reference**
  (D-2).
- `src/Cloudstrap.Mvc/MvcLog.cs` *(create)* — `internal static partial`, source-generated
  `LoggerMessage` for the unhandled-exception entry (the `WebApiLog` pattern).
- `src/Cloudstrap.Mvc/WebApplicationBuilderExtensions.cs` *(modify)* — `AddProblemDetails()` +
  `AddExceptionHandler<MvcExceptionHandler>()` registered **last** (consumer handlers registered before
  `AddCloudstrapMvc` get the first attempt — the ordering contract replacing the source's reflection
  loop).
- `src/Cloudstrap.Mvc/WebApplicationExtensions.cs` *(modify)* — the pipeline head, mechanic (i.3):
  resolve `UseDeveloperExceptionPage` via `EnvironmentDefault`; when it resolves **true**: leave the
  framework's auto-inserted developer page in charge in `Development`, add
  `app.UseDeveloperExceptionPage()` explicitly outside it; when **false**:
  `app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandlerPath = application.ExceptionHandlerPath })`
  — Core's shipped `ExceptionHandlerPath` (default `/error`) finds its deliberate consumer here.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Infrastructure/TestControllers.cs` *(modify)* — a
  `BoomController` throwing a nested exception (chain deeper than 5) and an `ErrorController` serving a
  marker page at `/error`.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/Infrastructure/MvcTestHost.cs` *(modify)* — a capturing
  `ILoggerProvider`.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/ExceptionHandlingTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `ExceptionHandlingTests.cs`
  - `Throwing_InProductionWithAcceptTextHtml_ReExecutesTheConsumersErrorPage` — 500, `text/html`, body
    is the `/error` marker, and contains none of the exception type/message/stack text (AC-MVC5a — the
    source's raw-JSON-to-browsers defect provably gone).
  - `Throwing_InProductionPreferringJson_ReturnsGenericProblemJson` — 500,
    `application/problem+json`, `title`/`status` present, no exception detail (AC-MVC5b).
  - `Throwing_WithAcceptAnyOnly_IsTreatedAsJsonPreferring` and
    `Throwing_WithNoAcceptHeader_IsTreatedAsJsonPreferring` — mechanic (d) pinned (spec edge case).
  - `Throwing_JsonPath_IncludesTheAmbientCorrelationId` — the `correlationId` extension equals the
    inbound header value.
  - `Throwing_InDevelopmentJsonPath_IncludesTypeMessageStackAndBoundedInnerChain` — the unset default
    resolves true in `Development`; an 8-deep chain surfaces at most 5 levels.
  - `Throwing_HtmlPath_NeverIncludesDetailEvenWithIncludeDetailsTrue` — the error page re-executes
    with no exception content even when `IncludeDetails=true` (D-2's confirmed sub-question).
  - `Throwing_WithIncludeDetailsExplicit_WinsInBothDirections` — `false` in Development strips detail;
    `true` in Production adds it (JSON path).
  - `Throwing_InDevelopmentBrowserRequest_RendersTheDeveloperExceptionPage` — `text/html` carrying the
    exception type text: the framework page, not Cloudstrap's handler (AC-MVC6).
  - `Throwing_WithUseDeveloperExceptionPageFalseInDevelopment_ReExecutesTheErrorPageInstead` — the
    override (and the Step 8 SUT posture, mechanic (j)).
  - `Throwing_WithUseDeveloperExceptionPageTrueInProduction_RendersTheDeveloperPage` — the other
    direction.
  - `Throwing_IsLoggedExactlyOnce_OnTheJsonPath` and `Throwing_IsLoggedExactlyOnce_OnTheHtmlPath` —
    the capturing provider recorded exactly one `Error` entry carrying the exception (mechanic (i.2):
    executor confirms the HTML-path category is the framework middleware's).
  - `Throwing_WithAConsumerExceptionHandlerRegisteredFirst_TheConsumerWins` — a handler registered
    before `AddCloudstrapMvc` returning `true` produces its response; Cloudstrap's payload and the
    re-execution are both absent.
  - `Throwing_HtmlWithNoEndpointAtTheErrorPath_SurfacesA500` — no consumer `/error` endpoint: stock
    `ExceptionHandlerMiddleware` semantics surface a 500, nothing silently swallowed (spec edge case).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Mvc.Tests\bin\Debug\net10.0\Cloudstrap.Mvc.Tests.exe --filter "ExceptionHandlingTests"
  ```

**GREEN**: the Scope items. XML docs on `ExceptionHandlingSettings`: both environment defaults, the
negotiation rule verbatim, the JSON-only scope of `IncludeDetails`, the "never enable details on a public
production app" warning, the consumer-handler ordering contract, and the minimal `/error` action the
consumer must supply (no shipped default page — the README and SUT host show one).

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an unhandled exception now produces the right shape for every caller — the
   consumer's error page for browsers, generic RFC 9457 for JSON clients with detail only on explicit
   opt-in, the developer page exactly where configured — each selection overridable in both directions,
   correlated, logged exactly once on both branches (AC-MVC5, AC-MVC6; spec finding 4's
   broken-by-construction handler provably replaced).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green;
   `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — Every response is hardened: nosniff and no-referrer always, HSTS outside Development, CORS only for origins you actually configured (AC-MVC8, AC-MVC9)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Mvc/HstsSettings.cs` *(create)* — the #5 shape as a package-local type, keys under
  `Cloudstrap:Mvc:Hsts`: `Enabled : bool = true`, `MaxAgeDays : int = 365`,
  `IncludeSubDomains : bool = true`, `Preload : bool = false`.
- `src/Cloudstrap.Mvc/CorsSettings.cs` *(create)* — the #5 shape as a package-local type, keys under
  `Cloudstrap:Mvc:Cors`: `AllowedOrigins : IList<string>` (get-only initialized, **empty** default;
  append-to-defaults caveat documented).
- `src/Cloudstrap.Mvc/CloudstrapMvcOptions.cs` *(modify)* — add `Hsts` and `Cors`.
- `src/Cloudstrap.Mvc/CloudstrapMvcOptionsValidator.cs` *(modify)* — `Hsts.MaxAgeDays > 0` while
  `Hsts.Enabled`, naming `Cloudstrap:Mvc:Hsts:MaxAgeDays`.
- `src/Cloudstrap.Mvc/SecurityHeadersMiddleware.cs` *(create)* — `internal sealed`, the re-expressed
  ~15-line D-4 middleware: `X-Content-Type-Options: nosniff` + `Referrer-Policy: no-referrer` on every
  response, never overwriting a value the app set. **No NWebsec, no NetEscapades** (D-4 — the richer
  HTML set is the documented README recipe via `hooks.BeforeRouting`, Step 7).
- `src/Cloudstrap.Mvc/WebApplicationBuilderExtensions.cs` *(modify)* — `AddHsts(...)` from the options;
  CORS registration mirroring #5's `ConfigureCors` exactly: a default policy **only when at least one
  origin is configured** (`WithOrigins` + `AllowAnyHeader` + `AllowAnyMethod` + `AllowCredentials` +
  `SetIsOriginAllowedToAllowWildcardSubdomains()` when any origin contains `*`); no origins → nothing
  registered (Deliberate Behavior Change 4 — the source's `AllowAnyOrigin` fallback is gone).
- `src/Cloudstrap.Mvc/WebApplicationExtensions.cs` *(modify)* — fill the reserved slots per the
  canonical order: `UseHsts()` (non-Development, when enabled) and the security-header middleware before
  the path base; `UseCors()` right after `UseRouting` only when origins are configured.
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/EdgeHardeningTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — HSTS cases issue the request against
`https://localhost/...` through the TestServer client, the #5 idiom)*:
- Unit test file: `EdgeHardeningTests.cs`
  - `EveryResponse_CarriesNosniffAndNoReferrer` — an MVC page response **and** a `/healthz` probe
    response both carry both headers (the middleware sits before routing) (AC-MVC8).
  - `SecurityHeaders_DoNotOverwriteAValueTheAppAlreadySet` — an action setting `Referrer-Policy` keeps
    its value (AC-MVC8's never-overwrite clause).
  - `Hsts_InProductionOverHttps_EmitsStrictTransportSecurityWithoutPreload` —
    `max-age=31536000; includeSubDomains`, no `preload` token (AC-MVC8).
  - `Hsts_InDevelopment_EmitsNothing` and `Hsts_WithEnabledFalse_EmitsNothing`.
  - `Hsts_WithConfiguredMaxAgeAndPreload_ReflectsThem` — overrides land in the header.
  - `Hsts_WithMaxAgeDaysZero_FailsStartupNamingTheKey` — `Cloudstrap:Mvc:Hsts:MaxAgeDays`.
  - `Cors_WithNoOriginsConfigured_NeverEmitsAccessControlAllowOrigin` — an `OPTIONS` preflight carrying
    `Origin` gets no `Access-Control-Allow-Origin` (AC-MVC9, browser default-deny).
  - `Cors_WithConfiguredOrigin_PreflightSucceedsForThatOriginOnly` — the configured origin gets
    `Access-Control-Allow-Origin` + `Access-Control-Allow-Credentials: true`; a different origin gets
    neither (AC-MVC9).
  - `Cors_WithWildcardSubdomainOrigin_AllowsMatchingSubdomains` — `https://*.contoso.example` allows
    `https://app.contoso.example`, rejects `https://app.fabrikam.example` (the kept source capability).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Mvc.Tests\bin\Debug\net10.0\Cloudstrap.Mvc.Tests.exe --filter "EdgeHardeningTests"
  ```

**GREEN**: the Scope items. XML docs: the two header constants and why no default CSP/`X-Frame-Options`
exists (D-4 — wrong defaults break real apps; the NetEscapades recipe is the README's), the HSTS
no-preload rationale, and the CORS additive/named-policies escape hatch.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: every response — page and probe alike — now carries the two constant headers
   without overwriting app-set values; HSTS is emitted exactly where it belongs, never claiming preload
   unasked; CORS is genuinely absent until origins are configured, then exact, credentialed and
   wildcard-subdomain capable (AC-MVC8, AC-MVC9).
2. `dotnet build src/Cloudstrap.sln` → zero warnings; full `runTests` green;
   `dotnet format src/Cloudstrap.sln --verify-no-changes` → exit 0.

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: the error contract and the edge posture *(covers Steps 5–6)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **the error contract is public behavior** — the negotiation rule
(mechanic (d)) decides which callers see which shape forever: confirm `text/html`/`application/xhtml+xml`
as the HTML markers and `*/*`-alone as JSON, or direct a change now · **`ExceptionHandlingSettings` and
the `Cloudstrap:Mvc:ExceptionHandling`/`Hsts`/`Cors` keys are permanent surface** · **security
defaults** — two constant headers instead of a security-headers library on an HTML surface (D-4, as
decided; the NetEscapades recipe is documentation), HSTS without preload, no-origins→no-CORS · mechanic
(b)'s deliberate internal duplication (`EnvironmentDefault`, `SecurityHeadersMiddleware`) — confirm
re-expression over a shared internals package · mechanics (i.2)/(i.3) executor reports — the
exactly-once logging categories on both branches and the developer-page interplay, as measured ·
`ApplicationOptions.ExceptionHandlerPath` now has its consumer (the #5 gate's "re-execution is #6's
pattern" note closed).

- [x] Behavioral verification: test exe output shows — the browser error page with nothing leaked, the
  generic JSON payload, all four negotiation pins, the correlation extension, the Development detail
  payload with the depth-5 bound, the JSON-only detail rule on the HTML path, both explicit-override
  directions for both switches, the developer page in both its selected modes, exactly-once logging on
  both branches, the consumer-handler-first proof and the missing-error-endpoint 500 (Step 5); both
  constant headers on page and probe with the no-overwrite rule, HSTS emitted/withheld/overridden with
  no preload and the zero-max-age fail-fast, and the three CORS proofs incl. wildcard subdomains
  (Step 6).
- [x] Code review: `MvcExceptionHandler` against D-2 line by line — negotiate, JSON-terminal,
  HTML-fall-through, no `Cloudstrap.WebApi` reference, no bespoke `{StatusCode, Message}` JSON anywhere;
  `SecurityHeadersMiddleware` ~15 lines, no new dependency;
  `dotnet list src/Cloudstrap.Mvc/Cloudstrap.Mvc.csproj package` → still zero package references.
- [x] User approved — implementation may continue past this gate *(2026-08-19; negotiation rule and
  D-4 two-header posture confirmed as permanent surface)*

---

## Slice 4 — Publishable, permanently guarded, and demonstrated in the running WASM SUT

---

## Step 7 — The package is publishable and guarded forever: metadata, README, and tripwires on the surface, the closure and the forbidden identifiers (AC-MVC12, AC-ASP2, AC-A3)

- [x] Done *(executor note: the nuspec dependency list flattens the transitive packages of the three
  referenced Cloudstrap projects — byte-for-byte the same behavior as the gate-approved
  Cloudstrap.WebApi nupkg, with the versioning/OpenAPI/Scalar/JwtBearer stack absent as required; the
  identifier-sweep hits are the guard test's own patterns and the README's mandated migration note.)* *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Mvc/Cloudstrap.Mvc.csproj` *(modify)* — `<Description>` (server-rendered MVC
  bootstrap: controllers + views, hardened session state on stock `Microsoft.AspNetCore.Session`,
  content-negotiated error handling — error page for browsers, RFC 9457 for JSON clients — correlation,
  health probes, security headers, HSTS and CORS; two calls and one `Cloudstrap:Mvc` section),
  `<PackageTags>$(PackageTags);mvc;session;errorhandling;problemdetails;aspnetcore</PackageTags>`,
  `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="/" />`.
- `src/Cloudstrap.Mvc/README.md` *(create)* — the sub-ten-line `Program.cs` quick start (mirrors the
  Step 8 SUT host — D-3's "doubles as the consumer example"); **the canonical middleware order** as a
  numbered list with each hook's slot named; settings tables for the four owned subsections
  (`Cloudstrap:Mvc:Session`, `:Hsts`, `:Cors`, `:ExceptionHandling`) plus the consumed sections
  (`Cloudstrap:Application` `PathBase`/`ExceptionHandlerPath`, `Cloudstrap:HealthChecks`,
  `Cloudstrap:Correlation`) marked "owned elsewhere, never redefined"; the session posture section —
  the D-1 defaults with their override keys, the plain-HTTP `Secure`-cookie edge case
  (`SameAsRequest` for local HTTP, never a silent downgrade), the `IsEssential`/CookieConsent (#21)
  interplay, and the **multi-instance recipe** (register a distributed `IDistributedCache` + call #4's
  `AddCloudstrapDataProtection`; single-instance apps need nothing — AC-MVC11); the error-handling
  section — the negotiation rule, the JSON-only `IncludeDetails` warning, and the minimal consumer
  `/error` action example (no shipped default page); the **NetEscapades security-headers recipe** via
  `hooks.BeforeRouting` for consumers wanting the full HTML bundle (D-4); the `MapStaticAssets`
  recipe (`UseStaticFiles = false` + `hooks.ConfigureEndpoints`); the forwarded-headers note
  (`ASPNETCORE_FORWARDEDHEADERS_ENABLED` or `hooks.BeforeRouting` — never a library default); the
  authentication pairing section (no schemes registered here; `AddCloudstrapOpenIdConnect`/#10 and the
  scheme-map predicate; no forced `RequireAuthorization`); the **"not with `AddCloudstrapWebApi`"**
  warning (two pipeline owners — compose the granular pieces instead, spec edge case); the
  configuration-ordering note from mechanic (l) (KeyVault first); the **Aspire coexistence** note
  (health additive on the stock builder, zero `Aspire.*`); the framework-reference consequence (server
  apps only); and the migration notes (Deliberate Behavior Changes 1–9 — session fork gone with cookie
  rename `nihdi.session` → `.Cloudstrap.Session`, inbound correlation honored, browsers get pages not
  JSON, no `AllowAnyOrigin` fallback, conventional route on by default, no forwarded-headers/static-web-assets/path-base
  magic, localization unbundled, no auto-`RequireAuthorization`, no missing-cache startup surprise).
- `src/Test/UnitTest/Cloudstrap.Mvc.Tests/PackageSurfaceTests.cs` *(create)* — permanent guards
  mirroring `Cloudstrap.WebApi.Tests/PackageSurfaceTests.cs`.

**RED** *(guard tests are written and run first but, as tripwires against already-correct code, may pass
immediately — the honest failing state is in the artifacts: before GREEN the Release nupkg has no
README/description/tags; recorded per the plan-2/3/4/5 precedent)*:
- Unit test file: `PackageSurfaceTests.cs`
  - `ReferencedAssemblies_OfMvcAssembly_MatchTheApprovedClosure` — every referenced assembly starts
    with `System` or `Microsoft.` or equals `Cloudstrap.Core`/`Cloudstrap.Observability`/`Cloudstrap.Extensions`;
    explicitly **zero** names starting `Aspire` (AC-ASP2), `NWebsec` (AC-MVC8/D-4), `NetEscapades`,
    `Nihdi` (AC-A3), `NSwag`, `Duende`, `Asp.Versioning`, `Scalar` (this closure is *smaller* than
    #5's — no versioning/OpenAPI stack may leak in).
  - `PublicTypes_OfMvcAssembly_ContainNoForbiddenIdentifiers` — no public type/member matches
    `(?i)nihdi|riziv|dynatrace|nservicebus`.
  - `PublicTypes_OfMvcAssembly_AreSealedOrStaticAndInTheSingleApprovedNamespace` — namespace
    `Cloudstrap.Mvc` only; every public class sealed or static; no public interfaces.
  - `MvcAssembly_DeclaresNoSessionOrCorrelationImplementationTypes` — the Step 2/3 sweeps made
    permanent: no `ISession`/`ISessionStore` implementors, no type name containing
    `SessionMiddleware`, `CookieProtection` or `Correlation` (AC-MVC4, AC-MVC7 — the D-1 and
    finding-5 drops guarded forever).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Mvc.Tests\bin\Debug\net10.0\Cloudstrap.Mvc.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: add the csproj metadata and write `README.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass, including the four new guards.
2. `dotnet build src/Cloudstrap.sln -c Release` →
   `src/Cloudstrap.Mvc/bin/Release/Cloudstrap.Mvc.<version>.nupkg`; expand a `.zip` copy → contains
   `README.md`, `icon.png`, `lib/net10.0/Cloudstrap.Mvc.dll` **and** `.xml`; the nuspec shows the MIT
   license expression, description, tags, repository URL, and a dependency list containing **only** the
   three `Cloudstrap.*` packages — no `NWebsec.*`, no `NetEscapades.*`, no `Nihdi.*`, no `Aspire.*`
   (AC-MVC12, AC-ASP2).
3. **AC-MVC12 identifier sweep** (new package + tests):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Mvc, src/Test/UnitTest/Cloudstrap.Mvc.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the hits,
   as in plans 2–5).
4. **Closure check**: `dotnet list src/Cloudstrap.Mvc/Cloudstrap.Mvc.csproj package` reviewed against
   the spec's Dependencies table — three project references, the framework reference, nothing else.
5. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + `runTests` (all
   suites) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 8 — The WASM SUT gains a running MVC host on `Cloudstrap.Mvc`: a session-backed visit counter and a real browser error page, proven by Playwright while every existing E2E test stays green (AC-MVC13; demonstration slice, D-3)

- [x] Done *(executor note for the gate: unrelated to this deliverable, the pre-existing
  `Cloudstrap.Authentication.ClientCredentials.Tests` test
  `DefaultIsolatedMode_ARegisteredIDistributedCacheNeverReceivesAnything` (#9, AC-CC12) turned
  timing-flaky during this session and now fails reproducibly — diagnosed as the assert racing an
  asynchronous HybridCache distributed-tier write that reaches the app's `IDistributedCache` even in
  Isolated mode. Reported at the gate; root-caused and fixed 2026-08-19 (commit `ebf8a4b`): Duende
  pre-registers the keyed token cache, so the package's `TryAddKeyedSingleton` was a silent no-op and
  `TokenCacheMode.Isolated` never took effect — a real AC-CC12 defect in shipped behavior, not an
  unsound test.)* *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Host/Mvc/Cloudstrap.WasmTestProject.Host.Mvc.csproj` *(create)* — the
  IdentityProvider-host precedent: `Microsoft.NET.Sdk.Web`, `net10.0`, `<ProjectReference>` to
  `Cloudstrap.Mvc` only; name does not end in `.Tests` so `src/Test/Directory.Build.props` leaves it a
  plain app (`IsPackable=false` inherited).
- `src/Test/WasmTestProject/src/Host/Mvc/Program.cs` *(create)* — the README's consumer example, live
  (D-3): `builder.AddCloudstrapMvc();` → `app.UseCloudstrapMvc();` → `app.RunAsync()` — deliberately
  under ten lines, no auth, no IdP dependency.
- `src/Test/WasmTestProject/src/Host/Mvc/Controllers/HomeController.cs` *(create)* — `Index`:
  increments an `ISession` visit counter and renders the view; `Boom`: throws a nested exception (the
  error-path fixture).
- `src/Test/WasmTestProject/src/Host/Mvc/Controllers/ErrorController.cs` *(create)* — the minimal
  consumer `/error` action (`[Route("/error")]`, returns the error view with status 500) the README
  documents.
- `src/Test/WasmTestProject/src/Host/Mvc/Views/` *(create)* — `Home/Index.cshtml` (renders the counter
  with `data-testid="visit-count"`), `Error/Index.cshtml` (neutral apology page,
  `data-testid="error-page"`, no exception content), `_ViewImports.cshtml`/`_ViewStart.cshtml` as
  needed — the real `.cshtml` rendering AC-MVC1's view clause live (mechanic (e)).
- `src/Test/WasmTestProject/src/Host/Mvc/wwwroot/site.css` *(create)* — referenced by the view: static
  files live.
- `src/Test/WasmTestProject/src/Host/Mvc/appsettings.json` *(create)* — valid `Cloudstrap:Application`
  values (`SystemName: "wasmtestproject"`, `SubsystemName: "mvcdemo"`, neutral) and mechanic (j):
  `Cloudstrap:Mvc:ExceptionHandling: { "UseDeveloperExceptionPage": false, "IncludeDetails": false }`
  with the explanatory comment (the E2E run is forced to `Development`; these are the documented
  overrides that make the hardened shapes assertable).
- `src/Test/WasmTestProject/src/Host/Mvc/Properties/launchSettings.json` *(create)* — http profile on
  `http://127.0.0.1:5320` for manual-run parity (the fixture uses `--no-launch-profile`).
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/MvcHostTests.cs` *(create)* —
  `: PageTestBase`; `[OneTimeSetUp]` boots the host per mechanic (k)
  (`SutProcess.Start("http://127.0.0.1:5320", null, "src/Test/WasmTestProject/src/Host/Mvc/Cloudstrap.WasmTestProject.Host.Mvc.csproj")`,
  poll `/healthz` to readiness — the `SelfHostedIdentityProviderTests` wait idiom); `[OneTimeTearDown]`
  disposes it.
- `src/Test/WasmTestProject/README.md` *(modify)* — layout tree + port map gain the Mvc host (5320);
  demo-table row (`/` visit counter · `/home/boom` + `/error` | Cloudstrap.Mvc (#6) | `MvcHostTests`);
  "Harness notes for deliverable #6": the host is the README consumer example, anonymous by design, the
  mechanic (j) pins and why, and the trustworthy-loopback note (the `Secure` session cookie works in
  Chromium over `http://127.0.0.1` — the #10 precedent).
- `src/Cloudstrap.sln` *(modify)* — the Mvc host under the `WasmTestProject` solution folder.

**RED** *(write these tests first, run them, confirm they fail — before GREEN the host project does not
exist, so the fixture's boot times out / the project path check throws)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/MvcHostTests.cs`
  - `MvcHost_VisitCounter_RoundTripsWithTheHardenedSessionCookie` — Playwright: navigate `/` →
    `visit-count` shows `1`; reload → `2` (the session round-trip through a real browser); then
    `Context.CookiesAsync()` → exactly one session cookie named `.Cloudstrap.Session` with
    `Secure=true`, `HttpOnly=true`, `SameSite=Lax` (AC-MVC2 live — AC-MVC13's first mandated proof).
  - `MvcHost_ThrowingAction_ShowsTheConsumersErrorPageNotAStackTrace` — Playwright: navigate
    `/home/boom` → the `error-page` marker is visible and the page contains none of the exception
    type/message/stack text (AC-MVC5a live — AC-MVC13's second mandated proof).
  - `MvcHost_ThrowingAction_PreferringJson_GetsGenericProblemDetails` — `HttpClient` GET `/home/boom`
    with `Accept: application/json` → 500 `application/problem+json`, generic, no exception detail
    (AC-MVC5b live, `IncludeDetails=false` pinned).
  - `MvcHost_HomePage_LoadsWithStaticAssetsAndNoConsoleErrors` — the view rendered via the
    conventional route with `site.css` served (AC-MVC1 live incl. the real `.cshtml`, mechanic (e)),
    zero JS console errors (the `HomePageTests` idiom — no CDN involved here).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "MvcHostTests"
  ```

**GREEN**: the Scope items. **Every pre-existing E2E test must stay green unchanged** — the new host is
additive on its own port; the Bff, IdP and all existing fixtures are untouched. *(If any existing test is
disturbed, the executor reports it at the gate rather than weakening the assertion.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the four new `MvcHostTests` pass **and all pre-existing E2E tests pass unchanged** (build
   first; one-time `playwright.ps1 install chromium` if needed).
2. Manual smoke (optional but recorded):
   `dotnet run --project src/Test/WasmTestProject/src/Host/Mvc` then browse `http://127.0.0.1:5320/`
   (counter increments across refreshes) and `/home/boom` (the error page, no stack trace).
3. Full gates on the final tree: `dotnet build src/Cloudstrap.sln` (zero warnings) + `runTests` (every
   unit suite + E2E — all green) + `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #6 complete *(covers Steps 7–8; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires
the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

- [x] Behavioral verification: the four `MvcHostTests` pass; **all pre-existing E2E tests pass
  unchanged** (full E2E 44/44); the four `PackageSurfaceTests` guards are green; the expanded Release
  `.nupkg` contents were reviewed; the identifier sweep is empty (self-referential hits only); the full
  suite (`dotnet build` + `runTests` + `dotnet format --verify-no-changes`) is green end to end.
  *(One unrelated pre-existing #9 test — `DefaultIsolatedMode_ARegisteredIDistributedCacheNeverReceivesAnything`
  — turned reproducibly timing-flaky during this session; diagnosed and reported at the gate, tracked
  as a separate #9 bugfix.)*
- [x] Spec acceptance sign-off: walk **AC-MVC1…AC-MVC13 + AC-ASP2 + AC-A3** against the step evidence
  using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop / Out-of-Scope
  lists was resurrected (no session middleware or cookie-protection code, no correlation middleware of
  our own, no static-web-assets loader, no reflection handler loop, no `UseForwardedHeaders` default, no
  `NWebsec.*`/`NetEscapades.*` in the closure, no localization, no forced `RequireAuthorization`, no
  shipped default error page, no session-store opinion, no auth registration, no `Aspire.*`) and that
  every De-NIHDI row is closed (`nihdi.session` → `.Cloudstrap.Session`, neutral fixtures, standard
  environments, no company headers).
- [x] ⚠️ Session/cookie re-review (risk area, D-1 standing rule): the E2E-observed cookie in a real
  Chromium matches the Gate-2 sign-off (name, `Secure`, `HttpOnly`, `SameSite=Lax`); the SUT host's
  mechanic (j) configuration pins are documented and justified in the SUT README.
- [x] Docs review: `src/Cloudstrap.Mvc/README.md` matches as-built behavior (canonical order, four
  settings tables, session posture + multi-instance recipe, negotiation rule, NetEscapades and
  `MapStaticAssets` recipes, the not-with-WebApi warning, the Aspire note); the SUT README's demo-table
  row, port map (5320) and #6 harness notes are accurate; the Step 8 host still mirrors the README
  quick start (D-3's "doubles as the consumer example").
- [x] User approved — deliverable #6 done *(2026-08-19)*; project-manager flips the ROADMAP row to ✅.
