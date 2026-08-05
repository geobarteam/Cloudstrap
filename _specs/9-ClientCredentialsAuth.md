# Spec: Client-Credentials Auth — `Cloudstrap.Authentication.ClientCredentials` (Roadmap Deliverable #9)

> **Approved 2026-08-05 — zero Open Questions remain; spec is planner-ready.** All five gate questions were
> answered by the user (see the Decision Log at the end). Three accepted this spec's recommendation: the
> configuration-provider credential model with a consumer-supplied client assertion (D-1), the flat
> `Cloudstrap:ClientCredentials` section (D-2), and the split of `Cloudstrap.Extensions`' shipped access-token
> seam (D-4). Two decisions **overrode** the recommendation: the token cache is **isolated from the
> application's distributed cache by default** (D-3), and verification is done against a **real, lightweight
> identity provider built into this repository** rather than a stub (D-5).
>
> Sources: `_plans/ROADMAP.md` §9 (hand-off brief, file inventory verified 2026-08-05) · `_specs/Cloudstrap.md`
> (Decisions Made row "Auth / token management", Package Map OAuth row, Auth Replacement AC-A1–AC-A3,
> De-NIHDI-fication Checklist, Aspire Coexistence AC-ASP1–AC-ASP3) · `_specs/5-WebApiBootstrap.md` (shape
> precedent; its Out-of-Scope routed old Core `Settings\Security\` here) · **shipped** code read in full:
> `src/Cloudstrap.Extensions/IAccessTokenHandlerProvider.cs`, `AccessTokenHandlerWiring.cs`,
> `ServiceCollectionExtensions.cs`, `HttpClientServiceOptionsValidator.cs`,
> `src/Cloudstrap.Core/HttpClientServiceOptions.cs`, `TokenRequestOptions.cs`, `CloudstrapOptions.cs`,
> `src/Cloudstrap.WebApi/CloudstrapJwtBearerOptions.cs`,
> `src/Test/UnitTest/Cloudstrap.Extensions.Tests/AccessTokenHandlerSeamTests.cs`,
> `src/Test/WasmTestProject/src/Host/Bff/Program.cs`, `src/Test/Directory.Build.props` · source reference repo
> (read-only) `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\` — every row of the Port Decision
> Table was opened: `Nihdi.Core.Configuration.OAuth\Extensions\WebApplicationBuilderExtensions.cs` + `README.md`
> + `.csproj`, `Common\HttpClient\ServiceCollectionExtensions.cs`, `Proxy\ServiceCollectionExtensions.cs`,
> `WebApi\WebApi\WebApplicationBuilderExtensions.cs` (lines 330–412),
> `OpenIdConnect\Extensions\WebApplicationBuilderExtensions.cs`, old Core `Settings\Security\*` (all six files),
> `AuthenticationFlow.cs`, the consumer `appsettings.json` files of `Test\TestProject\src\Host\{Bff,Wfe}` and
> `Test\WasmTestProject\src\Host\Cfe`, and **all four files** of
> `Test\TestProject\src\STS\Nihdi.TestProject.STS.TestServer\` plus its build-output closure.
>
> External evidence gathered 2026-08-05:
> [Duende.AccessTokenManagement 4.2.0 on NuGet](https://www.nuget.org/packages/Duende.AccessTokenManagement) —
> **Apache-2.0**, published 2026-03-18, `net8.0`/`net9.0`/**`net10.0`**, 20.2M downloads, owners
> DuendeSoftware · [DuendeSoftware/foss — access-token-management](https://github.com/DuendeSoftware/foss/tree/main/access-token-management)
> (the live source home; the standalone `DuendeArchive/Duende.AccessTokenManagement` repo was folded into the
> FOSS monorepo — a consolidation, not an abandonment) ·
> [Duende.IdentityModel 8.1.0](https://www.nuget.org/packages/Duende.IdentityModel) — Apache-2.0, 2026-03-17,
> 35.5M downloads · [ATM docs — workers/client credentials](https://docs.duendesoftware.com/accesstokenmanagement/workers/)
> and [extensibility](https://docs.duendesoftware.com/accesstokenmanagement/advanced/extensibility/) ·
> [OpenIddict.Server.AspNetCore 7.6.0](https://www.nuget.org/packages/OpenIddict.Server.AspNetCore) —
> **Apache-2.0**, published 2026-07-15, `net8.0`/`net9.0`/**`net10.0`**, 23.5M downloads, owner `openiddict` ·
> [OpenIddict degraded mode](https://kevinchalet.com/2020/02/18/creating-an-openid-connect-server-proxy-with-openiddict-3-0-s-degraded-mode/)
> and [openiddict/openiddict-samples](https://github.com/openiddict/openiddict-samples).
>
> **⚠️ Risk areas this deliverable touches** — **auth code, and for the first time credential *handling***: #5
> only validated inbound tokens; #9 holds a client secret (or assertion), calls a token endpoint and caches
> bearer tokens · **public API one-way door**: `AddCloudstrapClientCredentials` and its options shape are the
> contract #10, #12 and #17 build on · **⚠️ breaking change to shipped API (D-4)**: `Cloudstrap.Extensions`'
> `IAccessTokenHandlerProvider` is replaced by two interfaces — permitted under the standing pre-release
> permission, but it is a shared-contract change and needs review at its gate · **new dependency**:
> `Duende.AccessTokenManagement` (Apache-2.0, verified) — the suite's first auth-stack *runtime* dependency ·
> **new test-only dependency and a second one-way door (D-5)**: the in-repo test identity provider becomes
> shared infrastructure for #10, #12 and #17.

## Code-reading findings that shaped this spec

1. **There is almost nothing to port — the source package is 45 lines of delegation.**
   `AddOAuthForNihdi` gates on `Security.EnableAuthentication` and calls three extension methods that live in
   `Nihdi.AspNetCore.Authentication.ClientCredentials` / `.AccessTokenManagement` 5.2.5 — internal-feed packages
   that cannot be restored (`obj\project.assets.json` records `"Unable to find package
   Nihdi.AspNetCore.AccessTokenManagement"`). The *observable* contract, reconstructed from its four call sites,
   is small: bind a client id/secret and a scope string, register token management with a cache, and expose a
   per-`IHttpClientBuilder` client-credentials handler that takes optional per-client token-request parameters.
   Everything else this deliverable ships is new code or library code.
2. **Most of the "shared" `Settings\Security\` surface is dead or does not even bind.** A repo-wide grep for
   `CacheKeyPrefix|CacheLifetimeBuffer|RefreshTokenCacheLifetime|IdentityTokenCacheLifetime|AuthenticationFlow|
   OAuthConfiguration|UseDefaultClaimTypeMapping` returns **only the declarations themselves** — no reader
   anywhere in the source repo. Worse, two of the values that consumers *do* configure bind to nothing:
   `Test\TestProject\src\Host\Wfe\appsettings.json` sets `Security:Authentication:OAuth:Scopes`, but the property
   is named `OAuthConfiguration`, so the binder never populates it; the same file (and the Cfe/Bff ones) sets
   `ClientCredentials:ClientScopes`, a key `ClientCredentialsConfiguration` does not declare. The scope setting
   this package supposedly honors has been inert in the source for its whole life. That is the evidence behind
   nine Drop verdicts, not a stylistic preference.
3. **The source ships live client secrets in committed `appsettings.json`.** `Host\Bff`, `Host\Wfe` and
   `Host\Cfe` all carry real-looking `ClientSecret` and `AppRegistration:ClientSecret` values in source control.
   Whatever else this deliverable does, the secret-free path must be first class and the documentation must
   never show a secret in `appsettings.json`.
4. **The shipped `TokenRequestOptions` splits cleanly across #9 and #10 — no change needed.** `Scope`,
   `Resource` and `ForceRenewal` are exactly Duende ATM's `TokenRequestParameters` (`Scope`, `Resource`,
   `ForceTokenRenewal`) for client credentials; `SignInScheme` and `ChallengeScheme` are ATM's
   *`UserTokenRequestParameters`* — user-token concepts that belong to #10. #9 honors three of the five
   properties and deliberately ignores two.
5. **⚠️ #4's shipped seam cannot host #9 and #10 at the same time.** `AccessTokenHandlerWiring.AddTokenHandlers`
   does `handlerBuilder.Services.GetService<IAccessTokenHandlerProvider>()` — a **single** resolve. When both
   authentication packages are installed, each registering its own `IAccessTokenHandlerProvider`, the last
   registration wins and the other package's handler is silently unreachable; the surviving provider is also
   forced to implement the half it knows nothing about. `AccessTokenHandlerSeamTests.BothFlagsTrue_…` passes
   today only because its test double implements both methods. This defect in shipped public API is fixed here
   (D-4).
6. **Duende ATM v4 is shaped exactly like the #9/#10 split.** `Duende.AccessTokenManagement` handles
   machine-to-machine client credentials; `Duende.AccessTokenManagement.OpenIdConnect` (a separate package)
   handles user tokens. #9 therefore takes **one** package and #10 adds the second, with no rework. v4's
   `AddClientCredentialsTokenManagement()` → `.AddClient(ClientCredentialsClientName, Action<ClientCredentialsClient>)`
   maps 1:1 onto a `Cloudstrap:ClientCredentials` section, and `services.Configure<ClientCredentialsClient>(name, …)` /
   `IConfigureNamedOptions<ClientCredentialsClient>` is the documented configuration path.
7. **ATM v4 builds its handler by direct instantiation inside an `AddHttpMessageHandler` factory** — dependencies
   resolved from the service provider, handler `new`-ed at pipeline-build time. That is precisely the moment
   #4's `AccessTokenHandlerWiring` asks the provider for a handler, so the seam's laziness and ATM's model agree;
   the seam's *method signatures* survive D-4 unchanged, only its multiplicity changes.
8. **ATM v4 brings two container side effects the spec must disclose.** It depends on
   `Microsoft.Extensions.Http.Resilience` (its own backchannel retry policy — applied to the *token endpoint*
   client, never to a consumer's typed client, so the AC-ASP3 no-stacked-resilience posture #4 established is
   preserved) and calls `services.AddHybridCache()`. HybridCache is two-tier: any registered `IDistributedCache`
   silently becomes the L2 tier, so access tokens would leave the process in any app that uses Redis for its own
   reasons. D-3 closes that by default.
9. **The WebApi's `AddNihdiAccessTokenManagement()` inside JWT-bearer registration was a bundling defect, and
   #5 already dropped it.** Both call sites (lines 377 and 405) register outbound token management as a side
   effect of configuring inbound validation, alongside `AddDistributedMemoryCache()` and a Blazor
   `CircuitServicesAccessor`. Validation and acquisition stay in separate packages, activated by separate calls.
10. **The source's own dev token server is OpenIddict — its build output proves it.** `STS\Program.cs` is ~30
    lines that call `webBuilder.UseStartup<Startup>()`, where `Startup` comes from the internal, unreadable
    package `Nihdi.IdentityModel.OpenIdConnect.DevTokenServer 5.0.0`. But its `bin\{Debug,Release}\net10.0\`
    closure ships `OpenIddict.Server.dll`, `OpenIddict.Server.AspNetCore.dll`, `OpenIddict.Core.dll`,
    `OpenIddict.Abstractions.dll`, `OpenIddict.EntityFrameworkCore.dll`, `OpenIddict.Validation*.dll` and
    `Microsoft.EntityFrameworkCore.InMemory.dll`. The internal package is a **thin configuration wrapper over
    OpenIddict with an in-memory EF Core store**. The buy-versus-build question therefore already has an
    answered precedent: the original authors bought, and only the configuration binding was theirs (D-5).
11. **The STS `Readme.txt` is the real contract, and it is mostly good — with three conventions that must not
    survive.** It documents the flows and a per-client, per-token-type claim model (`Common`, `IdToken`,
    `AccessToken`, `ClientCredentialsToken`, `UserInfo`) that is genuinely well-shaped for a test IdP. Its three
    workarounds are not: pipe-separated multi-values (`role1|role2`) because "JSON config can't do lists" —
    which is untrue, JSON arrays bind fine and the file's own `Scopes` is already an array; a `schemas.` prefix
    the server rewrites to `http://` to reconstruct legacy WIF/SOAP claim types; and implicit + hybrid flows,
    both discouraged by the OAuth 2.0 Security BCP and removed in OAuth 2.1.
12. **The STS `appsettings.json` is the worst De-NIHDI offender read so far.** Four client entries carry a real
    person's name and email address, a `riziv.org` account identifier, enterprise client ids and live-looking
    secrets. Its **shape** is a reference; not one character of its **content** may appear in Cloudstrap.

---

## User Story

**As an** ASP.NET Core developer deploying to Azure whose service calls other services,
**I want to** turn on machine-to-machine tokens by referencing one package and making one registration call —
after which every outbound typed `HttpClient` that already declares `AddClientAccessToken: true` carries a
transparently cached and renewed bearer token,
**So that** no hand-written token endpoint calls, cache, refresh timer or retry-on-401 code lives in my
application — and my client secret never has to sit in `appsettings.json`.

---

## Acceptance Criteria

> AC-A2, AC-A3 and AC-ASP2 are carried **verbatim** from the founding spec. AC-CC1…AC-CC16 are new,
> spec-specific criteria (precedent: AC-W1…AC-W15 in `_specs/5-WebApiBootstrap.md`). Verification runs against
> the in-repo test identity provider (D-5), not a stub and not a container; founding AC-A1's interactive
> sign-in scenario remains #10's.

| # | Given | When | Then |
|---|-------|------|------|
| AC-A2 | Client-credentials `HttpClient` registered | Two calls 1 h apart with a 5-min token lifetime | Token transparently renewed; no 401s. *(carried verbatim)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried verbatim — must stay green)* |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-CC1 | An app that already has `Cloudstrap:HttpClients:{name}:AddClientAccessToken = true` and a typed client registered through `AddCloudstrapHttpServiceClient` | The package is referenced and `AddCloudstrapClientCredentials()` is called — **no other consumer code change** | Every outbound request of that client carries `Authorization: Bearer <token>`; unflagged clients are untouched; the correlation header still flows. |
| AC-CC2 | A configured token client and several outbound requests inside one token lifetime | The requests are made | The test IdP's token endpoint is called **once**; the same token is reused. |
| AC-CC3 | A token issued by the test IdP with a short configured lifetime, which has elapsed | The next outbound request is made | Exactly one new token request is made, the outbound call succeeds, and the caller observes no failure. *(the mechanised form of AC-A2)* |
| AC-CC4 | `AddCloudstrapClientCredentials()` called with `Cloudstrap:ClientCredentials` missing, or missing `TokenEndpoint`/`ClientId`, or a relative `TokenEndpoint` | The host starts | Startup fails naming the exact offending key (e.g. `'Cloudstrap:ClientCredentials:TokenEndpoint'`), via the inherited source-generated `[OptionsValidator]` + `ValidateOnStart` pattern. |
| AC-CC5 | A configured client secret; validation failures, log output at Debug, exception messages and exported telemetry are all inspected | The app starts, fails validation, and makes a token request | The secret value appears **nowhere** — not in a validation message, log, activity tag, problem-details payload or exception text. Only key *names* are ever echoed. |
| AC-CC6 | Two flagged clients with different `TokenRequestParameters:Scope` (and/or `Resource`) | Both make requests | Two distinct token requests are made with the respective scope/resource, and the two tokens are cached separately (one client's scope never leaks to the other). |
| AC-CC7 | `Cloudstrap:HttpClients:{name}:AddUserAccessToken = true` with **only** this package installed | The client is resolved | Creation fails fast with a message naming the flag, the key, and `Cloudstrap.Authentication.OpenIdConnect`; **no unauthenticated request is ever sent**. |
| AC-CC8 | The test IdP is configured to reject the credential (`invalid_client`), to fail (500), or is unreachable | An outbound request is attempted | The call fails with an exception whose message identifies token acquisition as the cause and names the configured token endpoint (never the secret); no request is sent without a token; the failure is logged once. |
| AC-CC9 | A consumer that applied a resilience handler via `ConfigureHttpClientDefaults`, plus Cloudstrap's correlation handler | A flagged client sends a request | The token handler sits outermost (ahead of correlation, per the shipped seam), the resilience handler is neither duplicated nor bypassed, and a 401-triggered token refresh re-executes the inner chain. *(AC-ASP3 posture, unchanged from #4)* |
| AC-CC10 | `AddCloudstrapClientCredentials()` called twice (e.g. by a composite bootstrap and by the app) | The host starts | One token-client registration, one provider registration, one set of ATM services; no duplicate handlers on any client. |
| AC-CC11 | Secret-free operation: a consumer-registered `IClientAssertionService` (Duende's extension point) and **no** `ClientSecret` configured | The app starts and makes an outbound call | Startup validation passes, the token request carries the assertion, and the call succeeds. Cloudstrap never overwrites a registered assertion service. *(D-1)* |
| AC-CC12 | An app that registers an `IDistributedCache` for its own purposes, with default settings | Access tokens are acquired and cached | **Nothing token-shaped is ever written to that distributed cache** — the token cache is an isolated, in-memory `HybridCache`. Setting `Cloudstrap:ClientCredentials:TokenCache = Shared` makes tokens use the application cache instead, and the mode in force is stated in a startup log line. *(D-3 — secure by default, visible not magic)* |
| AC-CC13 | Both `Cloudstrap.Authentication.ClientCredentials` and a package registering `IUserAccessTokenHandlerProvider` are installed, and one client sets **both** flags | The client is resolved and sends a request | **Both** handlers are in the chain, user first — neither provider is lost. With only one of the two registered and both flags set, client creation fails naming **only** the missing flag and its package. *(D-4 — the regression the old single-resolve seam could not pass)* |
| AC-CC14 | The in-repo test identity provider is running, and a Cloudstrap API host protected by #5's `AddCloudstrapJwtBearer` pointed at it | A Cloudstrap-registered typed client with `AddClientAccessToken: true` calls that API | The token is acquired from the test IdP's token endpoint via the client-credentials grant, the API validates it against the IdP's discovery document and JWKS, and the call returns 200 — **acquisition and validation proven end to end against a real OpenID Connect server**. The IdP enables the client-credentials grant only; implicit and hybrid flows are not enabled. *(D-5)* |
| AC-CC15 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, closure review, case-insensitive search for `Nihdi`, `Riziv`, `Keycloak`, and for the source STS fixture's personal data | All green; XML docs on all public API; package metadata + README complete; zero forbidden identifiers and zero personal data; zero `Nihdi.*`, `NSwag.*`, `Aspire.*` in any closure; **the shipped package closure contains no `OpenIddict.*` and no EF Core** (test-only, D-5); every dependency OSI-licensed and CPM-pinned. |
| AC-CC16 | The WASM SUT Bff adopts this package (its existing `SelfApi` typed client flagged `AddClientAccessToken: true`, a protected endpoint, and the test IdP booted by the E2E fixture) | The E2E suite runs | The 28 pre-existing E2E tests stay green and ≥ 1 new E2E test proves, through the running app, that the outbound call carries a bearer token issued by the test IdP and reaches the protected endpoint. *(standing SUT rule / workflow rule 9)* |

---

## Port Decision Table

One row per source public type/feature and per observed internal-package call (the internal packages are
unreadable, so their *contract* is rowed at the call site). "Superseded" = already adjudicated and shipped by an
earlier deliverable. "Routed" = belongs to a later deliverable; listed so the planner does not build it here.

### Part A — `Nihdi.Core.Configuration.OAuth` and old Core `Settings\Security\`

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `OAuth\Extensions\WebApplicationBuilderExtensions.AddOAuthForNihdi` | **Redesign** | `AddCloudstrapClientCredentials(this IServiceCollection, Action<CloudstrapClientCredentialsConfigurator>?)` | The one-call registration earns its place; the shape does not. `WebApplicationBuilder` is the wrong receiver (a Worker calling an API is a first-class case), and the method takes a settings object and an `ILogger` the container already owns. |
| ↳ its `if (nihdiConfiguration.Security.EnableAuthentication)` gate | **Drop** | — | #5's D-2 precedent: whether an app acquires tokens must be visible in `Program.cs`, not buried in a config flag whose `false` branch silently logs a warning and does nothing. |
| ↳ its `NihdiConfiguration` + `ILogger` parameters | **Drop** | — | Settings come from `IConfiguration` binding (#1 pattern); logging comes from DI. Passing both in is the source's house style, not a requirement. |
| `AddClientCredentialsConfiguration()` *(internal pkg, observed)* | **Replace** | ATM `.AddClient(...)` fed by `CloudstrapClientCredentialsOptions` | Binding a client id/secret is one options class; the acquisition machinery behind it is a library's job. |
| `AddClientAccessTokenManagement()` *(internal pkg, observed)* | **Replace** | `Duende.AccessTokenManagement` `AddClientCredentialsTokenManagement()` | Token acquisition, caching, expiry buffering, concurrent-refresh control and 401 retry are security-sensitive, well-solved problems. Bespoke re-implementation would be ~300 lines Cloudstrap owns forever; the library is Apache-2.0, `net10.0`, 20.2M downloads. |
| `AddOAuthConfiguration()` *(internal pkg, observed)* | **Replace** | ATM client's `Scope` / `Resource`, from `Cloudstrap:ClientCredentials` | A whole extension method + settings class existed to carry one space-delimited scope string — which finding 2 shows never bound. It becomes one property on the client. |
| `IHttpClientBuilder.AddClientAccessTokenHandler(TokenRequestParameters)` *(internal pkg; call sites `Common\HttpClient\ServiceCollectionExtensions.cs:62`, `Proxy\ServiceCollectionExtensions.cs:74`)* | **Replace** | ATM's client-credentials access-token handler, surfaced through the `IClientAccessTokenHandlerProvider` seam | The capability is the deliverable's whole point. The mechanism is ATM's (finding 7); Cloudstrap owns the seam adapter and the per-client parameter mapping, not the HTTP token request. |
| `IHttpClientBuilder.AddUserAccessTokenHandler(TokenRequestParameters)` *(internal pkg; `Common\HttpClient\ServiceCollectionExtensions.cs:57`)* | **Drop** *(here)* | — → #10 | User tokens require an interactive sign-in and `Duende.AccessTokenManagement.OpenIdConnect`; both arrive with #10. #9 must nevertheless fail *loudly* when the flag is set (AC-CC7). |
| `WebApi\WebApplicationBuilderExtensions` `.AddNihdiAccessTokenManagement()` (2 call sites, lines 377/405) + the adjacent `AddDistributedMemoryCache()` / `CircuitServicesAccessor` | **Drop** | — | Finding 9: outbound token management registered as a side effect of inbound JWT validation. #5 already dropped it; nothing is resurrected here. |
| `OpenIdConnect\...AddNihdiAccessTokenManagement(accessTokenManagementOptionsBuilder)` (line 67) | **Routed** | #10 | The user-token half of the same internal package. |
| `Settings\Security\ClientCredentialsConfiguration` (`ClientId`, `ClientSecret`, both `[Required]`) | **Redesign** | `CloudstrapClientCredentialsOptions.ClientId` (required) + `.ClientSecret` (**optional**) | The capability stays; the unconditional `[Required]` on the secret does not — it makes secret-free credentials (assertion / federated identity) unrepresentable, which is backwards for a 2026 Azure-targeting library and is precisely what pushed the source into committing secrets (finding 3). |
| `Settings\Security\OAuthConfiguration` (`Scopes`) | **Redesign** | `CloudstrapClientCredentialsOptions.Scope` (singular, space-delimited per RFC 6749) | One property, on the type that owns it. The separate class existed only to mirror the internal package's own split. |
| `Settings\Security\AuthenticationConfiguration.Authority` | **Redesign** | `CloudstrapClientCredentialsOptions.TokenEndpoint` (absolute `Uri`, required) | De-NIHDI: `Authority` was only ever half an address — the internal package appended Keycloak's `/protocol/openid-connect/token`. An explicit endpoint removes an IdP-shaped convention from a library that must work against any conformant server (the #5 precedent that turned `Authority + path` into an explicit `TokenUrl`). |
| `AuthenticationConfiguration.AuthenticationFlow` + the `AuthenticationFlow` enum (6 members, incl. two deprecated Implicit variants) | **Drop** | — | Zero readers repo-wide (finding 2). The flow is decided by the package you install (client credentials here, auth-code+PKCE in #10); a library should not offer `Implicit` as a configurable option in 2026. |
| `AuthenticationConfiguration.CacheKeyPrefix` | **Replace** | ATM `ClientCredentialsTokenManagementOptions.CacheKeyPrefix`, reachable through the configurator hook | Same knob, already in the library, already documented. Re-modelling it in a Cloudstrap options class would add surface that only forwards. |
| `AuthenticationConfiguration.CacheLifetimeBuffer` | **Replace** | ATM `ClientCredentialsTokenManagementOptions.CacheLifetimeBuffer` (+ `DefaultCacheLifetime`, `LocalCacheExpiration`, `UseCacheAutoTuning`) | Same reasoning; ATM's defaults are better than the source's `0`. Cloudstrap adds exactly one cache setting of its own — the isolation switch of D-3 — and forwards the rest. |
| `AuthenticationConfiguration.RefreshTokenCacheLifetime` | **Drop** | — | Client-credentials grants have no refresh token (RFC 6749 §4.4.3 explicitly says SHOULD NOT). Dead here by protocol, not just by grep. |
| `AuthenticationConfiguration.IdentityTokenCacheLifetime` | **Drop** *(here)* | — → #10 if it earns it | An identity token belongs to an interactive sign-in. Zero readers today. |
| `AuthenticationConfiguration.UseDefaultClaimTypeMapping` | **Drop** *(here)* | — | A token *validation* concern; #5 already shipped `Cloudstrap:JwtBearer:MapInboundClaims`. An acquiring client does not read the claims it forwards. |
| `AuthenticationConfiguration` nesting (`OpenIdConnect`/`JwtBearer`/`ClientCredentials`/`OAuthConfiguration`) + its `IValidatableObject` cascade (incl. the "at least one of three" cross-rule) | **Drop** | — | #1 replaced the hand-rolled cascade with `Microsoft.Extensions.Options` + source-generated validators, and #5 established one flat `Cloudstrap:<Feature>` section per package (D-2). The cross-rule also mis-fires: it demands one of three sub-sections even for an app that only validates tokens. |
| `Settings\Security\SecurityConfiguration.EnableAuthentication` | **Drop** | — | Same as the gate above; #5 already removed it from the validation path. |
| `SecurityConfiguration.AllowedOrigins` / `.EnableHttps` | **Superseded** | #5's `Cloudstrap:WebApi:Cors` / `:Hsts` | Adjudicated and shipped by #5. |
| `Settings\Security\JwtBearerConfiguration` | **Superseded** | #5's `Cloudstrap:JwtBearer` | Inbound validation; explicitly out of scope here — though AC-CC14 now proves the two halves interoperate. |
| `Settings\Security\OpenIdConnectConfiguration` (`ClientScopes`, `SaveTokens`, `UseTokenLifeTime`) | **Routed** | #10 | Interactive sign-in settings. |
| Per-client token-request overrides (`Scope`, `Resource`, `ForceRenewal` reaching the token request per HTTP client) | **Port** | mapped onto ATM `TokenRequestParameters` | A genuinely useful capability: one application identity, different scopes/resources per downstream API. Already modelled by the shipped `Cloudstrap.Core.TokenRequestOptions` (finding 4). |
| One application identity shared by all outbound clients (source had a single `ClientCredentials` block) | **Port** | one token client, named `cloudstrap` | The right default: an app has one identity. Multi-identity is deferred, not designed away — adding a `TokenClientName` to `HttpClientServiceOptions` later is purely additive (see Out of Scope). |
| `Common\HttpClient\MapTokenRequestParameters` + old Core `Settings\HttpClient\TokenRequestParameters` | **Superseded** | shipped `Cloudstrap.Core.TokenRequestOptions` (#1) + `AccessTokenHandlerWiring` (#4) | The mapping layer already exists on the Cloudstrap side; #9 maps `TokenRequestOptions` → ATM's `TokenRequestParameters` and ignores the two user-token properties. |
| `Proxy\ServiceCollectionExtensions.AddNihdiTrustedSubsystemProxy`'s client-credentials handler usage | **Routed** | #17 | #9 supplies the capability #17 consumes; the YARP forwarder itself is not built here. |
| `BlazorServer.csproj` → `Nihdi.AspNetCore.AccessTokenManagement 5.2.5` | **Routed** | #12 | A package reference with no code usage in the source repo; #12 decides what it actually needs. |
| `OAuth.csproj` → `Nihdi.AspNetCore.Authentication.ClientCredentials 5.2.5` | **Replace** | `Duende.AccessTokenManagement` 4.2.0 | Founding Auth Replacement + AC-A3; internal feed, unreadable, unrestorable. |
| `OAuth.csproj` → `Nihdi.AspNetCore.AccessTokenManagement 5.2.5` | **Replace** | `Duende.AccessTokenManagement` 4.2.0 (same package covers both) | Two internal packages collapse into one OSS dependency. |
| `OAuth\README.md` ("Copyright © NIHDI/RIZIV", "Nihdi authentication infrastructure") | **Redesign** | new MIT package README | De-NIHDI; the README is also where the secret-handling guidance and the manual-verification procedure live. |
| `Riziv-Inami` file copyright header | **Drop** | — | De-NIHDI checklist: licensing is carried by `LICENSE` + `PackageLicenseExpression`, no per-file headers. |
| Committed live `ClientSecret` values in `Host\{Bff,Wfe,Cfe}\appsettings.json` | **Drop** | — (documented anti-pattern) | Finding 3. No Cloudstrap sample, test fixture, README or SUT config may contain a real-looking secret; test doubles use obvious placeholders and neutral hosts (`example.com`, `contoso`). |

### Part B — `Test\TestProject\src\STS\Nihdi.TestProject.STS.TestServer` (the test identity provider, D-5)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `STS\Program.cs` (30-line host calling `UseStartup<Startup>` from the internal package, plus `ConfigurationHelper.GetValidBaseUrl()`) | **Redesign** | `Cloudstrap.TestIdentityProvider` — a test-infrastructure **library** under `src/Test/`, with an `AddCloudstrapTestIdentityProvider` / `MapCloudstrapTestIdentityProvider` pair over a `WebApplication` | The generic-host + `Startup` shape is .NET Core 2.x-era, and the address-from-config helper exists only because the server was an exe. A library is what both consumers need: unit tests host it in-process (no sockets), the E2E fixture boots it on a loopback address (real JWKS retrieval over HTTP). |
| `Nihdi.IdentityModel.OpenIdConnect.DevTokenServer` 5.0.0 — the entire token server | **Replace** | **OpenIddict** (`OpenIddict.Core` + `.Server.AspNetCore` + an in-process EF Core store), Apache-2.0 | Internal feed, unreadable, forbidden by the De-NIHDI checklist. Finding 10 shows the package *is already* OpenIddict + EF Core In-Memory — so "replace" here restores the original decision without the wrapper. Building a conformant OIDC server by hand (discovery document, JWKS, RFC 6749 grant handling, error responses) is hundreds of lines of protocol code Cloudstrap would own in order to test *someone else's* protocol client. See D-5. |
| `Readme.txt` supported flows: **implicit**, **hybrid** | **Drop** | — | Deprecated by the OAuth 2.0 Security BCP and removed in OAuth 2.1. A test identity provider that can issue tokens through them invites a test — and then a sample — that demonstrates them. Not enabled, not configurable. |
| `Readme.txt` supported flows: **authorization code + PKCE**, **refresh token** | **Routed** | #10 | #9 enables the client-credentials grant only. #10 adds `AllowAuthorizationCodeFlow()`/`AllowRefreshTokenFlow()` and the authorization/userinfo endpoints to the same project — an extension, not a rewrite (D-5). |
| `Readme.txt` client configuration model (`ClientId`, `ClientSecret`, `Scopes`) | **Port** | `TestIdentityProviderOptions.Clients[]` | A config-driven client list is exactly right for test infrastructure: a test declares its clients as data instead of code. Kept nearly verbatim in shape. |
| `Readme.txt` per-token-type claim sets (`Common`, `AccessToken`, `ClientCredentialsToken`) | **Port** | `TestIdentityProviderOptions.Clients[].TokenClaims.{Common, AccessToken, ClientCredentialsToken}` | The best idea in the source's test server: claims are data, so a test can prove authorization behavior (roles, audiences) without touching the IdP's code. `IdToken` and `UserInfo` follow with #10. |
| `Readme.txt` per-client `RedirectUri` | **Routed** | #10 | Meaningless for client credentials; the source's own Readme admits it is present only "because the OIDC protocol demands it". |
| `Readme.txt` pipe-separated multi-value convention (`role1\|role2\|role3`) | **Drop** | JSON arrays | The stated rationale — that JSON configuration cannot express lists — is simply wrong; `Microsoft.Extensions.Configuration` binds arrays natively, and the same file's `Scopes` is already an array. |
| `Readme.txt` `schemas.` → `http://` claim-type prefix rewriting | **Drop** | — | Exists to reconstruct legacy WIF/SOAP claim URIs, which no Cloudstrap consumer emits. #5 already shipped `MapInboundClaims = false` — Cloudstrap's posture is that a claim is called what the token calls it. |
| `Readme.txt` / `appsettings.json` `LogParameters` flag | **Drop** | standard `ILogger` at Debug | A bespoke console dump of the client configuration — secrets included — replaced by ordinary logging that never prints a secret. |
| `appsettings.json` `TokenServerOptions.BaseUrl` | **Drop** | — | The host decides its own address (`WebApplication` URLs / the E2E fixture's chosen port). A server that reads its own public address from configuration is a source of "works on my port" failures. |
| `appsettings.json` `AccessTokenLifetime` | **Port** | `TestIdentityProviderOptions.AccessTokenLifetime` | Load-bearing for AC-A2/AC-CC3: renewal cannot be tested deterministically without a short, configurable lifetime. |
| `appsettings.json` `RefreshTokenLifetime`, `UsePKCE` | **Routed** | #10 | Both belong to the interactive flows. |
| `appsettings.json` fixture **content** (real name + email, `riziv.org` account, enterprise client ids, live-looking secrets, `signin-oidc` redirect URIs) | **Drop** | neutral fixtures (`contoso`, `example.com`, obvious placeholder secrets) | Finding 12; De-NIHDI checklist plus a personal-data concern. Only the file's *shape* is a reference. AC-CC15 greps for the residue. |
| `STS\.csproj` `StyleCop.Analyzers.Unstable` reference | **Drop** | — | Deliverable #0 dropped StyleCop suite-wide in favour of .NET SDK analyzers + `.editorconfig`. |

**Tally**: 5 Port · 6 Redesign · 9 Replace · 19 Drop · 3 Superseded · 6 Routed *(48 rows)*.
*(Part A alone: 2 Port · 5 Redesign · 8 Replace · 12 Drop · 3 Superseded · 4 Routed = 34 rows.)*

---

## Public API Sketch

### 1. New package — `Cloudstrap.Authentication.ClientCredentials`

Namespace **`Cloudstrap.Authentication.ClientCredentials`** (single namespace, matching the package id —
Core/Extensions/WebApi precedent). Everything `public sealed` / `static`; the provider, the options validator
and the parameter mapping are `internal`. Type names carry the `Cloudstrap` prefix where a Duende type is in
scope (`ClientCredentialsClient`, `ClientCredentialsTokenManagementOptions`) — the `CloudstrapJwtBearerOptions`
precedent from #5.

```text
Cloudstrap.Authentication.ClientCredentials
├── ServiceCollectionExtensions (static)
│     AddCloudstrapClientCredentials(
│         this IServiceCollection services,
│         Action<CloudstrapClientCredentialsConfigurator>? configure = null)
│         : IServiceCollection                              ⚠️ auth risk area
│       — binds + validates CloudstrapClientCredentialsOptions (Cloudstrap:ClientCredentials) with
│         ValidateOnStart; calls Duende's AddClientCredentialsTokenManagement() and registers ONE named
│         token client ("cloudstrap") configured from those options; registers the isolated token cache
│         (D-3) unless TokenCache = Shared; registers the IClientAccessTokenHandlerProvider that fills
│         the Cloudstrap.Extensions seam. Idempotent (AC-CC10).
│         Registers NO inbound authentication, NO authorization policy, NO user-token provider.
│
├── CloudstrapClientCredentialsOptions        — section Cloudstrap:ClientCredentials (owned HERE)
│     const SectionName  = "Cloudstrap:ClientCredentials"
│     TokenEndpoint : Uri?     — required, absolute. NO authority-plus-path convention (De-NIHDI).
│     ClientId      : string   — required.
│     ClientSecret  : string?  — optional; omit it when an IClientAssertionService is registered  [D-1]
│     Scope         : string?  — space-delimited, RFC 6749; the default for every flagged client
│     Resource      : string?  — RFC 8707 resource indicator; default for every flagged client
│     TokenCache    : TokenCacheMode = Isolated                                                  [D-3]
│     BackchannelHttpClientName : string = "cloudstrap-clientcredentials"
│                               — the named HttpClient ATM uses to call the token endpoint
│
├── TokenCacheMode (enum)                                                                        [D-3]
│     Isolated  — default: tokens live in a HybridCache registered for tokens alone, with no
│                 distributed tier, so an application IDistributedCache never receives them
│     Shared    — tokens use the application's HybridCache, including its L2 tier (opt-in)
│
├── CloudstrapClientCredentialsConfigurator   — code-level hooks (the #5 configurator precedent)
│     Client          : Action<ClientCredentialsClient>?                    — Duende type; runs LAST,
│                        final say over every value (ClientCredentialStyle, DPoPJsonWebKey, …)
│     TokenManagement : Action<ClientCredentialsTokenManagementOptions>?    — Duende type; cache knobs
│                        (CacheKeyPrefix, CacheLifetimeBuffer, DefaultCacheLifetime, …)
│     Backchannel     : Action<IHttpClientBuilder>?                         — resilience/proxy/handlers
│                        on the token-endpoint client only
│
└── CloudstrapClientCredentials (static)      — the constants consumers need
      const TokenClientName = "cloudstrap"    — the ATM client name, so a consumer can inject Duende's
                                                IClientCredentialsTokenManager and ask for a token itself
                                                (no Cloudstrap facade over it)

internal: ClientCredentialsAccessTokenHandlerProvider (fills the #4 seam),
CloudstrapClientCredentialsOptionsValidator (source-generated [OptionsValidator] — inherited fact #1,
no Microsoft.Extensions.Options.DataAnnotations), TokenRequestParameterMapper
(Cloudstrap.Core.TokenRequestOptions → Duende TokenRequestParameters).
```

**Deliberately not shipped**: no Cloudstrap wrapper over `IClientCredentialsTokenManager` (Duende's interface is
injectable as-is once registered — a facade would be surface with no behavior), no `ClientCredentialStyle` /
DPoP / assertion property duplication (the `Client` hook reaches all of them), no `IHostApplicationBuilder`
overload (`builder.Services.AddCloudstrapClientCredentials()` reads fine and keeps the package host-agnostic),
and no credential-modelling type of Cloudstrap's own (D-1 — the secret is a configuration value like any other).

**Configuration** — this package owns exactly one new subsection, `Cloudstrap:ClientCredentials`. It **consumes**
Core's shipped `Cloudstrap:HttpClients:{name}:AddClientAccessToken` and `:TokenRequestParameters` and never
redefines them: **the opt-in for a client is the flag that already exists**, which is what makes AC-CC1
("no consumer code change") achievable. This package introduces no collection or dictionary options, so #1's
binder-append caveat does not apply to it.

### 2. ⚠️ Breaking change to the shipped `Cloudstrap.Extensions` seam (D-4)

`IAccessTokenHandlerProvider` is **removed** and replaced by two single-method interfaces in the same namespace.
Method names and signatures are carried over unchanged, so the seam's documented contract (lazy resolution at
pipeline-build time, a fresh handler per client, no pre-set `InnerHandler`) survives verbatim; only the
*multiplicity* changes.

```text
Cloudstrap.Extensions
├── IClientAccessTokenHandlerProvider          (implemented by Cloudstrap.Authentication.ClientCredentials)
│     DelegatingHandler CreateClientTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
│
└── IUserAccessTokenHandlerProvider            (implemented by Cloudstrap.Authentication.OpenIdConnect, #10)
      DelegatingHandler CreateUserTokenHandler(string clientName, TokenRequestOptions? tokenRequest)
```

`AccessTokenHandlerWiring` (internal) resolves **each interface independently**, at pipeline-build time, with
this required behavior:

| `AddUserAccessToken` | `AddClientAccessToken` | Registered providers | Behavior |
|---|---|---|---|
| false | false | any | No resolve attempted, no handler added. *(unchanged)* |
| true | false | user provider present | User handler at position 0. |
| false | true | client provider present | Client handler at position 0. |
| true | true | both present | Both handlers, **user first** — the shipped ordering contract, now actually achievable (AC-CC13). |
| true | * | user provider missing | `InvalidOperationException` naming `…:AddUserAccessToken` and `Cloudstrap.Authentication.OpenIdConnect`. |
| * | true | client provider missing | `InvalidOperationException` naming `…:AddClientAccessToken` and `Cloudstrap.Authentication.ClientCredentials`. |
| true | true | both missing | One exception listing **both** flags and both packages (the current aggregated message shape is kept). |
| true | true | exactly one present | Fails naming **only** the missing flag and its package — a partial registration must never degrade to a partially authenticated request. |

**Required test update** (the defect's root cause): `AccessTokenHandlerSeamTests`' `RecordingTokenHandlerProvider`
implements both methods on one type, which is exactly what masked the single-resolve bug. It splits into two
independently registered doubles, and the fixture gains the regression case that the old seam could not pass —
two *separate* providers registered, both flags set, both handlers observed in order — plus the
one-present/one-missing failure case. `IAccessTokenHandlerProvider`'s XML-doc paragraph stating that "a client
may set both flags … whether that combination is meaningful is the implementation's contract" moves onto the
wiring, where it is now true by construction.

Nothing else in `Cloudstrap.Extensions` changes: `AddCloudstrapHttpServiceClient`, the registry, the health-check
setup and the correlation handler are untouched, and no consumer of those APIs is affected. Permitted under the
standing pre-release rule (nothing is published); recorded here because it **is** a shared-contract change.

### 3. Test infrastructure — `Cloudstrap.TestIdentityProvider` (D-5, not a shipped package)

A test-only library under `src/Test/` (suggested `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/`).
`src/Test/Directory.Build.props` already sets `IsPackable=false` and treats non-`*.Tests` projects as plain
libraries, so **its dependencies can never reach a shipped package closure** (AC-CC15 asserts this) — which is
why an OpenIddict + EF Core dependency set that would be unacceptable in a `Cloudstrap.*` package is
unremarkable here.

```text
Cloudstrap.TestIdentityProvider
├── AddCloudstrapTestIdentityProvider(this IServiceCollection, Action<TestIdentityProviderOptions>?)
├── MapCloudstrapTestIdentityProvider(this IEndpointRouteBuilder)
│     — OpenIddict server: discovery document (/.well-known/openid-configuration), JWKS, token endpoint.
│       Client-credentials grant ONLY in #9; signed (not encrypted) JWT access tokens so #5's
│       AddCloudstrapJwtBearer can validate them; ephemeral signing key per process.
│
└── TestIdentityProviderOptions
      AccessTokenLifetime : TimeSpan  — short by default; the lever AC-CC3 pulls
      Issuer              : Uri?      — null → derived from the host's own address
      Clients : IList<TestIdentityProviderClient>
            ClientId     : string
            ClientSecret : string      — obvious placeholder values only
            Scopes       : IList<string>
            Audiences    : IList<string>
            TokenClaims  : TestIdentityProviderClaims
                  Common                 : IDictionary<string, IList<string>>
                  AccessToken            : IDictionary<string, IList<string>>
                  ClientCredentialsToken : IDictionary<string, IList<string>>
```

Two hosting modes, both required: **in-process** (unit/integration tests host it with
`Microsoft.AspNetCore.TestHost` and hand its `HttpMessageHandler` to ATM's backchannel client — no sockets, no
ports, fully deterministic) and **loopback** (the E2E fixture boots it on a real address so #5's JWT bearer
handler performs genuine discovery-document and JWKS retrieval over HTTP — the only way AC-CC14 proves anything).

⚠️ **One-way door**: #10 (OIDC login), #12 (Blazor Server) and #17 (proxy) will all use this project. #9
therefore fixes its shape — options model, hosting modes, project location — while enabling only the grant #9
needs. #10 *extends* it (`AllowAuthorizationCodeFlow()`, `AllowRefreshTokenFlow()`, authorization + userinfo
endpoints, the `IdToken`/`UserInfo` claim sets and per-client `RedirectUri` already routed to it above) rather
than replacing it.

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Activation | Nothing happens until `AddCloudstrapClientCredentials()` is called — no config flag turns token acquisition on (#5's D-2 posture). | Call it, or don't. |
| Which clients get a token | Exactly those whose `Cloudstrap:HttpClients:{name}:AddClientAccessToken` is `true`, via #4's seam. Nothing is attached to clients registered outside `AddCloudstrapHttpServiceClient`. | Per-client config flag; or use Duende's own `AddClientCredentialsTokenHandler(...)` on any `IHttpClientBuilder` for clients outside the Cloudstrap registry. |
| Token client identity | One client named `cloudstrap`, from `Cloudstrap:ClientCredentials`. | `configurator.Client` for anything the options type does not model; consumers needing several identities register additional ATM clients directly (see Out of Scope). |
| Per-client token request | `TokenRequestParameters:Scope`/`Resource` override the client-level defaults; `ForceRenewal` bypasses the cache for that client. `SignInScheme`/`ChallengeScheme` are **ignored** (user-token settings) with a one-time startup warning naming the key. | The per-client `Cloudstrap:HttpClients:{name}:TokenRequestParameters` section. |
| Token endpoint address | Explicit absolute `TokenEndpoint`; no discovery request, no IdP path convention. | The setting itself; the README shows how to read it from the IdP's `/.well-known/openid-configuration`. |
| Credential | `ClientSecret` from configuration — supplied by **any** provider: #4's `AddCloudstrapKeyVault`, environment variables, user-secrets. Never required to be in `appsettings.json`, and every example uses a placeholder. Secret-free operation via a consumer-registered `IClientAssertionService`, which Cloudstrap never overwrites. *(D-1)* | `configurator.Client`; register an `IClientAssertionService`. |
| **Token cache location** | **Isolated** — tokens are cached in a `HybridCache` registered for tokens alone (Duende's keyed `ServiceProviderKeys.ClientCredentialsTokenCache` extension point), with **no distributed tier**. An `IDistributedCache` the application registers for its own purposes therefore never receives access tokens. The mode in force is stated in one startup log line, so the default is visible rather than magic. *(D-3)* | `Cloudstrap:ClientCredentials:TokenCache = Shared` — tokens then use the application's `HybridCache` and its L2 tier, for multi-instance token reuse; the README states the trade-off (fewer token requests vs. bearer tokens at rest in a shared store) and points at Duende's cache-encryption guidance. |
| Token cache tuning | Duende's own lifetime buffer, default lifetime and auto-tuning. | `configurator.TokenManagement`. |
| Handler position | Outermost on the client's pipeline (ahead of correlation), exactly as #4's wiring inserts it, so a 401-triggered refresh re-executes the whole inner chain including any resilience handler. | Not configurable — ordering is the domain knowledge the seam encodes. |
| Backchannel client | ATM's token-endpoint calls go through a named `HttpClient` (`cloudstrap-clientcredentials`). It deliberately does **not** carry Cloudstrap's correlation handler: the correlation id identifies a business transaction and has no business at a third-party IdP. | `configurator.Backchannel` (proxy, certificate, resilience, extra handlers); `BackchannelHttpClientName` to rename it. |
| Resilience | ATM's own retry policy applies to the token endpoint only. Cloudstrap adds no resilience handler to consumer clients (unchanged from #4) — no stacking (AC-ASP3 posture). | Consumer's own `ConfigureHttpClientDefaults` / per-client policies, untouched. |
| Health probes | The `{client}-liveness` probe client from #4 gets **no** token: probes must not consume tokens, and a 401 on a probe would be indistinguishable from a dead dependency. Cloudstrap's own `MapCloudstrapHealthChecks` endpoints are anonymous (#5). | Consumers whose dependency requires auth on its probe path configure that probe client themselves. |
| Secrets & telemetry | Validation messages, logs and exceptions name configuration **keys**, never values. Nothing this package writes contains a token or a secret; token values never reach an `Activity` tag or a log scope. | None — this is not a convention, it is a rule (AC-CC5). |
| Failure when unconfigured | `AddClientAccessToken: true` without this package → fail fast naming `Cloudstrap.Authentication.ClientCredentials`. `AddUserAccessToken: true` with only this package → fail fast naming `Cloudstrap.Authentication.OpenIdConnect`. Never a silent unauthenticated request. *(D-4 makes both messages precise)* | Turn the flag off. |
| Test identity provider | Client-credentials grant only; signed, unencrypted JWT access tokens; ephemeral signing key per process; clients and claims from configuration; short access-token lifetime. Implicit and hybrid flows are not enabled and are not configurable. *(D-5)* | `TestIdentityProviderOptions`; #10 enables the interactive flows on the same project. |
| Aspire coexistence | No overlap: token acquisition is outside ServiceDefaults' remit. AC-ASP2 is carried as a closure tripwire; AC-ASP3's no-stacked-resilience posture is preserved by construction. | — (posture). |

**Test strategy (spec-level):** NUnit 4 on Microsoft.Testing.Platform in
`src/Test/UnitTest/Cloudstrap.Authentication.ClientCredentials.Tests`. Tests boot a real host and a real
`IHttpClientFactory` pipeline (the `AccessTokenHandlerSeamTests` pattern already proven in
`Cloudstrap.Extensions.Tests`), with **`Cloudstrap.TestIdentityProvider` hosted in-process** and its
`HttpMessageHandler` wired as ATM's backchannel — a real OpenID Connect token endpoint with no sockets, giving
deterministic control over token lifetime (AC-CC3), claims (AC-CC6) and error responses (AC-CC8). Token renewal
is driven by a short configured `AccessTokenLifetime`, with `Microsoft.Extensions.TimeProvider.Testing` (MIT) if
the library's clock proves fakeable — the plan confirms which at its first slice. `Cloudstrap.Extensions.Tests`
gains the D-4 regression cases. The demonstration slice (AC-CC16) flags the SUT's existing `SelfApi` client,
protects a Bff endpoint with `AddCloudstrapJwtBearer` pointed at the test IdP, and adds ≥ 1 Playwright E2E test;
AC-CC14's acquisition-plus-validation round trip is proven in integration tests and again through the SUT.

---

## Dependencies

### Shipped package — `Cloudstrap.Authentication.ClientCredentials`

| Package | License | Evidence & justification |
|---|---|---|
| `Cloudstrap.Core` *(project reference)* | MIT | `TokenRequestOptions`, `HttpClientServiceOptions` — the per-client contract this package honors. |
| `Cloudstrap.Extensions` *(project reference)* | MIT | `IClientAccessTokenHandlerProvider` — the seam this deliverable fills (D-4). ⚠️ Consequence: Extensions carries a `Microsoft.AspNetCore.App` framework reference, so this package inherits it transitively even though it needs no ASP.NET Core type of its own (documented in the README, as #2 did). |
| **`Duende.AccessTokenManagement` 4.2.0** | **Apache-2.0** ✅ | [nuget.org](https://www.nuget.org/packages/Duende.AccessTokenManagement): Apache-2.0, published **2026-03-18**, targets `net8.0`/`net9.0`/**`net10.0`**, **20.2M** downloads, owners DuendeSoftware. Source lives in [DuendeSoftware/foss](https://github.com/DuendeSoftware/foss/tree/main/access-token-management) (the standalone repo was consolidated into the FOSS monorepo — the archive banner reflects the move, not abandonment; v4.2.0 post-dates it). **No usage threshold, no commercial licence, no license key** — unlike Duende IdentityServer/BFF, this library is unconditionally OSS (CLAUDE.md rule 4 satisfied). Replaces two unreadable internal packages and eliminates all token acquisition, caching, expiry-buffer and refresh-concurrency code Cloudstrap would otherwise own. |
| `Duende.IdentityModel` 8.1.0 *(transitive)* | Apache-2.0 | [nuget.org](https://www.nuget.org/packages/Duende.IdentityModel): 2026-03-17, 35.5M downloads. OAuth/OIDC protocol primitives. |
| `Microsoft.Extensions.Caching.Hybrid` ≥ 10.0.0 *(transitive)* | MIT | Microsoft; the token cache tier ATM v4 uses, and the type Cloudstrap registers keyed-and-isolated for D-3. |
| `Microsoft.Extensions.Http.Resilience` ≥ 10.0.0 *(transitive)* | MIT | Microsoft; ATM's backchannel retry policy. Already CPM-pinned test-only since #4 — this promotes it to a runtime closure entry. Applies to the token endpoint only (AC-CC9). |
| `Microsoft.Extensions.{Http, Logging.Abstractions, Options, Telemetry.Abstractions}` *(transitive)* | MIT | Microsoft; already in the suite's closure. |
| `System.IdentityModel.Tokens.Jwt` ≥ 8.0.1 `< 9.0.0` *(transitive)* | MIT | Microsoft. ⚠️ The upper bound is a real constraint: #5 already brings the `Microsoft.IdentityModel` 8.x family via `Microsoft.AspNetCore.Authentication.JwtBearer`, so today they agree — but with `CentralPackageTransitivePinningEnabled` on (suite-wide since #4), any future pin to 9.x breaks restore. The plan pins deliberately and the README records the constraint. |

**One new CPM pin** for the shipped closure: `Duende.AccessTokenManagement` 4.2.0 (exact patch re-confirmed
against the feed at plan time). Transitive pinning means this package's nuspec will also list the promoted
transitives — the accepted consequence recorded for #4.

### Test-only — `Cloudstrap.TestIdentityProvider` (never in a shipped closure, AC-CC15)

| Package | License | Evidence & justification |
|---|---|---|
| `OpenIddict.Core` + `OpenIddict.Server.AspNetCore` 7.6.0 | **Apache-2.0** ✅ | [nuget.org](https://www.nuget.org/packages/OpenIddict.Server.AspNetCore): Apache-2.0, published **2026-07-15**, targets `net8.0`/`net9.0`/**`net10.0`**, **23.5M** downloads, owner `openiddict`, actively developed in [openiddict/openiddict-core](https://github.com/openiddict/openiddict-core) with an official [samples repo](https://github.com/openiddict/openiddict-samples). Provides the discovery document, JWKS, token endpoint and RFC-conformant grant handling. Finding 10: the source's own dev token server is built on it. |
| `OpenIddict.EntityFrameworkCore` 7.6.0 + an in-process EF Core store provider | Apache-2.0 / MIT | OpenIddict's client, scope and token stores. **Store-based rather than `EnableDegradedMode()`**: degraded mode removes the store dependency but also disables client-id/secret validation, which #9 needs (AC-CC8 rejects a bad credential) and #10 needs far more (authorization codes, PKCE and refresh tokens all require storage). Choosing degraded mode now would mean hand-writing those handlers for #10 — the opposite of "design so #10 extends this". The concrete provider (SQLite in-memory versus the EF In-Memory provider the source used) is a plan-level pick: SQLite is Microsoft's recommendation for testing and OpenIddict's stores are relational-shaped, so it is the expected choice, but it does not change this spec. |
| `Microsoft.AspNetCore.TestHost` | MIT | Already CPM-pinned test-only since #4; the in-process hosting mode. |
| `Microsoft.Extensions.TimeProvider.Testing` *(conditional)* | MIT | Microsoft; only if the plan confirms the token clock is fakeable. Otherwise short real lifetimes drive AC-CC3. |

Considered and **rejected**:

- **`Microsoft.Identity.Web`** (MIT, Microsoft, active) — covers client credentials for downstream APIs via
  `ITokenAcquisition`/`IDownstreamApi`, but is MSAL-based and therefore **Entra-ID-only**. The source library
  authenticates against Keycloak, and Cloudstrap's auth story is deliberately IdP-neutral (founding AC-A1 names
  "any standards-compliant IdP"). Rejected on fit, not health.
- **`Azure.Identity` `TokenCredential` alone** (MIT, already in the suite via #3/#4) — the right tool for tokens
  to *Azure resources*, but it cannot perform a generic OAuth 2.0 client-credentials grant against a non-Entra
  token endpoint, so it cannot be the base. Recorded as an additive future (D-1).
- **`Duende.IdentityModel` alone** (Apache-2.0) — protocol primitives only. Choosing it means Cloudstrap owns
  the cache, the expiry buffer, the refresh stampede control, the 401-retry handler and their tests.
- **A bespoke handler over `IMemoryCache`** — ~300 lines of security-sensitive code that a 20.2M-download
  Apache-2.0 library already provides. Fails the cost-of-ownership test outright.
- **`Duende.AccessTokenManagement.OpenIdConnect`** — the user-token package; #10's dependency, not #9's.
- **Duende IdentityServer as the test IdP** — technically excellent and the natural sibling of ATM, but it is
  **commercially licensed**; CLAUDE.md rule 4 (OSI-approved only) rules it out even for test infrastructure.
- **A Keycloak (or any) container as the test IdP** — a real server, but it adds Docker to every developer's and
  CI agent's critical path for a suite that is currently container-free, cannot be hosted in-process for unit
  tests, and makes token lifetimes and claim sets awkward to vary per test. Rejected for #9; #10 may still add
  one for interactive-flow conformance (founding AC-A1).
- **A hand-written stub token endpoint** — this spec's original recommendation, overridden by D-5. It would have
  proved parameter mapping and caching but *not* that Cloudstrap's acquisition and #5's validation interoperate
  over a genuine discovery document and JWKS, which is the more valuable assertion, and it would have had to be
  thrown away and rebuilt for #10.
- **Any `Aspire.*` package** — prohibited (AC-ASP2).

---

## Deliberate Behavior Changes (vs. the source library)

1. **Activation is an explicit call**, not `Security:EnableAuthentication`. An app with the flag off used to log
   a warning and silently send unauthenticated requests; now the package is either registered or it is not, and
   a flagged client without a provider fails fast.
2. **`Authority` + an implied Keycloak path becomes an explicit `TokenEndpoint`.** No IdP path convention ships
   in any default (De-NIHDI checklist; the #5 `TokenUrl` precedent).
3. **The client secret is no longer unconditionally required**, and secret-free credentials (client assertion)
   are a supported, documented path. No Cloudstrap example, fixture or SUT config contains a secret value.
4. **Configuration moves to a flat `Cloudstrap:ClientCredentials`** — no `Security:Authentication:*` graph, no
   `IValidatableObject` cascade, no "at least one of three sub-sections" cross-rule.
5. **Scopes are a single `Scope` string on the client** (`Security:Authentication:OAuth:Scopes` — which never
   bound, finding 2 — is gone), overridable per HTTP client via the already-shipped `TokenRequestParameters`.
6. **The token cache is an isolated `HybridCache` by default**, so tokens never reach an application
   `IDistributedCache` unless `TokenCache = Shared` says so. ATM's lifetime buffer and auto-tuning replace the
   source's `CacheKeyPrefix`/`CacheLifetimeBuffer` settings, which defaulted to empty/`0` and had no readers.
7. **`AuthenticationFlow` is gone.** The grant is determined by the package, not by an enum that could name
   `Implicit`.
8. **`SignInScheme`/`ChallengeScheme` in `TokenRequestParameters` are ignored for client tokens**, with a
   startup warning naming the key — the source silently forwarded them into a client-credentials request where
   they mean nothing.
9. **Health-probe clients never receive a token** (the source's probe client was likewise unauthenticated, but
   by omission rather than by decision — this makes it a documented rule).
10. **The token backchannel does not carry the correlation header**, so Cloudstrap's correlation id is not
    disclosed to the identity provider.
11. **The access-token seam is two interfaces, not one** (D-4). The source had no such seam; this changes
    *Cloudstrap's own* shipped `Cloudstrap.Extensions` API, which is why it is recorded here as well as in the
    risk header.
12. **The test identity provider issues only client-credentials tokens and never implicit or hybrid ones**, its
    multi-valued claims are JSON arrays rather than pipe-separated strings, it does not rewrite `schemas.` claim
    types, and its configuration carries no personal data, no enterprise identifiers and no real secrets
    (findings 11, 12).

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| `AddCloudstrapClientCredentials()` called, no client anywhere sets `AddClientAccessToken` | Services registered, nothing attached, no token ever requested. No warning — a Worker may inject `IClientCredentialsTokenManager` directly. |
| `AddClientAccessToken: true` but `AddCloudstrapClientCredentials()` never called | Fail fast at client creation, naming the key and this package. |
| Both `AddUserAccessToken` and `AddClientAccessToken` are `true`, only #9 installed | Fails naming **only** the user flag and `Cloudstrap.Authentication.OpenIdConnect` before any request is sent; the message must not suggest that turning off the *client* flag would help (D-4). |
| Both #9 and #10 installed, both flags set on one client | Two handlers, user first — now genuinely supported because each interface resolves independently (AC-CC13). |
| Token endpoint unreachable at startup | Startup **succeeds** — tokens are acquired lazily on first use, not at boot. The first outbound call fails per AC-CC8. (Deliberate: a transient IdP outage must not stop a service from starting and serving its liveness probe.) |
| `TokenRequestParameters:ForceRenewal = true` configured statically | Honored — every request of that client fetches a fresh token. Logged once at startup as a warning, because it disables caching for that client and can rate-limit the IdP. |
| Two clients with identical scope/resource | One cache entry, one token — cache keys are derived from the token request, not the HTTP client name. |
| The consumer registers an `IDistributedCache` (Redis) for other purposes | Tokens stay in the isolated in-memory cache; the distributed cache is untouched (AC-CC12). Opting into `TokenCache = Shared` is the documented way to get cross-instance token reuse, with the trade-off spelled out. |
| `TokenCache = Shared` but the app has no `IDistributedCache` | Works — `HybridCache` simply has no L2 tier; behavior is identical to `Isolated` apart from sharing the cache instance. No error, no warning beyond the startup mode line. |
| The consumer already called Duende's `AddClientCredentialsTokenManagement()` themselves | Cloudstrap's registration is additive and idempotent (`TryAdd` semantics); its own client name (`cloudstrap`) does not collide with the consumer's names. |
| `Cloudstrap:ClientCredentials` present but the package is not referenced | Nothing happens — an unread section is not an error (consistent with the rest of the suite). |
| Token response omits `expires_in` | ATM's `DefaultCacheLifetime` applies; the README states the value and how to change it. |
| Configuration reload changes `ClientSecret` at runtime | The named ATM client is configured through the options system, so a reload is picked up on the next token request; already-cached tokens remain valid until they expire. |
| Both a `ClientSecret` and an `IClientAssertionService` are present | The assertion service wins (Duende's own precedence); the startup log states which credential type is in use, so the situation is never silent. |
| The test IdP is asked for an unconfigured client id, or given a wrong secret | Standards-shaped `invalid_client` error response — which is what AC-CC8 asserts against; the IdP never issues a token for an unknown client. |
| A test needs two isolated identity providers (e.g. a wrong-issuer scenario) | Two instances hosted on different addresses/handlers; the options carry no static or ambient state. |

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- Everything in the founding spec's global Out of Scope: message encryption, MessagingBridge, Dynatrace,
  ServicePlatform/ServicePulse, `Cloudstrap.Functional`, `Cloudstrap.Aspire`.
- **Inbound JWT validation** — shipped by #5 (`AddCloudstrapJwtBearer`, `Cloudstrap:JwtBearer`). This package
  never registers an authentication scheme, an authorization policy or middleware. (AC-CC14 *uses* #5; it does
  not modify it.)
- **User/interactive tokens and the OIDC login flow** — #10, including
  `Duende.AccessTokenManagement.OpenIdConnect`, `IUserAccessTokenHandlerProvider`'s implementation,
  `OpenIdConnectConfiguration`, `IdentityTokenCacheLifetime` and refresh-token lifetimes.
- **The test identity provider's interactive half** — authorization endpoint, authorization-code + PKCE grant,
  refresh-token grant, userinfo endpoint, `IdToken`/`UserInfo` claim sets, per-client `RedirectUri`, consent and
  any login UI: **#10 extends the same project**. #9 builds the client-credentials grant, discovery and JWKS only.
- **Implicit and hybrid flows anywhere**, including in test infrastructure — dropped permanently.
- **YARP trusted-subsystem forwarding** (#17) and **Blazor Server token flow** (#12) — both consume this
  package's capability; neither is built here.
- **Multiple named token clients / a per-HTTP-client `TokenClientName`.** One application identity with
  per-client scope/resource overrides covers the realistic case, and adding a `TokenClientName` property to
  `HttpClientServiceOptions` later is purely additive — deferring costs nothing. Consumers needing several
  identities today use Duende's `AddClient(...)` + `AddClientCredentialsTokenHandler(...)` directly.
- **OIDC discovery** to derive the token endpoint from an authority (bespoke fetch + cache + a startup network
  call); an explicit `TokenEndpoint` is required instead.
- **Built-in DPoP, mTLS, `private_key_jwt`, Azure workload-identity federated credentials, or a
  `DefaultAzureCredential` acquisition mode.** D-1 keeps all of these outside the package: the `Client` hook and
  a consumer-registered `IClientAssertionService` reach them without Cloudstrap owning crypto or environment
  conventions. Workload identity and `TokenCredential` remain **additive futures** — each is an extra options
  member plus a registration, breaking nothing — to be requested on their own merits, not built here.
- **Token exchange (RFC 8693), token revocation, introspection, and a Cloudstrap facade over
  `IClientCredentialsTokenManager`.**
- **At-rest encryption of cached tokens** — unnecessary under the `Isolated` default; consumers choosing
  `Shared` are pointed at Duende's cache-encryption guidance rather than given Cloudstrap code.
- **A containerised identity provider** (Keycloak or otherwise) and any test requiring Docker; #10 may revisit
  this for interactive-flow conformance under founding AC-A1.
- Everything **Dropped** above: the `EnableAuthentication` gate, `AuthenticationFlow` and its enum,
  `RefreshTokenCacheLifetime`, `IdentityTokenCacheLifetime`, `UseDefaultClaimTypeMapping`, the nested
  `AuthenticationConfiguration` graph and its validation cascade, `AddNihdiAccessTokenManagement()` inside JWT
  registration, the pipe-separated multi-value and `schemas.`-prefix conventions, `LogParameters`, `BaseUrl`,
  the StyleCop reference, the `Riziv-Inami` header, and any committed secret or personal-data value.

---

## Decision Log (gate answers, 2026-08-05 — zero Open Questions remain; spec is planner-ready)

| # | Question | Answer (user, 2026-08-05) |
|---|---|---|
| **D-1** | ⚠️ Where does the client secret come from? (auth + public API — the deliverable's central decision) | **Option A, as recommended.** `ClientSecret` is an **optional** setting in `Cloudstrap:ClientCredentials`, supplied by any configuration provider (#4's KeyVault, environment variables, user-secrets); secret-free operation comes from a consumer-registered `IClientAssertionService`, Duende's own extension point, which Cloudstrap never overwrites. **No Cloudstrap credential-modelling type** is introduced and **zero** credential code is owned. A shipped Azure workload-identity federated-credential reader (the analysis's option B) and a `DefaultAzureCredential`/`TokenCredential` acquisition mode (option C) are recorded as **additive futures** — each is an extra options member plus a registration and breaks nothing — and are explicitly **not built** here. Documentation and every example show a placeholder, never a secret value (finding 3). Covered by AC-CC5 and AC-CC11. |
| **D-2** | The `Cloudstrap:ClientCredentials` section shape and the #9/#10 line | **Option (a), as recommended.** A **flat `Cloudstrap:ClientCredentials`** section sitting alongside #5's shipped `Cloudstrap:JwtBearer` and, later, #10's own section. **No shared `Cloudstrap:Authentication` parent** — it would have required breaking #5's shipped section and would couple three packages through one configuration node. An app that both validates and acquires configures two short URLs in two places; each package's configuration stays readable in isolation, and #9's validity never depends on whether another package happens to be installed. |
| **D-3** | Token cache posture — how much of ATM's cache do we surface, and what about the distributed tier? | **Option (b) — NOT this spec's recommendation.** Duende ATM v4 caches in `HybridCache`, whose L2 tier is *any* registered `IDistributedCache`, so an app using Redis for its own reasons would silently push bearer tokens out of process. Cloudstrap therefore ships an **isolated, in-memory token cache as the default**, registered through Duende's keyed `ServiceProviderKeys.ClientCredentialsTokenCache` extension point (still zero bespoke cache code). Secure-by-default beats least-surprise here. The default is **visible, not magic**: it is a first-class enum setting, `Cloudstrap:ClientCredentials:TokenCache` (`Isolated` \| `Shared`), documented in the README with its trade-off, and the mode in force is stated in a startup log line. `Shared` opts into the application cache and its L2 tier for cross-instance token reuse. Every other cache knob stays Duende's, reachable via `configurator.TokenManagement`. Covered by AC-CC12. |
| **D-4** | ⚠️ #4's shipped seam resolves **one** provider — #9 and #10 cannot coexist *(shared-contract change)* | **Option (ii), as recommended: split the seam.** `IAccessTokenHandlerProvider` is replaced by **`IUserAccessTokenHandlerProvider`** and **`IClientAccessTokenHandlerProvider`**, each with one method, keeping the existing method names and signatures so the seam's documented contract is otherwise unchanged. `AccessTokenHandlerWiring` resolves each independently and fails per flag with the precise package name; the full behavior matrix (neither / one / both / partially registered) is specified in the Public API Sketch. This is an **acknowledged breaking change to the shipped `Cloudstrap.Extensions`**, permitted under the standing pre-release permission and reviewable at its gate. `AccessTokenHandlerSeamTests`' `RecordingTokenHandlerProvider` — the double whose implementation of *both* methods is exactly what masked the defect — splits into two independently registered doubles, and the fixture gains the regression case the old seam could not pass (two separate providers, both flags, both handlers in order) plus the one-present/one-missing failure case. Covered by AC-CC7 and AC-CC13. |
| **D-5** | Verification posture — does #9 need an integration test against a real identity provider? | **A lightweight identity provider is built into this repository — NOT this spec's stub-only recommendation, and not a container.** Reference material: the source's `Test\TestProject\src\STS\Nihdi.TestProject.STS.TestServer\`. Analysis of it (findings 10–12) settled the buy-vs-build question decisively: **buy — the source's own dev token server is a thin configuration wrapper over OpenIddict with an in-memory EF Core store**, proven by its build-output closure (`OpenIddict.Server*.dll`, `OpenIddict.Core.dll`, `OpenIddict.EntityFrameworkCore.dll`, `Microsoft.EntityFrameworkCore.InMemory.dll`). Cloudstrap does the same on **OpenIddict 7.6.0 (Apache-2.0, 2026-07-15, net10.0, 23.5M downloads, actively developed)** — Duende IdentityServer is excluded as commercially licensed, and hand-writing a conformant discovery document, JWKS and grant handler is protocol code with no place in this repo. It lives in a **test-infrastructure library under `src/Test/`** (`Cloudstrap.TestIdentityProvider`), where `src/Test/Directory.Build.props` already forces `IsPackable=false`, so **OpenIddict and EF Core can never enter a shipped closure** (AC-CC15 asserts it) — which is what makes a dependency set that would be unacceptable in a `Cloudstrap.*` package unremarkable here. **Scoped to #9's need**: the client-credentials grant plus a discovery document and JWKS that #5's shipped `AddCloudstrapJwtBearer` can validate against, so a single test proves acquisition **and** validation end to end (AC-CC14) — the real prize a stub could never deliver. Two hosting modes: in-process for unit tests (no sockets) and loopback for the E2E fixture (genuine JWKS retrieval). Implicit and hybrid flows are dropped permanently; authorization-code + PKCE, refresh, userinfo, `IdToken`/`UserInfo` claims and `RedirectUri` are **routed to #10, which extends this project rather than replacing it**. ⚠️ It becomes shared infrastructure for #10, #12 and #17, so its options model, hosting modes and location are a **one-way door** fixed here. Not one character of the source `appsettings.json` content is carried over — it contains a real person's name and email, a `riziv.org` account, enterprise client ids and live-looking secrets; only its *shape* is a reference, and AC-CC15 greps for the residue. |
