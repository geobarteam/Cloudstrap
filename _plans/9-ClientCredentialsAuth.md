# Plan: 9-ClientCredentialsAuth — A consumer references one package and calls `AddCloudstrapClientCredentials()`, and every outbound typed `HttpClient` already flagged `AddClientAccessToken: true` transparently carries a cached, renewed bearer token — with the secret never in `appsettings.json` and never in a log

## Overview

Deliverable #9 of the extraction roadmap: the new **`Cloudstrap.Authentication.ClientCredentials`** package,
plus the two pieces of infrastructure the spec binds to it — the **D-4 split of `Cloudstrap.Extensions`' shipped
access-token seam** (a ⚠️ breaking shared-contract change, reviewed at its own gate) and the **D-5 in-repo test
identity provider `Cloudstrap.TestIdentityProvider`** (test-only, OpenIddict, shared infrastructure for #10, #12
and #17). **Binding spec: `_specs/9-ClientCredentialsAuth.md`** (approved 2026-08-05, zero Open Questions;
Decision Log **D-1** config-provider credential + consumer `IClientAssertionService` · **D-2** flat
`Cloudstrap:ClientCredentials` section · **D-3** isolated token cache by default · **D-4** seam split · **D-5**
real in-repo IdP). Its Port Decision Table (5 Port · 6 Redesign · 9 Replace · 19 Drop · 3 Superseded · 6 Routed),
Public API Sketch (all three parts), Behaviors & Conventions table, Edge Cases table, Dependencies tables and Out
of Scope list are authoritative and not re-litigated here. Nothing the spec marked Drop appears in this plan: no
`EnableAuthentication` gate, no `AuthenticationFlow` enum, no `Authority`+path convention, no
`RefreshTokenCacheLifetime`/`IdentityTokenCacheLifetime`/`UseDefaultClaimTypeMapping`, no nested
`Security:Authentication` graph, no implicit/hybrid flows anywhere, no pipe-separated claim values, no `schemas.`
prefix rewriting, no `LogParameters`, no `BaseUrl`, no OIDC discovery for the token endpoint, no DPoP/mTLS/
workload-identity code, no facade over `IClientCredentialsTokenManager`, and not one character of the source STS
fixture content.

Reference patterns, all read in full before planning:

- **Shipped seam this deliverable fills and reshapes (read in the shipped code)**:
  `src/Cloudstrap.Extensions/IAccessTokenHandlerProvider.cs` (the two method signatures that survive D-4
  verbatim), `AccessTokenHandlerWiring.cs` (lazy pipeline-build-time resolve, insert-at-position-0 user-first,
  the aggregated missing-provider message shape), `ServiceCollectionExtensions.cs`
  (`AddCloudstrapHttpServiceClient`, `HttpClientsSectionPath`, the `{name}-liveness` probe client),
  `src/Cloudstrap.Core/HttpClientServiceOptions.cs` + `TokenRequestOptions.cs` (the per-client contract:
  `AddClientAccessToken`, `TokenRequestParameters` with `Scope`/`Resource`/`SignInScheme`/`ChallengeScheme`/
  `ForceRenewal`), `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AccessTokenHandlerSeamTests.cs` (the
  host-level test pattern this plan's package tests reuse, and the `RecordingTokenHandlerProvider` double whose
  both-methods shape masked the D-4 defect), `Cloudstrap.Extensions.Tests/PackageSurfaceTests.cs` (the
  interface-list guard Step 1 must update).
- **Repo pattern (deliverables 1–5, verified on disk)**: `src/Cloudstrap.WebApi/` csproj + options + validators +
  configurator shapes and `_plans/5-WebApiBootstrap.md` (slice/gate granularity, RED-on-a-new-project precedent,
  library-API-confirmation mechanic, package-hygiene step shape); `CloudstrapJwtBearerOptions` (the
  `RequireAuthenticatedEndpoints` whole-app opt-out the SUT demo uses, `RequireHttpsMetadata` null → Development
  exemption); `src/Test/Directory.Build.props` (non-`*.Tests` projects under `src/Test/` are plain libraries,
  `IsPackable=false` — what makes the OpenIddict/EF dependency set safe, AC-CC15); `src/Directory.Packages.props`
  (CPM, transitive pinning on).
- **Demonstration harness (verified on disk)**: `src/Test/WasmTestProject/src/Host/Bff/Program.cs` (the
  "deliberately absent: `AddCloudstrapJwtBearer` … arrives with #9/#10" comment this plan finally redeems),
  `appsettings.json` (`Cloudstrap:HttpClients:SelfApi`), `ISelfApiClient`/`SelfApiClient`,
  `E2eFixture` (boot order, `CapturedSutOutput`), `SutProcess.Start(baseUrl, applicationArguments)`,
  `PageTestBase`, the **28 pre-existing E2E tests** (AC-CC16 baseline), and the harness port map
  (5300 Bff · 5301–5303 second instances · 59999 dead port → **the test IdP takes 127.0.0.1:5310**).

This is a library deliverable with no database and no UI of its own: the plan template's endpoint-integration
block does not apply literally. Its equivalent here is that **every package step's tests boot a real host with
the real `IHttpClientFactory` pipeline and the real test identity provider in-process as ATM's backchannel** —
a genuine OpenID Connect token endpoint with no sockets — plus the mandatory E2E demonstration slice (Step 10)
where the IdP runs on a real loopback address and #5's JWT bearer performs genuine discovery/JWKS retrieval.

### The two plan-level picks the spec deferred (committed here, reviewable at Gates 2 and 3)

1. **OpenIddict store provider: SQLite in-memory** (`Microsoft.EntityFrameworkCore.Sqlite`), not the EF
   InMemory provider the source's dev server used. Each IdP instance opens **one `SqliteConnection` to
   `Data Source=:memory:`, holds it open for the instance's lifetime, and runs `EnsureCreated()`** — SQLite is
   Microsoft's documented testing recommendation, OpenIddict's stores are relational-shaped, and
   one-connection-per-instance gives the spec's "two isolated identity providers" edge case for free.
2. **Token-renewal clock strategy: short real lifetimes, not a fake clock.** Renewal tests (AC-CC3/AC-A2) set
   the IdP's `AccessTokenLifetime` to 1–2 s and zero ATM's cache lifetime buffer through
   `configurator.TokenManagement`, then wait past expiry (bounded ≤ ~3 s, retry-polled, not sleep-flaky).
   `Microsoft.Extensions.TimeProvider.Testing` is **not** added. Rationale: a faked clock would have to be
   honored coherently by Duende ATM's expiry computation, by the isolated `HybridCache`'s entry expiration
   **and** by OpenIddict's token issuance — three libraries, one of which (the IdP) deliberately runs as a real
   server. Step 5's RED contains the spec-mandated confirmation as a **bounded spike**: if a DI-registered
   `FakeTimeProvider` provably drives both ATM and the isolated cache in-process, the executor may switch to it
   (adding the MIT test-only pin) and reports the deviation at Gate 3; otherwise short real lifetimes stand.

### AC coverage map (every criterion claimed by at least one step)

| Criterion | Step(s) |
|---|---|
| AC-A2 (transparent renewal across the lifetime, no 401s — mechanised as AC-CC3) | 5 |
| AC-A3 (zero `Nihdi.AspNetCore` — must stay green) | 9 (permanent guard) |
| AC-ASP2 (zero `Aspire.*` in any shipped closure) | 9 |
| AC-CC1 (flagged client carries `Bearer` with no consumer code change; unflagged untouched; correlation flows) | 4 (+ live in 10) |
| AC-CC2 (one token request per lifetime, token reused) | 5 (+ live in 10) |
| AC-CC3 (elapsed lifetime → exactly one new token request, caller sees no failure) | 5 |
| AC-CC4 (startup fails naming the exact offending key) | 4 |
| AC-CC5 (secret value appears nowhere — validation, logs, exceptions, telemetry) | 4 (validation half) + 6 (logs/exceptions half) |
| AC-CC6 (per-client Scope/Resource → distinct tokens, separately cached) | 5 |
| AC-CC7 (`AddUserAccessToken` with only #9 → fail fast naming the flag + `Cloudstrap.Authentication.OpenIdConnect`) | 1 (seam mechanism) + 6 (with the real package registered) |
| AC-CC8 (rejected/failed/unreachable IdP → loud failure naming acquisition + endpoint, never an unauthenticated request, logged once) | 6 |
| AC-CC9 (token handler outermost; consumer resilience neither duplicated nor bypassed; 401-refresh re-executes the inner chain) | 5 |
| AC-CC10 (registration idempotent) | 4 |
| AC-CC11 (secret-free via consumer `IClientAssertionService`, never overwritten) | 6 |
| AC-CC12 (isolated cache default — nothing token-shaped in an app `IDistributedCache`; `Shared` opt-in; mode logged) | 7 |
| AC-CC13 (two separate providers + both flags → both handlers user-first; one-present/one-missing → names only the missing one) | 1 |
| AC-CC14 (acquisition + validation end to end against real discovery/JWKS) | 8 (in-process) + 10 (live over HTTP) |
| AC-CC15 (hygiene: build/tests/format, XML docs, metadata, closure free of `OpenIddict.*`/EF Core/`Aspire.*`/`Nihdi.*`, identifier + personal-data sweep) | 9 |
| AC-CC16 (SUT demo: 28 pre-existing E2E green + ≥ 1 new E2E proving the bearer round trip) | 10 |

### New CPM entries (`src/Directory.Packages.props` — transitive pinning is on; the executor verifies each exact stable version on nuget.org at pin time and reports any deviation at the covering gate)

| Package | Version | License | Closure | Step |
|---|---|---|---|---|
| `Duende.AccessTokenManagement` | 4.2.0 (spec-verified 2026-08-05; re-confirm the current 4.2.x patch at pin time) | Apache-2.0 | **runtime** (the suite's first auth-stack runtime dependency) | 4 |
| `OpenIddict.Server.AspNetCore` | 7.6.0 | Apache-2.0 | test-only (TestIdentityProvider) | 2 |
| `OpenIddict.EntityFrameworkCore` | 7.6.0 | Apache-2.0 | test-only (TestIdentityProvider) | 2 |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.x — match the repo's `Microsoft.*` 10.0.10 family | MIT | test-only (TestIdentityProvider) | 2 |

Transitive-pinning consequences the executor resolves **by pinning, never by disabling the setting**, reported
at the covering gate: ATM promotes `Microsoft.Extensions.Http.Resilience` (already pinned 10.8.0, today
test-only) and `Microsoft.Extensions.Caching.Hybrid` into a **runtime** closure; ⚠️ `System.IdentityModel.Tokens.Jwt`
must stay ≥ 8.0.1 **< 9.0.0** — #5's JwtBearer already brings the `Microsoft.IdentityModel` 8.x family so they
agree today, but any future 9.x pin breaks restore; the Step 9 README records the constraint.
`Microsoft.Extensions.TimeProvider.Testing` is **not** pinned (plan-level pick 2) unless Step 5's spike flips it.

### ⚠️ Risk areas (spec header; reviewed at the gates named)

- **⚠️ Breaking change to shipped public API / shared contract (D-4)** — `IAccessTokenHandlerProvider` removed,
  two interfaces replace it, the wiring behavior matrix changes: **Gate 1**, a dedicated gate covering only
  Step 1, before any new-package code builds on the new seam. Permitted under the standing pre-release rule.
- **⚠️ Auth code + credential handling** — this deliverable holds a client secret (or assertion), calls a token
  endpoint and caches bearer tokens: **Gates 3 and 4** (implementation + hardening), with Gate 4 the dedicated
  auth-surface sign-off before the SUT demo.
- **⚠️ Public API one-way door** — `AddCloudstrapClientCredentials`, `CloudstrapClientCredentialsOptions`,
  `TokenCacheMode`, `CloudstrapClientCredentialsConfigurator`, `CloudstrapClientCredentials.TokenClientName`
  are the contract #10, #12 and #17 build on: **Gate 3** (signatures reviewed verbatim against the spec sketch).
- **⚠️ New runtime dependency** `Duende.AccessTokenManagement` (Apache-2.0, unconditionally OSS — verified in
  the spec; rule-4 review): **Gate 3**.
- **⚠️ Second one-way door (D-5)** — `Cloudstrap.TestIdentityProvider`'s options model, hosting modes and
  location become shared infrastructure for #10/#12/#17: **Gate 2**. Its OpenIddict + EF Core + SQLite
  dependency set is test-only by construction (`src/Test/Directory.Build.props` forces `IsPackable=false`);
  the Step 9 closure guard makes that permanent (AC-CC15).
- **Aspire**: no overlap — token acquisition is outside ServiceDefaults' remit; AC-ASP2 carried as a closure
  tripwire (Step 9); the AC-ASP3 no-stacked-resilience posture is preserved and proven (Step 5, AC-CC9).

### Planner mechanics decided here (no spec conflict; each flagged for review at the named gate)

**(a) Library-API confirmations the executor makes during RED and reports at the covering gate** (the plan-5
mechanic (i) precedent — outcomes are fixed, exact member names are confirmed against the installed package):
   1. **Duende ATM 4.2.0** — `AddClientCredentialsTokenManagement()` → `.AddClient("cloudstrap", …)` and the
      `ClientCredentialsClient` members (`TokenEndpoint`, `ClientId`, `ClientSecret`, `Scope`, `Resource`,
      `HttpClientName`); the keyed isolated-cache extension point `ServiceProviderKeys.ClientCredentialsTokenCache`
      (D-3); `ClientCredentialsTokenManagementOptions` cache knobs; `IClientAssertionService`;
      `TokenRequestParameters` (`Scope`, `Resource`, `ForceTokenRenewal`); the handler type ATM's own
      `AddClientCredentialsTokenHandler` news up at pipeline-build time (spec finding 7) — the internal provider
      mirrors that construction. Confirm in Steps 4–7 RED. *(Gate 3.)*
   2. **OpenIddict 7.6.0** — the server builder calls for: client-credentials grant only, token endpoint URI,
      discovery + JWKS exposure, ephemeral signing key, **disabled access-token encryption** (so #5's JwtBearer
      can validate the JWT), per-client claim/audience/lifetime shaping (custom claims via the server's token
      generation event/principal), and the EF Core store registration. Confirm in Steps 2–3 RED. *(Gate 2.)*
   3. **#5's `AddCloudstrapJwtBearer` hook** — the `Action<JwtBearerOptions>` hook is where Step 8 injects the
      in-process IdP's `HttpMessageHandler` as the metadata backchannel (`BackchannelHttpHandler`), so discovery
      and JWKS retrieval are genuine without sockets. *(Gate 4.)*

**(b) Test strategy.** Package tests (Steps 4–8) follow the `AccessTokenHandlerSeamTests` pattern:
`HostApplicationBuilder` + in-memory `Cloudstrap:` configuration + `AddCloudstrapHttpServiceClient` with a
`CapturingPrimaryHandler`, plus the **test IdP hosted in-process** (TestHost) whose `HttpMessageHandler` is
wired as the ATM backchannel's primary handler through `configurator.Backchannel`. Assertions are made on the
captured outbound request (headers, the JWT itself — parsed, its `iss`/`aud`/`client_id`/`scope` claims) and on
a token-endpoint hit counter in the IdP host fixture. Fixtures are neutral (`Contoso`, `Catalog`,
`example.com`, obvious placeholder secrets like `"placeholder-not-a-real-secret"`).

**(c) Validators.** `CloudstrapClientCredentialsOptionsValidator` — source-generated `[OptionsValidator]` for
the attribute rules (`[Required] ClientId`, `[Required] TokenEndpoint`), per inherited fact #1 (no
`Microsoft.Extensions.Options.DataAnnotations`), plus a small hand-written `internal sealed
IValidateOptions<CloudstrapClientCredentialsOptions>` for the parse-shaped rule (TokenEndpoint must be
**absolute**) — the #5 mechanic (a) split. Every failure message names the full
`Cloudstrap:ClientCredentials:*` key and **never echoes a configured value** (AC-CC5). *(Gate 3.)*

**(d) Idempotency (AC-CC10).** `AddCloudstrapClientCredentials` uses `TryAdd`/marker semantics: one ATM client
registration, one provider registration, one options pipeline, no duplicate handlers when called twice — and it
stays additive next to a consumer's own `AddClientCredentialsTokenManagement()` (`cloudstrap` never collides
with consumer client names; edge-case row). *(Gate 3.)*

**(e) `Cloudstrap.TestIdentityProvider` hosting helper — a deliberate, test-only addition to the spec's sketch.**
The spec's Public API Sketch §3 lists `AddCloudstrapTestIdentityProvider` / `MapCloudstrapTestIdentityProvider` /
`TestIdentityProviderOptions` and mandates two hosting modes. Rather than copy-pasting TestHost/Kestrel
boilerplate into three consuming test projects (#9 unit, #9 E2E, then #10/#12/#17), the library also ships one
disposable host helper, `TestIdentityProviderHost`, with `StartInProcess(Action<TestIdentityProviderOptions>)`
(TestHost; exposes `CreateHandler()`, `CreateClient()`, `BaseAddress`, `TokenEndpoint`, and a
`TokenRequestCount` hit counter) and `StartLoopback(int port, Action<TestIdentityProviderOptions>)` (Kestrel on
`http://127.0.0.1:{port}`). This is test infrastructure, not shipped surface; flagged for explicit review at
**Gate 2** because it becomes part of the D-5 one-way door.

**(f) The SUT adopts JWT bearer with `RequireAuthenticatedEndpoints: false`.** Adding `AddCloudstrapJwtBearer`
to the Bff with #5's default fallback policy would 401 all 28 pre-existing anonymous E2E tests. The demo sets
`Cloudstrap:JwtBearer:RequireAuthenticatedEndpoints: false` and protects exactly one endpoint with
`[Authorize]` — which is itself the live demonstration of #5's documented whole-application opt-out. The
README states this explicitly. *(Final gate.)*

**(g) SUT port map.** The test IdP's loopback address is **`http://127.0.0.1:5310`** — 5300 (Bff),
5301–5303 (second-instance tests) and 59999 (dead-port test) are already taken by the harness. The E2E fixture
boots the IdP **in the test process** (mechanic (e) helper, loopback mode) *before* the Bff, and tears it down
after. In attach mode (`CLOUDSTRAP_E2E_BASEURL`) the IdP is still booted by the fixture. A manual `dotnet run`
of the Bff without the IdP still starts (lazy acquisition — spec edge case); the machine endpoints then fail
until an IdP listens on 5310 — recorded in the SUT README. *(Final gate.)*

**(h) `InternalsVisibleTo`** from `Cloudstrap.Authentication.ClientCredentials` to its own test project only
(Extensions/WebApi precedent), so the internal provider, validator and mapper are directly testable. No
cross-package IVT.

**(i) Full-suite VERIFY commands (environment facts).** `dotnet test` is not supported and the `runTests` alias
is not on the agent PATH — every VERIFY runs the built test executables directly. "**Full suite**" below means
all of (Debug paths; append the new ones as their steps create them):

```powershell
src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe
src\Test\UnitTest\Cloudstrap.Observability.Tests\bin\Debug\net10.0\Cloudstrap.Observability.Tests.exe
src\Test\UnitTest\Cloudstrap.Observability.AzureMonitor.Tests\bin\Debug\net10.0\Cloudstrap.Observability.AzureMonitor.Tests.exe
src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe
src\Test\UnitTest\Cloudstrap.WebApi.Tests\bin\Debug\net10.0\Cloudstrap.WebApi.Tests.exe
src\Test\UnitTest\Cloudstrap.TestIdentityProvider.Tests\bin\Debug\net10.0\Cloudstrap.TestIdentityProvider.Tests.exe   # from Step 2
src\Test\UnitTest\Cloudstrap.Authentication.ClientCredentials.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.ClientCredentials.Tests.exe   # from Step 4
src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe
```

plus `dotnet build src/Cloudstrap.sln` (zero warnings/errors) and
`dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

---

## Slice 1 — ⚠️ D-4: the shipped seam is split so both authentication packages can coexist

---

## Step 1 — Two independently registered token providers now coexist on one client: both handlers attach user-first, and a partial registration fails naming exactly the missing flag and its package (D-4; AC-CC13, AC-CC7's mechanism)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope** *(⚠️ breaking change to shipped `Cloudstrap.Extensions` public API — nothing else in the package changes)*:
- `src/Cloudstrap.Extensions/IAccessTokenHandlerProvider.cs` *(delete)*
- `src/Cloudstrap.Extensions/IClientAccessTokenHandlerProvider.cs` *(create)* — single method
  `DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)` — name and
  signature carried over unchanged; XML docs carry the seam contract forward (lazy resolution at pipeline-build
  time, a fresh handler per client, never a pre-set `InnerHandler`; implemented by
  `Cloudstrap.Authentication.ClientCredentials`).
- `src/Cloudstrap.Extensions/IUserAccessTokenHandlerProvider.cs` *(create)* — single method
  `DelegatingHandler CreateUserTokenHandler(string clientName, TokenRequestOptions? tokenRequest)`; implemented
  by `Cloudstrap.Authentication.OpenIdConnect` (#10).
- `src/Cloudstrap.Extensions/AccessTokenHandlerWiring.cs` *(modify)* — resolve **each interface independently**
  at pipeline-build time and implement the spec's 8-row behavior matrix exactly:

  | `AddUserAccessToken` | `AddClientAccessToken` | Registered providers | Behavior |
  |---|---|---|---|
  | false | false | any | No resolve attempted, no handler added. *(unchanged)* |
  | true | false | user present | User handler at position 0. |
  | false | true | client present | Client handler at position 0. |
  | true | true | both present | Both handlers, **user first** (AC-CC13). |
  | true | * | user missing | `InvalidOperationException` naming `…:AddUserAccessToken` + `Cloudstrap.Authentication.OpenIdConnect`. |
  | * | true | client missing | `InvalidOperationException` naming `…:AddClientAccessToken` + `Cloudstrap.Authentication.ClientCredentials`. |
  | true | true | both missing | One exception listing **both** flags and both packages (current aggregated message shape kept). |
  | true | true | exactly one present | Fails naming **only** the missing flag and its package — never a partially authenticated request. |

  The old interface's "a client may set both flags … is the implementation's contract" XML-doc paragraph moves
  onto the wiring, where it is now true by construction (spec §2).
- `src/Cloudstrap.Extensions/ServiceCollectionExtensions.cs` *(modify)* — the XML-doc cross-reference on
  `AddCloudstrapHttpServiceClient` (currently `<see cref="IAccessTokenHandlerProvider"/>`) now names both
  interfaces.
- `src/Cloudstrap.Extensions/README.md` *(modify)* — the "access token seam" section names the two interfaces
  and the per-flag failure behavior.
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AccessTokenHandlerSeamTests.cs` *(modify)* — split
  `RecordingTokenHandlerProvider` into two independent doubles (`RecordingUserTokenHandlerProvider :
  IUserAccessTokenHandlerProvider`, `RecordingClientTokenHandlerProvider : IClientAccessTokenHandlerProvider`,
  sharing `MarkerHandler`); re-register every existing test against the split interfaces; update the
  `FlaggedClient_WithoutProvider_FailsFastNamingFlagAndPackage` message assertion to the new interface name;
  add the regression cases below.
- `src/Test/UnitTest/Cloudstrap.Extensions.Tests/PackageSurfaceTests.cs` *(modify)* — the interface guard
  (currently `Is.EqualTo(new[] { nameof(IAccessTokenHandlerProvider) })`) now pins exactly the two new names.

**RED** *(write these tests first; the honest first failure is the test project failing to compile against the
removed interface — the plan-5 precedent — followed by real red runs once the two interfaces exist but the
wiring still resolves a single provider)*:
- Unit test file: `AccessTokenHandlerSeamTests.cs`
  - `BothFlagsTrue_WithTwoSeparateProviders_AddsBothHandlersUserFirst` — **the regression the old seam could
    not pass** (D-4): two independent doubles registered, both flags set → `X-Token-Kind` sequence is exactly
    `user`, `client`, and each provider was called once with the right client name and parameters (AC-CC13).
  - `BothFlagsTrue_WithOnlyClientProviderRegistered_FailsNamingOnlyTheUserFlagAndItsPackage` — message contains
    `Cloudstrap:HttpClients:Catalog:AddUserAccessToken` and `Cloudstrap.Authentication.OpenIdConnect`, and does
    **not** contain `AddClientAccessToken` (the message must not suggest turning off the client flag would help).
  - `BothFlagsTrue_WithOnlyUserProviderRegistered_FailsNamingOnlyTheClientFlagAndItsPackage` — the mirror case.
  - `BothFlagsTrue_WithNoProviderRegistered_FailsListingBothFlagsAndBothPackages` — the aggregated shape kept.
  - Existing seven tests, re-targeted at the split interfaces, all green again (flag→handler, unflagged
    untouched, lazy registration order, fail-fast naming flag+package, token handler ahead of correlation).
- Unit test file: `PackageSurfaceTests.cs` — the updated interface guard (RED against the old single name).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln   # RED = Cloudstrap.Extensions.Tests fails to compile against the removed interface
  src\Test\UnitTest\Cloudstrap.Extensions.Tests\bin\Debug\net10.0\Cloudstrap.Extensions.Tests.exe --filter "AccessTokenHandlerSeamTests"
  ```

**GREEN**: the two interfaces + the reworked wiring per the matrix. Full XML docs on both interfaces and the
wiring. Nothing else in `Cloudstrap.Extensions` changes — `AddCloudstrapHttpServiceClient`, the registry, the
health-check setup and the correlation handler are untouched (spec §2 closing paragraph).

**DB changes**: none — this repository has no database.

**VERIFY** *(when all green, mark this step's `Done` checkbox — the next plan item is a 🛑 HUMAN GATE, so stop there)*:
1. Test exe → all seam tests pass, including the four new matrix cases: two independently shipped
   authentication packages can now coexist on one client, and every partial registration fails precisely —
   behavior the shipped seam could not deliver before this step.
2. Full suite (Overview mechanic (i)) + `dotnet build src/Cloudstrap.sln` (zero warnings) +
   `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 1: ⚠️ the D-4 breaking change to shipped `Cloudstrap.Extensions` *(covers Step 1 — a dedicated gate: this is a shared-contract change to shipped public API, reviewed on its own before any new-package code builds on it)*

*Executor: STOP here. Present the results and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **breaking change to shipped public API / shared contract** — permitted under the
standing pre-release permission, but `IClientAccessTokenHandlerProvider` / `IUserAccessTokenHandlerProvider` are
the seam #9 fills now and #10 fills next; review the two interfaces against the spec's Public API Sketch §2
verbatim (method names and signatures carried over unchanged, only multiplicity changed) and the wiring against
the 8-row matrix row by row.

- [x] Behavioral verification: test exe output shows the four new matrix cases green (two separate providers →
  both handlers user-first; each one-present/one-missing case naming only the missing flag and its package; both
  missing → the aggregated message) plus the seven pre-existing seam behaviors unchanged.
- [x] Code review: `git diff` on `src/Cloudstrap.Extensions/` is confined to the seam files listed in Scope
  (no other Extensions surface touched); the split test doubles are genuinely independent types; the
  `PackageSurfaceTests` guard now pins exactly the two interfaces; XML docs carry the full seam contract.
- [x] User approved — implementation may continue past this gate

---

## Slice 2 — D-5: a real, lightweight OpenID Connect server lives in the repo for tests to verify against

---

## Step 2 — A test boots a real OpenID Connect server in-process and obtains a signed client-credentials token from its real discovery document (D-5)

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Directory.Packages.props` *(modify)* — pin `OpenIddict.Server.AspNetCore` 7.6.0,
  `OpenIddict.EntityFrameworkCore` 7.6.0, `Microsoft.EntityFrameworkCore.Sqlite` 10.0.x in a new ItemGroup
  commented "Test identity provider only (src/Test/TestIdentityProvider) — never referenced by a shipped
  package" (the `Microsoft.AspNetCore.TestHost` precedent).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/Cloudstrap.TestIdentityProvider.csproj`
  *(create)* — plain library under `src/Test/` (not `*.Tests`, so `src/Test/Directory.Build.props` gives it
  `IsPackable=false` and no MTP wiring); `<FrameworkReference Include="Microsoft.AspNetCore.App" />`;
  `<PackageReference>` to the three pins above and `Microsoft.AspNetCore.TestHost` (for the in-process host
  helper).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderOptions.cs`,
  `TestIdentityProviderClient.cs`, `TestIdentityProviderClaims.cs` *(create)* — the spec's Public API Sketch §3
  options model **verbatim**: `AccessTokenLifetime : TimeSpan` (default 5 minutes — short; tests override it
  shorter), `Issuer : Uri?` (null → derived from the host's own address), `Clients :
  IList<TestIdentityProviderClient>` with `ClientId`, `ClientSecret` (obvious placeholders only), `Scopes :
  IList<string>`, `Audiences : IList<string>`, `TokenClaims : TestIdentityProviderClaims` with `Common` /
  `AccessToken` / `ClientCredentialsToken` as `IDictionary<string, IList<string>>` (JSON arrays — the
  pipe-separator convention is Dropped).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/ServiceCollectionExtensions.cs` *(create)* —
  `AddCloudstrapTestIdentityProvider(this IServiceCollection, Action<TestIdentityProviderOptions>? configure = null)`:
  EF Core `DbContext` on one open per-instance `SqliteConnection` to `Data Source=:memory:` (plan-level pick 1)
  with `EnsureCreated()` + OpenIddict client seeding from the options at startup; OpenIddict server configured
  for the **client-credentials grant only**, token endpoint `/connect/token`, discovery document + JWKS
  (`/.well-known/openid-configuration`), **ephemeral signing key per process**, **access-token encryption
  disabled** so the JWT is validatable by #5, per-client audiences/scopes/claims and the configured
  `AccessTokenLifetime`. Implicit and hybrid flows are not enabled and not configurable (Dropped permanently).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/EndpointRouteBuilderExtensions.cs` *(create)* —
  `MapCloudstrapTestIdentityProvider(this IEndpointRouteBuilder)` wiring whatever pipeline pieces OpenIddict's
  ASP.NET Core host needs (mechanic (a.2): exact calls confirmed in RED).
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/TestIdentityProviderHost.cs` *(create)* —
  mechanic (e): `StartInProcess(...)` (TestHost — `CreateHandler()`, `CreateClient()`, `BaseAddress`,
  `TokenEndpoint`, `TokenRequestCount`) and `StartLoopback(int port, ...)` (Kestrel), both `IDisposable`.
- `src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests/Cloudstrap.TestIdentityProvider.Tests.csproj`
  *(create)* — standard MTP test project (NUnit wiring inherited), `ProjectReference` to the library.
- `src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests/DiscoveryAndTokenTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — both projects under the appropriate `Test` solution folders.

**RED** *(write these tests first; first failure is the new test project failing to compile against missing
types — plan-5 precedent — then real red runs)*:
- Unit test file: `DiscoveryAndTokenTests.cs`
  - `Discovery_Get_ServesTheDocumentWithClientCredentialsGrantOnly` — in-process host; GET
    `/.well-known/openid-configuration` → 200; `grant_types_supported` is exactly `["client_credentials"]`
    (no implicit, no hybrid, no code); `token_endpoint` and `jwks_uri` present.
  - `Jwks_Get_ServesASigningKey` — the advertised `jwks_uri` returns ≥ 1 key usable for signature verification.
  - `TokenEndpoint_WithAConfiguredClient_IssuesASignedJwtWithTheConfiguredAudienceAndScope` — POST
    `grant_type=client_credentials` + the configured id/secret → 200 with an `access_token` that is a **signed,
    unencrypted JWT**: parseable, signature verifies against the fetched JWKS, `iss` equals the host's address,
    `aud` contains the configured audience, the granted scope matches, `client_id` claim present.
  - `TwoInstances_AreFullyIsolated` — two in-process instances: a client configured on one gets
    `invalid_client` from the other, and their JWKS keys differ (per-instance ephemeral key + per-instance
    SQLite connection — the spec's isolation edge case).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.TestIdentityProvider.Tests\bin\Debug\net10.0\Cloudstrap.TestIdentityProvider.Tests.exe --filter "DiscoveryAndTokenTests"
  ```

**GREEN**: the library per Scope (mechanic (a.2) API confirmations made during RED). XML docs throughout
(test-only code still gets them — it is shared infrastructure). All fixture values neutral: `contoso-*` client
ids, `example.com`-style audiences, placeholder secrets.

**DB changes**: none *(the SQLite in-memory store is process-transient test infrastructure, not a database
deliverable)*.

**VERIFY**:
1. Test exe → all pass: the repo can now boot a real, standards-shaped OpenID Connect server in-process and
   obtain a verifiable client-credentials JWT from it — capability that did not exist before.
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 3 — The identity provider is a controllable test double: claims are configuration data, the token lifetime is short and exact, bad credentials fail standards-shaped, and it serves real HTTP for the E2E fixture

- [x] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/` *(modify — claim shaping, lifetime wiring,
  error paths, loopback polish as needed by RED)*
- `src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests/ClaimShapingAndErrorTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `ClaimShapingAndErrorTests.cs`
  - `TokenClaims_CommonAndClientCredentialsSets_LandInTheAccessToken` — a client configured with `Common` and
    `ClientCredentialsToken` claim sets (including one multi-valued claim declared as a JSON array, e.g.
    `"role": ["reader", "writer"]`) → the issued JWT carries all of them, the multi-valued claim as a proper
    array (the pipe-separator and `schemas.` conventions are provably absent).
  - `AccessTokenLifetime_Configured_DrivesTheTokenExpiry` — `AccessTokenLifetime = 120 s` → the token's
    `exp − iat` and the response's `expires_in` are 120 (the lever AC-CC3 pulls).
  - `TokenEndpoint_WithAWrongSecret_ReturnsInvalidClient` — standards-shaped error payload with
    `error = "invalid_client"`, no token issued (what AC-CC8 asserts against).
  - `TokenEndpoint_WithAnUnknownClient_ReturnsInvalidClient` — same contract.
  - `Loopback_Host_ServesDiscoveryAndTokensOverRealHttp` — `TestIdentityProviderHost.StartLoopback` on a free
    port → a plain `HttpClient` fetches the discovery document over real HTTP, the advertised issuer equals the
    bound loopback address, and a token request succeeds (proves the E2E hosting mode four steps before the E2E
    fixture depends on it).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.TestIdentityProvider.Tests\bin\Debug\net10.0\Cloudstrap.TestIdentityProvider.Tests.exe --filter "ClaimShapingAndErrorTests"
  ```

**GREEN**: the Scope items — claim-set shaping on token issuance, lifetime plumbed to OpenIddict's token
options, loopback hosting hardened.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: the IdP is now a fully data-driven test double — claims, lifetime and failure modes are
   all controllable from `TestIdentityProviderOptions`, in-process and over real HTTP.
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 2: ⚠️ the test identity provider's shape is a one-way door *(covers Steps 2–3)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **D-5 one-way door** — #10 (interactive flows), #12 (Blazor Server) and #17
(proxy) will extend this project rather than replace it; review the options model against the spec sketch §3
verbatim, the project location (`src/Test/TestIdentityProvider/`), the two hosting modes, and mechanic (e)'s
`TestIdentityProviderHost` helper (a plan-level addition to the sketch — confirm or direct its removal) ·
**plan-level pick 1** — SQLite in-memory store (vs the source's EF InMemory): confirm ·
**new test-only dependencies** — OpenIddict 7.6.0 (Apache-2.0) + EF Core Sqlite (MIT), rule-4 review;
`src/Test/Directory.Build.props` guarantees `IsPackable=false`, and the Step 9 closure guard will make
"never in a shipped closure" permanent · **De-NIHDI** — implicit/hybrid not enabled and not representable;
fixtures neutral; zero source-STS content (the reviewer greps `src/Test/TestIdentityProvider` for
`(?i)nihdi|riziv|keycloak` and checks for any personal data → nothing).

- [ ] Behavioral verification: the nine `Cloudstrap.TestIdentityProvider.Tests` methods pass — discovery with
  the single grant, JWKS, a verifiable signed JWT with configured audience/scope/claims, exact lifetime,
  `invalid_client` on wrong/unknown credentials, instance isolation, and real-HTTP loopback serving.
- [ ] Code review: options model + public helper surface vs spec sketch; OpenIddict server configuration
  (client-credentials only, ephemeral key, encryption disabled — and nothing else enabled); the CPM pins and
  the "test-only" ItemGroup comment; identifier/personal-data grep clean.
- [ ] User approved — implementation may continue past this gate

---

## Slice 3 — The package: one registration call and a flagged client transparently carries a cached, renewed bearer token

---

## Step 4 — `AddCloudstrapClientCredentials()`: a flagged typed client carries a bearer token issued by the real test IdP with no other consumer change; startup validation names exact keys and never echoes the secret; registration is idempotent (AC-CC1, AC-CC4, AC-CC10, AC-CC5-validation) ⚠️ *(auth risk area begins)*

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Directory.Packages.props` *(modify)* — pin `Duende.AccessTokenManagement` 4.2.0 (runtime; executor
  re-confirms the current 4.2.x patch on nuget.org at pin time and reports at Gate 3), plus any transitive pins
  restore demands under transitive pinning (Overview table notes — including the
  `System.IdentityModel.Tokens.Jwt` < 9.0.0 constraint).
- `src/Cloudstrap.Authentication.ClientCredentials/Cloudstrap.Authentication.ClientCredentials.csproj`
  *(create)* — `net10.0`, `GeneratePackageOnBuild` + `GenerateDocumentationFile`; `<ProjectReference>` to
  `..\Cloudstrap.Core\` and `..\Cloudstrap.Extensions\` (the seam; ⚠️ inherits Extensions'
  `Microsoft.AspNetCore.App` framework reference — README notes it in Step 9); `<PackageReference>`
  `Duende.AccessTokenManagement`; `<InternalsVisibleTo Include="Cloudstrap.Authentication.ClientCredentials.Tests" />`
  (mechanic (h)). Description/tags/README metadata land in Step 9.
- `src/Cloudstrap.Authentication.ClientCredentials/CloudstrapClientCredentialsOptions.cs` *(create)* — spec
  sketch §1 verbatim: `const string SectionName = "Cloudstrap:ClientCredentials"`; `TokenEndpoint : Uri?`
  (required, absolute — no authority-plus-path convention); `ClientId : string` (required);
  `ClientSecret : string?` (**optional** — D-1); `Scope : string?`; `Resource : string?`;
  `TokenCache : TokenCacheMode = TokenCacheMode.Isolated` (D-3);
  `BackchannelHttpClientName : string = "cloudstrap-clientcredentials"`.
- `src/Cloudstrap.Authentication.ClientCredentials/TokenCacheMode.cs` *(create)* — `Isolated` / `Shared`, XML
  docs stating the D-3 trade-off.
- `src/Cloudstrap.Authentication.ClientCredentials/CloudstrapClientCredentialsOptionsValidator.cs` *(create,
  internal)* — mechanic (c): source-generated `[OptionsValidator]` for the `[Required]` rules + a hand-written
  `IValidateOptions<>` for "TokenEndpoint must be absolute", every message naming the full key, never a value.
- `src/Cloudstrap.Authentication.ClientCredentials/CloudstrapClientCredentialsConfigurator.cs` *(create)* —
  `Client : Action<ClientCredentialsClient>?` (runs **last**, final say),
  `TokenManagement : Action<ClientCredentialsTokenManagementOptions>?`,
  `Backchannel : Action<IHttpClientBuilder>?`.
- `src/Cloudstrap.Authentication.ClientCredentials/CloudstrapClientCredentials.cs` *(create)* — static;
  `const string TokenClientName = "cloudstrap"` (so a consumer can inject Duende's
  `IClientCredentialsTokenManager` and ask for a token itself — no Cloudstrap facade).
- `src/Cloudstrap.Authentication.ClientCredentials/ServiceCollectionExtensions.cs` *(create)* —
  `AddCloudstrapClientCredentials(this IServiceCollection services, Action<CloudstrapClientCredentialsConfigurator>? configure = null) : IServiceCollection`:
  guards; **idempotent** (mechanic (d)); bind + `ValidateOnStart` the options; Duende
  `AddClientCredentialsTokenManagement()` + `.AddClient(TokenClientName, …)` configured from the options
  (endpoint, id, secret, scope, resource, `HttpClientName = BackchannelHttpClientName`); register the named
  backchannel `HttpClient` and apply `configurator.Backchannel` to it; register the **isolated keyed token
  cache** (D-3 default — Duende's `ServiceProviderKeys.ClientCredentialsTokenCache` extension point, a
  memory-only `HybridCache` instance; the `Shared` mode + startup log line are proven in Step 7); apply
  `configurator.TokenManagement` then `configurator.Client` last; register the internal
  `IClientAccessTokenHandlerProvider`. Registers **no** inbound authentication, **no** authorization policy,
  **no** user-token provider.
- `src/Cloudstrap.Authentication.ClientCredentials/ClientCredentialsAccessTokenHandlerProvider.cs` *(create,
  internal, sealed)* — fills the D-4 seam: `CreateClientTokenHandler(clientName, tokenRequest)` returns a fresh
  ATM client-credentials access-token handler for `TokenClientName` with the mapped per-client parameters
  (mechanic (a.1): mirror ATM v4's own handler construction — dependencies from `IServiceProvider`, handler
  new-ed per call, no pre-set `InnerHandler`).
- `src/Cloudstrap.Authentication.ClientCredentials/TokenRequestParameterMapper.cs` *(create, internal, static)* —
  `Cloudstrap.Core.TokenRequestOptions` → Duende `TokenRequestParameters`: `Scope`, `Resource`,
  `ForceRenewal → ForceTokenRenewal`; `SignInScheme`/`ChallengeScheme` deliberately unmapped (their one-time
  warning is Step 5's).
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/Cloudstrap.Authentication.ClientCredentials.Tests.csproj`
  *(create)* — `ProjectReference` to the package **and** to `Cloudstrap.TestIdentityProvider`; version-less
  `PackageReference`s mirroring `Cloudstrap.Extensions.Tests` (Hosting, Configuration, TestHost).
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/Infrastructure/ClientCredentialsTestHost.cs`
  *(create)* — mechanic (b) fixture: `HostApplicationBuilder` + in-memory `Cloudstrap:` config (a `Catalog`
  client section + a `Cloudstrap:ClientCredentials` section pointed at an in-process IdP), the IdP started via
  `TestIdentityProviderHost.StartInProcess`, its handler wired as the backchannel's primary handler through
  `configurator.Backchannel`, a `CapturingPrimaryHandler` on the typed client (the seam-tests pattern).
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/RegistrationTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/TokenAttachmentTests.cs` *(create)*
- `src/Cloudstrap.sln` *(modify)* — package at the solution root, test project under `Test\UnitTest`.

**RED** *(write these tests first; first failure = the new test project failing to compile, then real red runs)*:
- Unit test file: `TokenAttachmentTests.cs`
  - `FlaggedClient_AfterOneRegistrationCall_CarriesABearerTokenIssuedByTheTestIdp` — the AC-CC1 headline: a
    typed client registered through the **shipped** `AddCloudstrapHttpServiceClient` with only
    `Cloudstrap:HttpClients:Catalog:AddClientAccessToken = true` in config; after
    `AddCloudstrapClientCredentials()` the captured outbound request carries `Authorization: Bearer <jwt>`, and
    the JWT's `iss` is the in-process IdP and its `client_id` the configured one — **no consumer code change
    beyond the registration call** (the test contains none).
  - `UnflaggedClient_IsUntouched` — a second client without the flag sends no `Authorization` header.
  - `FlaggedClient_StillCarriesTheCorrelationHeader` — `X-Correlation-ID` still present downstream of the token
    handler (the #4 ordering preserved; AC-CC1's correlation clause).
- Unit test file: `RegistrationTests.cs`
  - `MissingSection_FailsStartupNamingTheSection` · `MissingTokenEndpoint_FailsNamingTheKey` ·
    `MissingClientId_FailsNamingTheKey` · `RelativeTokenEndpoint_FailsNamingTheKey` — each failure message
    contains the exact `Cloudstrap:ClientCredentials:*` key (AC-CC4).
  - `ValidationFailure_NeverEchoesTheConfiguredSecret` — a secret is configured, another key is broken; the
    failure text does not contain the secret value (AC-CC5, validation half).
  - `CalledTwice_RegistersEverythingOnce` — two calls → the flagged client's request carries exactly one
    `Authorization` header value, the token endpoint is hit once, one provider registration (AC-CC10).
  - `ConsumerOwnDuendeRegistration_CoexistsWithCloudstraps` — the consumer already called
    `AddClientCredentialsTokenManagement()` with a client of their own name; both work; `cloudstrap` does not
    collide (spec edge-case row).
  - `OnNullServices_ThrowsArgumentNullException` — guard clause.
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.ClientCredentials.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.ClientCredentials.Tests.exe --filter "TokenAttachmentTests"
  ```

**GREEN**: the package per Scope (mechanic (a.1) confirmations during RED). Full XML docs on every public
member, naming the exact configuration keys.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an application that already had the flag in configuration now gets transparent bearer
   tokens from a real token endpoint by adding one package reference and one call — the deliverable's headline
   behavior, previously impossible (AC-CC1, AC-CC4, AC-CC10).
2. Full suite + build (zero warnings) + format (exit 0).
3. `dotnet build src/Cloudstrap.sln -c Release` → a `Cloudstrap.Authentication.ClientCredentials.*.nupkg`
   appears (packable from day one; metadata completed in Step 9).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 5 — Tokens are cached, renewed and scoped: one token request per lifetime, expiry renews transparently, per-client Scope/Resource stay separate, a 401 triggers one refresh through the intact inner chain, and user-token settings on a client token warn once (AC-CC2, AC-CC3/AC-A2, AC-CC6, AC-CC9; plan-level pick 2 confirmed)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.ClientCredentials/` *(modify — whatever the lifecycle behaviors need:
  per-client parameter flow through the provider, the one-time `SignInScheme`/`ChallengeScheme` and
  `ForceRenewal` startup warnings)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/TokenLifecycleTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/Infrastructure/ClientCredentialsTestHost.cs`
  *(modify — token-endpoint hit counting via `TestIdentityProviderHost.TokenRequestCount`, a capturing
  `ILoggerProvider`, a scripted primary handler for the 401 scenario)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `TokenLifecycleTests.cs`
  - `SeveralRequestsWithinOneLifetime_CallTheTokenEndpointExactlyOnce` — three outbound calls, IdP hit counter
    = 1, same token value on all three (AC-CC2).
  - `ElapsedLifetime_RenewsTransparentlyWithExactlyOneNewTokenRequest` — IdP `AccessTokenLifetime` = 1–2 s,
    ATM cache lifetime buffer zeroed via `configurator.TokenManagement`; call → wait past expiry (retry-polled,
    ≤ ~3 s) → call: hit counter = 2, both calls succeeded, the caller observed no failure (AC-CC3 — the
    mechanised AC-A2). **Plan-level pick 2 confirmation lives here**: the RED includes the bounded
    `FakeTimeProvider` spike; outcome (fake clock adopted or short-lifetime strategy confirmed) reported at
    Gate 3.
  - `TwoFlaggedClientsWithDifferentScopes_GetTwoDistinctSeparatelyCachedTokens` — per-client
    `TokenRequestParameters:Scope`/`Resource` in config → two token requests carrying the respective values,
    two different tokens, and neither client ever sends the other's (AC-CC6).
  - `TwoFlaggedClientsWithIdenticalParameters_ShareOneCachedToken` — hit counter = 1 (spec edge-case row: the
    cache key derives from the token request, not the client name).
  - `Response401_TriggersExactlyOneRefreshAndReExecutesTheInnerChain` — scripted downstream: 401 for the first
    token, 200 for a renewed one; a consumer resilience handler added via `ConfigureHttpClientDefaults`
    (`Microsoft.Extensions.Http.Resilience`, already pinned) plus the correlation handler; assert the retry
    went through both (marker + header), the resilience handler ran once per attempt (neither duplicated nor
    bypassed), the token handler sat outermost, hit counter = 2 (AC-CC9 / AC-ASP3 posture).
  - `StaticForceRenewal_BypassesTheCacheAndWarnsOnceAtStartup` — every request fetches a fresh token; exactly
    one warning naming `…:TokenRequestParameters:ForceRenewal` (spec edge-case row).
  - `SignInSchemeOnAClientTokenRequest_IsIgnoredWithOneWarningNamingTheKey` — configured `SignInScheme`; the
    token request is unaffected; exactly one warning naming the key; same for `ChallengeScheme` (Behaviors
    row / Deliberate Behavior Change 8).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.ClientCredentials.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.ClientCredentials.Tests.exe --filter "TokenLifecycleTests"
  ```

**GREEN**: the Scope items — the caching/renewal machinery itself is Duende's (nothing bespoke); Cloudstrap
adds only the parameter mapping, the warnings, and the tests that pin the library's behavior to the ACs.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: tokens are now provably reused within a lifetime, renewed exactly once at expiry with
   no caller-visible failure, scoped per client without leakage, and refreshed once on a 401 through an intact
   consumer pipeline (AC-CC2, AC-CC3, AC-CC6, AC-CC9).
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 3: ⚠️ the package's public API and its Duende registration *(covers Steps 4–5 — the first half of the mandated auth-surface review)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

⚠️ **Risk areas at this gate**: **public API one-way door** — `AddCloudstrapClientCredentials`,
`CloudstrapClientCredentialsOptions`, `TokenCacheMode`, `CloudstrapClientCredentialsConfigurator` and
`CloudstrapClientCredentials.TokenClientName` are the contract #10, #12 and #17 build on: review verbatim
against the spec's Public API Sketch §1 (including everything "deliberately not shipped") · **auth code** —
the registration path that binds a credential and configures Duende: review line by line; no credential value
may be logged or echoed · **new runtime dependency** `Duende.AccessTokenManagement` — rule-4 review: exact pin,
Apache-2.0 confirmed, transitive promotions (Hybrid cache, Http.Resilience now runtime,
`System.IdentityModel.Tokens.Jwt` < 9.0.0) inspected in the restored graph · **mechanic (a.1)** ATM API-name
confirmations and **plan-level pick 2** outcome (fake clock vs short lifetimes) reported here.

- [ ] Behavioral verification: test exe output shows — the no-consumer-change bearer attachment with a real JWT
  from the in-process IdP, the untouched unflagged client, correlation intact, all four exact-key validation
  failures, the secret-free validation message, double-registration idempotence, Duende-coexistence and the
  guard (Step 4); single-token-per-lifetime, transparent renewal, scope separation and sharing, the
  401-refresh-through-intact-chain proof, and the two one-time warnings (Step 5).
- [ ] Code review (auth + API): signatures vs spec sketch §1 verbatim; `internal` by default, sealed, full XML
  docs; the internal provider returns a fresh handler per call with no `InnerHandler`; the mapper ignores
  exactly the two user-token properties; `dotnet list src/Cloudstrap.Authentication.ClientCredentials/Cloudstrap.Authentication.ClientCredentials.csproj package`
  → `Duende.AccessTokenManagement` + the two project references and nothing else direct; zero `OpenIddict.*`
  anywhere near the shipped project.
- [ ] User approved — implementation may continue past this gate

---

## Slice 4 — Hardened: loud lazy failure, secret-free operation, an isolated cache, proven interop with #5, and a publishable package

---

## Step 6 — Failure is loud, lazy and secret-free: a rejected, failing or unreachable IdP never lets an unauthenticated request out and never leaks the secret; the user-token flag names #10's package; a consumer assertion service replaces the secret (AC-CC7, AC-CC8, AC-CC11, AC-CC5-logs)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.ClientCredentials/` *(modify — acquisition-failure surfacing per AC-CC8's
  message contract, the credential-type startup log line, whatever the assertion path needs)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/FailureModeTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/CredentialTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `FailureModeTests.cs`
  - `RejectedCredential_FailsNamingAcquisitionAndTheEndpoint_AndSendsNothingDownstream` — IdP configured to
    return `invalid_client`; the outbound call throws with a message identifying token acquisition and the
    configured token endpoint; the capturing primary handler saw **zero** requests; the failure is logged once
    (AC-CC8).
  - `TokenEndpoint500_SameContract` and `TokenEndpointUnreachable_SameContract` — a failing/throwing
    backchannel produces the same loud, request-free, once-logged outcome (AC-CC8's other two arms).
  - `StartupSucceedsWithTheIdpDown_TheFirstCallFailsInstead` — the host builds and starts with no IdP anywhere
    (lazy acquisition — spec edge case: a transient IdP outage must not stop a service from starting).
  - `UserFlagWithOnlyThisPackageInstalled_FailsFastNamingOnlyTheUserFlagAndTheOpenIdConnectPackage` — the real
    package registered (its client provider fills the seam), `AddUserAccessToken = true` (alone and together
    with the client flag): client creation fails **before any request**, naming `…:AddUserAccessToken` and
    `Cloudstrap.Authentication.OpenIdConnect` and **not** suggesting the client flag (AC-CC7 — Step 1 proved
    the mechanism with doubles; this proves it with the shipped provider).
- Unit test file: `CredentialTests.cs`
  - `ConsumerRegisteredClientAssertionService_WithNoSecret_SendsTheAssertionAndSucceeds` — no `ClientSecret`
    configured; a recording `IClientAssertionService` double registered by the consumer; startup validation
    passes; the captured token request form carries `client_assertion` and no `client_secret`; the outbound
    call succeeds; the double was invoked (Cloudstrap never overwrote it). *(This one case scripts the token
    response at the backchannel instead of using the real IdP — teaching the test IdP `private_key_jwt` belongs
    to no deliverable; the assertion-carrying request is the AC's observable.)* (AC-CC11, D-1)
  - `BothSecretAndAssertionPresent_TheAssertionWins_AndTheStartupLogStatesTheCredentialType` — Duende's own
    precedence, made visible (spec edge-case row).
  - `SecretValueNeverAppearsInLogsOrExceptions` — with Debug-level capture across startup, a successful
    acquisition and a failed one: the configured secret value appears in no log line, no exception message and
    no inner exception (AC-CC5, logs half).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.ClientCredentials.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.ClientCredentials.Tests.exe --filter "FailureModeTests"
  ```

**GREEN**: the Scope items — surfacing ATM's failure with the AC-CC8 message contract (endpoint named, secret
never), the one-time credential-type log line, nothing else.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: every failure mode is now loud, early and secret-free; secret-free operation is a
   proven first-class path; a mis-flagged client points precisely at #10 (AC-CC5, AC-CC7, AC-CC8, AC-CC11).
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 7 — Tokens never leak into the application's caches, and the token backchannel is a quiet, dedicated client: isolated `HybridCache` by default with the mode logged, `Shared` as the documented opt-in, no correlation header to the IdP, no token on the liveness probe (AC-CC12, D-3; Behaviors rows)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.ClientCredentials/ServiceCollectionExtensions.cs` *(modify — `Shared` mode
  wiring, the startup log line stating the mode in force)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/CachePostureTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/BackchannelTests.cs` *(create)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `CachePostureTests.cs`
  - `DefaultIsolatedMode_ARegisteredIDistributedCacheNeverReceivesAnything` — the app registers a recording
    `IDistributedCache` for its own purposes; tokens are acquired, cached and reused; the recording cache saw
    **zero writes** (AC-CC12 — the D-3 headline).
  - `SharedMode_TokensUseTheApplicationCacheIncludingItsDistributedTier` — `TokenCache = Shared` → the
    recording `IDistributedCache` (HybridCache's L2) receives writes during acquisition (the opt-in works and
    is observable).
  - `SharedModeWithoutADistributedCache_WorksWithoutErrorOrExtraWarning` — spec edge-case row.
  - `StartupLog_StatesTheTokenCacheModeInForce` — exactly one log line naming `Isolated` (default run) and one
    naming `Shared` (opt-in run) — "visible, not magic" (D-3).
- Unit test file: `BackchannelTests.cs`
  - `TokenEndpointRequest_CarriesNoCorrelationHeader` — ambient correlation id set; the captured token request
    has no `X-Correlation-ID`, while the consumer client's request still does (Deliberate Behavior Change 10).
  - `BackchannelHook_ReachesOnlyTheTokenEndpointClient` — `configurator.Backchannel` adds a marker handler; the
    token request carries its mark, the flagged consumer client's request does not.
  - `BackchannelHttpClientName_IsOverridable` — a renamed backchannel client is the one that calls the IdP.
  - `LivenessProbeClient_SendsNoToken` — a flagged client with `EnableHealthCheck = true`: the `{name}-liveness`
    probe request carries no `Authorization` header while the client's own requests do (Behaviors row /
    Deliberate Behavior Change 9 — true by construction in #4's wiring, pinned here forever).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.ClientCredentials.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.ClientCredentials.Tests.exe --filter "CachePostureTests"
  ```

**GREEN**: the Scope items — the `Shared` branch registers nothing extra (the application `HybridCache` is
simply used), the `Isolated` branch keeps the keyed memory-only instance from Step 4, one `ILogger` line states
the mode.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: an application `IDistributedCache` provably never sees a token by default, the opt-in
   provably works, the mode is visible in the log, and the backchannel behaves exactly per the Behaviors table
   (AC-CC12 + three convention rows).
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 8 — Acquisition and validation interoperate end to end: a #5-protected API accepts the acquired token after genuine discovery-document and JWKS retrieval (AC-CC14, in-process)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/Cloudstrap.Authentication.ClientCredentials.Tests.csproj`
  *(modify — add `ProjectReference` to `Cloudstrap.WebApi` (#5) and `Microsoft.AspNetCore.TestHost` if not
  already present — test-only)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/EndToEndInteropTests.cs` *(create)*
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/Infrastructure/ProtectedApiHost.cs`
  *(create — a TestServer-hosted `WebApplication` using `AddCloudstrapWebApi` + `AddCloudstrapJwtBearer`
  (Authority = the in-process IdP's address; the IdP's `HttpMessageHandler` injected as the JwtBearer metadata
  backchannel through the `Action<JwtBearerOptions>` hook — mechanic (a.3)), one `[Authorize]` controller
  action echoing the caller's `client_id`/`scope` claims; Development environment so `RequireHttpsMetadata`
  follows #5's exemption)*

**RED** *(write these tests first, run them, confirm they fail)*:
- Unit test file: `EndToEndInteropTests.cs`
  - `AcquiredToken_IsAcceptedByACloudstrapJwtBearerProtectedApi` — three real in-process hosts chained: the
    test IdP → the protected #5 API (validating against the IdP's **real discovery document and JWKS**) → the
    consumer host whose flagged typed client's primary handler routes to the API's TestServer handler and whose
    ATM backchannel routes to the IdP. The consumer's call returns **200** and the API saw the configured
    `client_id` — acquisition and validation proven end to end against a real OpenID Connect server, no
    sockets (AC-CC14's integration half).
  - `ProtectedApi_WithoutAToken_Returns401` — the same API called by an unflagged client → 401 (the control
    proving validation is real, not permissive).
  - `TokenMintedForAnotherAudience_IsRejectedWith401` — a second IdP client with a different audience; the
    flagged consumer using it gets 401 from the API (the interop actually validates, end to end).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.ClientCredentials.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.ClientCredentials.Tests.exe --filter "EndToEndInteropTests"
  ```

**GREEN**: the two infrastructure pieces per Scope; **no production code change expected** — this step proves
the two shipped halves (#9 acquisition, #5 validation) interoperate; any mismatch it surfaces is fixed at the
source and reported at Gate 4.

**DB changes**: none.

**VERIFY**:
1. Test exe → all pass: a token acquired by this package is now proven acceptable to a Cloudstrap-protected API
   via genuine OIDC metadata — the assertion D-5 existed to make possible (AC-CC14).
2. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## Step 9 — The package is publishable and guarded forever: metadata, README, and permanent tripwires on the surface, the closure and the forbidden identifiers (AC-CC15, AC-ASP2, AC-A3)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Cloudstrap.Authentication.ClientCredentials/Cloudstrap.Authentication.ClientCredentials.csproj`
  *(modify)* — `<Description>` (machine-to-machine OAuth 2.0 client-credentials tokens on Duende
  AccessTokenManagement: one call, transparent caching and renewal on every flagged Cloudstrap typed client,
  isolated token cache by default, secret-free client-assertion support), `<PackageTags>` (…;oauth2;
  client-credentials;token;duende;machine-to-machine), `<PackageReadmeFile>` + packed `README.md`.
- `src/Cloudstrap.Authentication.ClientCredentials/README.md` *(create)* — quick start (config flag +
  `AddCloudstrapClientCredentials()`); the `Cloudstrap:ClientCredentials` settings table; **secret-handling
  guidance** (the secret is a configuration value: KeyVault via #4, environment variables, user-secrets —
  never `appsettings.json`; every example a placeholder; the `IClientAssertionService` secret-free path);
  **the TokenCache trade-off** (Isolated default vs `Shared` — fewer token requests across instances vs bearer
  tokens at rest in a shared store, pointing at Duende's cache-encryption guidance); **how to read the token
  endpoint from the IdP's `/.well-known/openid-configuration`** (documented manual step — no discovery code
  ships); per-client `TokenRequestParameters` overrides and the ignored user-token settings; the
  `cloudstrap` token-client name + direct `IClientCredentialsTokenManager` injection; the backchannel
  conventions (no correlation header, `configurator.Backchannel`); probe clients get no token; lazy
  acquisition (IdP down ≠ startup failure); missing `expires_in` → ATM's default lifetime; ⚠️ the
  `System.IdentityModel.Tokens.Jwt` < 9.0.0 pin constraint; the inherited `Microsoft.AspNetCore.App`
  framework-reference note; the Aspire posture (no overlap; resilience untouched).
- `src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests/PackageSurfaceTests.cs` *(create)* —
  permanent guards mirroring the Extensions/WebApi precedent:
  - `ReferencedAssemblies_MatchTheApprovedClosure` — every referenced assembly starts with `System`,
    `Microsoft.` or `Duende.` or equals `Cloudstrap.Core`/`Cloudstrap.Extensions`; explicitly **zero** names
    starting `OpenIddict`, `Microsoft.EntityFrameworkCore`, `Aspire`, `Nihdi`, `NSwag`, `LanguageExt`
    (AC-CC15, AC-ASP2, AC-A3 made permanent).
  - `PublicTypes_AreSealedOrStaticAndInTheSingleApprovedNamespace` — namespace
    `Cloudstrap.Authentication.ClientCredentials` only; no public interfaces (this package publishes none —
    the seam interfaces live in Extensions).
  - `PublicTypes_ContainNoForbiddenIdentifiers` — no public type/member matches `(?i)nihdi|riziv|keycloak`.

**RED** *(guard tests are written and run first but, as tripwires against already-correct code, may pass
immediately — the honest failing state is in the artifacts: before GREEN the Release nupkg has no
README/description/tags; the plan-2/3/4/5 precedent)*:
- Unit test file: `PackageSurfaceTests.cs` (the three guards above).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\UnitTest\Cloudstrap.Authentication.ClientCredentials.Tests\bin\Debug\net10.0\Cloudstrap.Authentication.ClientCredentials.Tests.exe --filter "PackageSurfaceTests"
  ```

**GREEN**: the csproj metadata and `README.md` per Scope.

**DB changes**: none.

**VERIFY**:
1. Test exe (full run) → all tests pass including the three new guards.
2. `dotnet build src/Cloudstrap.sln -c Release` → expand a `.zip` copy of
   `src/Cloudstrap.Authentication.ClientCredentials/bin/Release/Cloudstrap.Authentication.ClientCredentials.<version>.nupkg`
   → contains `README.md`, `icon.png`, `lib/net10.0/*.dll` **and** `.xml`; the nuspec shows the MIT license
   expression, repository URL, and a dependency list with `Duende.AccessTokenManagement` + the promoted
   transitives and **no** `OpenIddict.*`, no EF Core, no `Nihdi.*`, no `Aspire.*` (AC-CC15, AC-ASP2).
3. **Identifier + personal-data sweep** (the whole deliverable's new trees):
   ```powershell
   Get-ChildItem -Recurse -File -Path src/Cloudstrap.Authentication.ClientCredentials, src/Test/TestIdentityProvider, src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests, src/Test/UnitTest/Cloudstrap.TestIdentityProvider.Tests |
     Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
     Select-String -Pattern '(?i)(nihdi|riziv|keycloak)'
   ```
   → zero matches (guard-test tripwire patterns are self-referential and excluded by reading the hits);
   solution-wide `Nihdi.AspNetCore` search still empty (AC-A3); the reviewer additionally confirms at the gate
   that no personal data from the source STS fixture appears anywhere (the plan deliberately does not reproduce
   those values to grep for).
4. Full suite + build (zero warnings) + format (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — end of Slice 4: ⚠️ AUTH SURFACE SIGN-OFF — hardening, cache posture, interop and closure *(covers Steps 6–9 — the dedicated auth-surface gate mandated for this deliverable, before the SUT demonstration slice)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step. This gate is a mandatory human review under CLAUDE.md's risk-area rule: auth code + credential handling + the new runtime dependency, signed off as a whole before the running-app demo.*

⚠️ **Risk areas at this gate**: **credential handling end to end** — the secret's whole journey (configuration →
Duende → token request) reviewed with Steps 4–6 evidence: never logged, never echoed, never required (assertion
path live), never sent nowhere-ward (no request without a token) · **D-3 cache posture** — the isolated default
and the observable `Shared` opt-in: confirm the posture once more with the AC-CC12 proofs on screen ·
**AC-CC14 interop** — the three-host chain evidence: real discovery, real JWKS, real 401s · **closure and
hygiene** — the Release nupkg contents, the closure guard, the identifier sweep, and the README's
secret-handling + TokenCache + JWT-pin sections · any executor deviations reported in Steps 6–9.

- [ ] Behavioral verification: test exe output shows — the three loud lazy failure contracts with zero
  downstream requests, the user-flag fail-fast naming #10, the assertion-carrying request without a secret, the
  assertion-wins log line, the secret-absent log sweep (Step 6); the zero-writes isolated cache, the observable
  Shared opt-in, the no-error no-cache edge, the mode log line, the quiet backchannel trio and the token-less
  probe (Step 7); the 200/401/wrong-audience interop chain (Step 8); the three permanent guards plus the
  artifact checks (Step 9).
- [ ] Code review (auth): the failure-surfacing code never embeds a credential or token value in any message;
  the credential-type and cache-mode log lines contain names/modes only; `EndToEndInteropTests` genuinely
  fetches metadata from the IdP (no pre-seeded configuration); README accuracy against as-built behavior.
- [ ] ⚠️ Dependency + identifier review: restored-graph inspection for the shipped package (Duende + promoted
  transitives, `System.IdentityModel.Tokens.Jwt` within `[8.0.1, 9.0.0)`); zero `OpenIddict.*`/EF Core in the
  shipped closure; sweeps from Step 9 VERIFY re-confirmed; no personal data anywhere.
- [ ] User approved — implementation may continue past this gate

---

## Slice 5 — Demonstration: the WASM SUT acquires machine tokens for its outbound client, and its protected endpoint validates them, live

---

## Step 10 — The Bff's flagged `SelfApi` client calls a JWT-protected endpoint on itself with a bearer token issued by the test IdP running on loopback — proven through the real running app while all 28 pre-existing E2E tests stay green (AC-CC16; AC-CC14 live)

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

**Scope**:
- `src/Test/WasmTestProject/src/Host/Bff/Cloudstrap.WasmTestProject.Host.Bff.csproj` *(modify —
  `ProjectReference` to `Cloudstrap.Authentication.ClientCredentials`)*
- `src/Test/WasmTestProject/src/Host/Bff/Program.cs` *(modify — `builder.Services.AddCloudstrapClientCredentials();`
  + #5's `AddCloudstrapJwtBearer` registration (the shipped signature); the "deliberately absent …
  arrives with #9/#10" comment becomes the #9 demo note, with interactive login still pointed at #10)*
- `src/Test/WasmTestProject/src/Host/Bff/appsettings.json` *(modify —
  `Cloudstrap:ClientCredentials` (`TokenEndpoint: "http://127.0.0.1:5310/connect/token"`,
  `ClientId: "wasmtestproject-bff"`, `ClientSecret: "local-e2e-placeholder-secret"` — an obvious placeholder
  for a local-only test IdP, per the spec's fixture rule, with a comment saying exactly that,
  `Scope: "selfapi"`); `Cloudstrap:JwtBearer` (`Authority: "http://127.0.0.1:5310"`,
  `Audience: "wasmtestproject-selfapi"`, `RequireAuthenticatedEndpoints: false` — mechanic (f), the #5
  documented whole-app opt-out, so the 28 anonymous tests stay green);
  `Cloudstrap:HttpClients:SelfApi:AddClientAccessToken: true` — **the AC-CC1 opt-in, config only**)*
- `src/Test/WasmTestProject/src/Contracts/Cloudstrap.WasmTestProject.Contracts/MachineStatusDto.cs` *(create —
  `record MachineStatusDto(string ClientId, string Issuer, string Scope)` echoed from validated claims)*
- `src/Test/WasmTestProject/src/Host/Bff/Controllers/MachineController.cs` *(create —
  `[ApiVersion("1.0")]`, route `api/v{version:apiVersion}/machine`: `GET status` `[Authorize]` returns
  `MachineStatusDto` from the caller's validated claims; `GET call` (anonymous) invokes the protected endpoint
  through `ISelfApiClient` and relays the DTO — the in-app round trip the E2E asserts)*
- `src/Test/WasmTestProject/src/Host/Bff/Services/ISelfApiClient.cs` + `SelfApiClient.cs` *(modify — add
  `GetMachineStatusAsync(CancellationToken)`)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/Cloudstrap.WasmTestProject.E2E.Tests.csproj`
  *(modify — `ProjectReference` to `Cloudstrap.TestIdentityProvider`)*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/E2eFixture.cs` *(modify — **before**
  starting the Bff, boot the test IdP via `TestIdentityProviderHost.StartLoopback(5310, …)` with the
  `wasmtestproject-bff` client (secret matching the Bff config, scope `selfapi`, audience
  `wasmtestproject-selfapi`, a short-but-not-flaky lifetime); expose its `TokenRequestCount`; dispose it in
  teardown; also booted in attach mode — mechanic (g))*
- `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/ClientCredentialsTests.cs` *(create)*
- `src/Test/WasmTestProject/README.md` *(modify — demo-table row (`GET api/v1/machine/call` +
  `api/v1/machine/status` | Cloudstrap.Authentication.ClientCredentials (#9) + the test IdP (D-5) |
  `ClientCredentialsTests`); port map gains 5310; harness notes: `RequireAuthenticatedEndpoints=false` and why,
  the placeholder-secret rule, and that a manual `dotnet run` without the IdP still boots (lazy acquisition)
  with the machine endpoints failing until an IdP listens on 5310)*

**RED** *(write these tests first, run them, confirm they fail — before the conversion the Bff has no IdP, no
JWT validation, no machine endpoints and no flagged client, so all of them fail)*:
- E2E test file: `src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/ClientCredentialsTests.cs`
  - `ProtectedEndpoint_CalledDirectlyWithoutAToken_Returns401` — `GET api/v1/machine/status` with a plain
    `HttpClient` → 401: #5's validation is live against the loopback IdP's real discovery document and JWKS.
  - `MachineCall_ThroughTheFlaggedClient_Returns200WithATokenIssuedByTheTestIdp` — `GET api/v1/machine/call` →
    200; the relayed body's `ClientId` is `wasmtestproject-bff` and `Issuer` is `http://127.0.0.1:5310` —
    the Bff acquired a real token from the test IdP over HTTP and its own protected endpoint validated it:
    acquisition **and** validation, live through the running app (AC-CC16 + AC-CC14's live half).
  - `MachineCall_Twice_ReusesTheCachedToken` — two calls; the fixture-hosted IdP's `TokenRequestCount`
    increased by exactly 1 across them (AC-CC2 live).
- Failing-run command:
  ```powershell
  dotnet build src/Cloudstrap.sln
  src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe --filter "ClientCredentialsTests"
  ```

**GREEN**: the Scope items — fixture IdP boot, Bff registration + configuration, the machine controller +
client method, the README row. **Every one of the 28 pre-existing E2E tests must stay green unchanged** — in
particular `ExtensionsTests` (the `SelfApi` outbound hop now carries a bearer token to an anonymous endpoint —
still 200 with correlation intact; the `SelfApi-liveness` readiness probe gets no token and still works; the
dead-port second instance still starts, because acquisition and JWT metadata retrieval are both lazy) and every
second-instance test (startup with the new config sections must still validate green without the IdP being
reachable from those instances). *(If any existing test is disturbed, the executor reports it at the gate
rather than weakening the assertion.)*

**DB changes**: none.

**VERIFY**:
1. E2E exe → the three new `ClientCredentialsTests` pass **and all 28 pre-existing E2E tests pass unchanged**
   (build first; one-time `playwright.ps1 install chromium` if needed).
2. Manual smoke (optional but recorded): `dotnet run --project src/Test/WasmTestProject/src/Host/Bff` with the
   IdP absent → the app boots and `/healthz` answers (lazy acquisition, live); `api/v1/machine/call` fails
   loudly naming the token endpoint.
3. Full suite + `dotnet build src/Cloudstrap.sln` (zero warnings) +
   `dotnet format src/Cloudstrap.sln --verify-no-changes` (exit 0).

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

## 🛑 HUMAN GATE — final: deliverable #9 complete *(covers Step 10; closes the deliverable)*

*Executor: STOP here. Present the results and WAIT for user approval. Any Git push afterwards requires the user's explicit go-ahead (CLAUDE.md: no push without confirmation).*

- [ ] Behavioral verification: the three new `ClientCredentialsTests` pass against the running app with the
  loopback IdP; **all 28 pre-existing E2E tests pass unchanged**; the full suite (Overview mechanic (i)) is
  green end to end.
- [ ] Spec acceptance sign-off: walk **AC-A2, AC-A3, AC-ASP2 and AC-CC1…AC-CC16** against the step evidence
  using the Overview's AC coverage map — all met; confirm nothing from the spec's Drop / Out-of-Scope lists was
  resurrected (no `EnableAuthentication` gate, no `AuthenticationFlow`, no authority-plus-path convention, no
  OIDC discovery for the token endpoint, no implicit/hybrid anywhere including the IdP, no user-token
  implementation, no DPoP/mTLS/workload-identity code, no token-manager facade, no `Cloudstrap:Authentication`
  parent section, no containerised IdP) and that every De-NIHDI row is closed (neutral fixtures, placeholder
  secrets only, zero `Nihdi`/`Riziv`/`Keycloak` identifiers, zero source-STS content or personal data).
- [ ] Demo + docs review: the SUT README demo-table row, port map (5310) and harness notes match as-built
  behavior, including the `RequireAuthenticatedEndpoints=false` posture and the manual-run note; the Bff
  `Program.cs` comment now correctly attributes the demo to #9 and points interactive login at #10.
- [ ] One-way-door recap for the record: the D-4 seam pair (Gate 1), the TestIdentityProvider shape (Gate 2)
  and the package public API (Gate 3) are what #10 builds on next — confirm no open reservations remain.
- [ ] User approved — deliverable #9 done; project-manager flips the ROADMAP row to ✅ (and any push happens
  only on the user's explicit go-ahead).
