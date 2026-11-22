# Spec: Trusted-Subsystem Proxy — `Cloudstrap.Proxy` (Roadmap Deliverable #17)

> Status: **APPROVED 2026-11-22, by the owner.** OQ-1 closed by the source check; OQ-2…OQ-8 answered with the
> recommended options (Decision Log D-1…D-8). Differences from the source are recorded as deliberate changes
> D-9…D-15. Plan: `_plans/17-TrustedSubsystemProxy.md`.
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
> ✅ **Source check done (OQ-1 closed, 2026-11-22).** The first draft was written without opening the reference repo.
> Job `01M81H8EC6WMTRD6Y5QNQ2V4NK` then read `Nihdi.Core.Configuration.Proxy\` and `…Hangfire.Proxy\` and their test
> projects. Job `01M81HKXBHD6SPN4R07MXJ9S33` checked every *(to verify)* row of the Port Decision Table against
> those facts. Each row now states the checked fact and its source file and line. The facts in short:
> - **Forwarding**: YARP **direct forwarding** (`Yarp.ReverseProxy` 2.3.0, `Proxy.csproj:11`). There is no
>   route/cluster model. The destination is the `BaseAddress` of an entry in the HTTP client registry
>   (`ServiceCollectionExtensions.cs:41,78`). There is no service discovery.
> - **Authorization**: the forwarder applies none. The mapping returns a builder, and the caller adds authorization
>   (`WebApplicationExtensions.cs:31,50`).
> - **Headers** (`ForwardedPrefixHttpTransformer`):
>   - `Host` is cleared (`:38`).
>   - `Cookie` is removed **only when not running in AKS** (`:46-49`).
>   - A client-sent `X-Forwarded-Prefix` is removed, then set from `PathBase` (`:53-58`).
>   - The inbound `Authorization` header is **not touched**.
> - **Token**: the client-credentials handler is added only when `AddClientAccessToken` is set
>   (`ServiceCollectionExtensions.cs:72-75`, default `false`). Without it, no token is added.
> - **No user-identity header** of any kind.
> - **TLS**: a custom certificate validator. The chain must be trusted, and the host name is re-checked against the
>   BFF host (`TrustedSubsystemCertificateValidator.cs:30-42`).
> - **`Hangfire.Proxy`**: maps `/hangfire` with a role policy. An empty role means any signed-in user.
> - **Tests**:
>   - `Proxy.Tests`: registration and guards, 8 transformer tests, 6 certificate tests.
>   - `Hangfire.Proxy.Tests`: policy registered, role denied, empty role allowed, null key.
>
> Where the source differs from this spec's design, the spec's design stands. Each difference is recorded as a
> deliberate change (D-9…D-15).
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
> AC-PX1…AC-PX14 are new (prefix `AC-PX`). Criteria marked *(OQ-n)* were drafted on the recommended answer. The owner
> chose every recommended answer (D-2…D-8), so they stand as written.

| # | Given | When | Then |
|---|-------|------|------|
| AC-PX1 | A host referencing `Cloudstrap.Proxy` with routes/clusters in YARP's standard `ReverseProxy` configuration section | It calls `AddCloudstrapProxy()` and maps the proxy (`MapCloudstrapProxy()`) | Requests matching a route are forwarded to the cluster's destination; the registration returns YARP's own `IReverseProxyBuilder` so every YARP extension (transforms, service discovery, custom providers) remains reachable. *(OQ-2, OQ-3)* |
| AC-PX2 | A proxied route with **no** `AuthorizationPolicy` | An unauthenticated request arrives | It is **challenged/rejected by the front door** (401 for a bearer front door, the configured challenge for a cookie front door) and **nothing is forwarded**. Only a route explicitly configured `AuthorizationPolicy: Anonymous` forwards anonymous callers — and such a route is refused at startup if its cluster attaches the application's token (AC-PX5). *(OQ-4)* |
| AC-PX3 | Any proxied request carrying `Cookie` and/or `Authorization` from the caller | It is forwarded | Neither inbound header reaches the destination (asserted on the downstream side). A per-route opt-out exists (explicit metadata) and is documented as unsafe for the trusted-subsystem pattern. *(OQ-5)* |
| AC-PX4 | A cluster whose token mode is `Client` (default, OQ-6) | A request is forwarded | It carries `Authorization: Bearer <application token>` obtained through `IClientAccessTokenHandlerProvider` (cached and renewed by #9 — the proxy acquires nothing itself). If no provider is registered, **startup (or first use of that cluster) fails** with a message naming the cluster, the metadata key and `Cloudstrap.Authentication.ClientCredentials` — no request is ever sent without the token. |
| AC-PX5 | A cluster in `Client` mode reachable from a route with `AuthorizationPolicy: Anonymous` | The host starts | Startup fails with a message naming the route and cluster (confused-deputy guard). An explicit, documented override exists for a deliberately public route. *(OQ-4)* |
| AC-PX6 | A cluster with token mode `User` (D-6) / `None` | A request is forwarded | `User`: the signed-in user's access token via `IUserAccessTokenHandlerProvider` (#10), with the same fail-loud rule naming `Cloudstrap.Authentication.OpenIdConnect`. `None`: no `Authorization` header (plain reverse proxy; still subject to AC-PX2/AC-PX3). An unknown mode value fails startup listing the allowed values. |
| AC-PX7 | A request with a Cloudstrap correlation id | It is forwarded | The destination receives the same correlation header as a Cloudstrap typed client would send (#2 contract), and W3C `traceparent` continues the front door's trace. |
| AC-PX8 | A path-prefixed route (e.g. `/downstream/{**catch-all}` → `/`) | It is forwarded | The destination receives `X-Forwarded-Prefix`, `X-Forwarded-Proto`, `X-Forwarded-Host` and `X-Forwarded-For` (YARP defaults, kept — the #18 input). A caller-supplied `X-Forwarded-Prefix` is **replaced**, never passed through, and the destination sees its own host in `Host`. This is the source transformer's contract (`:38`, `:53-58`), now met by YARP defaults. Cloudstrap adds no bespoke forwarding header. |
| AC-PX9 | A consumer that already configured `ConfigureHttpClientDefaults` resilience, or an Aspire ServiceDefaults host | `AddCloudstrapProxy()` runs | Cloudstrap adds **no** resilience, retry or timeout handler to the forwarder (AC-ASP3 posture — YARP's own `ActivityTimeout` and the consumer's choices stand) and registers no second OTel pipeline/exporter (AC-ASP1 contribute posture). |
| AC-PX10 | The package's own options (`Cloudstrap:Proxy`) | They are invalid (e.g. a configured section name that does not exist, an unknown token mode) | `ValidateOnStart` fails with an actionable message naming the key — the #1 source-generated-validator pattern. |
| AC-PX11 | The `Cloudstrap.Proxy` package | It is packed and inspected | XML docs on every public member; sealed public classes; guard clauses; MIT `PackageLicenseExpression`, icon, README (incl. "trusted subsystem" explanation, the cookie/authorization stripping rule, and the "one owner" note for service discovery); `Yarp.ReverseProxy` pinned in CPM with its license recorded. |
| AC-PX12 | A host that resolves destinations through YARP's destination-resolver hook (`IDestinationResolver`, the hook `Microsoft.Extensions.ServiceDiscovery.Yarp` plugs into) | A custom resolver is registered on the builder `AddCloudstrapProxy()` returns | Forwarding reaches the resolved address with the Cloudstrap behaviors intact (token, stripping, authorization). Proven in `Cloudstrap.Proxy.Tests` with a test-local resolver: **no** `Aspire.*` or `Microsoft.Extensions.ServiceDiscovery.*` reference anywhere, test projects included (D-8). The README shows the Aspire one-liner as guidance. |
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-A3 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Nihdi.AspNetCore.*` packages. *(carried verbatim)* |
| AC-PX13 | The files this deliverable adds | A case-insensitive search for `Nihdi`, `NIHDI`, `Riziv`, `Wfe`, `Cfe` | Zero hits. Fixtures are neutral (`contoso`, `example.com`, `demo-bff`). |
| AC-PX14 | CLAUDE.md workflow rule 9 | The deliverable closes | **BlazorWasm Bff** (5300) proxies a route to the **Api** (5330) in `Client` mode, and ≥ 1 new E2E test in `Cloudstrap.Demo.E2E.Tests` proves through the running apps: (a) signed-in browser → proxied `whoami` returns the **machine** identity (`demo-bff`), not the user; (b) anonymous call → rejected at the Bff, Api never hit; (c) the Bff session cookie is absent downstream. *(OQ-7)* |

---

## Port Decision Table (source-checked 2026-11-22)

> Line references are to `Nihdi.Core.Configuration.Proxy\` unless the file name says otherwise. They are the facts
> job `01M81H8EC6WMTRD6Y5QNQ2V4NK` read. "Not established" means the check did not record that detail. It is then
> stated as such, never guessed.

| Source | Verdict | Target | Justification |
|---|---|---|---|
| `ServiceCollectionExtensions.AddNihdiTrustedSubsystemProxy`. **Checked**: it registers YARP **direct forwarding** (`IHttpForwarder`). The destination is the `BaseAddress` of an entry in the Cloudstrap-style HTTP client registry (`ServiceCollectionExtensions.cs:41,78`). The exact receiver and parameter list were not established; the redesign does not depend on them. | **Redesign** | `AddCloudstrapProxy(this IHostApplicationBuilder, Action<CloudstrapProxyOptions>?)` → `IReverseProxyBuilder`, plus `MapCloudstrapProxy(this IEndpointRouteBuilder)` → `ReverseProxyConventionBuilder` | The one-call registration earns its place. Returning YARP's builder keeps every YARP extension reachable ("every convention has an override"). D-3 (OQ-3). |
| ↳ client-credentials handler. **Checked**: added **only when `AddClientAccessToken` is set** (`ServiceCollectionExtensions.cs:72-75`, default `false`). Without the flag the request is forwarded **with no token**. | **Replace** (already decided by #9: Routed → #17) + **deliberate change D-12** | `IClientAccessTokenHandlerProvider` wrapped around YARP's forwarder handler per cluster. Default mode `Client`; `None` must be explicit; a missing provider fails loudly. | Finding 1. The token mechanism is #9's (Duende ATM behind the seam); the proxy consumes the seam. AC-A3. The source's silent no-token default is not kept (D-12). |
| ↳ route/cluster configuration. **Checked**: there is **no route/cluster model**. Direct forwarding to one registry `BaseAddress`, and no service discovery (`ServiceCollectionExtensions.cs:41,78`). | **Replace** + **deliberate change D-9** | YARP's native `ReverseProxy` section (section name overridable) + cluster `Metadata` for the Cloudstrap token mode | Finding 3. Nothing to mirror: the source had no settings tree. The destination moves from `Cloudstrap:HttpClients:{name}:BaseAddress` to YARP cluster destinations, which also opens YARP's destination-resolver hook (D-8). D-2 (OQ-2). |
| ↳ front-door authorization. **Checked**: the forwarder applies **no authorization**. The mapping returns a builder for the caller to secure (`WebApplicationExtensions.cs:31,50`). | **Redesign** + **deliberate change D-10** | Authenticated-by-default convention on mapped proxy routes + startup confused-deputy guard | Finding 4. A caller who forgets `RequireAuthorization` in the source forwards anonymous traffic with the app's token when `AddClientAccessToken` is on. D-4 (OQ-4). |
| ↳ inbound credential handling. **Checked** (`ForwardedPrefixHttpTransformer`): `Cookie` is removed **only when not running in AKS** (`:46-49`). The inbound `Authorization` header is **not touched** by the transformer. | **Redesign** + **deliberate change D-11** | Default request transform removing both `Cookie` and `Authorization`, always, with a per-route opt-out | Finding 4. The credential rule must not depend on the hosting platform. D-5 (OQ-5). |
| ↳ `Host` and `X-Forwarded-Prefix`. **Checked** (`ForwardedPrefixHttpTransformer`): `Host` is cleared (`:38`), so the destination's host is used. A client-sent `X-Forwarded-Prefix` is removed, then set from the request `PathBase` (`:53-58`). | **Keep the behavior, drop the class** | YARP defaults: the original `Host` is not copied, and the `X-Forwarded-*` transforms *set* (replace) inbound values, `Prefix` from `PathBase` *(verify the YARP defaults at plan time)*. No Cloudstrap transformer class. | Same observable contract, no bespoke code. AC-PX8 now asserts that a spoofed `X-Forwarded-Prefix` is replaced. This is the #18 input. |
| ↳ user-identity propagation header. **Checked**: the source sends **none**. | **Drop** (nothing to drop) | — | Confirms D-6: no identity header in v1. No #18 behavior depends on one. |
| ↳ environment sniffing. **Checked**: the AKS test that decides `Cookie` removal (`ForwardedPrefixHttpTransformer.cs:46-49`) is the only environment check found. The check did not record an `EnableAuthentication`-style gate or `ILogger`/settings parameters. | **Drop** | — | #5 D-2 and #9 precedents: no environment taxonomy. Covered by D-11. |
| `TrustedSubsystemCertificateValidator`. **Checked**: a custom TLS server-certificate check. The chain must be trusted, **and** the certificate host name is re-checked against the BFF (front-door) host (`TrustedSubsystemCertificateValidator.cs:30-42`). 6 source tests. | **Drop** (out of scope) + **deliberate change D-13** | Platform default validation of YARP's `SocketsHttpHandler`: trusted chain + **destination** host name. No custom callback. | The default already enforces a trusted chain and a matching destination name. The extra rule binds the downstream certificate to the *front door's* name, a deployment-specific convention (shared certificate). A custom validator is a security-sensitive one-way door. Consumers who need it keep YARP's own seam (`IForwarderHttpClientFactory` / cluster `HttpClient` config). **Recommendation: out of scope for v1.** It returns only on demonstrated need, as an opt-in. |
| `Nihdi.Core.Configuration.Proxy.csproj`. **Checked**: `Yarp.ReverseProxy` **2.3.0** (`Proxy.csproj:11`). | **Redesign** | `Cloudstrap.Proxy.csproj` → `Cloudstrap.Extensions` (seams, correlation via #2's transitive closure) + `Yarp.ReverseProxy` (new CPM pin, latest stable `net10.0`-compatible, **≥ 2.3.0**) | Roadmap dependency row (depends on #4). **No** reference to either auth package (finding 1). 2.3.0 is the floor because the destination-resolver hook (D-8) and the forwarder APIs used here are present at that version. |
| `InternalsVisibleTo` consumers (`Hangfire.Proxy`). **Not established** by the check. | **Drop** | Public seam only | #16 DD-1 precedent: #18 builds on public API (route config, token mode, forwarded headers). The verdict holds either way. |
| `Nihdi.Core.Configuration.Hangfire.Proxy`. **Checked**: maps `/hangfire` with a role policy; an **empty role means any signed-in user**. `Hangfire.Proxy.Tests`: policy registered, role denied, empty role allowed, null key. | **Route → #18** (out of scope here) + **D-15** | `Cloudstrap.Hangfire.Proxy` (#18) on top of `MapCloudstrapProxy` | #17 supplies what #18 consumes: a path-prefixed route, `X-Forwarded-Prefix`, `Client` token mode, authenticated-by-default (= the "empty role" case). The role policy is a route `AuthorizationPolicy`, YARP's own knob. Nothing role-specific ships in #17. |
| README / `Riziv-Inami` headers / `Wfe`/`Cfe` vocabulary | **Drop** | New MIT README; neutral "front-door host" / "downstream API" vocabulary | De-NIHDI checklist + terminology memory (Cfe → Bff; industry terms). |
| Source tests. **Checked**: `Proxy.Tests` exists: registration and guards, 8 transformer tests, 6 certificate tests. | **Read as observed contract** | `Cloudstrap.Proxy.Tests` (TestHost-based: front door + in-process downstream echo server) | Registration/guard tests → carried as AC-PX1/AC-PX10 tests. Transformer tests → carried as the `Host`/`X-Forwarded-Prefix` contract (AC-PX8), changed by D-11 for `Cookie`/`Authorization`. Certificate tests → not carried (D-13). |

**Tally**: 0 Port · 4 Redesign · 2 Replace · 1 Keep-behavior · 5 Drop · 1 Route → #18 · 1 Read-as-contract
(14 rows). The draft had **nine** *(to verify)* rows. Eight are settled by the source facts. One
(`InternalsVisibleTo`) was not established, and its verdict does not depend on it. Three rows were added: the
certificate validator, the Hangfire proxy, and `Host`/`X-Forwarded-Prefix` (split out of the credential row).

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

public enum ProxyTokenMode { None, Client, User }      // D-6

public static class ProxyMetadataKeys                  // cluster/route Metadata keys, public constants
{
    public const string TokenMode = "Cloudstrap.TokenMode";                  // per cluster
    public const string ForwardInboundCredentials = "Cloudstrap.ForwardInboundCredentials"; // per route, unsafe opt-out
    public const string AllowAnonymousWithToken = "Cloudstrap.AllowAnonymousWithToken";   // per route, AC-PX5 override
}
```

Per-cluster token request parameters (scope/resource) reuse the shipped `Cloudstrap.Core.TokenRequestOptions` shape —
bound from cluster metadata or a `Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters` subsection. **Plan
decision**: the `Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters` subsection. It is bound to the shipped
type, which a flat string-to-string metadata map cannot express (`_plans/17-TrustedSubsystemProxy.md`, mechanic (d)).

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

## Deliberate Behavior Changes (vs the source, source-checked)

Each item is a Decision Log entry. The source fact and its reference are in the Port Decision Table.

1. **D-9: Routing model.** The source forwards directly to one HTTP-client-registry `BaseAddress`, with no routes or
   clusters. Cloudstrap uses YARP routes/clusters from the `ReverseProxy` section.
2. **D-10: Front-door authorization.** The source applies none and leaves it to the caller. Cloudstrap routes are
   **authenticated by default**. Anonymous forwarding with the app's token needs two explicit opt-ins.
3. **D-11: Inbound credentials.** The source removes `Cookie` only outside AKS and never removes `Authorization`.
   Cloudstrap removes **both, always**, with no platform check and a per-route opt-out.
4. **D-12: Token default.** The source adds a token only on `AddClientAccessToken = true`; otherwise it forwards with
   none. Cloudstrap defaults to `Client`. A missing provider **fails loudly** (startup or first use) and never
   forwards unauthenticated. `None` must be chosen explicitly.
5. **D-13: TLS.** The source's custom certificate validator (trusted chain + host name re-checked against the BFF
   host) is **not carried**. Platform default validation applies. Out of scope for v1, recommended.
6. **D-14: `Host` / `X-Forwarded-Prefix`.** Not a behavior change. The source transformer's contract (clear `Host`;
   replace a caller's `X-Forwarded-Prefix` with `PathBase`) is kept through YARP defaults. The bespoke transformer
   class is dropped.
7. **D-15: Hangfire proxy.** Stays #18. The role-policy route and "empty role = any signed-in user" are #18's to
   rebuild on `MapCloudstrapProxy` plus a route `AuthorizationPolicy`.
8. No user-identity header (D-6). The source sends none either, so this is confirmation, not change.
9. #9's provider warnings name `Cloudstrap:HttpClients:{name}` even when the caller is a proxy cluster (finding 6).
   **Planner decision: not in the #17 plan.** No shipped package changes in #17. It is recorded as a follow-up.

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
- A custom TLS server-certificate validator (the source's `TrustedSubsystemCertificateValidator`; D-13).
- Environment/platform detection of any kind (the source's AKS check; D-11).

---

## Decision Log

Owner's answers recorded 2026-11-22. Every recommended option was accepted.

| # | Question | Decision | By / date |
|---|---|---|---|
| D-1 | OQ-1 source gap | **(a), done.** Source read by job `01M81H8EC6WMTRD6Y5QNQ2V4NK`. Every *(to verify)* row was checked by job `01M81HKXBHD6SPN4R07MXJ9S33`, and the Port Decision Table updated. | Owner, 2026-11-22 |
| D-2 | OQ-2 configuration model | **(a)** YARP's native `ReverseProxy` section + cluster `Metadata` + a small `Cloudstrap:Proxy` options class. No Cloudstrap route/cluster schema. | Owner, 2026-11-22 |
| D-3 | OQ-3 entry-point shape | **(a)** `AddCloudstrapProxy(IHostApplicationBuilder)` → `IReverseProxyBuilder` + `MapCloudstrapProxy(IEndpointRouteBuilder)` → `ReverseProxyConventionBuilder`. | Owner, 2026-11-22 |
| D-4 | OQ-4 front-door authorization | **(a)** Signed-in user required by default + startup confused-deputy guard (AC-PX2, AC-PX5). | Owner, 2026-11-22 |
| D-5 | OQ-5 inbound credentials | **(a)** Remove the caller's `Cookie` and `Authorization` by default; per-route opt-out (`Cloudstrap.ForwardInboundCredentials`). | Owner, 2026-11-22 |
| D-6 | OQ-6 token modes | **(b)** `Client` (default) + `User` + `None`. **No** user-identity header (option (d) rejected). | Owner, 2026-11-22 |
| D-7 | OQ-7 demo vehicle | **(a)** BlazorWasm Bff (5300) → Api (5330), `Client` mode, E2E per AC-PX14. | Owner, 2026-11-22 |
| D-8 | OQ-8 Aspire proof depth | **(a)** Test through YARP's destination-resolver hook (`IDestinationResolver`) with a test-local resolver. No Aspire or ServiceDiscovery reference anywhere. | Owner, 2026-11-22 |
| D-9 | Source difference: routing model | YARP routes/clusters replace direct forwarding to a registry `BaseAddress`. | Analyst (follows D-2) |
| D-10 | Source difference: authorization | Authenticated by default replaces "no authorization, caller secures the builder". | Analyst (follows D-4) |
| D-11 | Source difference: credential stripping | Always remove `Cookie` + `Authorization`. Replaces "remove `Cookie` outside AKS only, keep `Authorization`". No platform detection. | Analyst (follows D-5) |
| D-12 | Source difference: token default | `Client` by default with fail-loud. Replaces "opt-in flag, default off, otherwise no token". | Analyst (follows D-6) |
| D-13 | Source difference: TLS validator | Not carried; platform default validation; **out of scope for v1** (recommended). | Analyst. ⚠️ **Not part of the owner's OQ answers.** Owner to confirm at plan approval. |
| D-14 | Source contract kept: `Host` / `X-Forwarded-Prefix` | Kept through YARP defaults; asserted in AC-PX8. | Analyst |
| D-15 | Source `Hangfire.Proxy` | Out of scope, stays #18. Facts recorded for #18 (role policy; empty role = any signed-in user). | Analyst (roadmap order) |

---

## Open Questions

> **All answered 2026-11-22** (Decision Log D-1…D-8). The questions are kept below as the record of what was asked.

### OQ-1: Close the source-reading gap before approval? — answered: (a), done (D-1)
**What I found.** The first draft did not open the reference repo (sandbox limit). The verdict table was built from
#9/#16 secondary evidence and marked **nine** *(to verify)* rows. The source check settled eight of them; one
(`InternalsVisibleTo`) was not established, and its verdict does not depend on it.
**Options.** (a) A technical-analyst pass with source access verifies every *(to verify)* row and amends findings
before the owner answers OQ-2…OQ-8; (b) approve on secondary evidence and let the planner read the source.
**Recommendation: (a).** This is an auth-risk deliverable; the planner must not discover source behavior (e.g. an
identity header the Hangfire proxy relies on) after the design is fixed.

### OQ-2: Configuration model — YARP native or a Cloudstrap schema? — answered: (a) (D-2)
**Options.** (a) YARP's `ReverseProxy` section + cluster `Metadata` + a tiny `Cloudstrap:Proxy` options class;
(b) a `Cloudstrap:Proxy:Routes/Clusters` schema translated to YARP.
**Recommendation: (a)** — finding 3: zero re-modelling, YARP docs apply verbatim, every YARP feature stays reachable.

### OQ-3: Entry-point shape — answered: (a) (D-3)
**Options.** (a) `AddCloudstrapProxy(IHostApplicationBuilder)` → `IReverseProxyBuilder` + `MapCloudstrapProxy()`;
(b) fold mapping into #5/#6's composite `UseCloudstrap…` pipelines; (c) `IServiceCollection` receiver.
**Recommendation: (a)** — mirrors `MapCloudstrapHealthChecks`; works on any web host type; the proxy needs
`UseAuthentication`/`UseAuthorization` before it, which endpoint routing guarantees.

### OQ-4: Front-door authorization default — answered: (a) (D-4)
**Options.** (a) Authenticated by default + startup confused-deputy guard (AC-PX2/AC-PX5); (b) authenticated by
default, no guard; (c) YARP default (fallback policy decides).
**Recommendation: (a).** Forwarding an anonymous caller with the application's token is the one failure that turns a
proxy into a privilege escalation; the guard costs one startup check.

### OQ-5: Inbound credential stripping — answered: (a) (D-5)
**Options.** (a) Strip `Cookie` + `Authorization` by default, per-route opt-out; (b) strip only `Authorization`;
(c) YARP default (forward all).
**Recommendation: (a)** — the user's session cookie is a bearer credential for the front door; it has no business
downstream.

### OQ-6: Token modes in v1 — answered: (b), default `Client`, no (d) (D-6)
**Options.** (a) `Client` (default) + `None`; (b) (a) + `User` via `IUserAccessTokenHandlerProvider`;
(c) `Client` only; (d) any of these + an opt-in user-identity header.
**Recommendation: (b), default `Client`, no (d).** `User` costs one switch branch because #10's seam already ships, and
it serves the delegated-call case (the demo's `UserApi`) without a second proxy library. Identity headers stay out
until a consumer demonstrates the need and a signed format is designed.

### OQ-7: Demo vehicle and headline E2E — answered: (a) (D-7)
**Options.** (a) BlazorWasm **Bff** (5300) → Api (5330), `Client` mode (AC-PX14); (b) BlazorServer (5340) → Api;
(c) a new dedicated proxy demo host.
**Recommendation: (a)** — the Bff already has the machine client, the OIDC cookie front door and the E2E fixture that
boots the Api; "BFF/browser features → BlazorWasm app" is CLAUDE.md rule 9's own row. No new host, no new port.

### OQ-8: Aspire service-discovery proof depth — answered: (a) (D-8)
**Options.** (a) README guidance + a test in `Cloudstrap.Proxy.Tests` that chains a custom `IDestinationResolver`
(YARP abstraction — no Aspire package); (b) (a) + a test-only reference to `Microsoft.Extensions.ServiceDiscovery.Yarp`;
(c) README only.
**Recommendation: (a)** — proves the composition seam (destination resolution is untouched by Cloudstrap) with zero
new packages, keeps AC-ASP2 trivially green; the real Aspire sample remains the founding spec's future AppHost sample.
