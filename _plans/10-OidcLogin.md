# Plan: 10-OidcLogin — A consumer references one package and calls `AddCloudstrapOpenIdConnect()`, and their app has interactive OpenID Connect login: auth-code + PKCE against any standards-compliant identity provider, a hardened `__Host-Cloudstrap` session cookie, transparent user-token refresh, and every typed `HttpClient` flagged `AddUserAccessToken: true` calling downstream APIs as the signed-in user

## Overview

Deliverable #10 of the extraction roadmap: the new **`Cloudstrap.Authentication.OpenIdConnect`** package — the
user-token half of the seam #9 left open — plus the **extension of the in-repo test identity provider** with the
interactive flows that make it the verification vehicle for AC-A1 (**D-4**). **Binding spec:
`_specs/10-OidcLogin.md`** (planner-ready 2026-08-08; Decision Log **D-1** hardened cookie defaults · **D-2**
cookie-stored tokens · **D-3** own credentials + own flat section + scope default · **D-4** extend the in-repo
IdP instead of a Keycloak container · **D-5** opt-in login + logout endpoints only · **D-6**
`RequireAuthenticatedEndpoints = true`). Its Port Decision Table (6 Port · 12 Redesign · 5 Replace · 17 Drop ·
3 Superseded · 1 Routed = 44 rows), Public API Sketch (all three parts), Behaviors & Conventions table,
Deliberate Behavior Changes, Edge Cases and Out of Scope list are authoritative and **not re-litigated here**.

> ⚠️ **D-1…D-6 are provisional.** The spec accepted its own recommended option (a) for each so that planning
> could proceed; **the user has not confirmed any of them**. Approving *this plan* is the user's first
> opportunity to object; the formal confirmation is the first item of **🛑 HUMAN GATE 1**, which runs before a
> single line of the package exists. **D-1 (cookie defaults) and D-2 (token storage) are one-way doors**, and
> **D-4 additionally requires an amendment to founding spec `_specs/Cloudstrap.md` AC-A1's parenthetical
> "(test: Keycloak container)"** — an edit only the user makes; neither the spec nor this plan makes it.

Nothing the spec marked **Drop** appears in this plan: no `Security:EnableAuthentication` gate, no
`NihdiConfiguration`/`ILogger` parameters, no unconditional `ClientSecret` requirement, no
`AddDistributedMemoryCache()`, no `AddNihdiPolicyAuthorization` equivalent, no `AuthenticationFlow` enum and no
implicit or hybrid flow anywhere (including in the test IdP), no `SaveTokens`/`UseTokenLifeTime` toggles, no
`IdentityTokenCacheLifetime`, no `RefreshTokenCacheLifetime`, no `UseDefaultClaimTypeMapping` under that name,
no nested `Security:Authentication` settings graph, no fallback from `Cloudstrap:OpenIdConnect` to
`Cloudstrap:ClientCredentials`, no `IdentityModelEventSource.ShowPII`, no committed real-looking secrets, no
per-client `UsePKCE` opt-out, no `Riziv-Inami` header, no StyleCop reference — and nothing from the Out of Scope
list: no inbound JWT validation (#5's), no client-credentials acquisition (#9's), no `BffAuthenticationStateProvider`
/ XSRF / user-info endpoint (**#13**), no Blazor Server circuit token store (**#12**), no front-/back-channel
logout, no server-side ticket or distributed token store, no multi-scheme or multi-tenant OIDC, no consent /
registration / account UI, no DPoP/mTLS/`private_key_jwt` option modelling, no refresh-token revocation on
logout, and no container, Docker or Testcontainers anywhere in the test suite.

### Reference patterns, all read in full before planning

- **The shipped seam this deliverable fills**: `src/Cloudstrap.Extensions/IUserAccessTokenHandlerProvider.cs`
  (the exact surface — `DelegatingHandler CreateUserTokenHandler(string clientName, TokenRequestOptions?)`,
  its XML-doc contract: lazy resolve at pipeline-build time, a fresh handler per client, never a pre-set
  `InnerHandler`, and the fail-fast that already names *this* package),
  `IClientAccessTokenHandlerProvider.cs`, `AccessTokenHandlerWiring.cs` (independent resolution of each seam,
  **user handler inserted at position 0, client handler after it**, the per-flag failure message),
  `src/Cloudstrap.Core/HttpClientServiceOptions.cs` + `TokenRequestOptions.cs` (`AddUserAccessToken`,
  `TokenRequestParameters` with `Scope`/`Resource`/`SignInScheme`/`ChallengeScheme`/`ForceRenewal` — the two
  scheme members #9 reserved for this deliverable), `src/Cloudstrap.Extensions/DataProtectionOptions.cs`
  (`Cloudstrap:DataProtection` — the multi-instance companion D-2's README section names).
- **The sibling auth package, structure-for-structure** (`src/Cloudstrap.Authentication.ClientCredentials/`):
  `ServiceCollectionExtensions.cs` (the `RegistrationMarker` idempotence idiom, `AddOptions<T>().BindConfiguration(...).ValidateOnStart()`,
  configurator hooks applied last, the hosted-service startup logger), `CloudstrapClientCredentialsOptions.cs`
  (`const SectionName`, `[Required]` messages naming full keys), `CloudstrapClientCredentialsOptionsValidator.cs`
  (source-generated `[OptionsValidator]` **plus** a hand-written shape validator — the split this plan reuses),
  `CloudstrapClientCredentialsConfigurator.cs`, `ClientCredentialsAccessTokenHandlerProvider.cs` (the seam-filling
  provider shape and its one-time warnings), `ClientCredentialsTokenHandler.cs` (⚠️ **the handler this plan
  amends** — see plan-level pick 3), `TokenRequestParameterMapper.cs`, `ClientCredentialsLog.cs`
  (source-generated `[LoggerMessage]`, never a value), the `.csproj` (packaging properties, `InternalsVisibleTo`)
  and `README.md`.
- **The test identity provider this deliverable extends** (`src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/`):
  `TestIdentityProviderOptions.cs` / `TestIdentityProviderClient.cs` / `TestIdentityProviderClaims.cs` (the
  options shape #9's D-5 fixed — `IList<>`/`IDictionary<string, IList<string>>`, neutral `contoso-*` fixtures),
  `ServiceCollectionExtensions.cs` (OpenIddict server: `AllowClientCredentialsFlow`, `SetTokenEndpointUris`,
  ephemeral keys, `DisableAccessTokenEncryption`, `EnableTokenEndpointPassthrough`,
  `DisableTransportSecurityRequirement`; per-instance open `SqliteConnection` to `Data Source=:memory:`),
  `TestIdentityProviderSeeder.cs` (`OpenIddictApplicationDescriptor` permissions), `EndpointRouteBuilderExtensions.cs`
  (the sign-in handler, claim stamping, `SetDestinations`), `TestIdentityProviderServerOptionsConfigurator.cs`,
  `TestIdentityProviderHost.cs` (`StartInProcess`/`StartLoopback`, `CreateHandler`, `TokenRequestCount`).
- **Coexistence partner**: `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` `AddCloudstrapJwtBearer`
  (`AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` — **it sets the default scheme**,
  which is precisely the collision AC-OIDC9 governs — plus the `RequireAuthenticatedEndpoints` fallback policy
  and the `EnvironmentDefault.Resolve` HTTPS-metadata pattern this package copies) and
  `CloudstrapJwtBearerOptions.cs`.
- **Demonstration harness (verified on disk)**: `src/Test/WasmTestProject/src/Host/Bff/Program.cs` (the "Interactive
  user login arrives with deliverable #10" comment this plan redeems), `appsettings.json`
  (`Cloudstrap:JwtBearer` with `RequireAuthenticatedEndpoints: false`, `Cloudstrap:ClientCredentials`,
  `Cloudstrap:HttpClients:SelfApi`), `Controllers/MachineController.cs` + `Services/ISelfApiClient.cs` /
  `SelfApiClient.cs`, `Contracts/MachineStatusDto`, `test/Cloudstrap.WasmTestProject.E2E.Tests/E2eFixture.cs`
  (IdP on loopback **5310** booted before the Bff, `IdentityProviderTokenRequestCount`, attach mode),
  `Infrastructure/PageTestBase.cs` (headless Chromium, fresh context per test, console-error collection),
  `ClientCredentialsTests.cs`, and the **31 pre-existing E2E tests** (counted on disk: 3+3+3+3+3+4+1+1+10)
  — the AC-OIDC12 baseline.

This is a library deliverable with no database and no UI of its own, so the plan template's endpoint-integration
block does not apply literally. Its equivalent here is that **every package step boots a real host and drives a
real OpenID Connect sign-in against the extended test identity provider in-process** — a genuine authorization
endpoint, login form, `form_post` callback, token endpoint and JWKS, with no sockets — plus the mandatory E2E
demonstration slice (Step 10) where a real Chromium performs the login against the IdP on loopback.

### Standing pre-release rule

Nothing is published yet, so a step that needs a breaking or behavioral change to an already-shipped package
**fixes it at the source** rather than working around it. The spec proposes none (the D-4 seam, `TokenRequestOptions`
and #9's surface all fit as designed). This plan finds exactly **one** — plan-level pick 3 below — and it is
reviewed at the gate that covers the step making it.

### The plan-level picks the spec left open (committed here, reviewable at the named gates)

1. **A browserless user agent drives the sign-in in package tests** (test infrastructure, Gate 2). The spec's test
   strategy says "drive the full code+PKCE round trip with a cookie-aware `HttpClient`". Concretely:
   `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/BrowserlessUserAgent.cs` —
   routes each request to the right in-process `TestServer` **by authority**, keeps a per-host cookie jar that
   honors `Secure`/`HttpOnly`/`Path`, follows 302s, and auto-submits two HTML forms: the IdP's login form and
   the `form_post` callback form. The two hosts get explicit, unambiguous base addresses —
   **`https://app.example.com/`** (the application) and **`https://idp.example.com/`** (the provider) — so
   routing is deterministic and `Secure` cookies behave exactly as a browser would make them behave. This needs
   one additive, test-only parameter on `TestIdentityProviderHost.StartInProcess(..., Uri? baseAddress = null)`.
2. **Token-renewal clock strategy: short real lifetimes, not a fake clock** — #9's plan-level pick 2 carries over
   verbatim and for the same reason (a faked clock would have to be honored coherently by Duende ATM, by the
   cookie ticket's stored expiry **and** by OpenIddict). Step 6 sets the IdP's `AccessTokenLifetime` to 1–2 s and
   zeroes ATM's refresh-before-expiry buffer through `configurator.TokenManagement`, then waits past expiry
   (bounded ≤ ~3 s, retry-polled, not sleep-flaky). `Microsoft.Extensions.TimeProvider.Testing` is **not** added.
3. ⚠️ **"User first" is made to mean "the user's token is the one that arrives"** — **one behavior fix in shipped
   `Cloudstrap.Authentication.ClientCredentials`** (Step 5, reviewed at Gate 3). As shipped,
   `ClientCredentialsTokenHandler.SendWithTokenAsync` assigns `request.Headers.Authorization` unconditionally, so
   on a client flagged **both** ways the inner client handler overwrites the outer user handler's header and the
   *machine* token reaches the peer — making the seam's user-first ordering unobservable and, worse, silently
   wrong. The fix is two lines of intent: the client-credentials handler **leaves an `Authorization` header
   another handler already set alone** (and says so once in its XML docs and README). Permitted by the standing
   pre-release rule; the alternative (keep the clobber and document that the machine token wins) is presented at
   Gate 3 for the user to direct instead.
4. **The default schemes are set deterministically, not by call order** (Gate 2). Both `AddCloudstrapJwtBearer`
   and `AddCloudstrapOpenIdConnect` want to name a default scheme, and stock `AddAuthentication(string)` is
   last-call-wins. This package sets `AuthenticationOptions.DefaultScheme` / `DefaultChallengeScheme` /
   `DefaultSignOutScheme` through a **`PostConfigure`**, so the specified coexistence posture (cookie default,
   OIDC challenge, bearer by header — AC-OIDC9) holds whichever order the consumer registered the two packages
   in. Both orders are tested in Step 7.
5. **The SUT demo drives a second typed client, `UserApi`, not the existing `SelfApi`** (Gate 5). AC-OIDC12's
   parenthetical suggests flagging `SelfApi` with `AddUserAccessToken`; the same criterion also requires the
   **31 pre-existing E2E tests to stay green**, and the two cannot both hold: `SelfApi` is driven from
   *anonymous* endpoints (`api/diagnostics/outbound`, `api/v1/machine/call`), where AC-OIDC8 mandates a fail-fast
   with no request. A second client at the same base address, flagged **both** `AddUserAccessToken` and
   `AddClientAccessToken`, demonstrates strictly more (it is AC-CC13 live) and disturbs nothing. Recorded as a
   deviation from the AC's parenthetical, for confirmation at the final gate.
6. **No new port.** The E2E fixture's identity provider stays on **`http://127.0.0.1:5310`** and simply gains a
   second, interactive client and one test user.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-A1 (auth-code + PKCE completes; tokens managed/refreshed by ATM — **against the extended in-repo IdP, D-4**) | 1–2 (vehicle) + 3 (flow) + 6 (refresh) + 10 (live) |
| AC-A3 (zero `Nihdi.AspNetCore` — must stay green) | 9 (permanent guard) |
| AC-ASP2 (zero `Aspire.*` in any shipped closure) | 9 |
| AC-CC13 (both flags on one client → both handlers, **user first** — re-proven with two real providers) | 5 (+ live in 10) |
| AC-OIDC1 (challenge carries `response_type=code`, `code_challenge` + `S256`, `state`, `nonce`; no secret in the URL) | 3 |
| AC-OIDC2 (`__Host-Cloudstrap` cookie: HttpOnly, Secure, SameSite=Lax, 8 h sliding; claims as issued; returned to the original local URL) | 3 (+ live in 10) |
| AC-OIDC3 (flagged client carries *that user's* token; two users never cross) | 5 |
| AC-OIDC4 (expired access token + valid refresh → exactly one refresh grant, no re-challenge) | 6 |
| AC-OIDC5 (logout ends the local session **and** the IdP session; next request anonymous) | 4 (+ live in 10) |
| AC-OIDC6 (startup fails naming the exact offending key, no value echoed) | 3 |
| AC-OIDC7 (no secret / code / token / PII in logs, telemetry, problem details or exception text; `form_post`; `ShowPII` never set) | 8 |
| AC-OIDC8 (flagged client with no signed-in user → fail fast naming the flag; **no request sent**) | 5 |
| AC-OIDC9 (bearer request → 401 via the JWT scheme; browser request → challenge; the #9 E2E test stays green) | 7 (+ live in 10) |
| AC-OIDC10 (registration idempotent) | 3 |
| AC-OIDC11 (hygiene: build/tests/format, XML docs, metadata, closure, forbidden-identifier sweep) | 9 |
| AC-OIDC12 (SUT demo: browser login E2E + the 31 pre-existing E2E tests green) | 10 |

### New CPM entries (`src/Directory.Packages.props` — transitive pinning is on; the executor verifies each exact stable version on nuget.org at pin time and reports any deviation at the covering gate)

| Package | Version | License | Closure | Step |
|---|---|---|---|---|
| `Duende.AccessTokenManagement.OpenIdConnect` | 4.2.0 (spec-verified 2026-08-06; re-confirm the current 4.2.x patch at pin time) | Apache-2.0 | **runtime** | 3 |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | 10.0.10 — match the repo's `Microsoft.*` 10.0.x family (ATM OIDC requires ≥ 10.0.4) | MIT | **runtime** (transitive, pinned) | 3 |

Transitive-pinning consequences the executor resolves **by pinning, never by disabling the setting**, reported at
Gate 2: ⚠️ `Microsoft.IdentityModel.Protocols.OpenIdConnect` must stay **≥ 8.0.1 and < 9.0.0** — the same
`Microsoft.IdentityModel` 8.x family constraint #9 recorded for `System.IdentityModel.Tokens.Jwt`, which #5's
JwtBearer already brings, so they agree today and a future 9.x pin breaks restore. The Step 9 README records the
constraint next to #9's. `Duende.AccessTokenManagement` 4.2.0 is already pinned by #9 and is not re-pinned.
**No new test-only packages**: the IdP extension is configuration of the OpenIddict 7.6.0 + EF Core SQLite set
already present (D-4), and the E2E test rides the existing `Microsoft.Playwright` harness.

### ⚠️ Risk areas (spec header; reviewed at the gates named)

- **⚠️ Auth risk area — every gate that touches it is an explicit human review** (CLAUDE.md). Because this whole
  deliverable *is* auth, **all four intermediate gates plus the final gate are auth reviews**: Gate 1 (the
  verification vehicle + the D-1…D-6 confirmation), Gate 2 (public API, cookie posture, new dependency), Gate 3
  (user tokens, the shipped-#9 fix, refresh), Gate 4 (coexistence, leak hygiene, closure), Gate 5 (the live flow).
- **⚠️ Cookie/session security defaults are a one-way door (D-1)** — `__Host-Cloudstrap`, HttpOnly, Secure always,
  SameSite=Lax, 8 h sliding, with the hardened trio reachable only in code: **Gate 1** (confirm the decision)
  and **Gate 2** (confirm the as-built defaults).
- **⚠️ Token storage is a one-way door (D-2)** — tokens in the cookie ticket, no store of any kind shipped:
  **Gate 1** (decision) and **Gate 2** (as-built).
- **⚠️ Tokens, secrets and authorization codes must never reach logs or telemetry** — a sign-in puts codes on the
  wire while #2's enrichers and #5's problem-details handler sit in the request path: **Gate 4** (AC-OIDC7).
- **⚠️ Public API one-way door** — `AddCloudstrapOpenIdConnect`, `MapCloudstrapAuthenticationEndpoints`,
  `CloudstrapOpenIdConnectOptions`, `CloudstrapAuthenticationCookieOptions`, `CloudstrapOpenIdConnectConfigurator`,
  `CloudstrapOpenIdConnect`: **Gate 2** (signatures reviewed verbatim against the spec's Public API Sketch §1).
- **⚠️ New runtime dependency** `Duende.AccessTokenManagement.OpenIdConnect` 4.2.0 (Apache-2.0 — rule-4 review):
  **Gate 2**.
- **⚠️ Behavior change to shipped `Cloudstrap.Authentication.ClientCredentials`** (plan-level pick 3): **Gate 3**.
- **⚠️ The test-IdP extension is itself one-way-door work** — it becomes deferred #26's source material and
  #12/#17's test infrastructure, extending the shape #9's D-5 fixed: **Gate 1**.
- **Aspire**: no overlap — interactive auth is outside ServiceDefaults' remit; AC-ASP2 carried as a closure
  tripwire (Step 9); the user-token handler joins the pipeline through the same seam #4/#9 proved leaves
  `ConfigureHttpClientDefaults` resilience untouched (AC-ASP3 posture).

### Planner mechanics decided here (no spec conflict; each flagged for review at the named gate)

**(a) Library-API confirmations the executor makes during RED and reports at the covering gate** (the plan-5/plan-9
mechanic: outcomes are fixed, exact member names are confirmed against the installed package):

1. **OpenIddict 7.6.0 interactive surface** — `AllowAuthorizationCodeFlow()`, `AllowRefreshTokenFlow()`, the
   server-wide PKCE requirement, `SetAuthorizationEndpointUris` / `SetUserInfoEndpointUris` /
   `SetEndSessionEndpointUris` (the 6.x+ names for userinfo and logout), the `UseAspNetCore()` passthrough
   toggles for each, `RefreshTokenLifetime`/`AuthorizationCodeLifetime`, and the per-client descriptor members
   (`RedirectUris`, `PostLogoutRedirectUris`, `ConsentTypes.Implicit`, the PKCE requirement, the
   authorization/logout endpoint and response-type permissions) plus the `Destinations` split that decides
   which claims land in the id token versus `/connect/userinfo`. Confirmed in Steps 1–2 RED. *(Gate 1.)*
2. **Stock `OpenIdConnectOptions` defaults this package leans on and pins** — `UsePkce` (true),
   `ResponseType` (`code`), `ResponseMode` (`form_post`), `PushedAuthorizationBehavior` (`UseIfAvailable`),
   `SaveTokens`, `MapInboundClaims`, `TokenValidationParameters.NameClaimType`/`RoleClaimType`, `CallbackPath`,
   `SignedOutCallbackPath`, `Scope` (a collection — cleared before applying the configured space-delimited
   string, so the stock defaults cannot silently append), and the sign-out event that carries
   `post_logout_redirect_uri`. Confirmed in Steps 3–4 RED. *(Gate 2.)*
3. **Duende ATM OIDC 4.2.0** — `AddOpenIdConnectAccessTokenManagement()`; `IUserTokenManager.GetAccessTokenAsync(ClaimsPrincipal, UserTokenRequestParameters?, CancellationToken)`
   returning `TokenResult<UserToken>`; the `UserTokenRequestParameters` members that receive
   `TokenRequestOptions`' five settings (`Scope`, `Resource`, `ForceTokenRenewal`, **`SignInScheme`**,
   **`ChallengeScheme`** — spec finding 10); `UserTokenManagementOptions`' refresh-before-expiry knob (the lever
   plan-level pick 2 zeroes); and **how the refresh backchannel resolves its `HttpClient`** (the OIDC handler's
   `Backchannel` versus a named client), because the test host must route it to the in-process IdP. Confirmed in
   Steps 3, 5 and 6 RED. *(Gates 2 and 3.)*

**(b) Test strategy.** New project `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests` (NUnit 4 on
Microsoft.Testing.Platform, standard MTP wiring inherited from `src/Test/Directory.Build.props`), with
`ProjectReference`s to the package, `Cloudstrap.TestIdentityProvider`, `Cloudstrap.Authentication.ClientCredentials`
(Step 5) and `Cloudstrap.WebApi` (Step 7), plus `Microsoft.AspNetCore.TestHost`. Its fixture idiom
(`Infrastructure/OidcTestHost.cs`, the `ClientCredentialsTestHost` pattern): a `WebApplication` on `TestServer` at
`https://app.example.com/` with in-memory `Cloudstrap:` configuration, the **real shipped**
`AddCloudstrapHttpServiceClient` with a `CapturingPrimaryHandler`, the extended IdP started in-process at
`https://idp.example.com/`, its handler wired as the OIDC backchannel through `configurator.OpenIdConnect`, and
`BrowserlessUserAgent` (plan-level pick 1) performing sign-in. Assertions are made on the challenge URL's query,
the issued `Set-Cookie` header, the captured outbound request (headers and the decoded JWT's `sub`/`client_id`/
`aud`/`scope`), the IdP's endpoint counters and a capturing `ILoggerProvider`. All fixtures neutral: `contoso-*`
client ids, `contoso.user` test users, `example.com` addresses, obvious placeholders like
`"placeholder-not-a-real-secret"` and `"placeholder-not-a-real-password"`.

**(c) Validators.** `CloudstrapOpenIdConnectOptionsValidator` — source-generated `[OptionsValidator]` for the
attribute rules (`[Required] Authority`, `[Required] ClientId`), per the inherited fact that this repo uses no
`Microsoft.Extensions.Options.DataAnnotations` — plus a small hand-written `internal sealed
IValidateOptions<CloudstrapOpenIdConnectOptions>` for the parse-shaped rules (`Authority` must be **absolute**;
`Cookie:Lifetime` must be greater than zero) — the #5/#9 split. Every message names the full
`Cloudstrap:OpenIdConnect:*` key and **never echoes a configured value** (AC-OIDC6, AC-OIDC7). *(Gate 2.)*

**(d) Idempotency (AC-OIDC10).** `AddCloudstrapOpenIdConnect` uses the #9 `RegistrationMarker` + `TryAdd`
semantics: one authentication builder, one cookie scheme, one OIDC scheme, one options pipeline, one ATM
registration, one provider, no duplicate handlers or events when called twice. *(Gate 2.)*

**(e) `InternalsVisibleTo`** from `Cloudstrap.Authentication.OpenIdConnect` to its own test project only (the
Extensions/WebApi/#9 precedent), so the provider, handler, mapper, validator and forward selector are directly
testable. No cross-package IVT.

**(f) Full-suite VERIFY commands (environment facts).** `dotnet test` is not supported and the `runTests` alias is
**not on the agent PATH** — every VERIFY runs the built test executables directly. "**Full suite**" below means
all of (Debug paths; the new one appears from Step 3):

```powershell
src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe
src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe
src\Test\UnitTest\Cloudstrap.Observability.AzureMonitor.Tests\bin\Debug\net10.0\Cloudstrap.Observability.AzureMonitor.Tests.exe
src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe
src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe
src\Test\UnitTest\Cloudstrap.TestIdentityProvider.Tests\bin\Debug\net10.0\Cloudstrap.TestIdentityProvider.Tests.exe
src\Test\UnitTest\Cloudstrap.Authentication.ClientCredentials.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.ClientCredentials.Tests.exe
src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe   # from Step 3
src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe
```

plus `dotnet build src/Cloudstrap.sln` (zero warnings/errors) and
`dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

---

## Slice 1 — D-4: the in-repo identity provider can log a human in

---

## Step 1 — A test signs a configured user in at the test identity provider and comes away with id, access and refresh tokens from a real authorization-code + PKCE round trip — and a code request without PKCE is refused (D-4; the AC-A1 verification vehicle)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(test-only; extends `Cloudstrap.TestIdentityProvider` in place — #9's D-5 "extend, never replace" one-way door honored)*:
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderOptions.cs` *(modify)* —
  add `RefreshTokenLifetime : TimeSpan` (default 30 minutes — short; AC-OIDC4's lever shortens it further) and
  `Users : IList<TestIdentityProviderUser>` (spec sketch §3).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderUser.cs` *(create)* —
  `Username : string`, `Password : string` (obvious placeholders only),
  `Claims : IDictionary<string, IList<string>>` — neutral, declarative test users, no personal data
  (Deliberate Behavior Change 14).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderClient.cs` *(modify)* —
  add `RedirectUris : IList<Uri>` and `PostLogoutRedirectUris : IList<Uri>` (lists, not the source's single
  value).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderClaims.cs` *(modify)* —
  add the two claim sets #9 routed here: `IdToken` and `UserInfo`.
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/ServiceCollectionExtensions.cs` *(modify)* —
  next to the existing client-credentials grant: `AllowAuthorizationCodeFlow()`, the **server-wide PKCE
  requirement** (no per-client opt-out — the `UsePKCE` flag is Dropped), `AllowRefreshTokenFlow()`,
  `SetAuthorizationEndpointUris("connect/authorize")`, the `UseAspNetCore()` authorization-endpoint passthrough,
  and a cookie authentication scheme (`TestIdentityProviderSession`) for the interactive login session.
  Implicit and hybrid flows are neither enabled nor representable.
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderSeeder.cs` *(modify)* —
  seed the interactive permissions per client (authorization endpoint, `authorization_code` + `refresh_token`
  grants, the `code` response type, the `openid`/`profile`/`offline_access` scopes, `ConsentTypes.Implicit` for
  auto-consent, the PKCE requirement) and the configured redirect URIs. A client with no `RedirectUris` keeps
  exactly today's client-credentials-only permission set.
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/EndpointRouteBuilderExtensions.cs` *(modify)* —
  map `GET/POST /connect/authorize` (if the session cookie is absent, render the **minimal login page**; on a
  valid username/password sign the user into the session cookie and complete the authorization with the
  configured claims, `Common` + `IdToken` + `AccessToken` claim sets destined appropriately) and
  `GET/POST /connect/login` (the form itself: `input[name="username"]`, `input[name="password"]`,
  `button[type="submit"]`, plus `data-testid` attributes for Playwright). One form, no account management, no
  consent UI. The existing `/connect/token` handler grows the `authorization_code` branch, signing in the
  principal carried by the code.
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderCounters.cs` *(create)* —
  a singleton with `AuthorizeRequestCount`, `RefreshTokenRequestCount` (and the existing token-endpoint count
  kept exactly as it is, so `E2eFixture.IdentityProviderTokenRequestCount` and the #9 E2E caching test are
  untouched).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderHost.cs` *(modify)* —
  surface the new counters; add the additive optional `Uri? baseAddress = null` parameter to `StartInProcess`
  (plan-level pick 1) which sets `TestServer.BaseAddress` and the advertised issuer.
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderServerOptionsConfigurator.cs`
  *(modify)* — carry `RefreshTokenLifetime` into OpenIddict's server options.
- `src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests/AuthorizationCodeFlowTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail — the honest first failure is the test project
failing to compile against the missing options members, the plan-9 precedent, then real red runs)*:
- Unit test file: `AuthorizationCodeFlowTests.cs`
  - `Discovery_Get_AdvertisesTheAuthorizationCodeAndRefreshGrantsAndRequiresPkce` — `grant_types_supported`
    contains `authorization_code` and `refresh_token` **and still** `client_credentials`; `response_types_supported`
    contains `code` and **not** `token` or `id_token token`; `code_challenge_methods_supported` is exactly
    `["S256"]`; `authorization_endpoint` is advertised.
  - `Authorize_WithoutASession_ServesTheLoginForm` — GET `/connect/authorize?...` returns 200 HTML containing
    the username/password form (no redirect loop, no consent page).
  - `Authorize_WithValidCredentials_RedirectsToTheClientRedirectUriWithACode` — posting the form for a configured
    user redirects to the client's registered redirect URI carrying `code` and the caller's `state`.
  - `TokenEndpoint_WithTheCodeAndVerifier_IssuesIdAccessAndRefreshTokens` — the `authorization_code` grant with
    the matching `code_verifier` returns `id_token`, `access_token` and `refresh_token`; the id token carries
    `sub` = the configured user, the configured `IdToken` claim set and the `nonce`; the access token is an
    unencrypted, signature-verifiable JWT carrying the client's audiences and granted scopes.
  - `TokenEndpoint_WithAMismatchedCodeVerifier_IsRejected` — standards-shaped error; no token issued.
  - `Authorize_WithoutACodeChallenge_IsRejected` — PKCE is required for every code-flow client, always
    (Deliberate Behavior Change 14; the `UsePKCE` opt-out is Dropped).
  - `Authorize_WithAnUnregisteredRedirectUri_IsRejected` — the client's `RedirectUris` list is the whitelist.
  - `WrongPassword_ReRendersTheFormAndIssuesNoCode` — no session cookie, no code.
  - `ClientCredentialsClient_WithNoRedirectUris_StillBehavesExactlyAsBefore` — the #9 regression guard: a
    client-credentials-only client's discovery-driven token request is unaffected by the new grants.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.TestIdentityProvider.Tests\bin\Debug\net10.0\Cloudstrap.TestIdentityProvider.Tests.exe --filter "AuthorizationCodeFlowTests"
  ```

**GREEN**: the Scope items (mechanic (a.1) API confirmations made during RED). XML docs throughout — this is
shared infrastructure for #10, #12, #17 and deferred #26. Every fixture value neutral; no personal data anywhere.

**DB changes**: none *(the SQLite in-memory store is process-transient test infrastructure, not a database
deliverable)*.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to Step 2)*:
1. Test exe → all pass: the repo can now perform a complete, PKCE-enforced authorization-code sign-in against a
   real OpenID Connect server in-process, with users as declarative configuration — capability that did not
   exist before, and the vehicle every remaining step verifies against (D-4).
2. Full suite (Overview mechanic (f)) + `dotnet build src/Cloudstrap.sln` (zero warnings) +
   `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 2 — The interactive session has a whole lifecycle: a refresh grant renews the access token on a short, configurable lifetime, `/connect/userinfo` returns the configured claim set, and `/connect/logout` ends the session and returns only to a registered address (D-4; the levers AC-OIDC4 and AC-OIDC5 pull)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/ServiceCollectionExtensions.cs` *(modify)* —
  `SetUserInfoEndpointUris("connect/userinfo")`, `SetEndSessionEndpointUris("connect/logout")` and their
  `UseAspNetCore()` passthroughs.
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/EndpointRouteBuilderExtensions.cs` *(modify)* —
  `GET/POST /connect/userinfo` (the `UserInfo` claim set plus `sub`, for a valid access token) and
  `GET /connect/logout` (end-session: clears the session cookie, honors `id_token_hint` +
  `post_logout_redirect_uri` against the client's registered list, redirects there); the `/connect/token`
  handler grows the `refresh_token` branch, counted by `TestIdentityProviderCounters.RefreshTokenRequestCount`.
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderSeeder.cs` *(modify)* —
  the end-session endpoint permission and `PostLogoutRedirectUris`.
- `src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests/InteractiveLifecycleTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `InteractiveLifecycleTests.cs`
  - `RefreshGrant_ExchangesTheRefreshTokenForANewAccessToken` — the refresh grant returns a **different**
    access token with a later `exp`, and `RefreshTokenRequestCount` increments by exactly one (the counter
    AC-OIDC4 asserts on).
  - `AccessTokenLifetime_Configured_DrivesTheInteractiveTokenExpiry` — `AccessTokenLifetime = 2 s` → the
    code-flow access token's `expires_in` is 2 (plan-level pick 2's lever, proven at the IdP before the package
    depends on it).
  - `RefreshTokenLifetime_Configured_ExpiresTheRefreshToken` — past the configured lifetime the refresh grant
    fails standards-shaped and issues nothing (the edge case the README's "refresh expired or revoked" row
    documents).
  - `UserInfo_WithAValidAccessToken_ReturnsTheConfiguredClaimSet` — `sub` plus exactly the client's `UserInfo`
    claims, and **not** the `IdToken`-only claims (the destination split is real).
  - `UserInfo_WithoutAToken_IsRejected` — 401, nothing echoed.
  - `EndSession_ClearsTheSessionAndRedirectsToARegisteredPostLogoutUri` — after `/connect/logout`, a fresh
    `/connect/authorize` serves the login form again (the session really ended), and the browser was redirected
    to the registered post-logout address.
  - `EndSession_WithAnUnregisteredPostLogoutUri_DoesNotRedirectThere` — the whitelist holds.
  - `Discovery_AdvertisesTheUserInfoAndEndSessionEndpoints` — both appear in the document, so a relying party
    discovers them exactly as it would at any conformant provider (AC-OIDC5's precondition).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.TestIdentityProvider.Tests\bin\Debug\net10.0\Cloudstrap.TestIdentityProvider.Tests.exe --filter "InteractiveLifecycleTests"
  ```

**GREEN**: the Scope items. No new packages — all of it is configuration of the OpenIddict server already present
(D-4: no container runtime, no Docker, no Testcontainers).

**DB changes**: none.

**VERIFY** *(when all green, mark this step's `Done` checkbox — the next plan item is a 🛑 HUMAN GATE, so stop there)*:
1. Test exe → all pass: the in-repo provider now covers the complete interactive lifecycle — sign in, refresh,
   userinfo, sign out — with every lifetime and claim set as configuration data. It is now a verification vehicle
   good enough to stand in for AC-A1's "any standards-compliant IdP".
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: ⚠️ CONFIRM THE DECISION LOG D-1…D-6, and the test identity provider's interactive shape *(covers Steps 1–2, which ran back-to-back)*

*Executor: STOP here. Present the results of both covered steps and WAIT for user approval — do not start the next step.*

⚠️ **This gate is first for a reason: the spec's Decision Log is provisional.** D-1…D-6 were accepted on the
analyst's recommendation so planning could proceed; **the user has not confirmed them**. Steps 1–2 realize D-4
only; **D-1, D-2, D-3, D-5 and D-6 govern code that does not exist yet** — confirming them here means the package
is built once, against decisions the user owns.

- [x] **D-1 confirmed — ⚠️ ONE-WAY DOOR (cookie defaults).** Cookie name **`__Host-Cloudstrap`** (the `__Host-`
  prefix makes the browser enforce Secure + `Path=/` + no `Domain`), **`HttpOnly` always**,
  **`SecurePolicy=Always`**, **`SameSite=Lax`** (Strict breaks the OIDC callback navigation), **8 h sliding**
  expiration. `Cookie:Name` / `Cookie:Lifetime` / `Cookie:SlidingExpiration` are configuration; the hardened trio
  is reachable **only in code** via `configurator.Cookie`, so weakening it is a visible act in `Program.cs`.
  `UseTokenLifeTime` stays dropped. *Changing this later changes every consumer's session semantics and cookie
  name — say so now.*
- [x] **D-2 confirmed — ⚠️ ONE-WAY DOOR (token storage).** User tokens live in the authentication session (the
  cookie ticket), `SaveTokens = true` — Duende ATM's default and requirement. **No server-side ticket store, no
  distributed token store and no cache registration ship**; Cloudstrap owns zero storage code. Multi-instance
  works through a shared Data Protection key ring (#4's `AddCloudstrapDataProtection`, documented with the
  symptom of forgetting it). Trade-off: a larger cookie carrying encrypted tokens. `IdentityTokenCacheLifetime`
  confirmed dropped; the Blazor Server circuit store is routed to #12. *Reversing this later means adding a store
  abstraction and a migration for live sessions.*
- [x] **D-3 confirmed.** Own flat `Cloudstrap:OpenIdConnect` section with its **own required `ClientId`** and
  **optional `ClientSecret`**, **no fallback** to `Cloudstrap:ClientCredentials`; default
  `Scope = "openid profile offline_access"`.
- [x] **D-4 confirmed — ⚠️ and it amends the founding spec.** *(User authorized the AC-A1 amendment at the gate; the parenthetical now names the in-repo provider.)* The in-repo OpenIddict provider (as just extended in
  Steps 1–2) is AC-A1's verification vehicle; no Keycloak, no Docker, no Testcontainers enters the test suite,
  and third-party-IdP verification is a documented manual README procedure. **Accepting this requires the user to
  amend `_specs/Cloudstrap.md` AC-A1's parenthetical "(test: Keycloak container)" to name the in-repo provider —
  an edit only the user makes; neither the spec nor this plan makes it.** Until it lands, founding AC-A1 and this
  deliverable differ on that parenthetical and nothing else.
- [x] **D-5 confirmed.** `MapCloudstrapAuthenticationEndpoints()` is opt-in and maps exactly two endpoints —
  `/account/login` (challenge, **local return URLs only**) and `/account/logout` (RP-initiated sign-out of both
  schemes). User-info is #13; front-/back-channel logout, consent, registration and account UI are out of scope.
- [x] **D-6 confirmed.** `RequireAuthenticatedEndpoints = true` — the same flag name, default and two opt-outs as
  #5's shipped posture.
- [x] Behavioral verification: the `Cloudstrap.TestIdentityProvider.Tests` output shows the full interactive
  round trip green — discovery advertising code + refresh with `S256`-only PKCE, the login form, a code, id +
  access + refresh tokens with the configured claim sets, PKCE and redirect-URI enforcement, a working refresh
  grant on a configurable lifetime, `/connect/userinfo`'s distinct claim set, `/connect/logout` really ending the
  session, and the #9 client-credentials client behaving exactly as before.
- [x] Code review (⚠️ one-way door — this project is #12/#17/#26 infrastructure): the options additions follow the
  shape #9's D-5 fixed (`IList<>` / `IDictionary<string, IList<string>>`, neutral fixtures); implicit and hybrid
  flows are neither enabled nor representable; PKCE has no per-client opt-out; the login page is one form with no
  account management or consent UI; `git diff` shows the project **extended**, never replaced; `TokenRequestCount`
  semantics are unchanged so `E2eFixture` and the #9 E2E caching test still hold; mechanic (a.1) OpenIddict API
  confirmations reported.
- [x] ⚠️ De-NIHDI review: `Select-String -Pattern '(?i)(nihdi|riziv|keycloak)'` over
  `src/Test/TestIdentityProvider` → zero matches; the test users are neutral placeholders carrying no personal
  data; no realm-style URLs (`/auth/realms/`) anywhere.
- [x] User approved — implementation may continue past this gate

---

## Slice 2 — One call, and a browser signs in: hardened cookie session over auth-code + PKCE

---

## Step 3 — `AddCloudstrapOpenIdConnect()`: an unauthenticated request is challenged with code + PKCE + `form_post`, the sign-in completes, and the caller comes back holding a hardened `__Host-Cloudstrap` session cookie whose claims are the token's own — while a misconfigured section fails startup naming the exact key and a second registration changes nothing (AC-OIDC1, AC-OIDC2, AC-OIDC6, AC-OIDC10; D-1, D-2, D-3, D-6) ⚠️ *(auth risk area; public API one-way door; new runtime dependency)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Directory.Packages.props` *(modify)* — pin `Duende.AccessTokenManagement.OpenIdConnect` 4.2.0 (runtime,
  Apache-2.0) next to #9's `Duende.AccessTokenManagement`, and pin
  `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.10 plus whatever else transitive pinning demands —
  resolved **by pinning, never by disabling the setting**, keeping `Microsoft.IdentityModel.Protocols.OpenIdConnect`
  within `[8.0.1, 9.0.0)` (Overview table).
- `src/Cloudstrap.Authentication.OpenIdConnect/Cloudstrap.Authentication.OpenIdConnect.csproj` *(create)* —
  `net10.0`, `GeneratePackageOnBuild` + `GenerateDocumentationFile`; `ProjectReference` to `..\Cloudstrap.Core\`
  and `..\Cloudstrap.Extensions\` (the seam; it also brings the `Microsoft.AspNetCore.App` framework reference
  the cookie handler needs); `PackageReference` `Duende.AccessTokenManagement.OpenIdConnect`;
  `InternalsVisibleTo Cloudstrap.Authentication.OpenIdConnect.Tests` (mechanic (e)). Description/tags/README
  metadata land in Step 9.
- `src/Cloudstrap.Authentication.OpenIdConnect/CloudstrapOpenIdConnectOptions.cs` *(create)* — spec sketch §1
  verbatim: `const string SectionName = "Cloudstrap:OpenIdConnect"`; `Authority : string` (**required**,
  absolute); `ClientId : string` (**required** — this package's own, never read from
  `Cloudstrap:ClientCredentials`, D-3); `ClientSecret : string?` (**optional**, D-3/#9's D-1);
  `Scope : string = "openid profile offline_access"` (space-delimited — deliberately a string, not a bound
  collection, so a default can be removed); `MapInboundClaims : bool = false`; `RequireHttpsMetadata : bool?`
  (null → required except `Development`, the #5 pattern); `CallbackPath : string = "/signin-oidc"`;
  `SignedOutCallbackPath : string = "/signout-callback-oidc"`; `LoginPath : string = "/account/login"`;
  `LogoutPath : string = "/account/logout"`; `RequireAuthenticatedEndpoints : bool = true` (D-6);
  `Cookie : CloudstrapAuthenticationCookieOptions`.
- `src/Cloudstrap.Authentication.OpenIdConnect/CloudstrapAuthenticationCookieOptions.cs` *(create)* — D-1:
  `Name : string = "__Host-Cloudstrap"`, `Lifetime : TimeSpan = 8 h`, `SlidingExpiration : bool = true`. Its XML
  docs state that HttpOnly / `SecurePolicy=Always` / `SameSite=Lax` are hardened constants reachable only through
  `configurator.Cookie` — deliberately not configuration values.
- `src/Cloudstrap.Authentication.OpenIdConnect/CloudstrapOpenIdConnectOptionsValidator.cs` *(create, internal)* —
  mechanic (c): the source-generated `[OptionsValidator]` partial plus the hand-written shape validator.
- `src/Cloudstrap.Authentication.OpenIdConnect/CloudstrapOpenIdConnectConfigurator.cs` *(create)* —
  `OpenIdConnect : Action<OpenIdConnectOptions>?`, `Cookie : Action<CookieAuthenticationOptions>?`,
  `TokenManagement : Action<UserTokenManagementOptions>?` — the first two stock types, all three running **last**
  with the final say (#9 precedent).
- `src/Cloudstrap.Authentication.OpenIdConnect/CloudstrapOpenIdConnect.cs` *(create)* — static;
  `const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme` and
  `const string ChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme` — **stock scheme names on purpose**,
  so unqualified `Challenge()` / `SignOut()` and ecosystem assumptions keep working.
- `src/Cloudstrap.Authentication.OpenIdConnect/ServiceCollectionExtensions.cs` *(create)* —
  `AddCloudstrapOpenIdConnect(this IServiceCollection services, Action<CloudstrapOpenIdConnectConfigurator>? configure = null) : IServiceCollection`:
  guards; **idempotent** (mechanic (d)); bind + `ValidateOnStart` the options; `AddAuthentication()` with the
  default/challenge/sign-out schemes set through `PostConfigure<AuthenticationOptions>` (plan-level pick 4);
  `AddCookie` with the D-1 defaults; `AddOpenIdConnect` with `ResponseType = code`, PKCE on, `form_post`,
  `SaveTokens = true` (D-2 — hard-set, not a toggle), `MapInboundClaims` from options, `NameClaimType = "name"`
  / `RoleClaimType = "role"`, the **cleared-then-applied** space-delimited `Scope`, `CallbackPath`,
  `SignedOutCallbackPath`, `RequireHttpsMetadata` via the #5 `EnvironmentDefault` pattern, and `ClientSecret`
  applied only when configured; Duende's `AddOpenIdConnectAccessTokenManagement()`; stock `AddAuthorization()`
  plus the require-authenticated fallback policy when `RequireAuthenticatedEndpoints` is true (D-6);
  `configurator.TokenManagement`, then `configurator.Cookie` and `configurator.OpenIdConnect` **last**. Registers
  **no** inbound JWT validation, **no** client-credentials services, **no** Blazor helpers.
- `src/Cloudstrap.Authentication.OpenIdConnect/OpenIdConnectLog.cs` *(create, internal)* — source-generated
  `[LoggerMessage]`s; no message ever carries a secret, code or token value.
- `src/Cloudstrap.Authentication.OpenIdConnect/OpenIdConnectStartupLogger.cs` *(create, internal)* — a hosted
  service stating the posture once — schemes in force, "tokens are stored in the authentication session", and
  whether the endpoints were mapped ("visible, not magic", the #9 D-3 precedent).
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Cloudstrap.Authentication.OpenIdConnect.Tests.csproj`
  *(create)* — mechanic (b).
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/OidcTestHost.cs` *(create)* —
  mechanic (b)'s fixture idiom.
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/BrowserlessUserAgent.cs`
  *(create)* — plan-level pick 1.
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/SignInTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/RegistrationTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — the package at the solution root, the test project under `Test\UnitTest`.

**RED** *(write these tests first; the honest first failure is the new test project failing to compile against
missing types — the plan-5/plan-9 precedent — then real red runs)*:
- Unit test file: `SignInTests.cs`
  - `UnauthenticatedRequest_IsChallengedWithCodePkceAndFormPost` — GET a protected path → 302 to the IdP's
    authorization endpoint whose query carries `response_type=code`, `code_challenge` with
    `code_challenge_method=S256`, `state`, `nonce`, `response_mode=form_post`, the configured `client_id` and
    `redirect_uri`; and the configured **client secret appears nowhere in the URL** (AC-OIDC1).
  - `CompletedSignIn_IssuesTheHardenedHostCookie` — the agent completes the round trip; the `Set-Cookie` header
    is named **`__Host-Cloudstrap`** and carries `HttpOnly`, `Secure`, `SameSite=Lax` and `Path=/` with **no**
    `Domain`; the ticket's expiry reflects the 8 h sliding lifetime (AC-OIDC2, D-1).
  - `CompletedSignIn_KeepsClaimTypesAsIssued` — the authenticated principal has `sub`, `name` and `role` under
    exactly those names — no legacy URI mapping — and `User.Identity.Name` resolves from `name`
    (Deliberate Behavior Change 8).
  - `CompletedSignIn_ReturnsTheUserToTheOriginallyRequestedLocalUrl` — the protected path the challenge came
    from, not the root (AC-OIDC2's last clause).
  - `CompletedSignIn_StoresTheTokensInTheAuthenticationSession` — `HttpContext.GetTokenAsync("access_token")`
    and `"refresh_token"` return values, and **no** `IDistributedCache` / server-side store is registered by
    this package (D-2's headline, asserted as the absence it is).
  - `ConfiguredScope_IsTheScopeRequested` — a custom `Scope` reaches the authorization request **exactly**, with
    no stock default silently appended (Deliberate Behavior Change 4 / the cleared-collection mechanic).
  - `SecondSignInAfterExpiry_IsChallengedAgain` — a request with no cookie is challenged rather than served
    (the fallback policy of D-6 is live).
- Unit test file: `RegistrationTests.cs`
  - `MissingSection_FailsStartupNamingTheSection` · `MissingAuthority_FailsNamingTheKey` ·
    `MissingClientId_FailsNamingTheKey` · `RelativeAuthority_FailsNamingTheKey` ·
    `NonPositiveCookieLifetime_FailsNamingTheKey` — each message contains the exact
    `Cloudstrap:OpenIdConnect:*` key (AC-OIDC6).
  - `ValidationFailure_NeverEchoesTheConfiguredSecret` — a secret is configured, another key is broken; the
    failure text does not contain the secret value (AC-OIDC6/AC-OIDC7 validation half).
  - `NoClientSecretConfigured_StartsAndChallengesNormally` — the secret is optional (D-3; the source's
    unconditional requirement is Dropped).
  - `CalledTwice_RegistersEverythingOnce` — one cookie scheme, one OIDC scheme, one ATM registration; a single
    challenge with a single `Set-Cookie`; no duplicated handler or event (AC-OIDC10).
  - `OnNullServices_ThrowsArgumentNullException` — guard clause.
  - `RequireAuthenticatedEndpointsFalse_LeavesEndpointsAnonymous` — the whole-application opt-out (D-6), the
    posture the SUT demo depends on in Step 10.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe --filter "SignInTests"
  ```

**GREEN**: the package per Scope (mechanics (a.2) and (a.3) confirmations made during RED). Full XML docs on
every public member, naming exact configuration keys. `internal` by default, `public` only for the sketch's
surface, everything sealed or static.

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to Step 4)*:
1. Test exe → all pass: an application that references one package and makes one call now performs a complete
   OpenID Connect sign-in — code + PKCE + `form_post`, a hardened `__Host-Cloudstrap` session, claims as issued,
   tokens in the ticket — against a real identity provider. Previously impossible (AC-OIDC1, AC-OIDC2, AC-OIDC6,
   AC-OIDC10).
2. Full suite + build (zero warnings) + format (exit 0).
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Authentication.OpenIdConnect.*.nupkg` appears
   (packable from day one; metadata completed in Step 9).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 4 — A signed-in user can sign out — locally *and* at the identity provider — through two opt-in endpoints whose return URLs can only be local (AC-OIDC5; D-5, closing the source's open redirect)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.OpenIdConnect/EndpointRouteBuilderExtensions.cs` *(create)* —
  `MapCloudstrapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints) : IEndpointRouteBuilder`, opt-in,
  exactly two endpoints and nothing else (D-5):
  - `GET {LoginPath}` (default `/account/login`) — `Challenge` with `RedirectUri` taken from the caller's
    `returnUrl` **only when it is a local URL**, otherwise `/`. Both endpoints are `[AllowAnonymous]` so the D-6
    fallback policy cannot make login itself require a login.
  - `GET {LogoutPath}` (default `/account/logout`) — `SignOut` of **both** the cookie and OIDC schemes
    (RP-initiated), with the same local-only return validation for `post_logout_redirect_uri`.
- `src/Cloudstrap.Authentication.OpenIdConnect/LocalReturnUrl.cs` *(create, internal, static)* — the one place
  the local-URL rule lives, with the rejected shapes in its XML docs (absolute URLs, protocol-relative `//host`,
  backslash and encoded variants).
- `src/Cloudstrap.Authentication.OpenIdConnect/ServiceCollectionExtensions.cs` *(modify)* — the startup log line
  reports whether the endpoints were mapped and at which paths.
- `src/Cloudstrap.Authentication.OpenIdConnect/OpenIdConnectLog.cs` *(modify)* — the once-only message for an
  identity provider whose discovery document omits `end_session_endpoint` (local sign-out still completes — the
  spec's edge-case row).
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/AuthenticationEndpointTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/OidcTestHost.cs` *(modify —
  map the endpoints; expose a host variant configured with the paths overridden)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `AuthenticationEndpointTests.cs`
  - `Logout_EndsTheLocalSessionAndSendsTheBrowserToTheEndSessionEndpoint` — after sign-in, GET `/account/logout`
    → the `__Host-Cloudstrap` cookie is expired **and** the response redirects to the IdP's advertised
    `end_session_endpoint` carrying `id_token_hint` and `post_logout_redirect_uri`; following it through the
    agent, the **next** request to a protected path is challenged (anonymous again) and the IdP serves its login
    form rather than silently re-authenticating (AC-OIDC5, Deliberate Behavior Change 9).
  - `Login_WithALocalReturnUrl_ComesBackToIt` — `/account/login?returnUrl=/protected/page` lands there after
    sign-in.
  - `Login_WithAnAbsoluteReturnUrl_IgnoresItAndUsesTheDefault` — plus the protocol-relative `//evil.example.com`,
    backslash and encoded variants — the open redirect of source finding 5 is closed
    (Deliberate Behavior Change 10).
  - `MapperNotCalled_MapsNothing` — without `MapCloudstrapAuthenticationEndpoints()` both paths are 404: the
    mapper is opt-in and the schemes alone map no endpoint (spec edge-case row).
  - `ConfiguredPaths_AreHonored` — `LoginPath`/`LogoutPath` overridden in configuration; the endpoints move and
    the startup log states the paths in force.
  - `ConsumerOwnSignOut_WorksWithoutTheMapper` — an application endpoint calling `SignOut()` with no scheme
    arguments signs out of both schemes, because the stock default scheme names were kept (the Behaviors row's
    reason for using them).
  - `IdpWithoutAnEndSessionEndpoint_StillCompletesLocalSignOutAndLogsOnce` — the IdP configured without the
    end-session endpoint: the cookie is cleared, no exception, exactly one log entry (spec edge-case row).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe --filter "AuthenticationEndpointTests"
  ```

**GREEN**: the Scope items — two endpoints, one local-URL rule, one log line. Nothing else is mapped: no
user-info (#13), no register/consent/account pages, no front-/back-channel logout.

**DB changes**: none.

**VERIFY** *(when all green, mark this step's `Done` checkbox — the next plan item is a 🛑 HUMAN GATE, so stop there)*:
1. Test exe → all pass: a consumer now gets working login and logout endpoints from one opt-in call, logout
   really ends **both** sessions, and a caller-supplied return URL can no longer bounce a user off-site —
   the three defects of the observed source consumer contract, closed.
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: ⚠️ AUTH REVIEW — the public API, the cookie posture and the new runtime dependency *(covers Steps 3–4, which ran back-to-back)*

*Executor: STOP here. Present the results of both covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **public API one-way door** — `AddCloudstrapOpenIdConnect`,
`MapCloudstrapAuthenticationEndpoints`, `CloudstrapOpenIdConnectOptions`, `CloudstrapAuthenticationCookieOptions`,
`CloudstrapOpenIdConnectConfigurator` and `CloudstrapOpenIdConnect` are what #12, #13 and #17 build on: review
verbatim against the spec's Public API Sketch §1, **including everything listed as "deliberately not shipped"** ·
**auth code** — the registration path that binds a credential and configures two handlers, reviewed line by line;
no credential value may be logged or echoed · **⚠️ D-1 as built** — the actual `Set-Cookie` header from the test
output, attribute by attribute, and the fact that HttpOnly/Secure/SameSite are unreachable from configuration ·
**⚠️ D-2 as built** — tokens in the ticket, and the *absence* of any store or cache registration · **new runtime
dependency** `Duende.AccessTokenManagement.OpenIdConnect` 4.2.0 — rule-4 review: exact pin, Apache-2.0 confirmed,
the restored graph inspected including `Microsoft.IdentityModel.Protocols.OpenIdConnect` within `[8.0.1, 9.0.0)` ·
**mechanics (a.2) and (a.3)** library-API confirmations and **plan-level picks 1 and 4** reported here.

- [x] Behavioral verification: test exe output shows — the challenge query with code/PKCE-S256/state/nonce/
  form_post and no secret, the hardened `__Host-Cloudstrap` cookie, claims as issued, the return to the original
  local URL, tokens in the session with no store registered, the exact-key validation failures, the secret-free
  message, the optional secret, idempotence and the `RequireAuthenticatedEndpoints` opt-out (Step 3); RP-initiated
  logout ending both sessions, the local-only return URL closing the open redirect, the opt-in mapper mapping
  nothing when not called, configurable paths, bare `SignOut()` working, and the missing-end-session edge
  (Step 4).
- [x] Code review (auth + API): signatures vs the spec sketch §1 verbatim; `internal` by default, sealed, full
  XML docs naming exact keys; the configurator hooks genuinely run last; `SaveTokens` is hard-set with the D-2
  consequence documented; `Scope` is cleared before applying so no stock default leaks in;
  `dotnet list src/Cloudstrap.Authentication.OpenIdConnect/Cloudstrap.Authentication.OpenIdConnect.csproj package`
  → `Duende.AccessTokenManagement.OpenIdConnect` plus the two project references and nothing else direct; zero
  `OpenIddict.*` anywhere near the shipped project.
- [x] Test-infrastructure review: `BrowserlessUserAgent` (plan-level pick 1) is honest — it follows real
  redirects and submits the real forms rather than short-circuiting the protocol; confirm or direct changes.
- [x] User approved — implementation may continue past this gate *(configurator hooks running in the
  Configure stage after Cloudstrap's own wiring, the sign-out event for bare `SignOut()`, and the
  fallback-policy-on-unmatched-paths observation accepted as reported)*

---

## Slice 3 — The signed-in user's token reaches downstream APIs, and keeps working

---

## Step 5 — A flagged typed client calls a downstream API **as the signed-in user** — two users never see each other's token, no signed-in user means no request at all, and a client flagged for both token kinds sends the user's (AC-OIDC3, AC-OIDC8, AC-CC13 re-proven with two real providers) ⚠️ *(auth risk area; ⚠️ one behavior fix in shipped #9 — plan-level pick 3)*

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.OpenIdConnect/UserAccessTokenHandlerProvider.cs` *(create, internal, sealed)* —
  fills the shipped `IUserAccessTokenHandlerProvider` seam exactly as its XML-doc contract requires: dependencies
  resolved from `IServiceProvider` at pipeline-build time, a **fresh handler per call**, `InnerHandler` never
  pre-set. Maps the client's `TokenRequestOptions` once and warns about nothing (unlike #9, **all five** members
  are honored here).
- `src/Cloudstrap.Authentication.OpenIdConnect/UserTokenRequestParameterMapper.cs` *(create, internal, static)* —
  `Cloudstrap.Core.TokenRequestOptions` → Duende `UserTokenRequestParameters`: `Scope`, `Resource`,
  `ForceRenewal → ForceTokenRenewal`, **`SignInScheme`** and **`ChallengeScheme`** — the two members #9 ignores
  with a warning and #9's finding 4 reserved for this deliverable (spec finding 10).
- `src/Cloudstrap.Authentication.OpenIdConnect/UserTokenHandler.cs` *(create, internal, sealed)* — resolves the
  current `ClaimsPrincipal` through `IHttpContextAccessor`, asks Duende's `IUserTokenManager` for that user's
  access token, attaches it, and applies the #9 handler pattern: on a 401 exactly one `ForceTokenRenewal` retry
  through the intact inner chain. **Throws instead of sending** when there is no signed-in user or no token —
  the message names `Cloudstrap:HttpClients:{name}:AddUserAccessToken`, states that no `HttpContext`/user was
  available, and points at `AddClientAccessToken` as the machine-identity alternative (AC-OIDC8, spec edge-case
  row). When the ticket carries no tokens the message names `SaveTokens` (spec edge-case row).
- `src/Cloudstrap.Authentication.OpenIdConnect/ServiceCollectionExtensions.cs` *(modify)* — register
  `IUserAccessTokenHandlerProvider` and `AddHttpContextAccessor()`.
- `src/Cloudstrap.Authentication.OpenIdConnect/OpenIdConnectLog.cs` *(modify)* — the once-per-failure message
  (no token, no user, no value ever).
- ⚠️ `src/Cloudstrap.Authentication.ClientCredentials/ClientCredentialsTokenHandler.cs` *(modify — plan-level
  pick 3, a behavior change to a shipped package under the standing pre-release rule)* — the handler no longer
  overwrites an `Authorization` header another handler already set, so the seam's user-first ordering means the
  **user's** token reaches the peer. Its XML docs state the rule.
- `src/Cloudstrap.Authentication.ClientCredentials/README.md` *(modify)* — the same rule in the both-flags row.
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/TokenAttachmentTests.cs` *(modify)* —
  one added case pinning the new rule from #9's own side (a pre-set `Authorization` header survives the
  client-credentials handler).
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Cloudstrap.Authentication.OpenIdConnect.Tests.csproj`
  *(modify)* — `ProjectReference` to `Cloudstrap.Authentication.ClientCredentials` (test-only, for the AC-CC13
  re-proof).
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/UserTokenAttachmentTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/OidcTestHost.cs` *(modify —
  a flagged typed client with a `CapturingPrimaryHandler`, and a variant that also registers
  `AddCloudstrapClientCredentials` against the same IdP)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `UserTokenAttachmentTests.cs`
  - `FlaggedClient_AfterSignIn_CarriesTheSignedInUsersAccessToken` — the headline: a typed client registered
    through the **shipped** `AddCloudstrapHttpServiceClient` with only
    `Cloudstrap:HttpClients:Catalog:AddUserAccessToken = true` in configuration; after sign-in the captured
    outbound request carries `Authorization: Bearer <jwt>` whose `sub` is the signed-in test user and whose
    `client_id` is the OIDC client — **no consumer code change beyond the registration call** (AC-OIDC3).
  - `TwoSignedInUsers_NeverObserveEachOthersToken` — two agents signed in as two different configured users
    drive parallel requests; the two captured tokens carry the two distinct `sub` values and neither request
    ever carries the other's (AC-OIDC3's second clause).
  - `UnflaggedClient_IsUntouched` — no `Authorization` header.
  - `FlaggedClient_StillCarriesTheCorrelationHeader` — `X-Correlation-ID` survives downstream of the token
    handler (#4's ordering preserved).
  - `FlaggedClient_WithNoSignedInUser_ThrowsNamingTheFlag_AndSendsNothing` — an anonymous request drives the
    client: it throws, the message names `…:AddUserAccessToken` and the missing user context and mentions
    `AddClientAccessToken`; the capturing primary handler saw **zero** requests (AC-OIDC8).
  - `FlaggedClient_FromABackgroundServiceWithNoHttpContext_SameContract` — AC-OIDC8's second arm.
  - `PerClientScopeAndResource_ReachTheTokenRequest` and
    `PerClientSignInAndChallengeScheme_AreHonored` — all five `TokenRequestOptions` members map onto
    `UserTokenRequestParameters` (spec finding 10; the Edge Cases row that says #9's warning keeps firing only
    for purely client-flagged clients).
  - `Response401_TriggersExactlyOneForcedRenewalThroughTheIntactInnerChain` — a scripted downstream returns 401
    then 200; a consumer resilience handler added through `ConfigureHttpClientDefaults` runs once per attempt,
    neither duplicated nor bypassed; the token handler sits outermost (AC-ASP3 posture).
  - `TicketWithoutStoredTokens_ThrowsNamingSaveTokens` — the configurator overrode `SaveTokens = false`; the
    failure names it (spec edge-case row).
  - **AC-CC13 re-proof with two real providers**:
    `ClientFlaggedForBothTokens_GetsBothHandlersUserFirst_AndSendsTheUsersToken` — both packages registered,
    both flags set on one client: **both** identity-provider grants are observed (the interactive token endpoint
    *and* the client-credentials token endpoint were each hit), and the request that reaches the peer carries the
    **user's** token (its `sub` is the signed-in user, not the machine client) — the ordering the shipped wiring
    guarantees, now observable end to end and enforced by plan-level pick 3.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe --filter "UserTokenAttachmentTests"
  ```

**GREEN**: the Scope items (mechanic (a.3) confirmations during RED). The token lifecycle itself is entirely
Duende's; Cloudstrap adds only the seam implementation, the parameter mapping, the failure contract — and the
one-line non-clobber rule in #9's handler.

**DB changes**: none.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to Step 6)*:
1. Test exe → all pass: a flagged typed client now calls downstream APIs **as the signed-in user**, users are
   provably isolated from one another, an absent user produces a loud failure instead of an anonymous request,
   and a client asking for both token kinds provably gets both handlers with the user's token on the wire
   (AC-OIDC3, AC-OIDC8, AC-CC13).
2. `Cloudstrap.Authentication.ClientCredentials.Tests` still fully green, including the added non-clobber case.
3. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 6 — An expired access token renews itself behind the user's back: exactly one refresh grant, the request proceeds, the user is never re-challenged — and when refresh genuinely cannot succeed it fails loudly instead of sending an unauthenticated request (AC-OIDC4; AC-A2's interactive half)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.OpenIdConnect/` *(modify — whatever the renewal path needs: the refresh
  backchannel wiring confirmed in mechanic (a.3), the refreshed tokens written back into the authentication
  session, and the refresh-failure message contract)*
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/TokenRefreshTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/OidcTestHost.cs` *(modify —
  short-lifetime provider variant, the zeroed refresh-before-expiry buffer through `configurator.TokenManagement`,
  and access to the provider's refresh counter)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `TokenRefreshTests.cs`
  - `ExpiredAccessToken_RenewsTransparentlyWithExactlyOneRefreshGrant` — IdP `AccessTokenLifetime` = 1–2 s and
    the ATM buffer zeroed (plan-level pick 2); sign in, call, wait past expiry (retry-polled, ≤ ~3 s), call
    again: the provider's `RefreshTokenRequestCount` increased by **exactly one**, the second request carried a
    **different** access token, both calls succeeded, and **no 302 challenge was issued** to the user
    (AC-OIDC4).
  - `SeveralRequestsWithinOneLifetime_TriggerNoRefresh` — the refresh counter is unchanged and the same token
    value is attached each time.
  - `RefreshedTokens_AreWrittenBackIntoTheSession` — after the renewal the ticket's stored `access_token` is the
    new one, so a subsequent request on a fresh scope does not refresh again (D-2's storage model working end to
    end).
  - `ExpiredRefreshToken_FailsLoudlyAndSendsNothing` — the provider's `RefreshTokenLifetime` elapsed: the call
    throws, the capturing primary handler saw **zero** requests, and the failure is logged once with no token or
    secret value in it (spec edge-case row; AC-OIDC7's failure arm).
  - `ProviderUnreachableDuringRefresh_SameContract` — a broken backchannel produces the same loud, request-free,
    once-logged outcome.
  - `ConcurrentRequestsAcrossExpiry_StillProduceExactlyOneRefreshGrant` — two parallel calls at the expiry
    boundary; the refresh counter increases by one (the stampede control that is exactly why ATM was chosen over
    a hand-rolled `OnValidatePrincipal` refresh).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe --filter "TokenRefreshTests"
  ```

**GREEN**: the Scope items. The refresh machinery is Duende's — Cloudstrap adds only the wiring and the failure
contract, and the tests pin the library's behavior to the acceptance criteria.

**DB changes**: none.

**VERIFY** *(when all green, mark this step's `Done` checkbox — the next plan item is a 🛑 HUMAN GATE, so stop there)*:
1. Test exe → all pass: a user's session now outlives their access token without them noticing — one refresh
   grant, no re-challenge, no stampede — and every unrecoverable refresh fails loudly rather than sending an
   unauthenticated request (AC-OIDC4, AC-A2's interactive half).
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: ⚠️ AUTH REVIEW — user tokens, the change to shipped #9, and the refresh lifecycle *(covers Steps 5–6, which ran back-to-back)*

*Executor: STOP here. Present the results of both covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **auth code holding a live user's tokens** — the whole path from the cookie ticket
to the outbound `Authorization` header, reviewed line by line: no token or credential value in any message, no
request ever sent without a token, one user's token never reachable from another's request ·
**⚠️ breaking/behavioral change to a shipped package (plan-level pick 3)** — the client-credentials handler no
longer overwrites an existing `Authorization` header. Permitted by the standing pre-release rule, but it changes
the observable behavior of a package already built: review the diff, the added #9-side test, and the README
sentence. **The alternative — keep the clobber and document that the machine token wins on a both-flagged
client — is on the table; direct it here if preferred** · **AC-CC13's meaning** — confirm that "user first" is
now "the user's token arrives" · **mechanic (a.3)** ATM member-name confirmations, including how the refresh
backchannel resolves, and **plan-level pick 2**'s outcome (short real lifetimes) reported here.

- [x] Behavioral verification: test exe output shows — the flagged client carrying the signed-in user's token
  with no consumer code change, two users provably isolated, the unflagged client untouched, correlation intact,
  the two no-user fail-fast arms with zero downstream requests, all five `TokenRequestOptions` members honored,
  the single 401 renewal through an intact consumer pipeline, the `SaveTokens` message, and the AC-CC13 re-proof
  with two real providers (Step 5); the single-refresh renewal with no re-challenge, the no-refresh-within-a-
  lifetime control, the write-back into the session, both loud refresh failures, and the concurrent-expiry
  single-grant proof (Step 6).
- [x] Code review (auth): the user handler resolves the principal per request (never captured), throws before
  sending, and its messages name flags and keys only; the mapper maps exactly the five members; the provider
  returns a fresh handler with no `InnerHandler`; `git diff src/Cloudstrap.Authentication.ClientCredentials/`
  is confined to the non-clobber rule, its docs and its README.
- [x] User approved — implementation may continue past this gate *(non-clobber fix kept; AC-CC13 confirmed as
  "user first = the user's token arrives", with no machine grant for an already-authenticated request)*

---

## Slice 4 — Hardened and shippable: coexistence with bearer callers, leak-proof telemetry, permanent guards

---

## Step 7 — Bearer and browser callers coexist in one host: a bearer request is validated by the JWT scheme and fails **401** without ever seeing a login page, a browser request is challenged to the identity provider — whichever order the two packages were registered in (AC-OIDC9)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.OpenIdConnect/BearerCoexistence.cs` *(create, internal, static)* — the forward
  selector installed on the cookie (default) scheme: when a request carries an `Authorization: Bearer …` header
  **and** a scheme named `Bearer` is registered (looked up through `IAuthenticationSchemeProvider`, so no
  `Microsoft.AspNetCore.Authentication.JwtBearer` package reference is taken and the forwarding is inert when #5
  is absent), authenticate and challenge forward to it; otherwise the cookie/OIDC path applies. The well-known
  scheme name lives in one documented constant.
- `src/Cloudstrap.Authentication.OpenIdConnect/ServiceCollectionExtensions.cs` *(modify)* — install the selector;
  the startup log line states whether bearer coexistence is active.
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Cloudstrap.Authentication.OpenIdConnect.Tests.csproj`
  *(modify)* — `ProjectReference` to `Cloudstrap.WebApi` (#5, test-only).
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/CoexistenceHost.cs` *(create)* —
  a `WebApplication` on TestServer registering **both** `AddCloudstrapWebApi` + `AddCloudstrapJwtBearer` (its
  metadata backchannel routed to the in-process IdP through the shipped `Action<JwtBearerOptions>` hook — the #9
  Step 8 mechanic) **and** `AddCloudstrapOpenIdConnect`, with three endpoints: one `[Authorize]` (default
  scheme), one `[Authorize(AuthenticationSchemes = "Bearer")]`, one `[AllowAnonymous]`. Built twice, once in
  each registration order.
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/SchemeCoexistenceTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `SchemeCoexistenceTests.cs`
  - `BearerRequestWithAnInvalidToken_Gets401AndNoLoginRedirect` — `Authorization: Bearer <garbage>` against the
    `[Authorize]` endpoint → **401**, `Location` absent, `WWW-Authenticate: Bearer` present (AC-OIDC9's
    headline; finding 6's regression).
  - `BearerRequestWithAValidMachineToken_Succeeds` — a real client-credentials token from the in-process IdP
    (acquired through #9) is accepted, so coexistence composes with #9 rather than shadowing it.
  - `BrowserRequestWithoutAToken_IsChallengedToTheIdentityProvider` — no `Authorization` header → 302 to the
    authorization endpoint.
  - `EndpointPinnedToBearer_WithNoHeaderAtAll_Gets401NotARedirect` — the documented per-endpoint override
    (`[Authorize(AuthenticationSchemes = "Bearer")]`), which is exactly how the SUT keeps the #9 E2E machine
    endpoint's 401 in Step 10.
  - `RegistrationOrder_DoesNotChangeAnyOfTheAbove` — the same assertions against the host built with
    `AddCloudstrapJwtBearer` **after** `AddCloudstrapOpenIdConnect` and before it (plan-level pick 4).
  - `WithoutTheJwtBearerPackage_TheForwardingIsInert` — OIDC alone: a request carrying a `Bearer` header is
    challenged like any other browser request, and nothing throws looking for an unregistered scheme (the
    Behaviors row's "active only when both packages are registered").
  - `AnonymousEndpoint_StaysAnonymousUnderBothPackages` — `[AllowAnonymous]` plus the Cloudstrap-mapped health
    and OpenAPI endpoints stay reachable with both fallback policies in play (D-6's documented carve-out).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe --filter "SchemeCoexistenceTests"
  ```

**GREEN**: the Scope items. **No change to `Cloudstrap.WebApi` is expected** — this step composes with #5, it does
not modify it; any mismatch it surfaces is fixed at the source under the standing pre-release rule and reported
at Gate 4.

**DB changes**: none.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to Step 8)*:
1. Test exe → all pass: one host now serves API callers and browser users correctly at the same time — bearer
   requests 401 and browsers get a login page — and it does so independently of the order the consumer wrote the
   two registration calls in (AC-OIDC9).
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 8 — Nothing secret ever surfaces: through a full successful login, a failing one, and everything the app emits — logs at Debug, exported telemetry, problem-details bodies and exception text — no secret, authorization code, token or PII appears (AC-OIDC7)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.OpenIdConnect/` *(modify — whatever the sweep proves is needed: message
  contracts that name keys and never values, and any suppression required so a protocol failure surfaces as an
  identified error rather than a dump of the provider's response)*
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Cloudstrap.Authentication.OpenIdConnect.Tests.csproj`
  *(modify — `ProjectReference` to `Cloudstrap.Observability` for the in-memory exporter, test-only)*
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/SecretHygieneTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/Infrastructure/OidcTestHost.cs` *(modify —
  a Debug-level capturing `ILoggerProvider`, the in-memory activity exporter, and #5's problem-details handler
  in the pipeline so all four output channels are inspectable in one run)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `SecretHygieneTests.cs`
  - `SuccessfulLogin_LeaksNothingAcrossAllOutputChannels` — a full round trip with a configured secret at Debug
    level: the configured secret value, the authorization code, the `code_verifier`, and the id/access/refresh
    token values appear in **no** log message or scope, **no** exported `Activity` tag or event, and **no**
    response body (AC-OIDC7).
  - `FailedLogin_LeaksNothingAndYieldsAnIdentifiedError` — the provider returns a bad response mid-flow: the
    resulting failure names what failed without echoing the provider's payload; the problem-details body carries
    no token, code or secret; the exception and every inner exception are clean.
  - `AuthorizationCodeNeverAppearsInAUrl` — the callback arrives by `form_post`: the request path/query
    reaching the app carries no `code`, so nothing code-shaped can reach an access log or #2's request telemetry
    (AC-OIDC7's `form_post` clause, Deliberate Behavior Change 11's neighbour).
  - `ShowPii_IsNeverEnabledByCloudstrap` — `IdentityModelEventSource.ShowPII` is `false` after a full
    registration and a full login, and a repository-wide search for `ShowPII` finds it only in documentation
    (Deliberate Behavior Change 11 / source finding 4).
  - `CookieValue_IsNotWrittenToLogsOrTelemetry` — the session cookie's protected value (which now carries the
    tokens, D-2) appears in no log or span.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe --filter "SecretHygieneTests"
  ```

**GREEN**: the Scope items — message contracts only; no new abstraction. If a stock or Duende component is found
to log something forbidden, the fix is a documented level/filter decision recorded in the README and reported at
Gate 4 — never a silent suppression of the whole category.

**DB changes**: none.

**VERIFY** *(when all green, mark this step's `Done` checkbox and continue straight to Step 9)*:
1. Test exe → all pass: an interactive sign-in — the one flow that puts codes and tokens on the wire — provably
   leaks none of them into any channel the platform exports, in success and in failure alike (AC-OIDC7).
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 9 — The package is publishable and guarded forever: metadata, README, and permanent tripwires on the surface, the closure and the forbidden identifiers (AC-OIDC11, AC-ASP2, AC-A3)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.OpenIdConnect/Cloudstrap.Authentication.OpenIdConnect.csproj` *(modify)* —
  `<Description>` (interactive OpenID Connect login on the stock handlers and Duende AccessTokenManagement: one
  call for auth-code + PKCE sign-in, hardened cookie defaults, transparent user-token refresh, and user tokens
  on every flagged Cloudstrap typed `HttpClient`), `<PackageTags>` (`…;oidc;openid-connect;authentication;login;
  cookie;pkce;duende`), `<PackageReadmeFile>` + packed `README.md`.
- `src/Cloudstrap.Authentication.OpenIdConnect/README.md` *(create)* — quick start (the section +
  `AddCloudstrapOpenIdConnect()` + the opt-in mapper); the `Cloudstrap:OpenIdConnect` settings table; **the
  cookie posture (D-1)** with the reason the hardened trio is code-only; **where tokens live (D-2)** and the
  ⚠️ **multi-instance note** naming #4's `AddCloudstrapDataProtection` as the required companion **and the
  symptom of forgetting it** (users bounced to login on the second instance), plus the larger-cookie trade-off;
  **secret handling** (KeyVault / environment / user-secrets, never `appsettings.json`; every example a
  placeholder; the secret is optional and secret-free client authentication is reachable through
  `configurator.OpenIdConnect`); **why `offline_access` is in the default scope** and how to remove it where a
  provider treats it specially; the **claims posture** (`sub` stays `sub`); **scheme coexistence with #5**
  including the per-endpoint `AuthenticationSchemes` override; `RequireAuthenticatedEndpoints` and its two
  opt-outs; the **login/logout endpoints** and the local-only return rule, plus "or map your own"; the
  **`AddUserAccessToken` flag**, the five `TokenRequestParameters` members and the no-user failure; the
  refresh-expiry choice (catch-and-challenge or a cookie lifetime shorter than the refresh token); ⚠️ the
  `Microsoft.IdentityModel.Protocols.OpenIdConnect` `< 9.0.0` pin constraint; the inherited
  `Microsoft.AspNetCore.App` framework-reference note; the **manual "verify against Keycloak, Entra or any
  conformant provider" procedure** (configuration only — D-4); the **`IdentityModelEventSource.ShowPII`
  troubleshooting tip marked local-only, with the warning that it copies tokens and personal data into logs**;
  what is deliberately **not** here (user-info → #13, Blazor circuit tokens → #12, front-/back-channel logout,
  server-side stores, multi-scheme/multi-tenant, consent/account UI); and the Aspire posture.
- `src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests/PackageSurfaceTests.cs` *(create)* — permanent
  guards mirroring the #9/Extensions/WebApi precedent:
  - `ReferencedAssemblies_MatchTheApprovedClosure` — every referenced assembly starts with `System`,
    `Microsoft.` or `Duende.` or equals `Cloudstrap.Core`/`Cloudstrap.Extensions`; explicitly **zero** names
    starting `OpenIddict`, `Microsoft.EntityFrameworkCore`, `Aspire`, `Nihdi`, `NSwag`, `LanguageExt` — and
    zero `Cloudstrap.WebApi` or `Cloudstrap.Authentication.ClientCredentials` (this package composes with them,
    it does not depend on them) (AC-OIDC11, AC-ASP2, AC-A3 made permanent).
  - `PublicTypes_AreSealedOrStaticAndInTheSingleApprovedNamespace` — namespace
    `Cloudstrap.Authentication.OpenIdConnect` only; no public interfaces (the seam interfaces live in
    `Cloudstrap.Extensions`).
  - `PublicTypes_ContainNoForbiddenIdentifiers` — no public type or member matches `(?i)nihdi|riziv|keycloak`.
  - `PublicSurface_MatchesTheSpecSketch` — the exported type names are exactly the six the spec's Public API
    Sketch §1 lists, so an accidental promotion of an internal type is caught forever.

**RED** *(guard tests are written and run first but, as tripwires against already-correct code, may pass
immediately — the honest failing state is in the artifacts: before GREEN the Release nupkg has no
README/description/tags; the plan-2/3/4/5/9 precedent)*:
- Unit test file: `PackageSurfaceTests.cs` (the four guards above).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.OpenIdConnect.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.OpenIdConnect.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: the csproj metadata and `README.md` per Scope.

**DB changes**: none.

**VERIFY** *(when all green, mark this step's `Done` checkbox — the next plan item is a 🛑 HUMAN GATE, so stop there)*:
1. Test exe (full run) → all tests pass including the four new guards.
2. `dotnet build src/Cloudstrap.sln -c Release` → expand a `.zip` copy of
   `src/Cloudstrap.Authentication.OpenIdConnect/bin/Release/Cloudstrap.Authentication.OpenIdConnect.<version>.nupkg`
   → contains `README.md`, `icon.png`, `lib/net10.0/*.dll` **and** `.xml`; the nuspec shows the MIT license
   expression, the repository URL, and a dependency list with `Duende.AccessTokenManagement.OpenIdConnect` plus
   the promoted transitives and **no** `OpenIddict.*`, no EF Core, no `Nihdi.*`, no `Aspire.*` (AC-OIDC11,
   AC-ASP2).
3. **Identifier + personal-data sweep** (the deliverable's new and touched trees):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Authentication.OpenIdConnect, src/Test/TestIdentityProvider, src/Test/UnitTest/Cloudstrap.Authentication.OpenIdConnect.Tests, src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|keycloak|/auth/realms/)'
   ```
   → the only hits are the guard test's own tripwire pattern and the README's documented manual
   "verify against Keycloak or Entra" procedure (read each hit; no identifier is used *as* an identifier);
   the solution-wide `Nihdi.AspNetCore` search is still empty (AC-A3); no personal data anywhere.
4. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 4: ⚠️ AUTH SURFACE SIGN-OFF — coexistence, leak hygiene and closure, before the running-app demo *(covers Steps 7–9, which ran back-to-back)*

*Executor: STOP here. Present the results of all three covered steps and WAIT for user approval — do not start the next step. This is the mandatory whole-surface human review under CLAUDE.md's risk-area rule, taken before anything is demonstrated in the running app.*

⚠️ **Risk areas at this gate**: **⚠️ tokens, secrets and codes in telemetry** — the AC-OIDC7 evidence across all
four channels, on screen, for a successful and a failing login; confirm the `form_post` claim and that `ShowPII`
appears only as documentation · **auth composition with #5** — the forward selector reviewed as auth code: it
must not be able to authenticate a browser request as a bearer caller or vice versa, and it must be inert without
#5 · **public API + closure** — the four permanent guards, the Release nupkg contents, and the README's accuracy
against as-built behavior (especially the D-1/D-2 sections and the multi-instance symptom) · any executor
deviations from Steps 7–9, including anything the hygiene sweep forced.

- [x] Behavioral verification: test exe output shows — the bearer 401 with no redirect, the valid machine token
  accepted, the browser challenge, the pinned-scheme 401, both registration orders identical, the inert
  forwarding without #5, and anonymous endpoints staying anonymous (Step 7); the four-channel clean sweep on
  success and failure, the code-never-in-a-URL proof, `ShowPII` never set, and the cookie value absent from logs
  and spans (Step 8); the four permanent guards plus the artifact checks and the identifier sweep (Step 9).
- [x] Code review (auth): the forward selector's condition is exactly "a `Bearer` `Authorization` header **and** a
  registered `Bearer` scheme"; no message anywhere embeds a credential, code or token value; the README's
  secret-handling, cookie, storage, multi-instance, coexistence and JWT-pin sections match as-built behavior.
- [x] ⚠️ Dependency + identifier review: restored-graph inspection for the shipped package
  (`Duende.AccessTokenManagement.OpenIdConnect` + `Microsoft.AspNetCore.Authentication.OpenIdConnect` + promoted
  transitives, with `Microsoft.IdentityModel.Protocols.OpenIdConnect` inside `[8.0.1, 9.0.0)`); zero
  `OpenIddict.*`/EF Core in the shipped closure; the Step 9 sweeps re-confirmed.
- [x] User approved — implementation may continue past this gate

---

## Slice 5 — Demonstration: a person signs in to the running SUT in a real browser, and it calls an API as them

---

## Step 10 — Interactive login runs through the SUT Bff: Chromium signs in at the test identity provider, the user-flagged typed client calls a protected API **as that user**, and signing out ends both sessions — while all 31 pre-existing E2E tests stay green (AC-OIDC12; AC-A1, AC-OIDC2, AC-OIDC5, AC-OIDC9 and AC-CC13 live)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Host/Bff/Cloudstrap.WasmTestProject.Host.Bff.csproj` *(modify —
  `ProjectReference` to `Cloudstrap.Authentication.OpenIdConnect`)*
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify)* —
  `builder.Services.AddCloudstrapOpenIdConnect();` with a comment replacing "Interactive user login arrives with
  deliverable #10" by the #10 demo note (and pointing the WASM auth-state UI at #13); the login/logout endpoints
  mapped through the **shipped** pipeline hook:
  `pipeline.ConfigureEndpoints = endpoints => { endpoints.MapCloudstrapAuthenticationEndpoints(); endpoints.MapFallbackToFile("index.html"); }`.
  ⚠️ The executor confirms the OIDC callback is reached in this pipeline shape (the auto-inserted authentication
  middleware relative to `UseCloudstrapWebApi`'s own `UseRouting`); if it is not, the fix is the documented
  `pipeline` hook in the SUT — **not** new middleware in the library — and it is reported at the gate.
- `src/Test/WasmTestProject/src/Host/Bff/appsettings.json` *(modify)* — `Cloudstrap:OpenIdConnect`:
  `Authority: "http://127.0.0.1:5310"`, `ClientId: "wasmtestproject-web"`,
  `ClientSecret: "local-e2e-placeholder-secret-web"` (an obvious placeholder for the local-only test provider,
  with the same comment the #9 section carries), `Scope: "openid profile offline_access selfapi"`, and
  **`RequireAuthenticatedEndpoints: false`** — D-6's documented whole-application opt-out, with a comment saying
  exactly why (the 31 pre-existing anonymous E2E tests); the cookie settings are left at their D-1 defaults **on
  purpose**, so the E2E run proves `__Host-`/`Secure` cookies work in Chromium on loopback. Plus
  `Cloudstrap:HttpClients:UserApi` — same base address as `SelfApi`, `AddUserAccessToken: true` **and**
  `AddClientAccessToken: true` (plan-level pick 5: the live AC-CC13).
- `src/Test/WasmTestProject/src/Contracts/Cloudstrap.WasmTestProject.Contracts/MachineStatusDto.cs` *(modify —
  add `Subject`, so the protected endpoint can report **who** the caller is and a user token is distinguishable
  from a machine token; the existing E2E assertions on `clientId`/`issuer` are unaffected)*
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/MachineController.cs` *(modify)* — `GetStatus` echoes `sub`
  as well, and its attribute becomes `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`
  — the documented per-endpoint override that keeps `ProtectedEndpoint_CalledDirectlyWithoutAToken_Returns401`
  answering **401** instead of redirecting to a login page now that a cookie/OIDC default scheme exists
  (AC-OIDC9; **the #9 E2E test file itself is not touched**).
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/UserController.cs` *(create)* — `[ApiVersion("1.0")]`,
  route `api/v{version:apiVersion}/user`: `GET whoami` `[Authorize]` returns the signed-in user's `sub` and
  `name` from the cookie principal; `GET call` `[Authorize]` drives `IUserApiClient` into
  `api/v1/machine/status` and relays the DTO — the in-app round trip proving the **user's** token was the one on
  the wire. *(This is SUT application code echoing claims for the demo; the BFF user-info **contract** is #13.)*
- `src/Test/WasmTestProject/src/Host/Bff/Services/IUserApiClient.cs` + `UserApiClient.cs` *(create — the
  `ISelfApiClient`/`SelfApiClient` shape, registered with `AddCloudstrapHttpServiceClient<…>("UserApi")`)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/E2eFixture.cs` *(modify — register a
  second provider client `wasmtestproject-web`: the placeholder secret, scopes
  `openid`/`profile`/`offline_access`/`selfapi`, audience `wasmtestproject-selfapi` (so the user's access token
  validates at the #5-protected endpoint), `RedirectUris = { http://127.0.0.1:5300/signin-oidc }`,
  `PostLogoutRedirectUris = { http://127.0.0.1:5300/ }`; and one neutral test user with placeholder credentials
  and `name`/`role` claims. No new port — 5310 keeps serving both clients.)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/OpenIdConnectTests.cs` *(create — inherits
  `PageTestBase`: headless Chromium, fresh context per test)*
- `src/Test/WasmTestProject/README.md` *(modify — a demo-table row (`/account/login` + `/account/logout` +
  `GET api/v1/user/whoami` + `api/v1/user/call` | Cloudstrap.Authentication.OpenIdConnect (#10) + the test
  identity provider's interactive flows (D-4) | `OpenIdConnectTests`); a "Harness notes for deliverable #10"
  section covering the `RequireAuthenticatedEndpoints: false` posture and why, the placeholder secret rule, why
  `machine/status` is now pinned to the `Bearer` scheme, the `UserApi` client and what it demonstrates
  (AC-CC13 live), and the note that a manual `dotnet run` without the provider still boots — login simply fails
  until something listens on 5310)*

**RED** *(write these tests first, run them, confirm they fail — before the conversion the Bff has no OIDC
registration, no login/logout endpoints, no user endpoints and no user-flagged client, so all of them fail)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/OpenIdConnectTests.cs`
  - `Login_ThroughTheBrowser_SignsTheUserInAndIssuesTheHardenedCookie` — the browser navigates to
    `/account/login`, is redirected to the provider on 5310, fills the login form and submits; it lands back on
    the app; `context.CookiesAsync()` shows a cookie named **`__Host-Cloudstrap`** with `HttpOnly`, `Secure`,
    `SameSite=Lax` and `Path=/` — **D-1 proven in a real browser on loopback** — and
    `GET api/v1/user/whoami` in the same context returns the signed-in user's `sub` and `name` (AC-A1,
    AC-OIDC2, AC-OIDC12).
  - `SignedInUser_CallsTheProtectedApiAsThemselves` — `GET api/v1/user/call` in the logged-in context returns
    200 and the relayed DTO's `subject` is the **test user**, while its `clientId` is `wasmtestproject-web` —
    the user's token, acquired through the interactive flow and validated by #5, reached the peer through a
    client flagged for **both** token kinds (AC-OIDC3 + AC-CC13, live).
  - `Logout_EndsBothTheLocalAndTheProviderSession` — `/account/logout`, then `GET api/v1/user/whoami` again:
    the browser ends up on the **provider's login form** rather than being silently re-authenticated, and the
    `__Host-Cloudstrap` cookie is gone (AC-OIDC5, Deliberate Behavior Change 9).
  - `AnonymousBrowser_IsChallengedWhileTheMachineEndpointStill401s` — an anonymous context hitting
    `api/v1/user/whoami` is redirected to the provider, while a plain `HttpClient` hitting
    `api/v1/machine/status` still gets **401** — coexistence live, and the #9 test's contract intact
    (AC-OIDC9).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "OpenIdConnectTests"
  ```
  *(one-time, if Chromium is missing: `pwsh src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`)*

**GREEN**: the Scope items — the fixture's second provider client and test user, the Bff registration,
configuration and endpoints, the `UserApi` client, and the README row and notes. **Every one of the 31
pre-existing E2E tests must stay green**, in particular `ClientCredentialsTests` (its 401 now comes from the
scheme-pinned attribute, its `call` relay still carries the machine token), `ExtensionsTests` (the anonymous
`SelfApi` hop is untouched because the user flag lives on the separate `UserApi` client) and the
second-instance startup tests (which must still validate the new configuration section without the provider
being reachable). *(If any existing test is disturbed, the executor reports it at the gate rather than weakening
the assertion.)*

⚠️ **Known risk to confirm in this step**: `__Host-` requires the `Secure` attribute, and the SUT serves plain
HTTP on `http://127.0.0.1:5300`. Chromium treats loopback as a trustworthy origin, so `Secure`/`__Host-` cookies
are expected to work — this is exactly what D-1 wanted proven. If Chromium refuses them, the fallback is
**configuration only** — set `Cloudstrap:OpenIdConnect:Cookie:Name` in the SUT's `appsettings.json`, or run the
E2E fixture against the host's `https` profile — never a weakening of the library's default. Either way the
outcome is reported at the final gate.

**DB changes**: none.

**VERIFY** *(when all green, mark this step's `Done` checkbox — the next plan item is a 🛑 HUMAN GATE, so stop there)*:
1. E2E exe → the four new `OpenIdConnectTests` pass **and all 31 pre-existing E2E tests pass unchanged**
   (build first; one-time `playwright.ps1 install chromium` if needed).
2. Manual smoke (optional but recorded): `dotnet run --project src/Test/WasmTestProject/src/Host/Bff` with the
   provider absent → the app boots and `/healthz` answers; `/account/login` fails loudly naming the authority.
3. Full suite (Overview mechanic (f)) + `dotnet build src/Cloudstrap.sln` (zero warnings) +
   `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: ⚠️ deliverable #10 complete *(covers Step 10; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

- [x] Behavioral verification: the four new `OpenIdConnectTests` pass against the running app with a real
  browser signing in at the loopback provider; **all 31 pre-existing E2E tests pass unchanged**; the full suite
  (Overview mechanic (f)) is green end to end.
- [x] ⚠️ D-1 proven in a browser: the `__Host-Cloudstrap` cookie's attributes as Chromium reports them, on
  screen — or, if the loopback fallback was needed, the configuration-only workaround and the reason, confirmed
  here. *(No fallback was needed: Chromium accepted and enforced the hardened cookie over plain-http loopback.)*
- [x] Spec acceptance sign-off: walk **AC-A1, AC-A3, AC-ASP2, AC-CC13 and AC-OIDC1…AC-OIDC12** against the step
  evidence using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop / Out-of-Scope
  lists was resurrected (no `EnableAuthentication` gate, no `AuthenticationFlow` or implicit/hybrid anywhere
  including the provider, no `SaveTokens`/`UseTokenLifeTime` toggle, no `IdentityTokenCacheLifetime` or
  `RefreshTokenCacheLifetime`, no `AddDistributedMemoryCache`, no fallback to `Cloudstrap:ClientCredentials`, no
  `ShowPII`, no user-info contract or Blazor auth-state helper (#13), no Blazor circuit token store (#12), no
  front-/back-channel logout, no server-side or distributed token store, no multi-scheme/multi-tenant OIDC, no
  consent/account UI, no containerised identity provider anywhere) and that every De-NIHDI row is closed
  (neutral fixtures, placeholder secrets and passwords only, zero `Nihdi`/`Riziv` identifiers, `Keycloak`
  appearing only in the documented manual procedure, no realm-style URLs, no personal data).
- [x] Plan-level deviations confirmed for the record: **pick 3** (the shipped-#9 non-clobber rule), **pick 5**
  (the `UserApi` client instead of flagging `SelfApi`, and why AC-OIDC12's parenthetical could not be taken
  literally), the `machine/status` scheme pin, and any pipeline-ordering fix Step 10 needed. *(No pipeline fix
  was needed; Step 10 instead surfaced and fixed a package bug — the scoped `IUserTokenManager` is now resolved
  per request from the request scope, and the unit-test host runs with Development-grade container validation.)*
- [x] Demo + docs review: the SUT README demo-table row and the #10 harness notes match as-built behavior; the
  Bff `Program.cs` comment now attributes interactive login to #10 and points the WASM auth-state UI at #13.
- [x] One-way-door recap for the record: the Decision Log confirmed at Gate 1, the package public API (Gate 2),
  the user-token semantics (Gate 3) and the extended test identity provider are what **#12, #13 and #17** build
  on next — confirm no open reservations remain, and that the founding-spec AC-A1 amendment (D-4) has been made
  or is explicitly scheduled. *(The AC-A1 amendment was made at Gate 1 with the user's authorization.)*
- [x] User approved — deliverable #10 done; project-manager flips the ROADMAP row to ✅ (and any push happens
  only on the user's explicit go-ahead).
