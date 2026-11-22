# Spec: Trusted-Subsystem Proxy — `Cloudstrap.Proxy` (Roadmap Deliverable #17)

> Status: **DRAFT 2026-11-22. Awaiting the owner's answers to OQ-1…OQ-8. Not planner-ready.**
> 🛑 **Spec approval gate**: the planner must not start `_plans/17-TrustedSubsystemProxy.md` until every Open Question
> is answered and this status line reads APPROVED.
>
> In short: a consumer's front-door host (a BFF, an interactive web app) authenticates the **user**, then forwards
> selected routes to a downstream API with **the application's own** access token — the downstream API trusts the
> calling subsystem, not the end user's credential. YARP does the forwarding; Cloudstrap's contribution is the
> opinionated, fail-safe glue: authenticated-by-default routes, inbound credentials stripped before forwarding, and the
> outbound token attached through the **token seams #4 already ships** (`IClientAccessTokenHandlerProvider`, filled
> by #9; optionally `IUserAccessTokenHandlerProvider`, filled by #10). The analyst's position is that this is a
> **redesign over YARP's own configuration model**, not a port of a bespoke settings tree: Cloudstrap should add
> perhaps four behaviors on top of `Yarp.ReverseProxy`, and re-model none of YARP's routes/clusters.
>
> Sources: `_plans/ROADMAP.md` §17 (goal, hand-off brief, 2026-11-22) · `_specs/Cloudstrap.md` (Package Map row
> `Nihdi.Core.Configuration.Proxy` → `Cloudstrap.Proxy` "YARP trusted-subsystem forwarder", v1-scope row, Aspire
> Coexistence AC-ASP1–AC-ASP3, Auth Replacement AC-A3) · `_specs/9-ClientCredentialsAuth.md` (Port table row
> "`Proxy\ServiceCollectionExtensions.AddNihdiTrustedSubsystemProxy`'s client-credentials handler usage → **Routed**
> #17"; D-4 seam split) · `_specs/4-ConfigKeyVaultHttpExtensions.md` (typed-client pipeline, AC-ASP3 "no resilience
> ever added", `UseProxy = false` dropped) · `_specs/16-HangfireScheduler.md` (Part C + DD-1: `ForwardedPrefixPathBase`
> and `Hangfire.Proxy` routed to #18 through a public seam; the dashboard's "JWT-authenticated caller" posture is what
> a #17 proxy presents) · `_specs/WasmTestProjectDemoCompletion.md` V-10 (the downstream JWT API host "is the topology
> #17 needs") · shape precedent `_specs/8-TestHelpers.md` / `_specs/16-HangfireScheduler.md` (findings → verdict table →
> Decision Log → Open Questions).
>
> **Cloudstrap code read**: `src/Cloudstrap.Extensions/IClientAccessTokenHandlerProvider.cs`,
> `IUserAccessTokenHandlerProvider.cs`, `AccessTokenHandlerWiring.cs` (fail-loud when a flag has no provider),
> `README.md` (seam table); `src/Cloudstrap.Authentication.ClientCredentials/ClientCredentialsAccessTokenHandlerProvider.cs`
> and the `AddCloudstrapClientCredentials` / `AddCloudstrapOpenIdConnect` registrations;
> `src/Cloudstrap.WebApi/WebApplicationBuilderExtensions.cs` (`AddCloudstrapJwtBearer`); `src/Directory.Packages.props`
> (Duende ATM 4.2.0 pinned; **no `Yarp.*` pinned yet**); `src/demo/README.md` (architecture, port map: Bff 5300,
> IdP 5310, Api 5330 — the Bff already holds a machine client `demo-bff` and a user-token `UserApi` client to the Api).
>
> ⚠️ **Source-reading limitation (must be closed before approval — see OQ-1).** This draft was produced in a job
> sandbox confined to the Cloudstrap worktree; the read-only reference repo
> (`D:\source\Nihdi-Core-Configuration\…\src\Nihdi.Core.Configuration.Proxy\`) was **not opened**. Everything this spec
> says about the source comes from **secondary evidence already recorded in approved Cloudstrap specs** (#9's Port
> table and source list, #16 Part C, the roadmap's dependency graph): the package references Common and
> `Yarp.ReverseProxy`, exposes `AddNihdiTrustedSubsystemProxy` in `ServiceCollectionExtensions.cs`, attaches an
> internal-package client-credentials handler around line 74, and is consumed by `Hangfire.Proxy`
> (`AddHangfireDashboardProxyForNihdiWfe` / `UseHangfireDashboardProxyForNihdiWfe`). The Port Decision Table below is
> therefore **provisional**: rows marked *(to verify)* name what a file-by-file read must confirm or correct.
>
> **External evidence**: none gathered in this session (no web access). Every library fact below is from the
> analyst's knowledge of YARP 2.x and is marked *(verify at plan time)* where a version or API detail matters:
> `Yarp.ReverseProxy` is **MIT** (.NET Foundation, `dotnet/yarp`); the forwarder exposes
> `ForwarderHttpClientFactory.WrapHandler(ForwarderHttpClientContext, HttpMessageHandler)` as its handler-chain
> extension point, per-route `AuthorizationPolicy` (incl. the reserved `Default` / `Anonymous` values), request
> transforms (`RequestHeaderRemove`, `AddRequestTransform`), default `X-Forwarded-For/Proto/Host/Prefix` emission, and
> route/cluster `Metadata`. **Duende.BFF** (the obvious "do it for me" library) is **not** an option: it is
> commercially licensed, not OSI (CLAUDE.md rule 4).
>
> **⚠️ Risk areas this deliverable touches**:
> - **Auth code (credential handling).** The proxy decides which credential leaves the process. A wrong default leaks
>   the user's cookie or bearer token to a downstream service, or forwards an unauthenticated caller with the
>   application's privileged token (a confused-deputy hole). Human review required.
> - **Public API surface.** A new package: entry points, options, cluster-metadata keys are all one-way doors.
> - **Dependency update.** First `Yarp.ReverseProxy` reference in the suite (MIT; new CPM pin).
> - **Shared contract with #18.** `Cloudstrap.Hangfire.Proxy` builds on this package; the forwarded-prefix headers
>   and the token-mode knob are its inputs.
> - **Aspire overlap.** Aspire offers `Microsoft.Extensions.ServiceDiscovery.Yarp` for destination resolution; the
>   proxy must compose with it without referencing it (AC-ASP2 tripwire, AC-PX12).
>
> **Standing constraint**: nothing is published to nuget.org yet, so breaking changes to shipped packages are allowed
> (propose, never silently). This spec proposes **no change to any shipped package's public API**; finding 6 flags one
> cosmetic log-message issue in #9 that the planner may fix internally.

## Code-reading findings that shaped this spec

1. **The capability #17 needs already ships, as two seams.** #4 declared `IClientAccessTokenHandlerProvider` and
   `IUserAccessTokenHandlerProvider` (D-4 split, #9), each returning a fresh `DelegatingHandler` whose `InnerHandler`
   is unset. #9 and #10 fill them. A YARP forwarder accepts exactly that shape through
   `ForwarderHttpClientFactory.WrapHandler`. #17 therefore needs **no** new token code and **no** reference to Duende
   or to either auth package — it consumes the seam, like every typed client does.
2. **The seam's fail-loud rule must carry over.** `AccessTokenHandlerWiring` throws a message naming the flag and the
   package to install when a client asks for a token and no provider is registered — "an unauthenticated request is
   never sent in its place". A proxy cluster configured for a token must behave identically (AC-PX4).
3. **YARP already has a complete, well-documented configuration model.** Routes, clusters, destinations, health
   checks, session affinity, load balancing, transforms — all bound from the `ReverseProxy` section or code. A
   Cloudstrap-owned mirror (`Cloudstrap:Proxy:Routes…`) would be surface that only forwards, and would lag every YARP
   release. The Cloudstrap-specific knobs are few enough to ride in **cluster `Metadata`** and one small options class.
4. **Stock YARP is not fail-safe for a trusted subsystem.**
   - It forwards inbound request headers by default, **including `Cookie` and `Authorization`**. Behind a cookie-auth
     front door that sends the user's session cookie to the downstream API; behind a JWT front door it forwards the
     user's token alongside (or instead of) the application's.
   - A route with no `AuthorizationPolicy` falls through to the app's fallback policy — anonymous when the consumer
     has none. Forwarding an anonymous caller **with the application's token** is a confused-deputy hole.
   These two defaults are the reason the package exists (AC-PX2, AC-PX3).
5. **Correlation does not flow automatically.** Cloudstrap's correlation handler (#2) is added to `IHttpClientFactory`
   clients; YARP builds its own `HttpMessageInvoker` and bypasses the factory. Without explicit wiring the proxied hop
   loses the correlation id (AC-PX7). W3C `traceparent` is unaffected (YARP's `HttpClient` activity propagates it).
6. **The #9 provider's warnings name a `Cloudstrap:HttpClients:{name}` path.** `ClientCredentialsAccessTokenHandlerProvider`
   uses `clientName` only to build warning text (`$"Cloudstrap:HttpClients:{clientName}:TokenRequestParameters"`).
   Passing a YARP cluster id produces a misleading key in the warning. Harmless, internal, fixable without API change
   (Deliberate Behavior Changes, item 4).
7. **#16 and #18 already fixed the downstream half.** #16's dashboard on a JWT host answers "anonymous → 401, valid
   token → 200"; the "trusted-subsystem" posture there *is* "a caller holding the application's token". #18 will
   need: a path-prefixed route, the `X-Forwarded-Prefix` header (YARP emits it by default — *verify*), and the
   client-credentials token mode. Nothing more from #17.
8. **The demo topology is already in place.** The Bff (5300) signs the user in with an OIDC cookie and owns a
   `demo-bff` machine client; the Api (5330) is a pure JWT host, authenticated by default, with
   `GET api/v1/downstream/whoami`. A proxied route `Bff → Api` with the machine token is a one-route demonstration
   whose E2E assertion is crisp: the Api reports the **machine** identity, never the user's.

---

## User Story

**As a** developer building a front-door host (BFF, interactive web app) on Cloudstrap,
**I want to** forward selected routes to a downstream API with one registration call, where the front door
authenticates the user and the downstream API receives **my application's** access token,
**So that** the downstream API trusts one well-known caller, the user's cookie or token never leaves my front door,
and an unauthenticated caller can never ride on my application's privileges.

---

## Acceptance Criteria

> The founding spec defines **no Proxy ACs** — only the Package Map row. AC-ASP2 and AC-A3 are carried verbatim.
> AC-PX1…AC-PX14 are new (prefix `AC-PX`). Criteria marked *(OQ-n)* assume the recommended answer and are rewritten
> if the owner chooses otherwise.

| # | Given | When | Then |
|---|-------|------|------|
| AC-PX1 | A host referencing `Cloudstrap.Proxy` with routes/clusters in YARP's standard `ReverseProxy` configuration section | It calls `AddCloudstrapProxy()` and maps the proxy (`MapCloudstrapProxy()`) | Requests matching a route are forwarded to the cluster's destination; the registration returns YARP's own `IReverseProxyBuilder` so every YARP extension (transforms, service discovery, custom providers) remains reachable. *(OQ-2, OQ-3)* |
| AC-PX2 | A proxied route with **no** `AuthorizationPolicy` | An unauthenticated request arrives | It is **challenged/rejected by the front door** (401 for a bearer front door, the configured challenge for a cookie front door) and **nothing is forwarded**. Only a route explicitly configured `AuthorizationPolicy: Anonymous` forwards anonymous callers — and such a route is refused at startup if its cluster attaches the application's token (AC-PX5). *(OQ-4)* |
| AC-PX3 | Any proxied request carrying `Cookie` and/or `Authorization` from the caller | It is forwarded | Neither inbound header reaches the destination (asserted on the downstream side). A per-route opt-out exists (explicit metadata) and is documented as unsafe for the trusted-subsystem pattern. *(OQ-5)* |
| AC-PX4 | A cluster whose token mode is `Client` (default, OQ-6) | A request is forwarded | It carries `Authorization: Bearer <application token>` obtained through `IClientAccessTokenHandlerProvider` (cached and renewed by #9 — the proxy acquires nothing itself). If no provider is registered, **startup (or first use of that cluster) fails** with a message naming the cluster, the metadata key and `Cloudstrap.Authentication.ClientCredentials` — no request is ever sent without the token. |
| AC-PX5 | A cluster in `Client` mode reachable from a route with `AuthorizationPolicy: Anonymous` | The host starts | Startup fails with a message naming the route and cluster (confused-deputy guard). An explicit, documented override exists for a deliberately public route. *(OQ-4)* |
| AC-PX6 | A cluster with token mode `User` *(only if OQ-6 (b) is chosen)* / `None` | A request is forwarded | `User`: the signed-in user's access token via `IUserAccessTokenHandlerProvider` (#10), with the same fail-loud rule naming `Cloudstrap.Authentication.OpenIdConnect`. `None`: no `Authorization` header (plain reverse proxy; still subject to AC-PX2/AC-PX3). An unknown mode value fails startup listing the allowed values. |
| AC-PX7 | A request with a Cloudstrap correlation id | It is forwarded | The destination receives the same correlation header as a Cloudstrap typed client would send (#2 contract), and W3C `traceparent` continues the front door's trace. |
| AC-PX8 | A path-prefixed route (e.g. `/downstream/{**catch-all}` → `/`) | It is forwarded | The destination receives `X-Forwarded-Prefix`, `X-Forwarded-Proto`, `X-Forwarded-Host` and `X-Forwarded-For` (YARP defaults, kept — the #18 input). Cloudstrap adds no bespoke forwarding header. |
| AC-PX9 | A consumer that already configured `ConfigureHttpClientDefaults` resilience, or an Aspire ServiceDefaults host | `AddCloudstrapProxy()` runs | Cloudstrap adds **no** resilience, retry or timeout handler to the forwarder (AC-ASP3 posture — YARP's own `ActivityTimeout` and the consumer's choices stand) and registers no second OTel pipeline/exporter (AC-ASP1 contribute posture). |
| AC-PX10 | The package's own options (`Cloudstrap:Proxy`) | They are invalid (e.g. a configured section name that does not exist, an unknown token mode) | `ValidateOnStart` fails with an actionable message naming the key — the #1 source-generated-validator pattern. |
| AC-PX11 | The `Cloudstrap.Proxy` package | It is packed and inspected | XML docs on every public member; sealed public classes; guard clauses; MIT `PackageLicenseExpression`, icon, README (incl. "trusted subsystem" explanation, the cookie/authorization stripping rule, and the "one owner" note for service discovery); `Yarp.ReverseProxy` pinned in CPM with its license recorded. |
| AC-PX12 | A host that resolves destinations through `Microsoft.Extensions.ServiceDiscovery.Yarp` (Aspire) | It chains `.AddServiceDiscoveryDestinationResolver()` on the builder `AddCloudstrapProxy()` returns | Forwarding works with the Cloudstrap behaviors intact (proven in a unit/integration test **without** an `Aspire.*` reference in the shipped package — the test project may reference the resolver package only if OQ-8 allows; otherwise README guidance only). |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-A3 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Nihdi.AspNetCore.*` packages. *(carried verbatim)* |
| AC-PX13 | The files this deliverable adds | A case-insensitive search for `Nihdi`, `NIHDI`, `Riziv`, `Wfe`, `Cfe` | Zero hits. Fixtures are neutral (`contoso`, `example.com`, `demo-bff`). |
| AC-PX14 | CLAUDE.md workflow rule 9 | The deliverable closes | **BlazorWasm Bff** (5300) proxies a route to the **Api** (5330) in `Client` mode, and ≥ 1 new E2E test in `Cloudstrap.Demo.E2E.Tests` proves through the running apps: (a) signed-in browser → proxied `whoami` returns the **machine** identity (`demo-bff`), not the user; (b) anonymous call → rejected at the Bff, Api never hit; (c) the Bff session cookie is absent downstream. *(OQ-7)* |

---

## Port Decision Table (provisional — see the source-reading limitation)

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `Nihdi.Core.Configuration.Proxy\ServiceCollectionExtensions.AddNihdiTrustedSubsystemProxy` *(to verify: signature, receiver, parameters)* | **Redesign** | `AddCloudstrapProxy(this IHostApplicationBuilder, Action<CloudstrapProxyOptions>?)` → `IReverseProxyBuilder`, plus `MapCloudstrapProxy(this IEndpointRouteBuilder)` → `ReverseProxyConventionBuilder` | The one-call registration earns its place; a settings-object + `ILogger` parameter style does not (#9 precedent). Returning YARP's builder keeps every YARP extension reachable ("every convention has an override"). OQ-3. |
| ↳ its client-credentials handler usage (`AddClientAccessTokenHandler`, ~line 74, internal package) | **Replace** (already decided by #9: Routed → #17) | `IClientAccessTokenHandlerProvider` wrapped around YARP's forwarder handler per cluster | Finding 1. The token mechanism is #9's (Duende ATM behind the seam); the proxy consumes the seam. AC-A3. |
| ↳ route/cluster configuration *(to verify: YARP `ReverseProxy` section vs a `Nihdi:*` settings tree)* | **Replace** | YARP's native `ReverseProxy` section (section name overridable) + cluster `Metadata` for the Cloudstrap token mode | Finding 3. If the source re-modelled routes in `Nihdi:` settings, that mirror is dropped; if it used YARP's section, this is a verbatim carry-over. OQ-2. |
| ↳ front-door authorization *(to verify: does the source enforce an authenticated user before forwarding?)* | **Redesign** | Authenticated-by-default convention on mapped proxy routes + startup confused-deputy guard | Finding 4. Fail-safe default is a hard requirement whatever the source did. OQ-4. |
| ↳ inbound credential handling *(to verify: does the source strip `Cookie`/`Authorization`?)* | **Redesign** | Default request transform removing both headers | Finding 4. OQ-5. |
| ↳ any user-identity propagation header *(to verify: e.g. `X-User`/`X-Forwarded-User` injection)* | **Drop** (provisional) | — | Unsigned identity headers are spoofable by anyone who can reach the downstream API directly; a downstream that trusts the subsystem should authorize the subsystem. If the source does this and a consumer depends on it, it returns as an opt-in transform — OQ-6 (d). |
| ↳ any `EnableAuthentication`-style gate, `NihdiConfiguration` / `ILogger` parameters, environment sniffing *(to verify)* | **Drop** | — | #5 D-2 and #9 precedents: whether a host forwards with credentials is visible in `Program.cs`; settings come from binding, logging from DI; no environment taxonomy. |
| `Nihdi.Core.Configuration.Proxy.csproj` → Common + `Yarp.ReverseProxy` *(version to verify)* | **Redesign** | `Cloudstrap.Proxy.csproj` → `Cloudstrap.Extensions` (seams, correlation via #2's transitive closure) + `Yarp.ReverseProxy` (new CPM pin, latest stable `net10.0`-compatible) | Roadmap dependency row (depends on #4). **No** reference to either auth package — the seams decouple them (finding 1). |
| `InternalsVisibleTo` consumers (`Hangfire.Proxy`) *(to verify)* | **Drop** | Public seam only | #16 DD-1 precedent: #18 builds on public API (route config, token mode, forwarded headers). |
| README / `Riziv-Inami` headers / `Wfe`/`Cfe` vocabulary | **Drop** | New MIT README; neutral "front-door host" / "downstream API" vocabulary | De-NIHDI checklist + terminology memory (Cfe → Bff; industry terms). |
| Source tests *(to verify: is there a `Proxy.Tests` project?)* | **Read as observed contract** | `Cloudstrap.Proxy.Tests` (TestHost-based: front door + in-process downstream echo server) | Executable evidence, if any, defines behaviors to preserve or deliberately change. |

**Tally (provisional)**: 0 Port · 4 Redesign · 2 Replace · 4 Drop · 1 Read-as-contract *(11 rows; recount after OQ-1)*.

---

## Public API Sketch (illustrative; the planner owns exact signatures)

```csharp
namespace Cloudstrap.Proxy;

public static class HostApplicationBuilderExtensions
{
    // Binds YARP from configuration (section from options, default "ReverseProxy"), applies the Cloudstrap
    // forwarder-handler wrapper and default transforms. Returns YARP's builder for further chaining.
    public static IReverseProxyBuilder AddCloudstrapProxy(
        this IHostApplicationBuilder builder, Action<CloudstrapProxyOptions>? configure = null);
}

public static class EndpointRouteBuilderExtensions
{
    // MapReverseProxy + the authenticated-by-default convention (AC-PX2).
    public static ReverseProxyConventionBuilder MapCloudstrapProxy(this IEndpointRouteBuilder endpoints);
}

public sealed class CloudstrapProxyOptions            // section Cloudstrap:Proxy
{
    public string ConfigurationSection { get; set; } = "ReverseProxy";
    public ProxyTokenMode DefaultTokenMode { get; set; } = ProxyTokenMode.Client;   // OQ-6
    public bool StripInboundCredentials { get; set; } = true;                        // OQ-5
}

public enum ProxyTokenMode { None, Client, User /* OQ-6 (b) only */ }

public static class ProxyMetadataKeys                  // cluster/route Metadata keys, public constants
{
    public const string TokenMode = "Cloudstrap.TokenMode";                  // per cluster
    public const string ForwardInboundCredentials = "Cloudstrap.ForwardInboundCredentials"; // per route, unsafe opt-out
    public const string AllowAnonymousWithToken = "Cloudstrap.AllowAnonymousWithToken";   // per route, AC-PX5 override
}
```

Per-cluster token request parameters (scope/resource) reuse the shipped `Cloudstrap.Core.TokenRequestOptions` shape —
bound from cluster metadata or a `Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters` subsection; the
planner picks the mechanism (both are additive).

---

## Behaviors & Conventions

| Concern | Behavior | Override |
|---|---|---|
| Configuration | YARP's own schema, section `ReverseProxy` | `CloudstrapProxyOptions.ConfigurationSection`; or code-based `LoadFromMemory` on the returned builder |
| Front-door authz | Mapped proxy routes require an authenticated user unless the route says `Anonymous` | Route `AuthorizationPolicy` (YARP's own knob) |
| Confused-deputy guard | `Anonymous` route + `Client`/`User` cluster → startup failure | `Cloudstrap.AllowAnonymousWithToken: true` route metadata |
| Inbound credentials | `Cookie` + `Authorization` removed before forwarding | `StripInboundCredentials` globally / `Cloudstrap.ForwardInboundCredentials` per route |
| Outbound token | Per cluster from the matching seam; acquisition, caching, renewal are #9/#10's | `Cloudstrap.TokenMode` cluster metadata; `DefaultTokenMode` |
| Correlation | Cloudstrap correlation handler wrapped into the forwarder chain | — (always on, idempotent like #4's typed clients) |
| Forwarded headers | YARP defaults (`X-Forwarded-*` incl. `Prefix`) | YARP transforms |
| Resilience / timeouts | None added | YARP `ActivityTimeout`, consumer's own handlers |
| Telemetry | YARP's activities on the host's existing OTel pipeline; no exporter | #2 owner/contribute modes |

---

## Dependencies

| Package | License | Status | Why |
|---|---|---|---|
| `Yarp.ReverseProxy` | MIT (.NET Foundation) | **New** CPM pin — latest stable supporting `net10.0` *(verify version + transitive closure at plan time; ⚠️ dependency-review gate)* | The forwarder. |
| `Cloudstrap.Extensions` (→ Core, Observability) | MIT | Shipped (#4) | Token seams, correlation handler, options patterns. |
| `Duende.AccessTokenManagement*` | Apache-2.0 | **Not referenced** by `Cloudstrap.Proxy` | Reached only through the seam implementations the consumer installs (#9/#10). |
| `Microsoft.Extensions.ServiceDiscovery.Yarp` | MIT | **Not referenced** by the shipped package | Aspire composability is a consumer choice (AC-PX12, OQ-8). |
| `Duende.BFF` | Commercial | **Rejected** | Not OSI (CLAUDE.md rule 4). |

---

## Deliberate Behavior Changes (vs the source, provisional)

1. Routes are **authenticated by default**; anonymous forwarding with the app's token needs two explicit opt-ins.
2. Inbound `Cookie` / `Authorization` are **stripped by default**.
3. A missing token provider **fails loudly** (startup or first use), never forwards unauthenticated.
4. #9's provider warnings name the right configuration path when the caller is a proxy cluster (internal change in
   `Cloudstrap.Authentication.ClientCredentials`, no API change; planner decides whether it is in scope — finding 6).
5. No user-identity header injection (provisional, OQ-6 (d)).

---

## Edge Cases

| Case | Expected behavior |
|---|---|
| Token endpoint unreachable / token request fails | Forward is not attempted; front door answers 502 (or the seam handler's failure surfaced as YARP's forwarder error) and logs without the secret. No unauthenticated retry. |
| Downstream answers 401 with the app's token | Passed through to the caller as-is; no automatic token renewal loop beyond what #9's handler already does. |
| WebSocket / SSE upgrade through a `Client` route | Token attached on the upgrade request; documented; covered by a unit test if cheap, otherwise README note. |
| Large/streamed bodies | YARP streaming unchanged; Cloudstrap adds no buffering transform. |
| Config hot-reload adds a cluster | Handler wrapping applies to the new cluster (YARP rebuilds the client per cluster change). |
| `User` mode outside a user context (e.g. anonymous route) | Same as AC-PX5: rejected at startup. |
| Two `AddCloudstrapProxy` calls | Idempotent registration or a clear exception — planner picks, tested. |

---

## Out of Scope (the planner must not resurrect any of it)

- The Hangfire dashboard proxy (`Cloudstrap.Hangfire.Proxy`) — #18.
- Token **exchange** (RFC 8693 on-behalf-of) — not in the seams today; a later additive token mode if demanded.
- A Cloudstrap-owned route/cluster configuration schema.
- Resilience policies, rate limiting, caching, request/response body transforms.
- Session/BFF features (anti-forgery, login/logout endpoints, server-side sessions) — #10/#13 already own the BFF.
- Any `Aspire.*` or `Microsoft.Extensions.ServiceDiscovery.*` reference in the shipped package.
- Multiple application identities per host (inherited deferral from #9).

---

## Decision Log

*(empty — filled from the owner's answers at the spec approval gate)*

---

## Open Questions

### OQ-1: Close the source-reading gap before approval?
**What I found.** This draft did not open the reference repo (sandbox limit). The verdict table is built from #9/#16
secondary evidence and marks seven *(to verify)* items.
**Options.** (a) A technical-analyst pass with source access verifies every *(to verify)* row and amends findings
before the owner answers OQ-2…OQ-8; (b) approve on secondary evidence and let the planner read the source.
**Recommendation: (a).** This is an auth-risk deliverable; the planner must not discover source behavior (e.g. an
identity header the Hangfire proxy relies on) after the design is fixed.

### OQ-2: Configuration model — YARP native or a Cloudstrap schema?
**Options.** (a) YARP's `ReverseProxy` section + cluster `Metadata` + a tiny `Cloudstrap:Proxy` options class;
(b) a `Cloudstrap:Proxy:Routes/Clusters` schema translated to YARP.
**Recommendation: (a)** — finding 3: zero re-modelling, YARP docs apply verbatim, every YARP feature stays reachable.

### OQ-3: Entry-point shape
**Options.** (a) `AddCloudstrapProxy(IHostApplicationBuilder)` → `IReverseProxyBuilder` + `MapCloudstrapProxy()`;
(b) fold mapping into #5/#6's composite `UseCloudstrap…` pipelines; (c) `IServiceCollection` receiver.
**Recommendation: (a)** — mirrors `MapCloudstrapHealthChecks`; works on any web host type; the proxy needs
`UseAuthentication`/`UseAuthorization` before it, which endpoint routing guarantees.

### OQ-4: Front-door authorization default
**Options.** (a) Authenticated by default + startup confused-deputy guard (AC-PX2/AC-PX5); (b) authenticated by
default, no guard; (c) YARP default (fallback policy decides).
**Recommendation: (a).** Forwarding an anonymous caller with the application's token is the one failure that turns a
proxy into a privilege escalation; the guard costs one startup check.

### OQ-5: Inbound credential stripping
**Options.** (a) Strip `Cookie` + `Authorization` by default, per-route opt-out; (b) strip only `Authorization`;
(c) YARP default (forward all).
**Recommendation: (a)** — the user's session cookie is a bearer credential for the front door; it has no business
downstream.

### OQ-6: Token modes in v1
**Options.** (a) `Client` (default) + `None`; (b) (a) + `User` via `IUserAccessTokenHandlerProvider`;
(c) `Client` only; (d) any of these + an opt-in user-identity header.
**Recommendation: (b), default `Client`, no (d).** `User` costs one switch branch because #10's seam already ships, and
it serves the delegated-call case (the demo's `UserApi`) without a second proxy library. Identity headers stay out
until a consumer demonstrates the need and a signed format is designed.

### OQ-7: Demo vehicle and headline E2E
**Options.** (a) BlazorWasm **Bff** (5300) → Api (5330), `Client` mode (AC-PX14); (b) BlazorServer (5340) → Api;
(c) a new dedicated proxy demo host.
**Recommendation: (a)** — the Bff already has the machine client, the OIDC cookie front door and the E2E fixture that
boots the Api; "BFF/browser features → BlazorWasm app" is CLAUDE.md rule 9's own row. No new host, no new port.

### OQ-8: Aspire service-discovery proof depth
**Options.** (a) README guidance + a test in `Cloudstrap.Proxy.Tests` that chains a custom `IDestinationResolver`
(YARP abstraction — no Aspire package); (b) (a) + a test-only reference to `Microsoft.Extensions.ServiceDiscovery.Yarp`;
(c) README only.
**Recommendation: (a)** — proves the composition seam (destination resolution is untouched by Cloudstrap) with zero
new packages, keeps AC-ASP2 trivially green; the real Aspire sample remains the founding spec's future AppHost sample.
