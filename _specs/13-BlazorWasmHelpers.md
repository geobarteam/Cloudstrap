# Spec: Blazor WASM Helpers — `Cloudstrap.BlazorWasm` (Roadmap Deliverable #13)

> Status: **APPROVED — zero Open Questions (all four resolved by the user 2026-08-31, in each case
> accepting the analyst's recommendation; see Decision Log DL-1–DL-4); planner-ready.**
> Source: `Nihdi.Core.Configuration.BlazorWasm` (12 source files, standalone, zero
> ProjectReferences) + its own executable test suite (6 files — the first package in the Blazor band
> whose observed contract is tests, not just call sites). Roadmap dependency: #11 ✅ (band choice);
> counterpart surfaces #10 (Bff cookie session + auth endpoints) and #4 (server typed clients — not
> reusable here, finding 8). Demo vehicle: the **existing** `src/demo/BlazorWasm/{Bff, Client,
> Presentation}` app — #13 scaffolds nothing. Implementation may not begin before #12's final 🛑 gate
> is user-approved.
> ⚠️ Risk areas: **auth** (cookie credentials, XSRF, auth state — human review); all-new public API
> (consumed later by #20); new external dependency **Refit** (DL-3, user-approved); a sanctioned
> breaking-allowed amendment to shipped #10 (DL-2, user-approved); WASM-linker-safe closure (no
> `FrameworkReference` anywhere in the package's dependency closure).
>
> ⚠️ One follow-up owned by the user, not the planner: the founding-spec Package Map amendment
> recorded under DL-1 (`_specs/Cloudstrap.md` is user-amended only).

---

## Code-reading findings that shaped this spec

1. **The package's real value is the browser-auth triad** — `CookieHandler` (browser credentials +
   XSRF attachment), `AntiforgeryTokenStore` (the shared token slot), and
   `BffAuthenticationStateProvider` (auth state fetched from the Bff's user endpoint). All three are
   genuinely standalone, WASM-safe (plain NuGet dependencies, no framework reference), covered by
   real unit tests (`CookieHandlerTests`, `BffAuthenticationStateProviderTests`), and consumed by
   live call sites (`Test\WasmTestProject\src\Host\Wasm\Program.cs`, the Dashboard WASM package's
   four `AddNihdiWasmHttpClient` registrations). The founding-spec "rename only" assumption holds for
   the *capability*; it does not hold for the shapes (findings 2–6).
2. **The XSRF half never ran end-to-end in the source.** The live Bff host
   (`Test\WasmTestProject\src\Host\Cfe\Controllers\UserController.cs`) returns the `UserInfo` JSON
   but **never sets the `X-XSRF-TOKEN` response header, and no antiforgery service or validation is
   registered anywhere in the WasmTestProject** (repo grep: zero `XSRF`/`Antiforgery` hits outside
   the library and its unit tests). In production, `AntiforgeryTokenStore.Token` stayed `null`,
   `CookieHandler` attached nothing, and no server ever validated anything — the client machinery
   was aspirational, proven only by unit tests. #13 must therefore specify the **two-sided**
   contract (issue + attach + validate) or drop the XSRF surface honestly → resolved as the #10
   amendment, **DL-2**.
3. **The header-name knob and the handler disagree.** `BffAuthenticationOptions.XsrfHeaderName`
   configures which *response* header the state provider reads, but `CookieHandler` attaches the
   hard-coded constant `DefaultXsrfHeaderName` on requests (`CookieHandler.cs:19,58-59`). A consumer
   who overrides the knob silently splits capture from attachment. The redesign feeds both sides
   from the same option (D-3).
4. **#10's shipped Bff surface deliberately maps no user-info endpoint.**
   `MapCloudstrapAuthenticationEndpoints` maps login/logout only — its XML remarks state "No
   user-info endpoint … deliberately" (#10 decision D-5). The demo Bff's `UserController` is
   explicitly SUT app code ("the BFF user-info contract is deliverable #13" in its own comments) and
   returns a *different* wire shape (`UserStateDto(SignedIn, Name)`, no claims). The
   `BffAuthenticationStateProvider` therefore has **no shipped server counterpart** → resolved as
   the #10 amendment, **DL-2**.
5. **`AddWasmBffAuthentication` has registration defects worth shedding**: the `configure` delegate
   is invoked twice — once on a throwaway `BffAuthenticationOptions` instance just to learn the
   `HttpClientName` (`Authentication\ServiceCollectionExtensions.cs:45-55`), bypassing the options
   pipeline and any `IConfiguration` contribution — and the provider is built by a hand-rolled
   factory lambda. The redesigned registration uses the normal options pipeline. The tests' entry
   seam (`internal AddBlazorWasmServices` + `InternalsVisibleTo`) shows the registration must stay
   testable without a `WebAssemblyHostBuilder` — kept as a design constraint for the planner.
6. **The `Extensions\` folder is a composite that composes one thing plus noise.**
   `AddBlazorWasmForNihdi` = `AddWasmBffAuthentication` + a hidden `AddLocalization()`; the only
   live call site immediately re-calls `AddLocalization(o => o.ResourcesPath = "Resources")` anyway
   (`Host\Wasm\Program.cs:28-31`), so the hidden registration bought nothing (composites don't hide
   unrelated registrations — #12 D-8 precedent, user-approved). `BlazorWasmOptions` is a wrapper
   whose single member nests `BffAuthenticationOptions` — pure indirection.
   `ApplyStoredCultureAsync` (culture from browser localStorage via JS interop) **does** have real
   call sites (`Program.cs:51`, `CultureSelector.razor`) but is localization territory, and needs
   consumer-side globalization MSBuild props to work → resolved as deferred to #24, **DL-4**.
7. **Refit**: the source references `Refit` 11.0.1 only — deliberately *not* `Refit.HttpClientFactory`
   — and registers via `AddHttpClient(...).AddTypedClient((hc,_) => RestService.For<TClient>(...))`
   with a documented reason: `Refit.HttpClientFactory` (≤ 10.1.6) was compiled against
   `Microsoft.Extensions.Http 9.0.0.0` and throws `MissingMethodException` in a .NET 10 WASM app
   (`HttpClient\ServiceCollectionExtensions.cs:70-75`). Verified 2026-08-29: Refit is MIT
   (reactiveui/refit), actively maintained, current release **11.2.0** (2026-06-18). The ~15-line
   registration helper spares every consumer that footgun → resolved as the hard dependency, **DL-3**.
8. **#4's `AddCloudstrapHttpServiceClient` is not reusable in the browser — stated explicitly per
   the hand-off brief.** `Cloudstrap.Extensions` carries `<FrameworkReference
   Include="Microsoft.AspNetCore.App"/>` (WASM-hostile), binds server-side `Cloudstrap:HttpClients`
   config, registers peer `/healthz` readiness checks and token-attachment seams — a server hosting
   idiom end to end. The WASM client-side registration is its own idiom (base address from
   `HostEnvironment`, cookie/XSRF handlers, no health checks, no token handlers — the cookie *is*
   the credential). No shared code; the packages stay unrelated.
9. **Replace-with-library candidates examined and rejected.** *Duende.BFF* (the canonical BFF
   framework, whose `/bff/user` claims endpoint this package's provider mirrors): source-available
   commercial license — fails the OSI-only rule → rejected.
   *Microsoft.AspNetCore.Components.WebAssembly.Authentication*: solves the **opposite** pattern
   (OIDC in the browser, tokens held client-side) — the BFF cookie pattern exists precisely to keep
   tokens out of the browser → not an alternative. The bespoke triad (~150 lines, fully unit-tested)
   stands.
10. **Auth-state caching semantics are sound for the BFF pattern and are kept.** State is cached
    after the first fetch until `ClearAuthenticationState()`; login/logout happen via full-page
    redirects to the Bff (the old `RedirectToLogin.razor` does `forceLoad: true`), which reload the
    WASM app and naturally refresh the state. `IBffAuthenticationStateProvider` is the explicit
    SPA-side refresh seam for anything else. Failure semantics (HTTP error / network error / `null`
    body → anonymous, never throw) are pinned by the source's own tests and preserved.
11. **Path conventions need reconciling across the band.** Source defaults: user endpoint
    `api/user`, login link `api/user/login` (test-host controller convention). Shipped #10
    defaults: `/account/login`, `/account/logout`. Industry BFF convention (Duende BFF): `/bff/user`.
    Proposal: user endpoint defaults to **`bff/user`** on both sides (D-2), login/logout stay #10's
    `/account/*`; every path overridable.

---

## User Story

**As an** ASP.NET Core developer building a Blazor WebAssembly client that talks to its own BFF host
on Azure,
**I want** one registration call that gives me cookie-credentialed HTTP, automatic XSRF protection,
and Blazor auth state driven by the BFF's session — plus a one-liner to register typed/Refit API
clients that ride the same hardened pipeline,
**so that** no token ever lives in the browser, `AuthorizeView` just works, and my mutating calls
are CSRF-protected without me hand-wiring handlers, header names, or the user-info round trip.

---

## Acceptance Criteria

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim — tripwire only; a browser client is outside ServiceDefaults' remit, no other Aspire overlap exists)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried verbatim — this package references no auth packages)* |
| AC-BW1 | A WASM host with `AddCloudstrapBlazorWasm()` registered and a Bff user endpoint answering the wire contract | A component reads the cascading `AuthenticationState` | Signed-in: the principal is authenticated (authentication type `BffCookie`), `Identity.Name` and all wire claims present. Signed-out / `null` body / HTTP error / network error: the anonymous principal — never a throw (source-test parity, finding 10). |
| AC-BW2 | Any HTTP client registered through this package's helpers | Any request is sent | Browser request credentials are `Include` (the session cookie always travels); on POST/PUT/DELETE/PATCH with a captured token, the configured XSRF header is attached; on GET (and when no token is captured) it is not. |
| AC-BW3 | The Bff's user endpoint responds with the XSRF response header | The auth state is (re)fetched | The token is captured into the shared store and used by **all** clients registered through the package's helpers (one store per app instance). |
| AC-BW4 | A cached auth state | `IBffAuthenticationStateProvider.ClearAuthenticationState()` is called | The cache is dropped, `NotifyAuthenticationStateChanged` fires, and the next read re-fetches from the Bff (exactly two HTTP calls across the sequence — source-test parity). |
| AC-BW5 | A Refit interface registered via `AddCloudstrapWasmRefitClient<T>` *(final, DL-3)* | The interface is resolved and a method invoked | The call goes through the cookie+XSRF pipeline with the configured base address; default serialization is System.Text.Json camelCase, case-insensitive; `RefitSettings` fully overridable per registration. |
| AC-BW6 | Any opinionated default (`UserEndpointPath`, `XsrfHeaderName`, `AuthHttpClientName`, base address, Refit settings) | The consumer overrides it via `Cloudstrap:BlazorWasm` config or the configure delegate | The override wins everywhere it is read — in particular, capture **and** attachment use the same configured header name (fixes finding 3). Every convention has an override. |
| AC-BW7 | The two-sided contract — `MapCloudstrapBffUserEndpoint()` on the Bff (#10 amendment) *(final, DL-2)* | A browser signs in via #10's `/account/login`, the client fetches the user endpoint, then performs a mutating call | The user endpoint returns the documented wire contract with the XSRF response header; the mutating call passes server-side antiforgery validation; the same mutating call **without** the header is rejected by the Bff (proves validation is real, not theater — the source's dead half, finding 2, is not reproduced). |
| AC-BW8 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`/`Riziv`/`Cfe` | All green; XML docs on all public API; package metadata complete (MIT, icon, README, SourceLink); zero forbidden identifiers; **no `FrameworkReference` in the package or its dependency closure** (WASM-linker-safe); all dependencies OSI-licensed and CPM-pinned. |
| AC-BW9 | The `Cloudstrap.Demo.BlazorWasm.Client` rewritten onto the package (composite + Refit clients replacing the plain `HttpClient`) and the Bff completing the server half | The E2E suite runs | All pre-existing E2E tests (incl. every `DoctorsTests` flow) stay green and ≥ 1 new E2E test proves #13 behavior through the running browser (standing demo rule / workflow rule 9). |

---

## Port Decision Table

*(All verdicts final — the four Open Questions were resolved by the user 2026-08-31; see Decision Log.)*

| Source artefact (`Nihdi.Core.Configuration.BlazorWasm\`) | Verdict | Target | Justification |
|---|---|---|---|
| `Authentication\BffAuthenticationOptions.cs` | **Redesign** | `CloudstrapBlazorWasmOptions` (bound from `Cloudstrap:BlazorWasm`, code delegate wins) | The three knobs survive; the class becomes the package's single options type (the `BlazorWasmOptions` wrapper falls, below), gains config binding per repo convention (D-4), and its `XsrfHeaderName` is honored by the handler too (finding 3 → D-3). Default `UserEndpointPath` `api/user` → `bff/user` (finding 11 → D-2). |
| `Authentication\BffAuthenticationStateProvider.cs` | **Port** | `internal sealed BffAuthenticationStateProvider` | The package's core, behavior pinned by seven source tests (caching, anonymous fallbacks, XSRF capture, clear-and-notify) — all preserved. Goes `internal`: consumers interact via the framework's `AuthenticationStateProvider` and `IBffAuthenticationStateProvider`; nothing needs the concrete type (repo internal-by-default rule). |
| `Authentication\IBffAuthenticationStateProvider.cs` | **Port** | `Cloudstrap.BlazorWasm.IBffAuthenticationStateProvider` | The one-method refresh seam has a real job in a SPA (finding 10); shape is correct as-is. |
| `Authentication\ClaimDto.cs` + `UserInfo.cs` | **Port** | Internal wire DTOs (camelCase JSON) | Already `internal`, already minimal; they *are* the client half of the wire contract (documented under Behaviors, both halves final per DL-2). |
| `Authentication\ServiceCollectionExtensions.cs` — `AddWasmBffAuthentication` | **Redesign** | Folded into `AddCloudstrapBlazorWasm` | The registration survives; the defects don't: double-invoked configure delegate on a temp options instance, hand-rolled factory lambdas (finding 5). Rebuilt on the standard options pipeline; adds `AddCascadingAuthenticationState()` (D-5). Internal `IServiceCollection`-level seam kept for testability (finding 5). |
| `Extensions\BlazorWasmOptions.cs` | **Drop** | — | A wrapper whose only member nests the auth options — indirection with zero information. The composite configures `CloudstrapBlazorWasmOptions` directly. |
| `Extensions\WebAssemblyHostBuilderExtensions.cs` — `AddBlazorWasmForNihdi` | **Redesign** | `AddCloudstrapBlazorWasm(this WebAssemblyHostBuilder, Action<CloudstrapBlazorWasmOptions>?)` | The composite earns its port as the one obvious entry point (base address from `HostEnvironment.BaseAddress`), but sheds the hidden `AddLocalization()` — its only live consumer overrode it immediately (finding 6); localization is #24's deliverable (#12 D-8 precedent: composites don't hide unrelated registrations) (D-1). |
| `Extensions\WebAssemblyHostExtensions.cs` — `ApplyStoredCultureAsync` | **Drop here / defer to #24** *(final, DL-4, user-approved)* | — (#24 decides) | Real call sites exist (finding 6), but it is culture/localization machinery (JS interop + localStorage + consumer-side `BlazorWebAssemblyLoadAllGlobalizationData` prop) — cohesive with `Cloudstrap.Localization` (#24), not with browser auth. Porting it here also drags `Microsoft.JSInterop` usage into this package's remit for a non-auth feature. |
| `Http\CookieHandler.cs` | **Redesign** (one fix) | `public sealed CookieHandler` | Behavior pinned by six source tests and kept (credentials `Include`, mutating-methods set, no token → no header); the single change: the attached header name comes from `CloudstrapBlazorWasmOptions.XsrfHeaderName` instead of the hard-coded constant (finding 3 → D-3). Stays `public` as the documented escape hatch for consumer-owned `AddHttpClient` chains. |
| `Http\AntiforgeryTokenStore.cs` + `IAntiforgeryTokenStore.cs` | **Port** | `IAntiforgeryTokenStore` public, implementation `internal` | Correct design for WASM: one singleton token slot per browser app instance (single user). Interface stays public (consumers may pre-seed or inspect the token); the trivial implementation goes internal. |
| `HttpClient\ServiceCollectionExtensions.cs` — `AddNihdiWasmHttpClient<TClient>` | **Port** (rename) | `AddCloudstrapWasmHttpClient<TClient>(services, baseAddress, configureClient?)` → `IHttpClientBuilder` | Proven by four Dashboard call sites + tests; returning `IHttpClientBuilder` keeps it the composition root for anything (Refit chaining included). `TryAdd` semantics for store/handler kept and unified across both helpers. |
| `HttpClient\ServiceCollectionExtensions.cs` — `AddNihdiWasmRefitClient<TClient>` | **Port** (rename) *(final, DL-3, user-approved)* | `AddCloudstrapWasmRefitClient<TClient>(services, baseAddress, refitSettings?)` → `IHttpClientBuilder` | Proven by three live Refit call sites; encapsulates the `Refit.HttpClientFactory`/`Microsoft.Extensions.Http` version-mismatch workaround the source documented (finding 7) — exactly the footgun a library should own once. Return type changed to `IHttpClientBuilder` (D-6). Hard `Refit` dependency user-approved (DL-3); registration stays `RestService.For`, never `Refit.HttpClientFactory`. |
| *(no source artefact)* — Bff-side user endpoint + XSRF issuance/validation | **New (amendment to shipped #10)** *(final, DL-2, user-approved)* | Opt-in `MapCloudstrapBffUserEndpoint()` in `Cloudstrap.Authentication.OpenIdConnect` + stock `AddAntiforgery` wiring | Not gold-plating: the source *had* the endpoint (as test-host app code the client half hard-depends on) and the client XSRF machinery is dead without a server half (finding 2). Supersedes #10's "no user-info endpoint" remark — proposed explicitly, never changed silently (pre-release breaking allowance). |
| `.csproj` — `Microsoft.AspNetCore.Components.WebAssembly` / `.Components.Authorization` / `Microsoft.Extensions.Http` | **Port** | CPM-pinned references | Respectively: `SetBrowserRequestCredentials` + `WebAssemblyHostBuilder`; `AuthenticationStateProvider` + `AddAuthorizationCore`/`AddCascadingAuthenticationState`; `IHttpClientFactory`. All plain NuGet packages — no framework reference, WASM-safe. |
| `.csproj` — `Microsoft.Extensions.Localization` | **Drop** | — | Referenced only for the composite's hidden `AddLocalization()` (dropped, D-1) — falls with it. #24 owns localization. |
| `.csproj` — `Refit` 11.0.1 | **Port** (bump) *(final, DL-3)* | CPM entry `Refit` 11.2.0 | MIT (reactiveui/refit), active, current release 2026-06-18 (finding 7). New CPM entry — flagged per CLAUDE.md rule 4. |
| `.csproj` — `InternalsVisibleTo` (tests) | **Port** (mechanic) | `Cloudstrap.BlazorWasm.Tests` | The registration-seam testability the source proved; standard repo test posture. |
| `.csproj` — `StyleCop.Analyzers.Unstable` | **Drop** | — | Repo-wide decision #0: SDK analyzers, no StyleCop. |

**Not referenced: `Cloudstrap.BlazorCommon`** *(final, DL-1, user-approved)* — nothing in this package's surface
consumes `IViewModel`/`IErrorHandler` (same evidence pattern as #12's D-13: an empty dependency edge
is closure weight and a false signal). The demo app already proves band interop by consuming
BlazorCommon directly.

---

## Public API Sketch

*(Shapes and names, not implementations. Final — reflects the user-approved answers to all four
Open Questions (DL-1–DL-4). Single flat namespace `Cloudstrap.BlazorWasm` — band precedent #11/#12.)*

```csharp
namespace Cloudstrap.BlazorWasm;

/// Settings bound from Cloudstrap:BlazorWasm (wwwroot/appsettings.json — public by nature,
/// see Behaviors: this section may never carry secrets); the configure delegate wins over config.
public sealed class CloudstrapBlazorWasmOptions
{
    public const string SectionName = "Cloudstrap:BlazorWasm";

    /// Relative path (against the host base address) of the Bff user endpoint. Default "bff/user" (D-2).
    public string UserEndpointPath { get; set; }

    /// Header name used BOTH to capture the XSRF token from the user-endpoint response and to
    /// attach it on mutating requests (single source of truth — D-3). Default "X-XSRF-TOKEN".
    public string XsrfHeaderName { get; set; }

    /// Logical name of the named HttpClient the auth state provider uses. Default "CloudstrapBffAuth".
    public string AuthHttpClientName { get; set; }
}

public static class WebAssemblyHostBuilderExtensions
{
    /// The composite (DL-1): antiforgery token store (singleton), CookieHandler, the named auth
    /// HttpClient (base address = HostEnvironment.BaseAddress), the BFF authentication state
    /// provider (scoped, registered as AuthenticationStateProvider and IBffAuthenticationStateProvider),
    /// AddAuthorizationCore and AddCascadingAuthenticationState (D-5). Nothing else — no
    /// localization (D-1), no analytics, no MudBlazor.
    public static WebAssemblyHostBuilder AddCloudstrapBlazorWasm(
        this WebAssemblyHostBuilder builder,
        Action<CloudstrapBlazorWasmOptions>? configure = null);
}

public static class ServiceCollectionExtensions
{
    /// Typed HttpClient riding the cookie+XSRF pipeline; returns the builder for further chaining.
    public static IHttpClientBuilder AddCloudstrapWasmHttpClient<TClient>(
        this IServiceCollection services,
        string baseAddress,
        Action<HttpClient>? configureClient = null)
        where TClient : class;

    /// DL-3 (final) — Refit interface client riding the same pipeline; registers via
    /// RestService.For (never Refit.HttpClientFactory — finding 7). Default settings:
    /// System.Text.Json, camelCase, case-insensitive; fully overridable per call.
    public static IHttpClientBuilder AddCloudstrapWasmRefitClient<TClient>(
        this IServiceCollection services,
        string baseAddress,
        RefitSettings? refitSettings = null)
        where TClient : class;
}

/// Clears the cached BFF auth state and notifies listeners (call after an in-app login/logout
/// round trip that did not reload the app).
public interface IBffAuthenticationStateProvider
{
    void ClearAuthenticationState();
}

/// The shared XSRF token slot (one per browser app instance). Implementation is internal.
public interface IAntiforgeryTokenStore
{
    string? Token { get; set; }
}

/// Delegating handler: browser credentials Include on every request; configured XSRF header on
/// POST/PUT/DELETE/PATCH when a token is captured. Public as the escape hatch for consumer-owned
/// AddHttpClient chains.
public sealed class CookieHandler : DelegatingHandler { /* ... */ }
```

**DL-2 (final, user-approved) — amendment to shipped `Cloudstrap.Authentication.OpenIdConnect` (#10)**:

```csharp
namespace Cloudstrap.Authentication.OpenIdConnect;

public static class EndpointRouteBuilderExtensions // existing class, new opt-in mapper
{
    /// Maps GET {UserEndpointPath} (default "/bff/user"), AllowAnonymous, returning the wire
    /// contract below from the current principal, and setting the XSRF request token
    /// (stock IAntiforgery.GetAndStoreTokens) as the {XsrfHeaderName} response header.
    /// Opt-in — MapCloudstrapAuthenticationEndpoints stays login/logout-only, preserving #10 D-5's
    /// "nothing mapped unless the consumer calls the mapper" posture.
    public static IEndpointRouteBuilder MapCloudstrapBffUserEndpoint(this IEndpointRouteBuilder endpoints);
}
```

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Configuration posture (**explicit statement per the hand-off brief**) | Options bind from `Cloudstrap:BlazorWasm` in the WASM host's configuration (i.e. `wwwroot/appsettings.json`, which is **publicly downloadable** — the section carries only paths/header/client names, never secrets; the README states this); the `configure` delegate is applied after binding and wins | Config section, delegate, or both |
| Auth state | Fetched once from `{BaseAddress}{UserEndpointPath}`, cached until cleared; failures → anonymous, never a throw | `UserEndpointPath` option; `IBffAuthenticationStateProvider.ClearAuthenticationState()` to re-fetch |
| Wire contract (both halves final, DL-2) | `GET {UserEndpointPath}` → 200 always (anonymous allowed), camelCase JSON `{ "isAuthenticated": bool, "userName": string?, "claims": [{ "type": string, "value": string }]? }`; XSRF request token in the `{XsrfHeaderName}` response header | Endpoint path + header name options (both sides must agree — documented contract) |
| Principal shape | Authentication type `BffCookie`; `ClaimTypes.Name` from `userName`; wire claims mapped 1:1 | Bff decides which claims to expose (server-side concern) |
| Cookie credentials | `BrowserRequestCredentials.Include` on every request through package-registered clients | Consumer-owned clients: attach `CookieHandler` themselves (public escape hatch) or don't |
| XSRF attachment | Configured header on POST/PUT/DELETE/PATCH when a token has been captured; never on GET/HEAD; never when the store is empty | `XsrfHeaderName` option; pre-seed/clear via `IAntiforgeryTokenStore` |
| Auth HttpClient | Named client `AuthHttpClientName`, base address = `HostEnvironment.BaseAddress` | `AuthHttpClientName` option; standard `IHttpClientFactory` configuration of that name |
| Typed/Refit client base address | Caller-supplied `baseAddress` (pass `builder.HostEnvironment.BaseAddress` for the same-origin Bff) | Per-call parameter + `configureClient` / `RefitSettings` |
| Refit serialization (DL-3) | System.Text.Json, camelCase, case-insensitive | `RefitSettings` parameter per registration |
| Cascading auth state | `AddCascadingAuthenticationState()` registered by the composite (D-5) — `AuthorizeView`/`CascadingAuthenticationState` work out of the box | Framework-standard: consumer wraps their router themselves if they skip the composite |
| Repeat registrations | Store/handler use `TryAdd` (idempotent); client registrations append per standard `IHttpClientFactory` semantics — documented, not "fixed" | — |
| Localization / culture | **Not wired** (D-1; DL-4) — `AddLocalization` is one visible consumer line; culture persistence is #24's remit | Consumer calls `AddLocalization(...)` directly (as the source's own call site already did) |
| Login/logout flow | Not this package's — full-page navigations to #10's `/account/login` / `/account/logout` (documented pattern incl. a `RedirectToLogin` snippet in the README); after the round trip the app reloads and auth state is fresh | #10's `LoginPath`/`LogoutPath` options |
| Doc drift repair (definition of done) | `.claude/instructions/blazor.md` BlazorWasm section + `.claude/skills/refit/SKILL.md` WASM half updated to the shipped surface (`AddCloudstrapWasmRefitClient` replaces `AddRefitClientWithCookies`; the skill's `PathBaseDelegatingHandler` does not exist in this package and is removed from the example) | — (artefacts-catalog drift rule; #11 precedent) |

---

## Dependencies

| Package | Version (CPM) | License | Justification |
|---|---|---|---|
| `Microsoft.AspNetCore.Components.WebAssembly` | 10.x (repo-pinned) | MIT | `WebAssemblyHostBuilder`, `SetBrowserRequestCredentials` — the WASM hosting model itself. Plain NuGet package, no framework reference. |
| `Microsoft.AspNetCore.Components.Authorization` | 10.x (repo-pinned) | MIT | `AuthenticationStateProvider`, `AddAuthorizationCore`, `AddCascadingAuthenticationState`. Plain NuGet package (verified in #11, finding 7 there). |
| `Microsoft.Extensions.Http` | 10.x (repo-pinned) | MIT | `IHttpClientFactory` + `AddHttpClient`. |
| `Refit` *(final, DL-3, user-approved)* | 11.2.0 (new CPM pin) | MIT (reactiveui/refit, active, release 2026-06-18) | Powers `AddCloudstrapWasmRefitClient`; named in the founding spec's remit for this package. **`Refit.HttpClientFactory` is deliberately not referenced** (finding 7). New CPM entry — flagged per CLAUDE.md rule 4. |
| ~~`Microsoft.Extensions.Localization`~~ | — | MIT | **Not referenced** — its only consumer was the dropped hidden `AddLocalization()` (D-1). |
| ~~`Cloudstrap.BlazorCommon`~~ | — | MIT | **Not referenced** *(final, DL-1, user-approved)*: zero consumed symbols; #12 D-13 precedent — band interop is proven in the demo, not by a dead edge. |

Zero `Aspire.*` (AC-ASP2), zero `Nihdi.*` (AC-A3/AC-BW8), zero project references, zero
`FrameworkReference` (AC-BW8). The DL-2 amendment adds **no** dependency to #10 (stock
`Microsoft.AspNetCore.Antiforgery` ships in its existing framework reference).

---

## Deliberate Behavior Changes (vs. the source library)

| # | Change | Why |
|---|---|---|
| D-1 | Composite → `AddCloudstrapBlazorWasm`; the hidden `AddLocalization()` is removed | De-NIHDI naming; the only live consumer re-called `AddLocalization` with its own settings anyway (finding 6); composites don't hide unrelated registrations (#12 D-8 precedent); localization is #24. |
| D-2 | Default user-endpoint path `api/user` → `bff/user` (both sides) | Aligns with the industry BFF convention (Duende BFF's `/bff/user`) instead of a test-host controller route; avoids colliding with consumers' own `api/*` route space; overridable. |
| D-3 | `CookieHandler` attaches the **configured** `XsrfHeaderName` instead of a hard-coded constant | Fixes the silent capture/attach divergence when the knob is overridden (finding 3); one option, both sides. |
| D-4 | Options bound from `Cloudstrap:BlazorWasm` configuration (delegate wins) | Repo convention ("configuration lives under the `Cloudstrap:` section, one subsection per package"); the source was code-only and even bypassed its own options pipeline (finding 5). |
| D-5 | The composite registers `AddCascadingAuthenticationState()` | Registering an auth state provider nobody cascades is a half-wired feature; this is the framework-idiomatic one-liner the WASM template itself uses. Consumers skipping the composite wire it themselves. |
| D-6 | Refit helper returns `IHttpClientBuilder` (source: `IServiceCollection`) | Consistency with the typed-client helper; enables further chaining without a second API shape. |
| D-7 | The XSRF contract becomes two-sided and *validated* *(final, DL-2, user-approved)* | The source shipped only the client half — the token was never issued and never validated in any live host (finding 2). Cloudstrap does not ship security theater: the demo proves a header-less mutating call is rejected (AC-BW7). |
| D-8 | `BffAuthenticationStateProvider` and the token-store implementation go `internal`; `BlazorWasmOptions` wrapper dropped | Repo internal-by-default rule; consumers use the framework type + the two interfaces; the wrapper nested one object (finding 6). |
| D-9 | `ApplyStoredCultureAsync` not shipped in this package *(final, DL-4, user-approved — deferred to #24)* | Localization cohesion — #24's remit; recorded there so the capability is not lost. |

---

## Edge Cases

| Case | Expected behavior |
|---|---|
| User endpoint returns 404/500 / network error / `"null"` body | Anonymous state, cached, no throw (source-test parity). |
| User-endpoint response carries no XSRF header | Token stays absent; mutating requests go out without the header (the Bff rejects them if validation is on) — documented, no client-side error. |
| `ClearAuthenticationState()` before any fetch | Safe: cache flag reset, `NotifyAuthenticationStateChanged` fires with a fresh fetch task. |
| Consumer overrides `XsrfHeaderName` on the client but not the Bff (or vice versa) | Requests carry the client's name, server validates its own → rejected. The two-sided contract is documented as one setting that must agree; the demo shows both sides reading their respective options. |
| A request already carrying the XSRF header (consumer-set) | Handler replaces it with the store's token (source `Remove`+`Add` semantics kept — last writer is the pipeline, documented). |
| `AddCloudstrapBlazorWasm` called twice | Options delegates compose (standard options semantics); `TryAdd`-guarded services register once; the named client configuration appends — documented, not "fixed". |
| No `Cloudstrap:BlazorWasm` section in `wwwroot/appsettings.json` | All defaults apply; no validation failure (section optional; a WASM host has no startup-validation gate). |
| `baseAddress` without trailing slash passed to the client helpers | Passed to `Uri` as-is; relative-path resolution follows `HttpClient` semantics — documented with the trailing-slash recommendation, no silent "fixing". |
| Blazor Server / prerendered host consuming this package | Out of scope — the package targets the standalone WASM hosting model; `SetBrowserRequestCredentials` is a browser-only concern (documented). |

---

## Demo & E2E (standing rule / workflow rule 9)

`src/demo/BlazorWasm` is rewritten onto the package — resolving the Bff `UserController`'s own
"the BFF user-info contract is deliverable #13" placeholder:

- **Client** (`Cloudstrap.Demo.BlazorWasm.Client/Program.cs`): the plain
  `AddScoped(_ => new HttpClient(...))` is replaced by `AddCloudstrapBlazorWasm()` +
  `AddCloudstrapWasmRefitClient<IDoctorServiceClient>(builder.HostEnvironment.BaseAddress)` (DL-3);
  the router gains `AuthorizeView`-driven sign-in state.
- **Presentation**: `DoctorsViewModel` swaps its raw `HttpClient` + hand-rolled `api/v1/user/state`
  probe for the Refit client and the cascading auth state (BlazorCommon `IViewModel`/`IErrorHandler`
  usage stays — band interop evidence).
- **Bff**: maps `MapCloudstrapBffUserEndpoint()` (DL-2) and enables antiforgery validation on the
  mutating `POST api/doctor`.
- **E2E** (`Cloudstrap.Demo.E2E.Tests`): all eight pre-existing `DoctorsTests` flows stay green
  (they are this app's regression net); ≥ 1 new test proves #13 — recommended: the signed-in
  add-doctor POST succeeds carrying `X-XSRF-TOKEN`, **and** the same POST stripped of the header is
  rejected (AC-BW7/AC-BW9).

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- **`BlazorWasmOptions`** (wrapper, dropped) and the hidden `AddLocalization()` (D-1); **localization
  and culture persistence** incl. `ApplyStoredCultureAsync` → **deferred to #24** (DL-4, user-approved
  — the artefact, its two call sites and the `BlazorWebAssemblyLoadAllGlobalizationData` consumer-prop
  requirement are recorded here so #24's analyst inherits them).
- **Browser-held tokens** — `Microsoft.AspNetCore.Components.WebAssembly.Authentication`, PKCE in
  the browser, `AuthorizationMessageHandler`: the opposite pattern of BFF (finding 9).
- **Duende.BFF** or any commercial BFF framework (license, finding 9).
- **Server-side session/token management** — #10's remit; #13 touches #10 only via the DL-2 mapper.
- **A WASM variant of #4's `AddCloudstrapHttpServiceClient`** (config-bound clients, health checks,
  token handlers) — server idiom, not reusable in-browser (finding 8).
- **Dashboard WASM clients and assembly discovery** — #20 (which consumes this package's client
  helpers); `PathBaseDelegatingHandler` (skill-doc drift — never existed in this package).
- **BlazorServer helpers, `IBlazorInteractionTrace`** — #12; no cross-reference between the WASM and
  Server packages (band rule).
- Founding-spec global out-of-scope items: message encryption, MessagingBridge, Dynatrace,
  ServicePlatform, `Cloudstrap.Functional`.

---

## Decision Log (gate answers, 2026-08-31 — zero Open Questions remain; spec is planner-ready)

All four Open Questions were answered by the user, in each case accepting the analyst's
recommendation (option a). The full evidence and rejected options remain on record in the
Code-reading findings, the Port Decision Table, and the Deliberate Behavior Changes rows they
resolved into.

| DL | Decision (final) | Rationale kept on record |
|---|---|---|
| DL-1 *(was OQ-1)* | **Composite + granular**: `AddCloudstrapBlazorWasm` for the auth triad, `AddCloudstrapWasmHttpClient<T>` / `AddCloudstrapWasmRefitClient<T>` per client; **no `Cloudstrap.BlazorCommon` ProjectReference**; **founding-spec Package Map amendment approved** (user-owned edit, wording below). | Matches the #11/#12 composite precedent and #12 D-13's evidence rule (no empty dependency edges — zero BlazorCommon symbols consumed; band interop stays proven in the demo). Rejected: granular-only (loses the one-obvious-call ergonomics for zero gain); composite + BlazorCommon reference (ships a dead dependency). |
| DL-2 *(was OQ-2)* | **Amend shipped #10** with the **separate opt-in** `MapCloudstrapBffUserEndpoint()` in `Cloudstrap.Authentication.OpenIdConnect`: wire contract as specced, XSRF request token via stock `IAntiforgery.GetAndStoreTokens`, header name + path options; antiforgery **validation** stays the consumer's stock ASP.NET Core wiring, demonstrated in the demo. Supersedes #10 D-5's "no user-info endpoint" remark; pre-release breaking change sanctioned by the user. `MapCloudstrapAuthenticationEndpoints` itself stays login/logout-only. | The seam belongs next to the cookie session it exposes; opt-in preserves #10's "nothing mapped unless the consumer calls the mapper" posture; the source's server half never existed, leaving the XSRF machinery dead in production (findings 2, 4) — Cloudstrap ships no security theater (D-7, AC-BW7). Rejected: documented-contract-only (every consumer hand-writes security-relevant code from a README); folding into the existing mapper (breaks #10's mapped-surface contract for non-WASM consumers). |
| DL-3 *(was OQ-3)* | **Hard `Refit` dependency** — new CPM pin `Refit` **11.2.0** (MIT, reactiveui/refit, active, release 2026-06-18) — and `AddCloudstrapWasmRefitClient<T>` ships; registration stays `AddHttpClient(...).AddTypedClient((hc,_) => RestService.For<T>(...))`, **never `Refit.HttpClientFactory`** (finding 7's `Microsoft.Extensions.Http` version-mismatch footgun, owned once by the library). | The founding spec names Refit in this package's remit; three live call sites; one MIT dependency that deletes consumer-side pitfall code is within the minimize-dependencies rule. Rejected: handlers-only with Refit as a documented recipe (fallback shape, not chosen); a `Cloudstrap.BlazorWasm.Refit` leaf (a package per method). |
| DL-4 *(was OQ-4)* | **`ApplyStoredCultureAsync` deferred to #24 (Localization)** — dropped from #13, recorded in Out of Scope with the artefact path, its two call sites and the `BlazorWebAssemblyLoadAllGlobalizationData` consumer-prop requirement so #24's analyst inherits the capability. | Cohesion: culture persistence is #24's exact remit ("thin setup over ASP.NET Core localization"), pairable there with the server-side culture story; nothing in #13's demo needs it. Rejected: port now (splits localization across two packages); drop permanently (discards evidenced value — two live call sites). |

**Founding-spec amendment (user-approved under DL-1, application pending — user-only edit).** This
spec's author is constrained from modifying `_specs/Cloudstrap.md`; the approved amendment to apply
there is, in the Package Map row for `Nihdi.Core.Configuration.BlazorWasm` → `Cloudstrap.BlazorWasm`,
to replace **"Already standalone; rename only."** with **"Browser-auth triad (cookie credentials,
XSRF, BFF auth state) + WASM client helpers (typed/Refit): redesigned entry points
(`AddCloudstrapBlazorWasm` + `AddCloudstrapWasm(Http|Refit)Client`), two-sided XSRF contract via the
#10 `MapCloudstrapBffUserEndpoint` amendment; localization/culture helpers deferred to #24.
*(amended 2026-08-31 — see `_specs/13-BlazorWasmHelpers.md` Decision Log)*"**. Until applied, the
founding spec's wording and this spec diverge deliberately, with this log as the audit trail.
