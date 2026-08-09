# Spec: OIDC Login — `Cloudstrap.Authentication.OpenIdConnect` (Roadmap Deliverable #10)

> **Provisionally settled 2026-08-08 — zero Open Questions remain; spec is planner-ready, pending the user's
> confirmation of D-1…D-6.** The six gate questions were resolved by accepting this spec's own recommended
> option (a) for each so that planning could proceed; **the user has not yet reviewed or approved them.** Each
> is a gate answer awaiting confirmation, and D-1 (cookie defaults) and D-2 (token storage) are one-way doors —
> raise them explicitly at the plan's first 🛑 HUMAN GATE. The answers, with their evidence
> compressed to the essentials, are the **Decision Log (D-1…D-6)** at the end; the planner cites them per step.
> To keep two numbering series apart, every cross-reference to `_specs/9-ClientCredentialsAuth.md`'s decisions is
> written **#9's D-…**; a bare `D-…` in this file always means *this* spec's Decision Log.
>
> ⚠️ **One founding-spec amendment is pending the user's confirmation (D-4).** Accepting D-4 amends founding spec
> `_specs/Cloudstrap.md` **AC-A1's parenthetical "(test: Keycloak container)"** to name the in-repo OpenIddict
> test identity provider instead. The decision is **accepted for this deliverable**; the founding-spec edit is the
> user's alone and this spec does not make it. Until that edit lands, founding AC-A1 and this spec differ on that
> parenthetical and nothing else — no other founding-spec decision is touched.
>
> Sources: `_plans/ROADMAP.md` §10 (hand-off brief, file inventory verified 2026-08-06) · `_specs/Cloudstrap.md`
> (Decisions Made "Auth / token management", Package Map OpenIdConnect row, Auth Replacement + AC-A1/AC-A3,
> De-NIHDI-fication Checklist, Aspire Coexistence AC-ASP2/AC-ASP3, Non-Goals) · `_specs/9-ClientCredentialsAuth.md`
> (**binding precedent** — #9's decisions D-1…D-5, findings 4 and 6, the "Routed → #10" backlog) · **shipped** code read
> in full: `src/Cloudstrap.Extensions/IUserAccessTokenHandlerProvider.cs`, `IClientAccessTokenHandlerProvider.cs`,
> `AccessTokenHandlerWiring.cs`, `DataProtectionOptions.cs`, `src/Cloudstrap.Core/HttpClientServiceOptions.cs`,
> `TokenRequestOptions.cs`, all of `src/Cloudstrap.Authentication.ClientCredentials/` (registration, options,
> configurator, provider, handler), `src/Cloudstrap.WebApi/CloudstrapJwtBearerOptions.cs`,
> `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/` (options, client, host, registration),
> `src/Test/WasmTestProject/src/Host/Bff/Program.cs` + `appsettings.json` · source reference repo (read-only)
> `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` — every row of the Port Decision Table was
> opened: `Nihdi.Core.Configuration.OpenIdConnect\Extensions\WebApplicationBuilderExtensions.cs` + `.csproj`,
> `Test\WasmTestProject\src\Host\Cfe\Program.cs` (**Bff** in Cloudstrap terminology) + `appsettings.json` +
> `Controllers\UserController.cs`, `Test\TestProject\src\Host\Wfe\Program.cs` + `appsettings.json`, old Core
> `Settings\Security\{OpenIdConnectConfiguration, AuthenticationConfiguration, SecurityConfiguration,
> ClientCredentialsConfiguration}.cs` and `AuthenticationFlow.cs`, plus repo-wide reader greps for every settings
> member (finding 2).
>
> External evidence gathered 2026-08-06:
> [Duende.AccessTokenManagement.OpenIdConnect 4.2.0 on NuGet](https://www.nuget.org/packages/Duende.AccessTokenManagement.OpenIdConnect)
> — **Apache-2.0**, published 2026-03-18, targets `net8.0`/`net9.0`/**`net10.0`**, 9.6M downloads, owners
> DuendeSoftware; net10.0 dependencies: `Duende.AccessTokenManagement >= 4.2.0` (the exact version #9 pinned),
> `Microsoft.AspNetCore.Authentication.OpenIdConnect >= 10.0.4` (MIT),
> `Microsoft.IdentityModel.Protocols.OpenIdConnect >= 8.0.1 && < 9.0.0` (MIT — the same 8.x-not-9.x constraint #9
> recorded) · [ATM docs — user tokens in web apps](https://docs.duendesoftware.com/accesstokenmanagement/web-apps/):
> registration `AddOpenIdConnectAccessTokenManagement()`; the OIDC handler **must** set `SaveTokens = true` and
> request `offline_access` for refresh tokens; default token storage is **the authentication session** (the cookie
> ticket); manual access via `IUserTokenManager.GetAccessTokenAsync(User, …)` returning `TokenResult<UserToken>`.
>
> **⚠️ Risk areas this deliverable touches** — **auth risk area** (explicit human review at every gate that touches
> it) · **cookie/session security defaults are a one-way door** (settled by D-1) · **tokens, secrets and authorization codes
> must never reach logs or telemetry** — a sign-in flow puts codes on the wire and #2's enrichers plus #5's
> problem-details handler sit in the request path (AC-OIDC7) · **public API one-way door**:
> `AddCloudstrapOpenIdConnect` + its options shape · **new dependency**
> `Duende.AccessTokenManagement.OpenIdConnect` 4.2.0 (Apache-2.0, verified above) · **the test-IdP extension is
> itself one-way-door work** — it becomes deferred #26's source material and #12/#17 test infrastructure ·
> **standing pre-release rule**: breaking changes to shipped packages are allowed, but **this spec proposes none**
> — #9's D-4 seam pair, `TokenRequestOptions` and #9's surface all fit as designed (finding 4 of #9 anticipated
> exactly this deliverable).

## Code-reading findings that shaped this spec

1. **There is nothing to port — the source package is 73 lines of delegation over five unreadable packages.**
   `AddOpenIdConnectForNihdi` gates on `Security.EnableAuthentication`, throws three `ArgumentException`s
   (OIDC section present, ClientId set, ClientSecret set), registers `AddDistributedMemoryCache()`, then calls
   `EnableNihdiAuthenticationConfiguration().ForOpenIdConnect(...).AddNihdiOpenIdConnectAuthentication(...)
   .AddNihdiAccessTokenManagement(...).AddNihdiPolicyAuthorization()` — all five methods live in internal-feed
   `Nihdi.AspNetCore.*` 5.2.5 packages that cannot be read or restored. Exactly like #9, the port surface is
   **behavioral**: rebuild on the stock OIDC + cookie handlers and Duende ATM against the observed contract.
2. **The source's entire OIDC settings surface is inert at this repo's level.** Repo-wide greps show
   `OpenIdConnectConfiguration.ClientScopes`, `.SaveTokens` and `.UseTokenLifeTime` have **zero readers** beyond
   their declarations, and every consumer left them at their defaults — because every consumer configured them in
   places that do not bind: the Bff and Wfe hosts set `OpenIdConnect:UseDefaultClaimTypeMapping` (the property
   lives on the *parent* `AuthenticationConfiguration`, so it binds to nothing) and `ClientCredentials:ClientScopes`
   (no such property exists on `ClientCredentialsConfiguration`). The `OpenIdConnect` section's only observable
   function in the source is **existence** — passing the `is null` fail-fast check. The scopes the login actually
   requested came from the internal package's own defaults, not from configuration.
3. **One shared credential, unconditionally required, committed to source control.** The OIDC login authenticated
   with `Security:Authentication:ClientCredentials:ClientId/ClientSecret` — the *same* values #9's M2M grant used —
   and the source threw at startup if the secret was absent, making secret-free client authentication
   unrepresentable. The Bff host's `appsettings.json` additionally commits a live-looking Entra
   `AppRegistration:ClientSecret`. #9's D-1 posture (secret optional, any config provider, assertion path never
   blocked, placeholder-only fixtures) carries over unchanged; the OIDC client gets its **own** credentials with
   no fallback to the M2M section (D-3).
4. **Both source hosts run `IdentityModelEventSource.ShowPII = true` unconditionally** — a diagnostic switch that
   copies token contents and personal data into exception messages and logs, enabled in every environment. It is
   dropped: Cloudstrap never sets it, and the README documents it as a local-only troubleshooting step.
5. **The login/logout surface was consumer-written, not library code.** The Bff host's `UserController` is 20
   lines: `Challenge` with a `returnUrl`, `SignOut()`, and a claims-dump `GetUser` endpoint for the WASM client.
   The unreadable `Nihdi.AspNetCore.Authentication.UI` package was referenced but observably not load-bearing for
   the flow. `GetUser` is the #13 BFF user-info contract; the login/logout pair ships here as the two opt-in
   endpoints of D-5. The
   source `Login` endpoint passes the raw `returnUrl` into `RedirectUri` unvalidated — an open-redirect shape the
   redesign must close (local URLs only).
6. **The source OIDC package references the internal JwtBearer package — cookie/bearer coexistence was in scope.**
   Cloudstrap hits the same need concretely: the SUT Bff already registers #5's `AddCloudstrapJwtBearer`, and its
   #9 E2E test asserts a **401** from the machine endpoint. If #10 naively set the cookie scheme as the default
   challenge, that endpoint would start redirecting to a login page and the #9 test would break. Scheme
   coexistence is therefore specified behavior (AC-OIDC9), not an accident.
7. **Duende ATM v4's OIDC half decides the storage question's shape.** `AddOpenIdConnectAccessTokenManagement()`
   requires `SaveTokens = true` and stores user tokens **in the authentication session** (the cookie ticket) by
   default. The source's `SaveTokens = false` + unconditional `AddDistributedMemoryCache()` implies the internal
   package kept tokens in a server-side store backed by a *memory* distributed cache — which silently breaks on
   the second instance of a scaled-out app. Cookie-stored tokens behave identically on every instance (given a
   shared Data Protection key ring — #4's `AddCloudstrapDataProtection` is the documented companion). Settled by D-2.
8. **The stock handler's modern defaults do real security work for free.** `response_mode=form_post` keeps the
   authorization code out of URLs (and therefore out of #2's request telemetry and any access log); PKCE is
   enforced by the handler for code flow (`UsePkce` default `true`); .NET 9+ automatically uses Pushed
   Authorization Requests when the IdP advertises them. The spec leans on these instead of re-implementing them,
   and pins the ones that must not drift (PKCE, form_post) with tests.
9. **`AuthenticationFlow` stays dropped and `IdentityTokenCacheLifetime` does not earn its deferred port.** The
   enum (including two `Implicit` members) had zero readers (#9 finding 2); the flow is auth-code + PKCE by
   construction. With cookie-stored tokens there is no server-side identity-token cache for a lifetime setting to
   govern — the "→ #10 if it earns it" routing resolves to **Drop**.
10. **The shipped seam fits without modification.** `IUserAccessTokenHandlerProvider.CreateUserTokenHandler(string,
    TokenRequestOptions?)` is exactly the surface this package implements; `AccessTokenHandlerWiring` already
    resolves it independently, orders user-before-client, and fails naming this package.
    `TokenRequestOptions.SignInScheme`/`ChallengeScheme` — ignored-with-warning by #9 — map 1:1 onto Duende's
    `UserTokenRequestParameters`. No breaking change to any shipped package is needed or proposed.

---

## User Story

**As an** ASP.NET Core developer deploying a server-rendered or BFF-style web app to Azure,
**I want to** turn on interactive OpenID Connect login with one package and one registration call — hardened
cookie defaults, auth-code + PKCE, and transparent user-token refresh — after which every typed `HttpClient`
flagged `AddUserAccessToken: true` calls downstream APIs as the signed-in user,
**So that** no hand-written challenge/callback plumbing, cookie-security checklist, token-refresh timer or
per-request token forwarding code lives in my application — against any standards-compliant identity provider.

---

## Acceptance Criteria

> AC-A1, AC-A3 and AC-ASP2 are carried **verbatim** from the founding spec; AC-CC13 is re-proven from #9 with a
> real user-token provider installed. AC-OIDC1…AC-OIDC12 are new, spec-specific criteria (precedent:
> AC-CC1…AC-CC16). Verification runs against the **extended in-repo OpenIddict test identity provider** (D-4) —
> no Keycloak and no Docker anywhere in the test suite; verifying against Keycloak, Entra or any other conformant
> IdP is a documented manual README procedure.

| # | Given | When | Then |
|---|-------|------|------|
| AC-A1 | OIDC configured against any standards-compliant IdP — **verified against the in-repo OpenIddict test identity provider extended with auth-code + PKCE, refresh, userinfo and end-session (D-4)** | User signs in | Auth code + PKCE flow completes; tokens managed/refreshed by Duende ATM. *(criterion carried verbatim; ⚠️ the **verification vehicle** amends founding AC-A1's parenthetical "(test: Keycloak container)" — accepted here, pending the user's confirmation of that founding-spec edit, which this spec does not make)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried — must stay green)* |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried)* |
| AC-CC13 | Both `Cloudstrap.Authentication.ClientCredentials` and **this package** installed, one client sets both flags | The client is resolved and sends a request | Both handlers are in the chain, **user first** — now proven with two real providers instead of test doubles. |
| AC-OIDC1 | `AddCloudstrapOpenIdConnect()` configured against the test IdP | An unauthenticated browser request triggers a challenge | The redirect to the authorization endpoint carries `response_type=code`, a `code_challenge` with `code_challenge_method=S256`, `state` and `nonce`; the client secret appears nowhere in the URL. |
| AC-OIDC2 | The user completes login at the IdP | The callback is processed | A session cookie named **`__Host-Cloudstrap`** is issued with **`HttpOnly`**, **`Secure`** (`SecurePolicy=Always`) and **`SameSite=Lax`**, an **8 h sliding** expiration (D-1); the principal's claim types are the token's own (`sub` stays `sub` — no legacy URI mapping); the user is returned to their original local URL. |
| AC-OIDC3 | A signed-in user and a typed client flagged `Cloudstrap:HttpClients:{name}:AddUserAccessToken = true` | The client sends requests | Every request carries `Authorization: Bearer` with **that user's** access token; two different signed-in users driving parallel requests never observe each other's token. |
| AC-OIDC4 | An access token past its lifetime and a valid refresh token in the session | The flagged client sends a request | Exactly one refresh-grant call is made to the IdP, the request proceeds with the new token, and the user is not re-challenged. |
| AC-OIDC5 | A signed-in user | The logout endpoint (or an equivalent `SignOut` of both schemes) runs | The local session cookie is invalidated **and** the browser is sent to the IdP's end-session endpoint (RP-initiated logout); the next request is anonymous. |
| AC-OIDC6 | `Cloudstrap:OpenIdConnect` missing, or missing `Authority`/`ClientId`, or a relative `Authority` | The host starts | Startup fails naming the exact offending key, via the inherited source-generated `[OptionsValidator]` + `ValidateOnStart` pattern; no configured value is echoed. |
| AC-OIDC7 | A configured client secret; a full login round trip; logs at Debug, exported telemetry, problem-details output and exception text inspected | The flow runs, including one failure (bad IdP response) | No secret, authorization code, id/access/refresh token or `PII`-bearing value appears anywhere. The authorization code returns via `form_post` (never a URL); `IdentityModelEventSource.ShowPII` is never set by Cloudstrap code. |
| AC-OIDC8 | A flagged `AddUserAccessToken` client used with no signed-in user (anonymous request or background execution) | The client attempts a request | It fails fast with a message naming the flag and the missing user context; **no unauthenticated request is ever sent**. |
| AC-OIDC9 | #5's `AddCloudstrapJwtBearer` registered in the same host | A request with an `Authorization: Bearer` header hits a protected endpoint; a browser request hits the same endpoint | The bearer request is validated by the JWT scheme and failure yields **401** (never a login redirect); the browser request is challenged to the IdP. The #9 E2E machine-endpoint test stays green unmodified. |
| AC-OIDC10 | `AddCloudstrapOpenIdConnect()` called twice | The host starts | One registration of everything — schemes, options, provider, ATM services; no duplicate handlers or events. |
| AC-OIDC11 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv`, `Keycloak`, realm-style URLs (`/auth/realms/`), and the source fixtures' personal data | All green; XML docs on all public API; package metadata + README complete; zero forbidden identifiers; zero `Nihdi.*`/`Aspire.*` in any closure and **no `OpenIddict.*`/EF Core in the shipped closure** (test-only); every dependency OSI-licensed and CPM-pinned. |
| AC-OIDC12 | The WASM SUT Bff adopts this package (login/logout wired, one page or endpoint requiring the signed-in user, the `SelfApi` client additionally flagged `AddUserAccessToken`) | The E2E suite runs | ≥ 1 new Playwright test signs in through the real browser against the test IdP, exercises the user-token-carrying call, and logs out; the 31 pre-existing E2E tests stay green. *(standing SUT rule / workflow rule 9)* |

---

## Port Decision Table

One row per source public type/feature and per observed internal-package call (the internal packages are
unreadable, so their *contract* is rowed at the call site). "Superseded" = already adjudicated and shipped by an
earlier deliverable. "Routed" = belongs to a later deliverable.

### Part A — `Nihdi.Core.Configuration.OpenIdConnect` (the whole package)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `WebApplicationBuilderExtensions.AddOpenIdConnectForNihdi` | **Redesign** | `AddCloudstrapOpenIdConnect(this IServiceCollection, Action<CloudstrapOpenIdConnectConfigurator>?)` | The one-call registration earns its place; the shape does not: `WebApplicationBuilder` receiver, a hand-passed `NihdiConfiguration` and an `ILogger` parameter are the source's house style (#9 precedent). `IServiceCollection` keeps the package host-agnostic; minimal hosting auto-inserts `UseAuthentication`/`UseAuthorization` when schemes are registered. |
| ↳ `if (configuration.Security.EnableAuthentication)` gate | **Drop** | — | #5/#9 precedent: the registration call is the activation. The source's `false` branch silently skipped auth — an app that *looks* configured but challenges nobody. |
| ↳ `NihdiConfiguration` + `ILogger` parameters | **Drop** | — | Settings bind from `IConfiguration` (#1 pattern); logging comes from DI. |
| ↳ three fail-fast `ArgumentException` checks (section present, ClientId, ClientSecret) | **Redesign** | source-generated `[OptionsValidator]` + `ValidateOnStart` | Fail-fast is right; hand-rolled `GetSection(...).Get<T>()` re-binds (twice!) inside the extension are not. Messages name exact `Cloudstrap:OpenIdConnect:*` keys (AC-OIDC6). |
| ↳ the unconditional `ClientSecret` requirement | **Drop** | — | #9's D-1 carried over (and re-affirmed by D-3): the secret is optional configuration; secret-free client authentication (consumer-registered assertion) must be representable. The unconditional `[Required]` is what pushed the source into committing secrets (finding 3). |
| ↳ unconditional `AddDistributedMemoryCache()` | **Drop** | — | A hidden container side effect serving an internal server-side token store that is single-instance-only by construction (finding 7). Token storage is decided explicitly — cookie-stored, D-2 — not smuggled in. |
| `EnableNihdiAuthenticationConfiguration().ForOpenIdConnect(...)` *(internal pkg, observed)* | **Replace** | options binding of `CloudstrapOpenIdConnectOptions` (`Cloudstrap:OpenIdConnect`) | A bespoke configuration-enabling chain becomes one bound + validated options class. |
| `AddNihdiOpenIdConnectAuthentication(...)` *(internal pkg, observed)* | **Replace** | stock `AddAuthentication().AddCookie(...).AddOpenIdConnect(...)` with hardened defaults | The founding-spec decision verbatim: stock handlers do the protocol; Cloudstrap owns only defaults and wiring. |
| `AddNihdiAccessTokenManagement(...)` — the user-token half *(internal pkg, observed; the client half shipped in #9)* | **Replace** | `Duende.AccessTokenManagement.OpenIdConnect` `AddOpenIdConnectAccessTokenManagement()` | Refresh-token lifecycle, expiry buffering, refresh-stampede control and per-user token retrieval are security-sensitive solved problems; Apache-2.0, `net10.0`, 9.6M downloads, same 4.2.0 version line #9 pinned (#9 finding 6: the v4 split matches the #9/#10 line exactly — no rework). |
| `AddNihdiPolicyAuthorization()` *(internal pkg, observed)* | **Drop** | stock `AddAuthorization()` | Zero readable logic behind it and no observed consumer depending on a named policy. The package calls stock `AddAuthorization()` as required plumbing; policies belong to the application. (Hand-off question 6 — resolved by evidence, no OQ needed.) |
| the three optional `Action<>` hook parameters (`NihdiOpenIdConnectOptions`, `OpenIdConnectOptions`, `AccessTokenManagementOptions`) | **Redesign** | `CloudstrapOpenIdConnectConfigurator` (`OpenIdConnect`, `Cookie`, `TokenManagement` hooks — run last, final say) | The escape-hatch idea is right (#9 configurator precedent); three positional optional delegates on the method signature are not. |
| `.csproj` → `Nihdi.AspNetCore.Authentication.OpenIdConnect` 5.2.5 | **Replace** | `Microsoft.AspNetCore.Authentication.OpenIdConnect` (≥ 10.0.4, MIT — transitive via Duende ATM OIDC) | Founding Auth Replacement + AC-A3; internal feed, unreadable, unrestorable. |
| `.csproj` → `Nihdi.AspNetCore.AccessTokenManagement` 5.2.5 | **Replace** | `Duende.AccessTokenManagement.OpenIdConnect` 4.2.0 | As above. |
| `.csproj` → `Nihdi.AspNetCore.Authentication.JwtBearer` 5.2.5 | **Redesign** | scheme-coexistence behavior (AC-OIDC9) — inbound validation itself stays #5's | The reference is evidence the internal chain arbitrated cookie-vs-bearer (finding 6). Cloudstrap does not re-bundle validation; it ships the *coexistence rule*: bearer-header requests authenticate and fail via the JWT scheme, browser requests via cookie/OIDC. |
| `.csproj` → `Nihdi.AspNetCore.Authentication.UI` 5.2.5 | **Redesign** *(minimal — D-5)* | `MapCloudstrapAuthenticationEndpoints()` — **login + logout only**, opt-in | Unreadable, and finding 5 shows consumers wrote their own 20-line controller anyway — so full parity is unjustifiable, but every BFF copy-pastes the same two endpoints. Founding spec: "dropped or minimal stock equivalents (explicit scoping required)" — **D-5 is that explicit scoping**: `/account/login` + `/account/logout` defaults, paths configurable, local-URL-only returns, RP-initiated logout of both schemes. Everything else (user-info → #13; front-/back-channel logout; consent/account UI) is dropped. |
| `.csproj` → `Nihdi.AspNetCore.Authorization` 5.2.5 | **Drop** | stock `AddAuthorization()` | Same evidence as `AddNihdiPolicyAuthorization()`. |
| `Riziv-Inami` copyright header | **Drop** | — | De-NIHDI checklist: `LICENSE` + `PackageLicenseExpression`, no per-file headers. |
| `.csproj` `StyleCop.Analyzers.Unstable` reference | **Drop** | — | #0 dropped StyleCop suite-wide. |
| *(no README in source)* | **Redesign** | new MIT package README | Home of the secret-handling guidance, the multi-instance/Data Protection note, the `ShowPII` troubleshooting tip and the manual any-IdP verification procedure. |

### Part B — old Core `Settings\Security\` members routed here by #9

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `OpenIdConnectConfiguration.ClientScopes` (space-delimited string, default `""`) | **Redesign** | `CloudstrapOpenIdConnectOptions.Scope` (space-delimited, default `"openid profile offline_access"` — D-3) | The capability is real; the source value never bound anywhere (finding 2). A single space-delimited string matches #9's `Scope` and RFC 6749, and deliberately avoids a bound collection — #1's binder append-not-replace caveat makes a get-only `IList<string>` scope setting a foot-gun (defaults could never be removed). `offline_access` is in the default because ATM's refresh requires a refresh token; the README documents removing it where an IdP treats `offline_access` specially. |
| `OpenIdConnectConfiguration.SaveTokens` (default `false`) | **Drop** | — (hard-set `true`) | Not a real choice in the target design: Duende ATM's OIDC half **requires** `SaveTokens = true` (verified docs). A visible toggle whose `false` value silently breaks token management is worse than no toggle. Storage consequence recorded in D-2 (tokens live in the cookie ticket). |
| `OpenIdConnectConfiguration.UseTokenLifeTime` (default `false`) | **Drop** | — | Mirrors the stock handler's `UseTokenLifetime` (session lifetime = id-token lifetime). Zero readers, and it couples session length to an IdP-chosen token lifetime invisibly. Session length is governed by the explicit cookie settings of D-1 instead (8 h sliding, configurable). |
| `AuthenticationConfiguration.Authority` | **Port** | `CloudstrapOpenIdConnectOptions.Authority` (required, absolute URL) | Unlike #9 — where an explicit `TokenEndpoint` replaced an authority-plus-Keycloak-path convention — the stock OIDC handler is *built on* discovery from an authority; that is the standards-native model here. De-NIHDI: no realm-style default or example (`https://idp.example.com` fixtures). |
| `AuthenticationConfiguration.UseDefaultClaimTypeMapping` (default `false`) | **Redesign** | `CloudstrapOpenIdConnectOptions.MapInboundClaims` (default `false`) | #5's shipped posture, now on the OIDC handler: a claim is called what the token calls it (`sub` stays `sub`); `NameClaimType`/`RoleClaimType` default to `name`/`role`, overridable via the configurator. The source's flag *intended* this but consumers set it where it never bound (finding 2) — now it actually works. |
| `AuthenticationConfiguration.IdentityTokenCacheLifetime` (default 10 h) | **Drop** | — | #9 deferred it "→ #10 if it earns it". It does not: with the cookie-stored tokens of D-2 there is no server-side identity-token cache to expire, and it had zero readers (finding 9). Confirmed dropped by D-2. |
| `AuthenticationConfiguration.AuthenticationFlow` + the `AuthenticationFlow` enum | **Drop** | — | Stays dropped (#9 verdict): the flow is auth-code + PKCE by construction; `Implicit` is not offered in 2026. PKCE is always on (stock `UsePkce = true` pinned by test). |
| `ClientCredentialsConfiguration` as the OIDC client's credential source | **Redesign** *(D-3)* | own `ClientId` (required) + `ClientSecret` (optional) on `Cloudstrap:OpenIdConnect`; **no fallback** to `Cloudstrap:ClientCredentials` | The source shared one client id/secret between interactive login and M2M. Two packages coupled through one config node is what #9's D-2 rejected; a login client and an M2M client are different principals with different lifecycles. A consumer who genuinely reuses one IdP registration duplicates two values (or maps one environment variable twice) — explicit beats a silent fallback that a typo turns into authenticating as the wrong client. |
| `SecurityConfiguration.EnableAuthentication` | **Superseded** | — | Dropped by #5 and #9; nothing resurrected. |
| nested `AuthenticationConfiguration` graph + `IValidatableObject` cascade | **Superseded** | — | #1 replaced the cascade; #9's D-2 fixed the flat-section convention. |
| `AuthenticationConfiguration.CacheKeyPrefix` / `CacheLifetimeBuffer` | **Superseded** | Duende option types via `configurator.TokenManagement` | Adjudicated by #9; the user-token equivalents stay Duende's, reachable through the same hook pattern. |
| `AuthenticationConfiguration.RefreshTokenCacheLifetime` | **Drop** | — | With session-stored tokens the refresh token lives in the ticket; its lifetime is the IdP's decision. Zero readers (#9 finding 2). |

### Part C — observed consumer contract (Bff `UserController` + host `Program.cs`)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `UserController.Login` — `Challenge(new AuthenticationProperties { RedirectUri = returnUrl })` | **Redesign** *(D-5)* | login endpoint in `MapCloudstrapAuthenticationEndpoints()` (`/account/login`) | The two-line challenge is the right mechanism; passing the caller's `returnUrl` through unvalidated is an **open-redirect** (finding 5). The redesign accepts local URLs only. |
| `UserController.Logout` — `SignOut()` (cookie only) | **Redesign** *(D-5)* | logout endpoint (`/account/logout`) signing out of **both** schemes | The source cleared the local cookie but left the IdP session alive — "logout" that silently signs you straight back in on the next challenge. RP-initiated logout (cookie + OIDC schemes) is the correct default (AC-OIDC5). |
| `UserController.GetUser` (claims → `UserInfo` DTO for the WASM client) | **Routed** | #13 | The BFF user-info contract belongs with `BffAuthenticationStateProvider` and the WASM auth helpers. |
| `IdentityModelEventSource.ShowPII = true` in both hosts | **Drop** | — (documented troubleshooting tip) | Finding 4: a PII-leaking debug switch enabled unconditionally. Cloudstrap code never sets it; AC-OIDC7 greps for it. |
| committed live-looking secrets (`AppRegistration:ClientSecret`, `ClientCredentials:ClientSecret` in the Bff/Wfe `appsettings.json`) | **Drop** | — (documented anti-pattern) | #9 finding 3 continued; no Cloudstrap fixture, README or SUT config carries a real-looking secret. |
| inert config keys set under non-binding sections (`OpenIdConnect:UseDefaultClaimTypeMapping`, `ClientCredentials:ClientScopes`) | **Drop** | — | Fixture noise proving finding 2; nothing to carry. The new options class + validator make a misplaced key visible instead of silent. |

### Part D — test identity provider extension (the "Routed → #10" backlog from #9's D-5, test-only)

| Source (STS `Readme.txt` / #9 routing) | Verdict | Target | Justification |
|---|---|---|---|
| authorization-code + PKCE flow | **Port** *(extend)* | `AllowAuthorizationCodeFlow()` + PKCE **required** on the OpenIddict server | The inherited backlog #9 explicitly deferred; extends `Cloudstrap.TestIdentityProvider`, never replaces it (#9's D-5 one-way door honored). This extension is the AC-A1 verification vehicle (D-4). |
| refresh-token flow | **Port** *(extend)* | `AllowRefreshTokenFlow()` + `TestIdentityProviderOptions.RefreshTokenLifetime` | Load-bearing for AC-OIDC4: deterministic refresh needs short, configurable lifetimes. |
| authorization endpoint + a login page | **Redesign** | `/connect/authorize` + a **minimal** test login page over config-declared users (`TestIdentityProviderOptions.Users`: name, password, claims); auto-consent | The source STS baked a real person's identity into per-client claim config (#9 finding 12). Test users become neutral, declarative data (`contoso`-style), so a Playwright test can type a username/password and assert the resulting claims. Minimal by design: one form, no account management, no consent UI. |
| userinfo endpoint + `IdToken`/`UserInfo` claim sets | **Port** *(extend)* | `/connect/userinfo` + the two routed claim-set members on `TestIdentityProviderClaims` | Routed here by #9's table verbatim. |
| per-client `RedirectUri` | **Port** *(extend)* | `TestIdentityProviderClient.RedirectUris` + `PostLogoutRedirectUris` (lists) | Required by the code flow; lists rather than the source's single value. |
| end-session (logout) endpoint | **Port** *(extend)* | `/connect/logout` (OpenIddict end-session support) | Without it AC-OIDC5's RP-initiated logout cannot be proven; the source relied on Keycloak's advertised end-session endpoint, so this is observed contract, not invention. |
| `UsePKCE` per-client flag | **Drop** | — | PKCE is required, always, for every code-flow client of the test IdP — a test IdP that can be talked out of PKCE invites a test that demonstrates skipping it. |

**Tally**: 6 Port · 12 Redesign · 5 Replace · 17 Drop · 3 Superseded · 1 Routed *(44 rows)*.

---

## Public API Sketch

### 1. New package — `Cloudstrap.Authentication.OpenIdConnect`

Namespace **`Cloudstrap.Authentication.OpenIdConnect`** (single namespace = package id; suite precedent).
Everything `public sealed`/`static`; the provider, handler, validator and scheme-forwarding pieces are
`internal`. Names carry the `Cloudstrap` prefix where a framework/Duende type shares the name
(`OpenIdConnectOptions` is stock — the `CloudstrapJwtBearerOptions` precedent).

```text
Cloudstrap.Authentication.OpenIdConnect
├── ServiceCollectionExtensions (static)
│     AddCloudstrapOpenIdConnect(
│         this IServiceCollection services,
│         Action<CloudstrapOpenIdConnectConfigurator>? configure = null)
│         : IServiceCollection                                      ⚠️ auth risk area
│       — binds + validates CloudstrapOpenIdConnectOptions (Cloudstrap:OpenIdConnect) with ValidateOnStart;
│         registers AddAuthentication (default scheme = cookie, default challenge = OIDC) + AddCookie with
│         the hardened defaults (D-1) + AddOpenIdConnect (code + PKCE, form_post, SaveTokens = true,
│         MapInboundClaims from options, scopes from options); calls Duende's
│         AddOpenIdConnectAccessTokenManagement(); registers the IUserAccessTokenHandlerProvider that fills
│         the Cloudstrap.Extensions seam; registers stock AddAuthorization() (plumbing only) plus the
│         require-authenticated fallback policy when RequireAuthenticatedEndpoints is true — the default
│         (D-6); installs the bearer-coexistence forwarding (AC-OIDC9). Idempotent (AC-OIDC10).
│         Registers NO inbound JWT validation, NO client-credentials services, NO Blazor helpers.
│
├── EndpointRouteBuilderExtensions (static)                          opt-in, login + logout only  [D-5]
│     MapCloudstrapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
│       — GET {LoginPath}  (default /account/login):  Challenge with a validated LOCAL returnUrl only
│       — GET {LogoutPath} (default /account/logout): SignOut of cookie + OIDC schemes (RP-initiated)
│       — nothing else: no user-info (#13), no register/consent/account pages
│
├── CloudstrapOpenIdConnectOptions            — section Cloudstrap:OpenIdConnect (owned HERE)
│     const SectionName = "Cloudstrap:OpenIdConnect"
│     Authority     : string   — required, absolute; the stock handler discovers endpoints from it
│     ClientId      : string   — required; this package's OWN client, never read from
│                                Cloudstrap:ClientCredentials                                    [D-3]
│     ClientSecret  : string?  — optional (#9's D-1): omit for secret-free client authentication [D-3]
│     Scope         : string = "openid profile offline_access"  — space-delimited                [D-3]
│     MapInboundClaims : bool = false                            — #5 parity (claims stay as issued)
│     RequireHttpsMetadata : bool? = null                        — #5 pattern: required except Development
│     CallbackPath  : string = "/signin-oidc"                    — stock default, overridable
│     SignedOutCallbackPath : string = "/signout-callback-oidc"  — stock default, overridable
│     LoginPath     : string = "/account/login"                                                  [D-5]
│     LogoutPath    : string = "/account/logout"                                                 [D-5]
│     RequireAuthenticatedEndpoints : bool = true                                                [D-6]
│     Cookie        : CloudstrapAuthenticationCookieOptions                                      [D-1]
│
├── CloudstrapAuthenticationCookieOptions     — nested under the same section (Cookie:*)         [D-1]
│     Name              : string  = "__Host-Cloudstrap"   — __Host- prefix: Secure, Path=/, no Domain
│     Lifetime          : TimeSpan = 8 h
│     SlidingExpiration : bool     = true
│     (HttpOnly, SecurePolicy=Always and SameSite=Lax are hardened constants, reachable only via
│      configurator.Cookie — deliberately not configuration values)
│
├── CloudstrapOpenIdConnectConfigurator       — code-level hooks (the #9 configurator precedent)
│     OpenIdConnect   : Action<OpenIdConnectOptions>?            — stock type; runs LAST, final say
│     Cookie          : Action<CookieAuthenticationOptions>?     — stock type; runs LAST, final say
│     TokenManagement : Action<UserTokenManagementOptions>?      — Duende's user-token knobs
│
└── CloudstrapOpenIdConnect (static)          — the constants consumers need
      const CookieScheme    = CookieAuthenticationDefaults.AuthenticationScheme   ("Cookies")
      const ChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme          ("OpenIdConnect")
      — stock scheme names on purpose: SignOut()/Challenge() without arguments and third-party
        libraries that assume the defaults keep working

internal: UserAccessTokenHandlerProvider (fills the #4/#9 D-4 seam — maps TokenRequestOptions including
SignInScheme/ChallengeScheme onto Duende UserTokenRequestParameters; finding 10), UserTokenHandler
(attach + single 401 ForceTokenRenewal retry, the #9 handler pattern, throwing — never sending — when
no user token exists, AC-OIDC8), CloudstrapOpenIdConnectOptionsValidator (source-generated
[OptionsValidator] — inherited fact, no Microsoft.Extensions.Options.DataAnnotations), the
bearer-coexistence forward selector (AC-OIDC9), and a startup logger stating scheme, storage and
endpoint-mapping posture (visible-not-magic, the #9 D-3 precedent).
```

**Deliberately not shipped**: no Cloudstrap facade over `IUserTokenManager` (Duende's interface is injectable
as-is), no front-/back-channel logout endpoints (Out of Scope, demand-driven), no user-info endpoint (#13), no
Blazor circuit token plumbing (#12 — Duende's `AddBlazorServerAccessTokenManagement` arrives there), no
server-side session/ticket store and no distributed token store (D-2), no consent/account UI, no multi-scheme
or multi-tenant OIDC registration, and no `IHostApplicationBuilder` overload.

**Configuration** — this package owns exactly one new subsection, `Cloudstrap:OpenIdConnect` (flat, #9's D-2
precedent — no `Cloudstrap:Authentication` parent, and per D-3 no fallback to any other package's section). It **consumes** Core's shipped
`Cloudstrap:HttpClients:{name}:AddUserAccessToken` and `:TokenRequestParameters` and never redefines them: the
opt-in for a client is the flag that already exists. The only collection-shaped member anywhere is inside the
test IdP options (test-only); the shipped options class has none, so #1's binder-append caveat does not bite —
`Scope` is deliberately a string for exactly that reason (Part B).

### 2. Shipped-surface impact: none

#9's D-4 seam (`IUserAccessTokenHandlerProvider`), `TokenRequestOptions` (whose `SignInScheme`/`ChallengeScheme`
were reserved for this deliverable by #9 finding 4) and `AccessTokenHandlerWiring`'s user-first ordering are
consumed exactly as shipped. **No breaking change to any shipped package is proposed.** (The standing
pre-release permission to break earlier packages exists but is not needed.)

### 3. Test infrastructure — `Cloudstrap.TestIdentityProvider` extension (test-only, not shipped)

Extends the existing project in place (#9's D-5: extend, never replace) — and, per **D-4**, this extension *is*
the AC-A1 verification vehicle: no Keycloak, no container, no Docker enters the test suite. New surface, all
config-driven:

```text
TestIdentityProviderOptions               (existing — extended)
├── RefreshTokenLifetime : TimeSpan       — short by default; the lever AC-OIDC4 pulls
├── Users : IList<TestIdentityProviderUser>          (NEW)
│     Username / Password : string        — obvious placeholder values, neutral names (contoso)
│     Claims : IDictionary<string, IList<string>>    — stamped per the claim-set model below
└── Clients[] (existing TestIdentityProviderClient — extended)
      RedirectUris / PostLogoutRedirectUris : IList<Uri>
      TokenClaims.IdToken / TokenClaims.UserInfo     — the two claim sets #9 routed here

Server: AllowAuthorizationCodeFlow (PKCE required) + AllowRefreshTokenFlow added next to the existing
client-credentials grant; /connect/authorize, /connect/userinfo, /connect/logout endpoints; one minimal
login page (username/password form over Users, auto-consent). Existing hosting modes (in-process +
loopback 127.0.0.1) and the token-endpoint hit counter are unchanged; an authorize/refresh counter is
added for AC-OIDC4-style assertions.
```

⚠️ **One-way door (flagged)**: this extension is deferred #26's source material and #12/#17's test
infrastructure; its options additions follow the shape #9 fixed. The README additionally documents a **manual**
"verify against Keycloak, Entra or any conformant IdP" procedure — configuration only, no test-suite dependency
(D-4).

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Activation | Nothing happens until `AddCloudstrapOpenIdConnect()` is called; no config flag turns login on. | Call it, or don't. |
| Schemes | Cookie is the default scheme, OIDC the default challenge; **stock scheme names** so unqualified `Challenge()`/`SignOut()` and ecosystem assumptions keep working. | `configurator.OpenIdConnect`/`.Cookie`; explicit `AuthenticationSchemes` on attributes. |
| Flow | Authorization code + PKCE (S256), `response_mode=form_post` (codes never in URLs), PAR automatically when the IdP advertises it (stock .NET 9+ behavior). **Not configurable** — no implicit, no hybrid, no PKCE opt-out. | None — this is a rule, not a convention (the #9 `AuthenticationFlow` drop). |
| Scopes | `openid profile offline_access` (refresh requires a refresh token — ATM requirement) (D-3). The README documents removing `offline_access` where an IdP treats it specially. | `Cloudstrap:OpenIdConnect:Scope`; per-HTTP-client `TokenRequestParameters:Scope` narrows the *token request*, not the login. |
| Credentials | This package's **own** `ClientId` (required) and `ClientSecret` (optional, any configuration provider); **no fallback** to `Cloudstrap:ClientCredentials` (D-3, #9's D-1). | The settings themselves; secret-free client authentication via the stock/Duende extension points reached through `configurator.OpenIdConnect`. |
| Sign-in cookie | Hardened (D-1): name **`__Host-Cloudstrap`**, `HttpOnly` always, `SecurePolicy=Always`, `SameSite=Lax` (Strict breaks the OIDC callback navigation), **8 h sliding** expiration. | `Cookie:Name`, `Cookie:Lifetime`, `Cookie:SlidingExpiration` in configuration; the hardened trio (HttpOnly / Secure / SameSite) only via `configurator.Cookie` — deliberately code-level, so weakening it is a visible act in `Program.cs` rather than config drift. |
| Claims | `MapInboundClaims = false` (`sub` stays `sub`); `NameClaimType = "name"`, `RoleClaimType = "role"`. | The `MapInboundClaims` setting; claim types via `configurator.OpenIdConnect` (`TokenValidationParameters`). |
| Token storage | In the authentication session (cookie ticket), `SaveTokens = true` — Duende ATM's default and requirement (D-2). **No server-side ticket store, no distributed token store, no cache registration** ships. | `configurator.TokenManagement` + a consumer-registered Duende store for advanced setups; #12 brings the Blazor Server circuit store. |
| Multi-instance | Works when the Data Protection key ring is shared — the README names #4's `AddCloudstrapDataProtection` as the required companion for scale-out and states the symptom of forgetting it (users bounced to login on the second instance). This is the documented multi-instance posture of D-2. | #4's `Cloudstrap:DataProtection` section. |
| Token refresh | Duende ATM refreshes an expiring access token transparently when a flagged client needs it; the user is not re-challenged while the refresh token is valid. | `configurator.TokenManagement` (expiry buffer etc.). |
| User-token handler | Fills the shipped seam: outermost, **before** the client-credentials handler when both flags are set (AC-CC13); honors all five `TokenRequestOptions` members (`Scope`, `Resource`, `ForceRenewal`, `SignInScheme`, `ChallengeScheme` — finding 10); throws instead of sending unauthenticated (AC-OIDC8); one `ForceTokenRenewal` retry on 401 (the #9 handler pattern). | Per-client `TokenRequestParameters`; turn the flag off. |
| Coexistence with #5 (`AddCloudstrapJwtBearer`) | Requests carrying `Authorization: Bearer` authenticate and **fail with 401** via the JWT scheme; everything else uses cookie/OIDC (AC-OIDC9). Active only when both packages are registered. | Explicit `AuthenticationSchemes` per endpoint; `configurator.OpenIdConnect`/`.Cookie` reach the forwarding. |
| Endpoint protection | `RequireAuthenticatedEndpoints = true` (D-6) — a require-authenticated fallback policy, the same flag name, default and opt-outs as #5. Health probes and OpenAPI endpoints stay anonymous (mapped so by #4/#5). | `[AllowAnonymous]` per endpoint; the flag for the whole app. |
| Login/logout endpoints | Opt-in `MapCloudstrapAuthenticationEndpoints()` (D-5) — exactly two: `/account/login` (challenge, **local** return URLs only) and `/account/logout` (RP-initiated sign-out of both schemes). Nothing is mapped unless the consumer calls it. | `LoginPath`/`LogoutPath` settings; or skip the mapper entirely and `Challenge()`/`SignOut()` from your own endpoints. |
| HTTPS | IdP metadata over HTTPS required everywhere except `Development` (`RequireHttpsMetadata = null` — #5 pattern). | The setting itself. |
| Secrets & telemetry | Validation, logs and exceptions name configuration **keys**, never values; no secret, code or token reaches an `Activity` tag, log scope or problem-details body; `ShowPII` never set. | None — rule, not convention (AC-OIDC7). |
| Failure when unconfigured | `AddUserAccessToken: true` without this package → the shipped wiring already fails naming this package. Anonymous user with the flag on → fail fast, no request (AC-OIDC8). | Turn the flag off. |
| Aspire coexistence | No overlap — interactive auth is outside ServiceDefaults' remit. AC-ASP2 carried as a closure tripwire; the user-token handler joins the pipeline via the same seam #4/#9 proved leaves `ConfigureHttpClientDefaults` resilience untouched (AC-ASP3 posture). | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in
`src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests`. Integration tests boot a real host with the
**extended test IdP in-process** and drive the full code+PKCE round trip with a cookie-aware `HttpClient`
(follow the challenge redirect, post the login form, follow the callback) — asserting AC-OIDC1/2/3/4/5/8/9
deterministically via the IdP's configurable lifetimes and endpoint counters. `Cloudstrap.TestIdentityProvider.Tests`
grows cases for the new grants. The demonstration slice (AC-OIDC12) wires login/logout into the SUT Bff, flags
`SelfApi` with `AddUserAccessToken` (making AC-CC13 real), and adds ≥ 1 Playwright browser-login E2E test.

---

## Dependencies

### Shipped package — `Cloudstrap.Authentication.OpenIdConnect`

| Package | License | Evidence & justification |
|---|---|---|
| `Cloudstrap.Core` *(project reference)* | MIT | `TokenRequestOptions`, `HttpClientServiceOptions` — the per-client contract the user handler honors. |
| `Cloudstrap.Extensions` *(project reference)* | MIT | `IUserAccessTokenHandlerProvider` — the seam half this deliverable fills (#9's D-4). Brings the `Microsoft.AspNetCore.App` framework reference (cookie + authentication core live there). |
| **`Duende.AccessTokenManagement.OpenIdConnect` 4.2.0** | **Apache-2.0** ✅ | [nuget.org](https://www.nuget.org/packages/Duende.AccessTokenManagement.OpenIdConnect): Apache-2.0, published **2026-03-18**, targets `net8.0`/`net9.0`/**`net10.0`**, **9.6M** downloads, owners DuendeSoftware — the OIDC/user-token half of the exact library and version #9 pinned (finding 6: the v4 package split matches the #9/#10 boundary; zero rework). No usage threshold, no commercial licence (CLAUDE.md rule 4 satisfied). Eliminates all refresh-lifecycle, token-store and per-user retrieval code Cloudstrap would otherwise own. |
| `Duende.AccessTokenManagement` 4.2.0 *(transitive — already CPM-pinned by #9)* | Apache-2.0 | Base token-management services. |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` ≥ 10.0.4 *(transitive)* | MIT | The stock protocol handler — the founding-spec replacement target itself. |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` ≥ 8.0.1 `< 9.0.0` *(transitive)* | MIT | ⚠️ Same 8.x-not-9.x family constraint #9 recorded for `System.IdentityModel.Tokens.Jwt`; with `CentralPackageTransitivePinningEnabled` a future 9.x pin breaks restore — the plan re-confirms the pin set, the README records the constraint. |

**One new CPM pin**: `Duende.AccessTokenManagement.OpenIdConnect` 4.2.0 (exact patch re-confirmed at plan time).

### Test-only (never in a shipped closure — AC-OIDC11 asserts it)

| Package | License | Evidence & justification |
|---|---|---|
| *(no new packages)* — `Cloudstrap.TestIdentityProvider` extension uses the already-present OpenIddict 7.6.0 + SQLite/EF Core set | Apache-2.0 / MIT | The auth-code, refresh and end-session features are configuration of the existing OpenIddict server, not new dependencies. D-4 keeps it this way: **no container runtime, no Docker, no Testcontainers** enters the repo. |
| `Microsoft.Playwright` *(existing)* | MIT | The E2E browser-login test (AC-OIDC12) rides the #25 harness. |

Considered and **rejected**:

- **`Microsoft.Identity.Web`** (MIT, Microsoft, active) — first-class OIDC login *for Entra ID*; MSAL-based and
  Entra-shaped. Cloudstrap's auth story is deliberately IdP-neutral (founding AC-A1). Rejected on fit (#9
  precedent).
- **`Duende.BFF`** — solves login/logout endpoints, session management and token forwarding as a product, but it
  is **commercially licensed**; CLAUDE.md rule 4 (OSI-approved only) excludes it outright.
- **`OpenIddict.Client.AspNetCore`** (Apache-2.0) — a full OIDC client stack, but it *replaces* the stock handler
  the founding spec explicitly decided on, for no capability this deliverable needs. Rejected against a standing
  decision, not on health.
- **Hand-rolled refresh in a cookie `OnValidatePrincipal` event** (the well-known ~100-line sample) — Cloudstrap
  would own refresh concurrency, stampede control, clock skew and failure semantics forever; ATM provides them
  under Apache-2.0. Fails cost-of-ownership.
- **Any `Aspire.*` package** — prohibited (AC-ASP2).

---

## Deliberate Behavior Changes (vs. the source library)

1. **Activation is an explicit call**, not `Security:EnableAuthentication` — the source's `false` branch silently
   ran without authentication.
2. **The OIDC client's credentials are its own** (`Cloudstrap:OpenIdConnect:ClientId/ClientSecret`) instead of
   being read from the shared `ClientCredentials` block, with no fallback between the two (D-3).
3. **The client secret is no longer unconditionally required** (#9's D-1 carried over); the source threw at
   startup without one.
4. **Scopes actually take effect.** The source's `ClientScopes` never bound anywhere (finding 2); the new `Scope`
   setting is validated, applied, and defaults to `openid profile offline_access`.
5. **Tokens live in the session cookie** (`SaveTokens = true`, ATM requirement) instead of the source's
   `SaveTokens = false` + hidden memory-backed distributed cache — which was single-instance-only by construction
   (finding 7). Scale-out is served by a shared Data Protection key ring, not by a token store (D-2).
6. **The hidden `AddDistributedMemoryCache()` registration is gone.**
7. **PKCE and code flow are not configurable** — `AuthenticationFlow` (including `Implicit`) stays deleted.
8. **Claim types stay as issued** (`MapInboundClaims = false` default, actually effective — the source's
   equivalent flag was configured where it never bound).
9. **Logout is RP-initiated by default** — the source's `SignOut()` cleared only the local cookie, leaving the
   IdP session alive to silently re-login on the next challenge.
10. **The login endpoint validates return URLs as local** — the source redirected to any caller-supplied URL
    (open redirect, finding 5).
11. **`IdentityModelEventSource.ShowPII` is never enabled by Cloudstrap** — the source hosts enabled it
    unconditionally in all environments.
12. **Cookie/bearer scheme coexistence is defined behavior** (bearer requests 401, browser requests redirect) —
    in the source it was buried in unreadable internal packages.
13. **Cookie attributes are hardened, named and pinned by tests** (D-1: `__Host-Cloudstrap`, HttpOnly, Secure
    always, SameSite=Lax, 8 h sliding) instead of inherited silently from framework-era defaults (a
    framework-fingerprinting name, a 14-day lifetime and `SecurePolicy=SameAsRequest`).
14. **The test IdP's interactive flows require PKCE, model users as neutral declarative data, and carry no
    personal data** — the source STS baked a real person's identity into client claim config and offered
    implicit/hybrid flows (#9 findings 11–12; the flows stay dropped).

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| `AddCloudstrapOpenIdConnect()` called, no endpoint mapped, no client flagged | Schemes registered; with `RequireAuthenticatedEndpoints` at its `true` default (D-6) an unauthenticated request is challenged to the IdP, and `[AllowAnonymous]` or the flag opts out. No token machinery runs until a flagged client is used. |
| `AddUserAccessToken: true` but this package never registered | The shipped wiring fails at client creation naming the flag and this package (already-tested #4/#9 behavior — unchanged). |
| Flagged client used from a background job / no `HttpContext` | Fail fast (AC-OIDC8); the message points at `AddClientAccessToken` as the machine-identity alternative. |
| Refresh token expired or revoked at the IdP | The refresh fails; the handler throws (no unauthenticated request). The README documents the app-level choice: catch-and-challenge, or shorten the cookie lifetime below the refresh-token lifetime so the session ends first. |
| Cookie ticket valid but tokens absent from it (consumer overrode `SaveTokens` to `false` via the configurator) | ATM returns failure → handler throws with a message naming `SaveTokens`; the configurator docs warn that this override breaks token management. |
| Second app instance without shared Data Protection keys | The cookie fails decryption → user is re-challenged (visible, not corrupt); README names `AddCloudstrapDataProtection` as the fix. |
| Both `AddUserAccessToken` and `AddClientAccessToken` on one client | Both handlers, user first (AC-CC13) — now with two real providers. |
| `TokenRequestParameters:SignInScheme`/`ChallengeScheme` set on a **user**-flagged client | Honored — mapped onto Duende's `UserTokenRequestParameters` (finding 10). #9's warning keeps firing only for purely client-flagged clients. |
| The IdP omits an `end_session_endpoint` from discovery | Local sign-out still completes; the OIDC handler simply cannot redirect to the IdP; logged once. |
| Consumer maps their own login/logout instead of `MapCloudstrapAuthenticationEndpoints()` | Fully supported — the mapper is opt-in sugar; `Challenge`/`SignOut` against the registered schemes is the contract. |
| `Cloudstrap:OpenIdConnect` present but the package not referenced | Nothing happens — an unread section is not an error (suite convention). |
| Registration called twice | No-op second call (AC-OIDC10, the #9 marker pattern). |
| Absolute non-local `returnUrl` passed to the login endpoint | Rejected; the challenge proceeds with the default local redirect (`/`). Open-redirect closed (finding 5). |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace,
  ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- **Inbound JWT validation** (#5) and **machine-to-machine token acquisition** (#9) — both shipped; this package
  registers neither. AC-OIDC9 *composes* with #5; it does not modify it.
- **Blazor browser-auth helpers** — `BffAuthenticationStateProvider`, XSRF, Refit clients and the user-info
  endpoint contract (`GetUser`) are **#13**; Blazor Server circuit token plumbing (including Duende's
  `AddBlazorServerAccessTokenManagement<TStore>`) is **#12**.
- **Front-channel and back-channel logout endpoints** and IdP-initiated logout notification handling —
  demand-driven post-v1; RP-initiated logout ships (AC-OIDC5), the rest is documented as not implemented.
- **Server-side session/ticket stores** and any distributed token store (D-2) — an additive future via Duende's
  store extension points; not built here.
- **Multiple OIDC schemes, external-provider composition (social logins), multi-tenant authority resolution.**
- **Consent, registration, profile or account-management UI** — the source's `Authentication.UI` parity beyond
  login/logout is dropped (D-5 scopes the two survivors).
- **DPoP, mTLS, `private_key_jwt` modelled in options** — reachable via `configurator` hooks and Duende/stock
  extension points (#9's D-1 posture); no Cloudstrap credential-modelling type.
- **Refresh-token revocation on logout** — reachable via Duende's API from a consumer's logout endpoint;
  evaluated on demand, not defaulted here.
- **A containerized IdP (Keycloak or otherwise), Docker or Testcontainers in the test suite** — excluded by D-4;
  verification against a third-party IdP is a documented manual README procedure only.
- Everything **Dropped** in the Port Decision Table: the `EnableAuthentication` gate, `AuthenticationFlow` and
  its enum, `SaveTokens`/`UseTokenLifeTime` toggles, `IdentityTokenCacheLifetime`, `RefreshTokenCacheLifetime`,
  `UseDefaultClaimTypeMapping` (as named), the nested settings graph, `AddDistributedMemoryCache()`,
  `AddNihdiPolicyAuthorization`, `ShowPII`, committed secrets, the `UsePKCE` opt-out, the `Riziv-Inami` header
  and the StyleCop reference.

---

## Decision Log (gate answers, 2026-08-08 — zero Open Questions remain; spec is planner-ready)

> **Status: provisional.** Each row records this spec's recommended option, accepted on 2026-08-08 so planning
> could start. **The user has not yet confirmed any of them.** Confirm D-1…D-6 at the plan's first 🛑 HUMAN GATE;
> D-1 and D-2 are one-way doors and D-4 additionally needs the founding-spec AC-A1 amendment.

All six questions were answered by the user on 2026-08-08, each **accepting this spec's recommended option (a)**.
Evidence is compressed to what a future reader needs to see why the decision landed where it did; the full
reasoning that produced each recommendation is in the findings and tables above. A bare `D-…` below is *this*
spec's id; `#9's D-…` refers to `_specs/9-ClientCredentialsAuth.md`.

| # | Question | Answer (user, 2026-08-08) |
|---|---|---|
| **D-1** | ⚠️ What are the "secure cookie defaults" concretely? *(the deliverable's central one-way door — the source pinned nothing: framework defaults meant a `.AspNetCore.Cookies` name, a 14-day lifetime and `SecurePolicy=SameAsRequest`, and its only cookie setting, `UseTokenLifeTime`, had zero readers)* | **Option (a), as recommended — the hardened set.** Cookie name **`__Host-Cloudstrap`** (the `__Host-` prefix makes the browser itself enforce Secure + `Path=/` + no `Domain`), **`HttpOnly` always**, **`SecurePolicy=Always`**, **`SameSite=Lax`** (Strict breaks the OIDC callback navigation), **8 h sliding** expiration. `Cookie:Name`, `Cookie:Lifetime` and `Cookie:SlidingExpiration` are configuration; the hardened trio (HttpOnly / Secure / SameSite) is reachable **only in code** via `configurator.Cookie`, so weakening the posture is a visible act in `Program.cs` rather than config drift. **`UseTokenLifeTime` stays dropped** — session length is an explicit application decision, not an IdP-chosen token property. Covered by AC-OIDC2; the E2E run proves loopback `Secure` cookies work in Chromium. |
| **D-2** | Where do user tokens live, and what is the multi-instance posture? *(source: `SaveTokens=false` + a hidden `AddDistributedMemoryCache()` — a server-side store that silently broke on instance two, finding 7)* | **Option (a), as recommended — cookie-stored.** Tokens live in the authentication session (the cookie ticket) with `SaveTokens = true`, which is Duende ATM's default **and** its requirement. **No server-side ticket store, no distributed token store and no cache registration ship**; Cloudstrap owns zero storage code. Multi-instance works through a shared Data Protection key ring — #4's `AddCloudstrapDataProtection` is documented as the required companion, together with the symptom of forgetting it (users bounced to login on the second instance). Trade-off recorded in the README: a larger cookie carrying encrypted tokens. **`IdentityTokenCacheLifetime` is confirmed dropped** (no server-side cache exists for it to govern). Server-side stores remain an additive future via Duende's extension points; the **Blazor Server circuit store is routed to #12**. |
| **D-3** | Own credentials and section, or shared with `Cloudstrap:ClientCredentials`? And the scope default? *(the source shared one `ClientId`/`ClientSecret` between interactive login and the M2M grant, finding 3)* | **Option (a), as recommended — own flat section, no fallback.** `Cloudstrap:OpenIdConnect` carries its **own required `ClientId`** and **optional `ClientSecret`** (#9's D-1 posture: any configuration provider — KeyVault, environment, user-secrets — never `appsettings.json`, and secret-free client authentication stays representable). **No fallback to `Cloudstrap:ClientCredentials`**: a silent fallback couples two shipped packages through one configuration node (what #9's D-2 rejected) and turns a missing-key typo into authenticating as the wrong client. A consumer genuinely reusing one IdP registration duplicates two values or maps one environment variable twice. Default **`Scope = "openid profile offline_access"`** — `offline_access` makes refresh work out of the box on mainstream IdPs, and the README documents removing it where an IdP treats it specially. |
| **D-4** | ⚠️ AC-A1's verification vehicle: extend the in-repo IdP, or add the founding spec's Keycloak container? | **Option (a), as recommended — extend the in-repo OpenIddict test identity provider.** #9's D-5 project grows authorization-code + PKCE (PKCE **required**, no opt-out), refresh, `/connect/userinfo`, `/connect/logout` and a minimal login page over neutral, config-declared test users — an extension, never a replacement. **No Keycloak, no Docker, no Testcontainers enters the test suite**; verifying against Keycloak, Entra or any other conformant IdP is a **documented manual README procedure** (configuration only). ⚠️ **This amends founding spec `_specs/Cloudstrap.md` AC-A1's parenthetical "(test: Keycloak container)" to name the in-repo IdP — accepted for this deliverable, pending the user's confirmation of the founding-spec edit, which only the user makes and this spec does not.** |
| **D-5** | Which login/logout surface ships (`Authentication.UI` parity scoping)? *(the internal UI package is unreadable; the observed consumer wrote its own 20-line controller — with an open redirect and a local-only logout, finding 5)* | **Option (a), as recommended — opt-in, exactly two endpoints.** `MapCloudstrapAuthenticationEndpoints()` maps **login** (`/account/login`, default) and **logout** (`/account/logout`, default) and nothing else; both paths are configurable, login accepts **local return URLs only** (closing the source's open redirect), and logout is **RP-initiated** — signing out of the cookie *and* OIDC schemes, so the IdP session ends too. The mapper is opt-in: a consumer may ignore it and call `Challenge()`/`SignOut()` from their own endpoints. **User-info is routed to #13** (the BFF auth-state contract), and **front-channel/back-channel logout, consent, registration and account-management UI are out of scope**. |
| **D-6** | Endpoint-protection default for an interactive app: fallback require-authenticated on or off? | **Option (a), as recommended — `RequireAuthenticatedEndpoints = true`.** Same flag name, same default and the same two opt-outs as #5's shipped posture: `[AllowAnonymous]` for one endpoint, the flag for the whole application. A public landing page is therefore one attribute away, and the suite never ships two auth packages with opposite postures behind one flag name. Health probes and the OpenAPI/Scalar endpoints stay anonymous (mapped so by #4/#5). |
