# Spec: MVC Bootstrap — `Cloudstrap.Mvc` (Roadmap Deliverable #6)

> **Approved 2026-08-16 — zero Open Questions remain; spec is planner-ready.** All five gate questions were resolved by the user per this spec's recommendations (see the Decision Log at the end): stock session over the fork with hardened defaults (D-1, ⚠️ auth-adjacent — user is the sign-off authority), the content-negotiating error handler with `/error` re-execution (D-2), the test-only MVC SUT host (D-3), inline security headers with zero new dependencies (D-4), and the stock `IDistributedCache` session-store posture (D-5).
>
> Sources: `_plans/ROADMAP.md` §6 (hand-off brief, file inventory verified 2026-08-16) · `_specs/Cloudstrap.md` (Decisions Made, Package Map Mvc row, De-NIHDI-fication Checklist, Aspire Coexistence) · `_specs/5-WebApiBootstrap.md` (D-3 composite-pipeline pattern this package inherits; activate-what-you-register posture; no-origins→no-CORS; the security-header re-evaluation routed here) · **shipped** code in `src/Cloudstrap.Core/` (`ApplicationOptions.PathBase`/`ExceptionHandlerPath`), `src/Cloudstrap.Observability/Correlation/` (`UseCloudstrapCorrelation`, `AddCloudstrapCorrelation`, `GetCloudstrapCorrelationId`), `src/Cloudstrap.Extensions/` (`AddCloudstrapDataProtection`, `MapCloudstrapHealthChecks`), `src/Cloudstrap.WebApi/` (`WebApplicationExtensions` pipeline, `SecurityHeadersMiddleware`, `CloudstrapWebApiExceptionHandler`, `HstsSettings`/`CorsSettings` shapes) · source reference repo (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` — **every file in the Port Decision Table was opened and read**: `Nihdi.Core.Configuration.Mvc\Extensions\WebApplicationBuilderExtensions.cs`, `Extensions\ApplicationBuilderExtensions.cs`, `SessionManagement\NihdiSessionMiddleware.cs`, `SessionManagement\NihdiCookieProtection.cs`, `ExceptionHandlers\WebExceptionHandler.cs`, `Correlation\CorrelationSourceMiddleware.cs`, `.csproj`, `README.md`, plus the consumed Common surfaces `Common\Services\ServiceCollectionExtensions.cs` and `Common\Extensions\ApplicationBuilderExtensions.cs` · **stock baseline read for the mandated diff** (dotnet/aspnetcore `main`): `src/Middleware/Session/src/SessionMiddleware.cs`, `SessionOptions.cs`, `CookieProtection.cs` · external evidence gathered 2026-08-16: [NWebsec.AspNetCore.Middleware on NuGet](https://www.nuget.org/packages/NWebsec.AspNetCore.Middleware/) (3.0.0, last release 2020-01, BSD-3-Clause — dormant 6+ years) · [NetEscapades.AspNetCore.SecurityHeaders on NuGet](https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders) (1.3.1, MIT, andrewlock, active).
>
> **⚠️ Risk areas this deliverable touches** — **session/cookie security defaults** (auth-adjacent; the options shape is a one-way door; the defaults were reviewed and signed off by the user at the spec gate, D-1 — every plan gate touching them stays a human-review point) · **public API surface** (`AddCloudstrapMvc`/`UseCloudstrapMvc` + the `Cloudstrap:Mvc` options shape) · **copied-stock-code provenance**: the source's session middleware is a near-verbatim copy of ASP.NET Core's `SessionMiddleware` (MIT — legal-clean, and per D-1 Cloudstrap owns zero of it) · **zero new external dependencies** (confirmed at the gate, D-4). Aspire overlap: **none** — session, MVC pipeline and security headers are outside ServiceDefaults' remit; health checks arrive additively via #4 (AC-ASP3 posture already shipped); AC-ASP2 carried as tripwire.

## Code-reading findings that shaped this spec

1. **The "session hardening" fork is a verbatim copy of stock `SessionMiddleware` with a three-line delta.** Line-by-line diff of `NihdiSessionMiddleware` against `dotnet/aspnetcore` `src/Middleware/Session/src/SessionMiddleware.cs`: the constructor, `Invoke`, session-key generation (36-char GUID via `RandomNumberGenerator`), `SessionEstablisher`, commit-in-finally and even the logger category and DataProtection purpose string (`nameof(SessionMiddleware)` — fork cookies and stock cookies are mutually readable) are **identical**. `NihdiCookieProtection` is a character-for-character copy of stock internal `CookieProtection` (`Protect`/`Unprotect`/`Pad`). The entire delta lives in `SessionEstablisher.SetCookie()`: cookie name `"nihdi.session"`, `Cookie.Path` from `Request.PathBase`, `HttpOnly = true`, `SecurePolicy = CookieSecurePolicy.Always`.
2. **Two of the four "hardening" claims are already stock behavior.** Stock `SetCookie()` stamps the very same `Cache-Control: no-cache,no-store` + `Pragma: no-cache` + `Expires: -1` headers on every session-establishing response, and stock `SessionOptions.Cookie` already defaults `HttpOnly = true` (plus `SameSite = Lax`, `IsEssential = false`). The **real** hardening delta is exactly: cookie name, `SecurePolicy.Always` (stock default `None`), and the PathBase-scoped cookie path — all three expressible as `SessionOptions` values at registration time, since Cloudstrap's `PathBase` is a startup-time setting (`Cloudstrap:Application:PathBase`), not a per-request discovery.
3. **The fork contains a latent defect the stock middleware does not have**: `SetCookie()` runs per request on the response-starting callback and **mutates the shared singleton `SessionOptions.Cookie`** (name, path, HttpOnly, SecurePolicy) — configuration state written on the hot path, racing across concurrent requests. Configuring stock options once at startup removes the bug by construction. → **decided D-1: no fork**.
4. **`WebExceptionHandler` is the same broken-by-construction shape #5 found, and doubly wrong for a server-rendered app.** `TryHandleAsync` always returns `true` and always writes bespoke `{StatusCode, Message}` JSON — so a browser navigating an MVC site gets a raw JSON body for any unhandled exception, and any error-page re-execution path after it is dead code. A server-rendered surface needs content negotiation: HTML for browsers, RFC 9457 for JSON clients. → **decided D-2**. Core's shipped `ApplicationOptions.ExceptionHandlerPath` (default `/error`) — deliberately left unconsumed by #5 ("re-execution is the MVC/#6 pattern") — finds its consumer here.
5. **`CorrelationSourceMiddleware` is a degenerate subset of #2's shipped middleware** (the mandated diff): it *always generates a fresh id* via `ICorrelationSource` and **never reads the inbound header**, breaking cross-service correlation by design; no response echo, no enforcement, no health-endpoint exemptions. Shipped `CloudstrapCorrelationMiddleware` (#2, amended by #5) reads-or-generates, echoes per `EchoInResponse`, and stashes the id on `HttpContext`. Nothing to salvage → Drop, pipeline uses `UseCloudstrapCorrelation()`.
6. **The source package has no composite Use-side of its own** — MVC apps ran Common's `UseNihdiWebMiddleware` (already adjudicated by #5) and called `UseNihdiSession` separately. Its MVC-relevant remainder not consumed by #5 — `UseStaticFiles`, `UseAntiforgery`, endpoint mapping for controllers — is adjudicated here inside `UseCloudstrapMvc` (the D-3 composite shape #6 inherits). Notably the source composite mapped **attribute-routed controllers only** (`MapControllers`, no conventional route) and **forced `RequireAuthorization()` on all controllers** when `Security.EnableAuthentication` was true — both re-decided below (Deliberate Behavior Changes 5 and 8).
7. **`StaticWebAssetsLoader.UseStaticWebAssets` is an artifact of the dropped environment taxonomy.** The framework auto-enables static web assets when the environment is `Development`; the explicit loader call existed because the source's environments were named `LOC`/`DEV`/`TST` — never `Development` — so the auto-enable never fired. Cloudstrap uses standard environments (founding De-NIHDI row) → the call is dead weight → Drop.
8. **NWebsec is confirmed unnecessary (refinement 9 closed).** The MVC-path security headers were NWebsec's `XContentTypeOptionsMiddleware` + `UseReferrerPolicy(NoReferrer)` — two constant headers, identical in effect to #5's shipped ~15-line internal `SecurityHeadersMiddleware`. NWebsec's last release is 3.0.0 (January 2020, BSD-3-Clause) — dormant 6+ years, disqualified on maintenance regardless of need. Whether an HTML surface should *default* to a richer set (CSP, `X-Frame-Options`) the source never emitted was put to the user → **decided D-4: no** — the two inline constants + HSTS, NetEscapades as a documented consumer recipe only.
9. **The reflection-based consumer exception-handler loop is replaced by ordering, as in #5.** The source accepted `IEnumerable<Type>` and invoked `AddExceptionHandler<T>` via `MethodInfo.MakeGenericMethod` — not AOT-compatible, no compile-time safety. The framework already honors registration order: handlers a consumer registers before `AddCloudstrapMvc` get the first attempt; Cloudstrap's handler is the terminal fallback. Same contract, zero reflection, zero parameters.

---

## User Story

**As an** ASP.NET Core developer deploying a server-rendered MVC application to Azure,
**I want to** bootstrap controllers + views, hardened session state (Secure, HttpOnly, DataProtection-backed cookie scoped to my path base), browser-appropriate error handling, correlation, health probes, HSTS/security headers and CORS with two calls (`AddCloudstrapMvc` / `UseCloudstrapMvc`) and one `Cloudstrap:Mvc` configuration section,
**So that** my `Program.cs` stays under ten lines, my app is secure by default (no session cookie over HTTP, no stack traces to visitors, browser default-deny CORS) — while every convention remains overridable, the pieces stay independently callable, and pairing with `AddCloudstrapOpenIdConnect` (#10) just works.

---

## Acceptance Criteria

> AC-ASP2 and AC-A3 are carried **verbatim** from the founding spec. The founding spec has no dedicated Mvc AC block (Package Map row only); AC-MVC1…AC-MVC12 are new, spec-specific criteria (precedent: AC-W…/AC-CC…/AC-OIDC…). The prefix is **AC-MVC**, not the brief's "AC-M…": AC-M1–AC-M4 already name the founding spec's *Messaging* criteria, and AC numbers are never overloaded.

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried verbatim — stays green; this package references no auth packages at all)* |
| AC-MVC1 | An app with a `HomeController` + `Index` view | `AddCloudstrapMvc()` + `UseCloudstrapMvc()` and a request to `/` | The view renders via the default conventional route `{controller=Home}/{action=Index}/{id?}`; static files under `wwwroot` are served; `/healthz` and `/ready` answer per #4's shipped `MapCloudstrapHealthChecks` (tag contract `"live"`/`"ready"`); this package re-implements **no** health code. |
| AC-MVC2 | Default configuration; an action writes to `ISession` | The response establishing the session is inspected | It carries exactly one session cookie: name `.Cloudstrap.Session` (default), `Secure`, `HttpOnly`, `SameSite=Lax`, `Path` = `Cloudstrap:Application:PathBase` when set else `/`, value opaque (DataProtection-protected); the response carries `Cache-Control: no-cache,no-store`, `Pragma: no-cache`, `Expires: -1`; a follow-up request with the cookie reads the stored session value back. |
| AC-MVC3 | `Cloudstrap:Mvc:Session` overrides (`CookieName`, `IdleTimeoutMinutes`, `CookieSecurePolicy`, `IsEssential`) and/or the `configurator.Session` hook | The app starts and establishes a session | Every override is honored; the hook runs **after** the Cloudstrap defaults and wins; with `Session:Enabled = false`, no session services/middleware are wired and no session cookie can be issued. |
| AC-MVC4 | The shipped `Cloudstrap.Mvc` assembly | Inspected for session types (review + reflection test) | It contains **no** session middleware, no `ISessionStore`/`ISession` implementation and no cookie-protection code — session flows exclusively through stock `Microsoft.AspNetCore.Session` (`AddSession`/`UseSession`). *(D-1)* |
| AC-MVC5 | An action throws; environment `Production` | (a) a request with `Accept: text/html`; (b) a request preferring JSON | (a) the pipeline re-executes the error path (`Cloudstrap:Application:ExceptionHandlerPath`, default `/error`) and returns the consumer's error page with status 500 — never a stack trace, never raw JSON; (b) an RFC 9457 `application/problem+json` payload, generic by default, with detail (type/message/stack/bounded inner chain) only when `IncludeDetails` resolves true; the exception is logged server-side exactly once either way. *(D-2)* |
| AC-MVC6 | Same app, environment `Development` | An action throws during a browser request | The developer exception page renders (framework behavior, not overridden by Cloudstrap's handler); `UseDeveloperExceptionPage`-vs-handler selection is overridable. *(D-2)* |
| AC-MVC7 | A request carrying the configured correlation header; and one without it | Responses are inspected | Correlation behaves exactly per #2's shipped contract — inbound id honored (flows to logs/`GetCloudstrapCorrelationId`), generated when absent, echoed per `EchoInResponse`; `Cloudstrap.Mvc` contains **no** correlation middleware type of its own. |
| AC-MVC8 | Defaults | Any response is inspected | `X-Content-Type-Options: nosniff` and `Referrer-Policy: no-referrer` are present (never overwriting values the app set itself); HSTS is emitted outside `Development` per `Cloudstrap:Mvc:Hsts` (365 d, subdomains, no preload); the dependency closure contains no `NWebsec.*`. *(D-4)* |
| AC-MVC9 | No `Cloudstrap:Mvc:Cors:AllowedOrigins` configured; then one origin configured | A cross-origin preflight request | With no origins: no CORS policy is registered and no `Access-Control-Allow-Origin` is ever emitted (browser default-deny). With origins: preflight succeeds for exactly those origins (credentialed; wildcard-subdomain support kept). *(#5 posture, carried)* |
| AC-MVC10 | `AddCloudstrapOpenIdConnect` (#10) registered alongside; then no auth registered at all | The pipeline runs | With schemes registered (scheme-map predicate, the #5 deviation carried): `UseAuthentication`/`UseAuthorization` run after routing and the OIDC login round-trips; with none: no auth middleware, all endpoints anonymous, no failures. `UseCloudstrapMvc` called twice throws `InvalidOperationException`; the four pipeline hooks and the `MapDefaultControllerRoute`/`UseStaticFiles` switches compose as documented. |
| AC-MVC11 | A consumer registers any distributed `IDistributedCache` (e.g. Redis) | Session state is written and read | The stock session store uses the consumer's cache registration (Cloudstrap's `AddDistributedMemoryCache` default is TryAdd-semantics fallback only); the README documents the multi-instance recipe: distributed cache + #4's `AddCloudstrapDataProtection`. *(D-5)* |
| AC-MVC12 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv` | All green; XML docs on all public API; package metadata complete; zero forbidden identifiers; closure contains no `NWebsec.*`, no `Nihdi.*`, no `Aspire.*`; every dependency OSI-licensed and CPM-pinned. |
| AC-MVC13 | The WASM SUT solution extended with the MVC demonstration host *(the D-3 vehicle: `src/Test/WasmTestProject/src/Host/Mvc/`)* | The E2E suite runs | All pre-existing E2E tests stay green and ≥ 1 new E2E test proves, through a running browser: the hardened session cookie behavior (AC-MVC2) and the browser error page (AC-MVC5a) — standing SUT rule / workflow rule 9. |

---

## Port Decision Table

One row per source public type/feature (all read in full; bundled sub-features of the entry point are rowed individually). "Superseded" = adjudicated and shipped by an earlier deliverable — this deliverable consumes the shipped seam and must not rebuild it.

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `Extensions\WebApplicationBuilderExtensions.AddAspNetMvcForNihdi` | **Redesign** | `AddCloudstrapMvc(this WebApplicationBuilder, Action<CloudstrapMvcConfigurator>?)` | The composite entry point earns its place (founding goal: < 10 lines). The shape does not: `NihdiConfiguration` + `ILogger` parameters (Cloudstrap binds from configuration), bundles DataProtection/localization, reflection-based handler registration (finding 9). D-3 pattern applied. |
| ├─ `StaticWebAssetsLoader.UseStaticWebAssets(...)` | **Drop** | — | Artifact of the dropped LOC/DEV/TST taxonomy (finding 7): the framework auto-enables static web assets in `Development`; published output carries the assets physically. Zero value on standard environments. |
| ├─ `AddNihdiCommonServices(...)` | **Superseded** | `AddCloudstrapCore()` + `AddCloudstrapCorrelation()` (#1/#2 shipped) | Adjudicated by #2/#4; the entry point calls the shipped registrations (idempotent), same as #5. |
| ├─ `ProtectKeysWithAzureKeyVaultDefaultCredentials(...)` | **Superseded** | #4's explicit `AddCloudstrapDataProtection` | Shipped by #4; also embodies two dropped postures: the `IsRunningInAks()` cloud-vs-on-prem sniff (founding hosting decision, deliverable-1 precedent) and env-var-driven silent skips. Not re-bundled: an explicit call, documented in the multi-instance session recipe (AC-MVC11). |
| ├─ `AddNihdiWebApiProtections(...)` (HSTS + `IHttpContextAccessor` + CORS) | **Redesign** | HSTS/CORS registration inside `AddCloudstrapMvc`, **activated** by `UseCloudstrapMvc` | Same #5 redesign, mirrored here with Mvc-owned settings: activate-what-you-register; the insecure `AllowAnyOrigin`-when-unconfigured fallback removed (no origins → no CORS); HSTS `Preload` off by default (#5 change 7 carried). |
| ├─ `AddProbeHealthChecks(...)` + `AddWebOptions.ConfigureProbeHealthChecks` | **Superseded** | stock `AddHealthChecks()` + #4's `MapCloudstrapHealthChecks` | #4/#5 adjudication; additive stock builder (Aspire posture). No probe delegates. |
| ├─ `AddControllersWithViews()` | **Port** | `AddControllersWithViews()` inside `AddCloudstrapMvc` + `configurator.Mvc` hook (`Action<IMvcBuilder>`) | The one registration that makes this the *MVC* package; stock call, hook for `AddApplicationPart`/Razor runtime compilation etc. |
| ├─ `AddNihdiLocalization(configuration)` | **Drop** *(routed → #24)* | — | Roadmap refinement 6: the localization edge is cut; `Cloudstrap.Localization` is a late standalone deliverable. Consumers needing it today use stock `AddLocalization`/`UseRequestLocalization` directly. |
| ├─ `AddSession()` (bare stock call, no options) | **Redesign** | `AddSession(...)` + `AddDistributedMemoryCache()` with the hardened `SessionOptions` defaults from `Cloudstrap:Mvc:Session` | The source registered session with **stock-default** options and left the hardening to the fork's per-request mutation (finding 3). The redesign moves the entire hardening delta (finding 2) into startup-time options — and adds the `AddDistributedMemoryCache` TryAdd fallback the source omitted (bare `AddSession()` throws at first use without an `IDistributedCache`). *(D-1)* |
| ├─ reflection `AddExceptionHandler<T>` loop (`exceptionHandlers` parameter) | **Drop** | — (registration-order contract) | Finding 9: `MakeGenericMethod` invocation, not AOT-safe, no compile-time typing. The framework's chain order gives consumers the identical capability: register handlers before `AddCloudstrapMvc`, they run first. #5 precedent. |
| ├─ `AddExceptionHandler<WebExceptionHandler>()` registered last | **Redesign** | internal terminal handler registered last *(shape per D-2)* | Ordering kept; the handler replaced (next row). |
| └─ `BootstrapConfiguration.ReadAppSettings()` fallback + `AddWebOptions.ConfigureServices` delegate | **Drop** | — | `builder.Configuration` is the single configuration source (#4 posture); `AddWebOptions` was already Dropped by #5 — service-side extensibility is calling the services yourself before `Build()`. |
| `ExceptionHandlers\WebExceptionHandler` | **Redesign** | internal content-negotiating `IExceptionHandler` + stock `UseExceptionHandler` re-execution for HTML | Finding 4: always-`true`, JSON-only — browsers get raw JSON and any error page after it is dead. Redesign: handle-and-return-`true` only for JSON-preferring requests (RFC 9457 via `IProblemDetailsService`, correlation-stamped, `IncludeDetails` switch — the #5 contract); return `false` for HTML so the framework re-executes `ExceptionHandlerPath`. **Not** a reference to `Cloudstrap.WebApi` — that would drag Asp.Versioning/OpenAPI/Scalar into every MVC closure; the ~100-line handler is re-expressed here. *(D-2)* |
| `Extensions\ApplicationBuilderExtensions.UseNihdiSession()` (both overloads) | **Replace** | stock `UseSession()` inside `UseCloudstrapMvc` | With the fork gone (next rows) there is nothing left to wrap: stock `UseSession` consumes the same `SessionOptions` the Add-side hardened. Consumers building their own pipeline call stock `UseSession()` — no Cloudstrap wrapper adds value over the framework call. |
| `Extensions\ApplicationBuilderExtensions.UseNihdiCorrelation()` | **Drop** | — (use #2's `UseCloudstrapCorrelation`) | Activates the degenerate middleware of finding 5; a second correlation middleware would double-process every request. |
| `SessionManagement\NihdiSessionMiddleware` | **Replace** | stock `Microsoft.AspNetCore.Session.SessionMiddleware` via `UseSession` + hardened `SessionOptions` | **The deliverable's central call — decided D-1 (2026-08-16)**. Findings 1–3: verbatim copy of stock; the real delta (cookie name, `SecurePolicy.Always`, PathBase-scoped path) is fully expressible as startup-time options; the fork's only original contribution is a shared-state mutation bug. Owning a copy of framework middleware means merging every upstream security fix by hand, forever — the definition of cost without value. |
| `SessionManagement\NihdiCookieProtection` | **Drop** | — | Character-for-character copy of stock internal `CookieProtection` (finding 1), which keeps running inside stock `SessionMiddleware` — same DataProtection purpose string, so even cookie compatibility is preserved. Nothing to carry. |
| `Correlation\CorrelationSourceMiddleware` | **Drop** | — (use #2's `UseCloudstrapCorrelation`) | Finding 5 (the mandated diff): generates-only, ignores inbound headers — strictly worse than the shipped middleware on every axis. Expected-verdict confirmed. |
| *(shared source, MVC-shaped remainder of Common's `UseNihdiWebMiddleware` — the composite #5 adjudicated for APIs)* `UseStaticFiles`, `UseAntiforgery`, controller endpoint mapping, path base, forwarded headers | **Redesign** | `UseCloudstrapMvc(this WebApplication, Action<MvcPipelineOptions>?)` — the D-3 composite | `UseStaticFiles` kept (switch, default on — every MVC app has `wwwroot`); `UseAntiforgery` kept after auth (stock, needed by Razor/minimal-API antiforgery metadata; MVC filters unaffected); endpoint mapping becomes `MapDefaultControllerRoute` (switch — a views package defaults to the conventional route; source mapped attribute-only, Deliberate Change 5); path base per #5 (`ApplicationOptions.PathBase` only, no env-var/workload magic); forced `RequireAuthorization()`-when-auth-enabled **not** carried (Deliberate Change 8); `UseForwardedHeaders` **not** carried (Azure Web Apps/container ingress is handled by `ASPNETCORE_FORWARDEDHEADERS_ENABLED`; a silent library default that trusts any proxy is a spoofing surface — consumers who need it use `hooks.BeforeRouting`). |
| *(shared source)* NWebsec `XContentTypeOptionsMiddleware` + `UseReferrerPolicy(NoReferrer)` | **Replace** | internal `SecurityHeadersMiddleware` (the #5 ~15-line pattern, re-expressed here) | Finding 8: NWebsec dormant since 2020-01 — disqualified on maintenance; the actually-emitted MVC-path headers are two constants. A *richer* default set than the source ever emitted was declined at the gate (D-4 — no-gold-plating). |
| `.csproj` (`Nullable` **disabled**, StyleCop artifacts, 0.5-prerelease version) + `README.md` + `stylecop.json` + `.ruleset` | **Drop** | new `Cloudstrap.Mvc.csproj` per #0 scaffolding | NRT enabled, SDK analyzers, GitVersion, MIT metadata, fresh README with neutral fixtures. Three of six source files carry `Riziv-Inami` headers — not carried (De-NIHDI). |

**Tally**: 1 Port · 7 Redesign · 3 Replace · 7 Drop · 3 Superseded-reuse.

---

## Public API Sketch

Namespace **`Cloudstrap.Mvc`** (single namespace — suite precedent). Everything `public sealed`/`static`; middleware, the exception handler and validators `internal`. The root options type carries the `Cloudstrap` prefix because `MvcOptions` collides with `Microsoft.AspNetCore.Mvc.MvcOptions` (precedent: `CloudstrapJwtBearerOptions`, `CloudstrapScalarOptions`).

```text
Cloudstrap.Mvc
├── WebApplicationBuilderExtensions (static)
│     AddCloudstrapMvc(this WebApplicationBuilder builder,
│                      Action<CloudstrapMvcConfigurator>? configure = null)
│         : WebApplicationBuilder
│       — binds + validates CloudstrapMvcOptions ([OptionsValidator], ValidateOnStart);
│         AddCloudstrapCore + AddCloudstrapCorrelation (idempotent, #1/#2);
│         AddHttpContextAccessor; AddHealthChecks() (stock, additive);
│         AddControllersWithViews(); AddProblemDetails();
│         AddDistributedMemoryCache() + AddSession(hardened defaults per Session settings,
│         skipped entirely when Session.Enabled = false);
│         HSTS + CORS registration per options (no origins → nothing registered);
│         AddExceptionHandler<internal handler> registered last (consumer handlers first).
│         Registers NO authentication — pair with AddCloudstrapOpenIdConnect (#10) or
│         AddCloudstrapJwtBearer (#5); the pipeline detects registered schemes.
│
├── WebApplicationExtensions (static)
│     UseCloudstrapMvc(this WebApplication app,
│                      Action<MvcPipelineOptions>? configure = null)
│         : WebApplication              (composite shape inherited from D-3; throws on 2nd call)
│       — pipeline, in order:
│         exception handling (Development → framework developer page stays in charge;
│           otherwise UseExceptionHandler: consumer handlers → Cloudstrap negotiating handler
│           (JSON) → re-execution at ApplicationOptions.ExceptionHandlerPath (HTML)) →
│         UseHsts (non-Development, when enabled) → security-header middleware →
│         UsePathBase (when ApplicationOptions.PathBase non-empty) → UseStaticFiles (switch) →
│         hooks.BeforeRouting → UseRouting → UseCors (only when origins configured) →
│         UseCloudstrapCorrelation (#2) →
│         UseAuthentication (only when a scheme is registered — scheme-map predicate, #5) →
│         hooks.BeforeAuthorization → UseAuthorization (same condition) →
│         UseSession (when enabled) → UseAntiforgery →
│         hooks.BeforeEndpoints → MapDefaultControllerRoute (switch; attribute routes included) →
│         MapCloudstrapHealthChecks (#4) → hooks.ConfigureEndpoints
│
├── CloudstrapMvcConfigurator            — code-level hooks carried by AddCloudstrapMvc
│     Mvc     : Action<IMvcBuilder>?     — e.g. AddApplicationPart, AddRazorRuntimeCompilation
│     Session : Action<SessionOptions>?  — runs AFTER the Cloudstrap defaults (final override)
│
├── MvcPipelineOptions                   — code-level hooks carried by UseCloudstrapMvc
│     BeforeRouting       : Action<IApplicationBuilder>?
│     BeforeAuthorization : Action<IApplicationBuilder>?
│     BeforeEndpoints     : Action<IApplicationBuilder>?
│     ConfigureEndpoints  : Action<IEndpointRouteBuilder>?
│     MapDefaultControllerRoute : bool = true    — {controller=Home}/{action=Index}/{id?}
│     UseStaticFiles            : bool = true    — off for MapStaticAssets adopters (documented)
│
└── CloudstrapMvcOptions                 — section Cloudstrap:Mvc (owned HERE)
      const SectionName = "Cloudstrap:Mvc"
      Session : SessionSettings                     ⚠️ defaults signed off at the gate (D-1)
          Enabled            : bool = true
          CookieName         : string = ".Cloudstrap.Session"   (source: "nihdi.session")
          CookieSecurePolicy : CookieSecurePolicy = Always      (stock default: None)
          IdleTimeoutMinutes : int = 20                         (stock parity, config-reachable)
          IsEssential        : bool = false                     (stock parity; consent-gated —
                                                                 CookieConsent #21 interplay documented)
          — HttpOnly true and SameSite Lax are stock defaults, asserted by tests, not modeled;
            Cookie.Path is set from Cloudstrap:Application:PathBase at startup (else "/");
            anything else → configurator.Session hook (full SessionOptions access, runs last)
      Hsts : HstsSettings                — #5 shape mirrored (package-local type)
          Enabled : bool = true · MaxAgeDays : int = 365 · IncludeSubDomains : bool = true ·
          Preload : bool = false
      Cors : CorsSettings                — #5 shape mirrored (package-local type)
          AllowedOrigins : IList<string> (get-only init — binder append caveat documented)
          — empty (default) → no CORS policy registered at all
      ExceptionHandling : ExceptionHandlingSettings              (D-2)
          IncludeDetails : bool? = null  — JSON path only; null → details only in Development
          UseDeveloperExceptionPage : bool? = null — null → Development

internal: MvcExceptionHandler (content-negotiating terminal handler), SecurityHeadersMiddleware
(nosniff + no-referrer, never overwrites app-set values), source-generated [OptionsValidator]
validators (inherited fact — no Microsoft.Extensions.Options.DataAnnotations).
```

**Configuration** — this package owns one new section: `Cloudstrap:Mvc` (subsections `Session`, `Hsts`, `Cors`, `ExceptionHandling`). It **consumes** Core's shipped `Cloudstrap:Application` (`PathBase`, `ExceptionHandlerPath`), `Cloudstrap:HealthChecks` and `Cloudstrap:Correlation` — never redefining them. `Cors.AllowedOrigins` follows the get-only append-not-replace caveat (#1 inherited fact); it ships an empty default so the caveat stays theoretical.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Controllers + views | `AddControllersWithViews` + conventional route `{controller=Home}/{action=Index}/{id?}` (attribute routes work too). | `MapDefaultControllerRoute = false` + `hooks.ConfigureEndpoints` (own routes); `configurator.Mvc` for the builder. |
| Session | On; stock `Microsoft.AspNetCore.Session` end-to-end; cookie `.Cloudstrap.Session`, `Secure` always, `HttpOnly`, `SameSite=Lax`, path = `PathBase` or `/`, `IsEssential=false`; DataProtection-protected value; idle timeout 20 min; in-memory `IDistributedCache` fallback via TryAdd. | `Cloudstrap:Mvc:Session:*`; `configurator.Session` hook (runs last, full `SessionOptions`); `Enabled=false` removes it entirely; any consumer-registered `IDistributedCache` wins (D-5); consumers composing their own pipeline call stock `UseSession()`. |
| Multi-instance session | Not silently handled — documented recipe: register a distributed `IDistributedCache` + call #4's `AddCloudstrapDataProtection` (shared keys make the session cookie readable by every instance). | The recipe *is* the override; single-instance apps need nothing. |
| Error handling | Outside `Development`: consumer handlers first, then JSON-preferring requests get RFC 9457 problem details (generic; correlation-stamped; `IncludeDetails` opt-in), HTML requests re-execute `ExceptionHandlerPath` (default `/error` — the consumer supplies the error action/view; the SUT host demonstrates one). `Development`: framework developer exception page. Exception logged server-side once. | `Cloudstrap:Mvc:ExceptionHandling:*`; `Cloudstrap:Application:ExceptionHandlerPath`; register own `IExceptionHandler`s before `AddCloudstrapMvc`. |
| Correlation | #2's shipped middleware after routing: inbound header honored, generated when absent, echoed per `EchoInResponse`. | `Cloudstrap:Correlation:*` (#2's contract). |
| Security headers | `X-Content-Type-Options: nosniff` + `Referrer-Policy: no-referrer` on every response; app-set values never overwritten. No default CSP/`X-Frame-Options` (D-4): the source never emitted them and wrong defaults break real apps; README shows the NetEscapades recipe via `hooks.BeforeRouting` for consumers who want the full HTML bundle. | `hooks.BeforeRouting` to add/replace header middleware. |
| HSTS | Outside `Development`: 365 days, subdomains, **no preload**. | `Cloudstrap:Mvc:Hsts:*`. |
| CORS | No origins → no policy, browser default-deny; origins → default policy, credentialed, wildcard-subdomain support. | `Cloudstrap:Mvc:Cors:AllowedOrigins`; stock `AddCors` for named policies (additive). |
| Static files | `UseStaticFiles()` before routing. | `UseStaticFiles = false` (+ `MapStaticAssets` via `hooks.ConfigureEndpoints` for .NET 10 optimized assets — documented). |
| Path base | Applied from Core's `ApplicationOptions.PathBase` only when non-empty; no env-var, no workload-name magic. | `Cloudstrap:Application:PathBase`. |
| Authentication | None registered here. When any scheme is registered (e.g. #10's `AddCloudstrapOpenIdConnect`), `UseAuthentication`/`UseAuthorization` run in the right slots (scheme-map predicate). No forced `RequireAuthorization` on controllers — endpoint protection belongs to the auth package's fallback policy or the consumer's attributes. | Register schemes/policies as usual; `hooks.BeforeAuthorization` for middleware between authN and authZ. |
| Health probes | `MapCloudstrapHealthChecks` (#4): `/healthz` + `/ready`, idempotent, additive stock builder (Aspire posture). | Core's `Cloudstrap:HealthChecks:*`; extra checks via stock `AddHealthChecks()`. |
| Aspire coexistence | No overlap: session/MVC/security headers are outside ServiceDefaults' remit; health/correlation route through the already-composable #2/#4 seams. Zero `Aspire.*` (AC-ASP2). | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in `src/Test/UnitTest/Cloudstrap.Mvc.Tests`; in-process pipeline tests (`Microsoft.AspNetCore.TestHost`, already CPM-pinned test-only) assert the session-cookie attributes and round-trip (AC-MVC2/3), the no-session-types reflection sweep (AC-MVC4), both error-handling branches (AC-MVC5/6), header/CORS/HSTS behavior (AC-MVC8/9) and pipeline composition incl. the scheme-map predicate (AC-MVC10). The demonstration slice adds the MVC SUT host (D-3) + ≥ 1 Playwright E2E test (AC-MVC13).

---

## Dependencies

| Package | License | Evidence & justification |
|---|---|---|
| `Cloudstrap.Core` *(project reference)* | MIT | `ApplicationOptions` (`PathBase`, `ExceptionHandlerPath`), `HealthChecksOptions`, `CorrelationOptions`. |
| `Cloudstrap.Observability` *(project reference)* | MIT | The one correlation middleware (`UseCloudstrapCorrelation`, `AddCloudstrapCorrelation`, `GetCloudstrapCorrelationId` for the problem-details stamp). |
| `Cloudstrap.Extensions` *(project reference)* | MIT | `MapCloudstrapHealthChecks`; `AddCloudstrapDataProtection` reachable for the multi-instance recipe with one package. |
| `Microsoft.AspNetCore.App` *(framework reference)* | MIT | MVC controllers + views, session (`Microsoft.AspNetCore.Session` ships in the shared framework), antiforgery, static files, exception handling. **Fourth** framework reference in the suite (#2, #4, #5 precedent). |

**Zero new external NuGet packages** — the roadmap's expectation, met. No new CPM pins.

Considered and **rejected**: `NWebsec.AspNetCore.Middleware` ([3.0.0, last release 2020-01](https://www.nuget.org/packages/NWebsec.AspNetCore.Middleware/), BSD-3-Clause — dormant 6+ years; refinement 9 closed: nothing in the MVC surface needs it) · `NetEscapades.AspNetCore.SecurityHeaders` ([1.3.1, MIT, andrewlock, active](https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders)) — healthy, but a default CSP the source never had is gold-plating and two constant headers do not justify a dependency (#5's reasoning holds for the defaults this spec ships; rejected at the gate, D-4 — the README points consumers at it as the documented recipe) · a `Cloudstrap.WebApi` project reference for handler reuse — rejected: drags `Asp.Versioning.*`/`Microsoft.AspNetCore.OpenApi`/`Scalar.AspNetCore` into every MVC closure for ~100 lines of internal code · any `Nihdi.*` internal package (AC-A3) · any `Aspire.*` package (AC-ASP2).

---

## Deliberate Behavior Changes (vs. the source library)

1. **The session middleware fork is gone** — session runs on stock `Microsoft.AspNetCore.Session` with the hardening delta (cookie name, `SecurePolicy.Always`, PathBase-scoped path) applied as startup-time `SessionOptions`. The fork's per-request mutation of shared options (finding 3) disappears with it. Cookie name default changes `nihdi.session` → `.Cloudstrap.Session` (De-NIHDI; configurable). Cookie compatibility with stock is inherent (same DataProtection purpose string).
2. **Inbound correlation ids are honored.** The source's MVC path *always generated a fresh id and ignored the inbound header* (finding 5); the shipped #2 contract (read-or-generate + echo) applies instead — cross-service traces now join up.
3. **Browsers get an error page, not raw JSON.** Content negotiation replaces the always-JSON terminal handler: HTML requests re-execute the consumer's error path, JSON requests get RFC 9457 problem details (generic by default, detail on explicit opt-in). In `Development` the developer exception page stays in charge (the source's DEV/TST switch, rebuilt on standard environments and made overridable).
4. **CORS/HSTS**: registered *and* activated together; no origins → no CORS at all (source: `AllowAnyOrigin` + log warning); HSTS `Preload` off by default (#5 changes 3/7, carried to the MVC surface).
5. **The conventional default route is mapped by default** (`{controller=Home}/{action=Index}/{id?}`) — the source composite mapped attribute-routed controllers only, forcing views apps to hook in their own routes. Switch-off available.
6. **No static-web-assets loader, no `basepath` env var, no `/{WorkloadName}` path-base auto-default, no `UseForwardedHeaders` by default** (findings 6/7; forwarded headers via the platform env var `ASPNETCORE_FORWARDEDHEADERS_ENABLED` or `hooks.BeforeRouting` — a library must not silently trust any proxy).
7. **Localization is not bundled** — #24's standalone package; stock calls in the meantime.
8. **No forced `RequireAuthorization()` on controllers when auth is registered.** The source composite auto-secured every controller endpoint when `Security.EnableAuthentication` was true — invisible, config-driven auth posture. In Cloudstrap, endpoint protection is the auth package's explicit business (#5's fallback-policy pattern) or the consumer's attributes; the MVC pipeline only places the middleware.
9. **Session no longer risks a missing-cache startup surprise**: `AddDistributedMemoryCache()` is registered as a TryAdd fallback (the source's bare `AddSession()` fails at first session use unless the consumer registered a cache themselves).

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| Session written but `Session:Enabled = false` | `ISession` feature absent → accessing `HttpContext.Session` throws the stock `InvalidOperationException`; no Cloudstrap masking. |
| `CookieSecurePolicy = Always` over plain HTTP (local run without TLS) | Stock behavior: the cookie is marked `Secure`, the browser won't return it, session doesn't stick. Documented (dev over HTTPS or explicit `SameAsRequest` override) — not silently downgraded. |
| Request with a valid stock-format session cookie issued before a `CookieName` change | Different cookie name → treated as no session; a new session cookie is issued. No migration shim. |
| HTML request throws but the consumer has no endpoint at `ExceptionHandlerPath` | Stock `ExceptionHandlerMiddleware` semantics (error path 404 → the original exception surfaces as 500). README + SUT host show the minimal `/error` action; not silently swallowed. |
| Client sends `Accept: */*` (no explicit HTML preference) on the error path | Treated as JSON-preferring → problem details (API clients and curl get machine-readable output; browsers always send `text/html` in `Accept`). Exact negotiation rule fixed at plan time and covered by tests. *(D-2)* |
| Consumer registers own `IExceptionHandler` before `AddCloudstrapMvc` | It runs first; Cloudstrap's handler stays terminal for JSON, re-execution stays terminal for HTML. |
| `UseCloudstrapMvc` called twice | `InvalidOperationException` (pipeline built once — #5 marker pattern). |
| `AddCloudstrapMvc` + `AddCloudstrapWebApi` in one host | Not a supported composite pairing (two pipeline owners); the granular pieces compose instead — documented. Both `Use*` calls guard with distinct markers; calling both still throws on neither but produces a double pipeline — README warns. |
| Health endpoints also mapped manually | One set of endpoints (#4's marker-based idempotence). |
| `MapDefaultControllerRoute = false` and no `ConfigureEndpoints` | App serves only static files + health probes; no controller endpoints — consumer's explicit choice, not an error. |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace, ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- Authentication of any kind — OIDC login is #10 (shipped, composes via the scheme-map predicate), JWT validation is #5, token acquisition is #9. This package registers no schemes, no policies.
- Localization (`AddNihdiLocalization` edge) — #24.
- API versioning, OpenAPI/Scalar, JSON API opinions — #5's surface; MVC apps that also expose an API host it separately or use the granular pieces.
- Everything Dropped above: the session middleware fork + `NihdiCookieProtection`, `CorrelationSourceMiddleware` + `UseNihdiCorrelation`, the static-web-assets loader, the reflection handler loop, `BootstrapConfiguration`/`AddWebOptions` remnants, `UseForwardedHeaders`-by-default, NWebsec.
- Re-implementation of anything Superseded: correlation (#2), DataProtection/KeyVault/health mapping/typed clients (#4).
- A shipped default error page/view or `UseStatusCodePages*` wiring (the source had neither — no gold-plating; the SUT host demonstrates the consumer-side `/error` action).
- A Redis/SQL session-store opinion or any caching dependency (D-5: stock `IDistributedCache` contract + documented recipe).
- Blazor Server/WASM hosting concerns — #11–#13.

---

## Decision Log (gate answers, 2026-08-16 — zero Open Questions remain; spec is planner-ready)

All five gate questions were answered by the user on 2026-08-16; each accepted this spec's recommendation as-is. The full findings/options/rationale for each question live in this repo's git history of this file (the pre-gate draft); the decided outcomes are:

| # | Question | Answer (user, 2026-08-16) |
|---|----------|---------------------------|
| **D-1** | Session hardening: configure stock `UseSession`, or maintain the middleware fork? (⚠️ auth-adjacent; the `Cloudstrap:Mvc:Session` options shape is a one-way door — the user is the sign-off authority) | **Stock `AddSession`/`UseSession` with hardened Cloudstrap defaults — the fork is not maintained.** Basis: the line-by-line diff (findings 1–3) — `NihdiSessionMiddleware`/`NihdiCookieProtection` are verbatim copies of stock `SessionMiddleware`/`CookieProtection`; the no-cache headers and `HttpOnly` are already stock behavior; the real delta (cookie name, `SecurePolicy.Always`, PathBase-scoped path) is fully expressible as startup-time `SessionOptions`; the fork's only original behavior is the per-request shared-options mutation defect. **Signed-off defaults**: `CookieName = ".Cloudstrap.Session"`, `CookieSecurePolicy = Always`, `IsEssential = false`, `SameSite = Lax` (stock), `HttpOnly = true` (stock), path from `Cloudstrap:Application:PathBase` else `/`, `IdleTimeoutMinutes = 20` — each overridable via `Cloudstrap:Mvc:Session` and the `configurator.Session` hook (runs last). Cloudstrap owns **zero** session middleware code (AC-MVC4); session/cookie changes remain a human-review area at every plan gate that touches them. |
| **D-2** | Exception handling for a server-rendered surface: negotiated handler + re-execution, HTML-only, or a shipped default error page? | **One internal content-negotiating `IExceptionHandler`**: JSON-preferring requests get RFC 9457 problem details (correlation-stamped, generic by default — #5's contract re-expressed with **no** `Cloudstrap.WebApi` reference); HTML requests fall through (`return false`) to stock `UseExceptionHandler` re-execution of `Cloudstrap:Application:ExceptionHandlerPath` (default `/error`, consumer-supplied page — no shipped default page, no gold-plating); the framework developer exception page keeps `Development` (overridable via `UseDeveloperExceptionPage`). **Sub-question confirmed**: `IncludeDetails` applies to the **JSON path only** — the HTML error page never shows exception details; the developer page is the HTML diagnostic in `Development`. Covered by AC-MVC5/AC-MVC6. |
| **D-3** | SUT demonstration vehicle (the WASM SUT has no MVC-views host) | **A minimal test-only MVC host at `src/Test/WasmTestProject/src/Host/Mvc/` (`Cloudstrap.WasmTestProject.Host.Mvc`, `IsPackable=false`)** — the IdentityProvider-host precedent: one `HomeController` with a session-backed visit-counter page (proves the hardened cookie + round-trip, AC-MVC2), a throwing action + `/error` page (proves AC-MVC5a), booted on a loopback port by the E2E fixture and driven by Playwright (AC-MVC13). Rejected: grafting MVC onto the Bff (two composite pipelines in one host — unsupported by this very spec) and deferral to #12 (breaks the standing SUT rule). The host doubles as the README's consumer example. |
| **D-4** | Security-header defaults for an HTML surface + the NWebsec (refinement 9) / NetEscapades verdict (⚠️ dependency decision) | **Option (a): two inline header constants (`X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, never overwriting app-set values) + HSTS outside `Development`; no default CSP/`X-Frame-Options`; zero new dependencies.** NWebsec is **confirmed unnecessary and disqualified** ([3.0.0, last release 2020-01, BSD-3-Clause — dormant 6+ years](https://www.nuget.org/packages/NWebsec.AspNetCore.Middleware/)); refinement 9 is closed — AC-MVC8/AC-MVC12 assert zero `NWebsec.*` in the closure. `NetEscapades.AspNetCore.SecurityHeaders` ([1.3.1, MIT, active](https://www.nuget.org/packages/NetEscapades.AspNetCore.SecurityHeaders)) is **documented as the consumer recipe only** (via `hooks.BeforeRouting`), not a dependency — a default CSP the source never emitted is gold-plating and a day-one breakage risk for consumer apps. |
| **D-5** | Session-store posture for multi-instance deployments | **Stay stock**: the `IDistributedCache` contract decides — `AddDistributedMemoryCache()` registered as TryAdd fallback; any consumer-registered distributed cache (Redis, SQL, Cosmos) wins (AC-MVC11); the README documents the two-step multi-instance recipe (distributed `IDistributedCache` + #4's `AddCloudstrapDataProtection` for shared cookie-protection keys). **No Redis/SQL provider opinion, no caching dependency shipped**; a non-Development memory-fallback startup warning stays a possible post-v1 addition (no API break required). |
