# Spec: Blazor Server Helpers — `Cloudstrap.BlazorServer` (Roadmap Deliverable #12)

> Status: **APPROVED — zero Open Questions (all three resolved by the user 2026-08-22, see Decision
> Log D-9/D-12/D-13); planner-ready.** Source: `Nihdi.Core.Configuration.BlazorServer`
> (13 source files). Depends on shipped #4 (`Cloudstrap.Extensions` typed clients + health probes),
> #9/#10 (Duende ATM token seams), #11 (`Cloudstrap.BlazorCommon`), #2 (`Cloudstrap.Observability`
> correlation + `BlazorHubSampler`). Demo vehicle: the **existing** `src/demo/BlazorServer/
> Cloudstrap.Demo.BlazorServer` app (port 5340) — #12 creates no SUT (roadmap §12 scope note).
> ⚠️ Risk areas: all-new public API surface; pipeline touches the auth middleware placement
> (same conditional mechanic as `Cloudstrap.Mvc`); zero new external dependencies proposed.

---

## Code-reading findings that shaped this spec

1. **The composite is the package's real value; most of its constituents already shipped elsewhere.**
   `AddBlazorForNihdi` / `UseBlazorForNihdi<T>` (`Extensions\WebApplicationBuilderExtensions.cs`,
   `Extensions\WebApplicationExtensions.cs`) compose: common services, KeyVault data protection, HSTS/
   security headers, W3C tracing middleware, localization, probe health checks, antiforgery, path base,
   forwarded headers, exception handling, Razor components + render modes, controllers, Scalar gating.
   Cloudstrap equivalents for almost every constituent are already shipped (#1/#2/#4/#5/#6/#10); what
   does **not** exist yet is the Blazor-flavored composite with correct middleware ordering — exactly
   the gap the demo app's hand-rolled `Program.cs` shows today (manual `UseAuthentication`/
   `UseAntiforgery`/`MapRazorComponents`, **no correlation middleware, no health probes, no security
   headers**).
2. **The entire `DistributedTracing` folder has zero production consumers in the source repo.**
   `IDistributedTraceService` / `UseDistributedTrace<T1..T5>` are injected nowhere outside the package's
   own tests (repo-wide grep). `ActivitySourceDelegatingHandler` is attached only by the BlazorServer
   `HttpClient` wrapper — and both production call sites that register HTTP clients from a Blazor Server
   host (TestProject WFE `BffClientConfigurator.cs`, `Dashboard.Components\IServiceCollectionExtensions.cs:95`)
   use the **Common** variant instead, so the handler never runs in production. Design defects on top:
   a static `ActivityListener` forcing `AllData` sampling for its source process-wide (bypasses the
   configured sampler, never removed), root-provider `CreateScope()` + `GetRequiredService` overloads
   for up to five services (service-locator), `IDisposable` with a no-op `Dispose`, and a legacy
   `new Activity(name).Start()` fallback.
3. **The underlying problem the folder aimed at is real, though.** In a Blazor Server circuit,
   interaction work runs under the long-lived SignalR component-hub activity, which Cloudstrap's
   `BlazorHubSampler` deliberately drops (AC-O3). Circuit-originated outbound calls therefore export as
   children of an unrecorded trace, and — because `CorrelationContextAccessor` is only populated by the
   inbound HTTP middleware — the correlation header is absent or stale on those calls. The source's fix
   was unused and over-built, but the gap is genuine → resolved as the slim redesign, **D-9**.
4. **`NihdiControls` is the twin of #11's dropped `NihdiWasmControls`** — a static mutable assembly
   registry (`GetAssemblies` throws before `SetAssembliesOptions`), which additionally loads DLLs **by
   file name from `AppContext.BaseDirectory`** and hard-codes the enterprise Dashboard component DLL
   names as non-removable defaults (`NihdiControlsAssemblyOptions._defaultControlLibraries`). Same
   verdict as #11 D-5: the framework-native `AdditionalAssemblies` costs one explicit line.
5. **The render-mode knobs are duplicated and the WASM path is a reflection hack.**
   `AddBlazorOptions.RegisterInteractiveServerComponents` and `BlazorRenderModeOptions.EnableInteractiveServer`
   are the same decision expressed twice (Add side + Use side), which the source keeps in sync by
   convention only. Interactive WebAssembly support is implemented by **reflection** against
   `Microsoft.AspNetCore.Components.WebAssembly.Server` to dodge a package reference
   (`TryAddInteractiveWebAssemblyComponents`, `TryAddWebAssemblyRenderMode`) — failure is a log line at
   runtime. The `ShouldRegisterScalar` gate is entangled with the enterprise two-host (Wfe/Cfe)
   architecture.
6. **The security-hardening surface is mostly superseded.** `SecurityHardeningOptions`' auth-cookie half
   (`PostConfigure<CookieAuthenticationOptions>`: HttpOnly/Secure/SameSite/sliding/60 min) is already
   shipped — hardened further — by #10's `CloudstrapAuthenticationCookieOptions` (`__Host-` prefix,
   `Secure=Always`, 8 h sliding). The antiforgery half survives as the composite's default. The
   *default* antiforgery mode `Cookie.SecurePolicy=SameAsRequest` is explicitly a workaround for an
   on-prem WAF terminating TLS ("needed to support http caused by WAF! :-(") — out of scope for the
   cloud-native hosting posture.
7. **The `.csproj` reference `Nihdi.AspNetCore.AccessTokenManagement` 5.2.5 is dead code** — zero
   `using`/type references anywhere in the package (verified by grep). The token-attachment story the
   roadmap flags already lives in shipped code: #4's `AddCloudstrapHttpServiceClient` +
   `AddUserAccessToken`/`AddClientAccessToken` config flags + the `IUserAccessTokenHandlerProvider` /
   `IClientAccessTokenHandlerProvider` seams implemented by #10/#9. The demo app already exercises the
   user-token path end-to-end (`Cloudstrap:HttpClients:DemoApi:AddUserAccessToken=true`, E2E
   `BlazorServerTests`).
8. **NWebsec** (`XContentTypeOptionsMiddleware`, `UseReferrerPolicy`, `UseXfo`) reaches this package via
   Common — last NWebsec release 2019. The repo already decided this per package (#5/#6 D-4 precedent):
   no security-header dependency; a package-local middleware for the constant headers; the richer set is
   a documented recipe. The only delta the source pipeline adds over Mvc's two headers is
   `X-Frame-Options: SameOrigin` (default on, `EnableXFrameOptions` opt-out) → kept, resolved as **D-12**.
9. **Shipped-pattern alignment**: `Cloudstrap.Mvc` is the closest shipped sibling — configurator for
   code-level overrides + options section for values + `Use*` pipeline with fixed order, conditional
   auth middleware keyed on the registered scheme map (not a config flag), path base from
   `Cloudstrap:Application:PathBase`, no forwarded headers ("platform's env var, never a silent library
   default"), no HTTPS redirection, CORS only when origins configured, double-`Use` call throws. This
   spec mirrors that idiom; divergences are listed as deliberate changes.

---

## User Story

**As an** ASP.NET Core developer building a Blazor Server (interactive server rendering) application on
Azure,
**I want** one registration call and one pipeline call that give me hardened defaults (antiforgery,
security headers, HSTS, hardened cookies via the auth package), correlation and health probes in the
right middleware order, typed HttpClients that attach the signed-in user's token, and traces that make
circuit-originated work visible,
**so that** my `Program.cs` shrinks to a few intention-revealing lines and I don't hand-order Blazor's
notoriously order-sensitive middleware myself.

---

## Acceptance Criteria

| # | Given | When | Then |
|---|-------|------|------|
| AC-ASP2 | Any shipped Cloudstrap package | Its dependency closure is inspected | Zero `Aspire.*` packages. *(carried verbatim)* |
| AC-ASP3 | Resilience handlers already applied via `ConfigureHttpClientDefaults` | `AddCloudstrapHttpServiceClient<TI,TImpl>` registers a typed client | The client works; Cloudstrap does not stack a second resilience layer. *(carried verbatim — #12 adds no wrapper around #4's registration, so this stays satisfied by construction)* |
| AC-A3 | Solution searched for `Nihdi.AspNetCore` | — | Zero references. *(carried verbatim — finding 7: the source's dead reference is not ported)* |
| AC-BS1 | A fresh Blazor Server app with a `Cloudstrap:` section | `AddCloudstrapBlazorServer()` + `UseCloudstrapBlazorServer<App>()` | The app serves the root component with Interactive Server rendering; `/healthz` and `/ready` answer 200 anonymously; the correlation middleware is active (responses carry the correlation header). |
| AC-BS2 | The default pipeline | Any page response is inspected | `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` and `X-Frame-Options: SAMEORIGIN` present (all set-if-absent, never overwriting consumer-set values; the frame-options header has an options switch to disable, D-12); the antiforgery cookie is `HttpOnly`, `Secure` and `SameSite=Strict`; HSTS outside Development. |
| AC-BS3 | No authentication scheme registered | The pipeline is built and a request handled | No auth middleware runs, every endpoint is anonymous, nothing throws. With `AddCloudstrapOpenIdConnect` registered, authentication/authorization middleware sit after routing and before antiforgery/endpoints. |
| AC-BS4 | A typed client `Cloudstrap:HttpClients:{name}` with `AddUserAccessToken=true` (registered via #4) and a signed-in user | A circuit event handler triggers the client | The call reaches the API with the user's bearer token and the correlation header — no BlazorServer-specific client registration API exists or is needed. |
| AC-BS5 | The composite registered (which registers `IBlazorInteractionTrace`, D-9) | A circuit event handler runs inside `StartInteraction(name)` and calls a typed client | A root span for the interaction is exported from the package's activity source, detached from the (dropped) hub trace; the outbound dependency span parents under it; the correlation header equals the interaction's trace id; on dispose the previous ambient activity and correlation id are restored. |
| AC-BS6 | An app whose OTel pipeline is owned elsewhere (Aspire ServiceDefaults-style, or Cloudstrap contribute mode) | The package registers its activity source | The source is contributed **additively** to the existing tracer pipeline — no second pipeline, no exporter, no duplicate spans (AC-ASP1 analog for this package). |
| AC-BS7 | `Interactivity = StaticServer` selected at registration | The app runs | No interactive-server component services or render mode are wired; pages render SSR; the rest of the pipeline is unchanged. |
| AC-BS8 | Any opinionated default (probe paths, HSTS values, antiforgery hardening, static-asset mapping, render mode, additional component assemblies, exception-handler path) | The consumer overrides it via the documented option/configurator/hook | The override wins; every convention has an override. |
| AC-BS9 | A fresh clone with this package | Build, tests, `dotnet format --verify-no-changes`, case-insensitive search for `Nihdi`/`Riziv`/`probe.aspx` | All green; XML docs on all public API; package metadata complete (MIT, icon, README, SourceLink); zero forbidden identifiers; no new external NuGet dependency. |
| AC-BS10 | The `Cloudstrap.Demo.BlazorServer` app rewritten onto the composite (replacing its stock hand-wiring, resolving #27's D-B placeholder) | The E2E suite runs | All pre-existing `BlazorServerTests` stay green and ≥ 1 new E2E test proves #12 behavior through the running app (e.g. `/healthz` + hardened headers + the user-token call still passing through the composite pipeline). |

Related, not re-tested here: AC-O3 (`_blazor` noise filtering / `BlazorHubSampler`) stays in
`Cloudstrap.Observability` per the roadmap migration decision — this package must not duplicate or move it.

---

## Port Decision Table

| Source artefact (`Nihdi.Core.Configuration.BlazorServer\`) | Verdict | Target | Justification |
|---|---|---|---|
| `AdditionalControls\NihdiControls.cs` | **Drop** | — | Static mutable assembly registry with read-before-write throw — the exact defect #11 dropped as D-5; additionally loads assemblies by file path from `AppContext.BaseDirectory` (fragile, trimming-hostile). Framework-native replacement: the pipeline options' `AdditionalAssemblies` list (finding 4). |
| `AdditionalControls\NihdiControlsAssemblyOptions.cs` | **Drop** | — | Hard-codes enterprise Dashboard DLL file names as non-removable defaults (De-NIHDI item); only feeds the dropped registry. Replaced by a plain `IList<Assembly>` on the pipeline options. |
| `DistributedTracing\IDistributedTraceService.cs` + `DistributedTraceService.cs` | **Redesign** *(final, D-9, user-approved)* | `IBlazorInteractionTrace.StartInteraction(string name)` → disposable scope (one interface, one method) | Zero production consumers of the old surface (finding 2), but the circuit-tracing gap is real (finding 3). The redesign keeps the capability and sheds the defects: no static `ActivityListener`, no forced `AllData` sampling, no DI-scope creation, no T1–T5 service-locator overloads. |
| `DistributedTracing\DistributedTrace.cs` | **Drop** | — | Carrier object bundling `Activity` + correlation context + DI scope; the redesigned scope owns its activity and correlation restore internally — consumers never manage the triple. |
| `DistributedTracing\ActivitySourceDelegatingHandler.cs` | **Drop** *(final, D-9 — the automatic-handler variant was explicitly rejected)* | — | Never runs in production (finding 2); per-request it duplicates what OTel `HttpClientInstrumentation` already emits, and it cannot group a multi-call interaction anyway (it wraps single requests). The explicit interaction scope covers the need honestly. |
| `DistributedTracing\IServiceCollectionExtensions.cs` (`AddDistributedTracing`) | **Redesign** | Folded into `AddCloudstrapBlazorServer` | One-liner registration; a composite exists precisely so consumers don't assemble registration calls. |
| `Extensions\AddBlazorOptions.cs` | **Redesign** | `CloudstrapBlazorServerConfigurator` (code-level) + `CloudstrapBlazorServerOptions` (`Cloudstrap:BlazorServer`) | Duplicated render-mode knobs unified into one `Interactivity` decision made once at Add time (finding 5); the 5-parameter `ConfigureServicesDelegate` dropped (consumer registrations belong in `Program.cs`, not a callback that re-passes the service collection); `ConfigureProbeHealthChecks` delegate dropped — the stock `IHealthChecksBuilder` is the seam (Aspire-additive by design); `WithSecurityHardening` superseded (finding 6). |
| `Extensions\BlazorRenderModeOptions.cs` | **Redesign** | `BlazorServerPipelineOptions` — hooks `BeforeRouting` / `BeforeAuthorization` / `BeforeEndpoints` / `ConfigureEndpoints`, `ConfigureComponentEndpoints`, `AdditionalAssemblies`, `MapStaticAssets` flag | The pipeline-hook idea is sound and matches the shipped `MvcPipelineOptions` idiom; render-mode booleans move to the configurator (single source of truth); `IncludeNihdiControlLibraries`/`NihdiControlsOptions` fall with the registry; `UseHttpsRedirection` dropped per D-5; `EnableXFrameOptions` survives as an options switch per D-12. |
| `Extensions\SecurityHardeningOptions.cs` | **Drop** | — | Auth-cookie half superseded by #10's hardened-by-default session cookie (finding 6); antiforgery half becomes the composite's *default* (not an opt-in) with a code-level override hook; the `X-XSRF-TOKEN` header-name knob is BFF/WASM territory → #13. |
| `Extensions\WebApplicationBuilderExtensions.cs` — `AddBlazorForNihdi` | **Redesign** | `AddCloudstrapBlazorServer(this WebApplicationBuilder, Action<CloudstrapBlazorServerConfigurator>?)` | The composite earns its port (finding 1) rebuilt from shipped constituents: `AddCloudstrapCore`, `AddCloudstrapCorrelation`, stock `AddHealthChecks`, `AddRazorComponents` (+ interactive server per `Interactivity`), `AddCascadingAuthenticationState`, hardened antiforgery, HSTS options. Dropped from the composite: KeyVault data protection auto-wiring (explicit `AddCloudstrapDataProtection`, documented in README — D-8), localization (deliverable #24, additive later), Scalar gating (WebApi package's job; the source gate served the enterprise Wfe/Cfe split), `StaticWebAssetsLoader` (framework handles Development automatically; the call existed for the LOC/DEV/TST taxonomy), controllers (D-6), WASM reflection registration (D-7). |
| `Extensions\WebApplicationBuilderExtensions.cs` — `AddBlazorServerForNihdi` (`[Obsolete]`) | **Drop** | — | Already deprecated in the source; Cloudstrap has no consumers to migrate. |
| `Extensions\WebApplicationExtensions.cs` — `UseBlazorForNihdi<T>` | **Redesign** | `UseCloudstrapBlazorServer<TRootComponent>(this WebApplication, Action<BlazorServerPipelineOptions>?)` | Middleware ordering is the product; rebuilt on the shipped `UseCloudstrapMvc` mechanics: exception head → HSTS → security headers → path base → hooks/routing → correlation → conditional auth (scheme-map test + placement markers) → antiforgery → `MapStaticAssets` → `MapRazorComponents<TRoot>` (+ render mode + `AdditionalAssemblies`) → `MapCloudstrapHealthChecks` → endpoint hooks. `/probe` + `/probe.aspx` → `/healthz` + `/ready` (De-NIHDI); path-base env-var sniffing (`basepath`, `EnvironmentIsLocal`, `/{WorkloadName}` fallback) → `Cloudstrap:Application:PathBase` only; `UseForwardedHeaders` dropped (platform convention, Mvc precedent); NWebsec → package-local three-constant-header middleware incl. `X-Frame-Options` (finding 8, D-12); reflection WASM render mode → `ConfigureComponentEndpoints` hook (D-7). |
| `Extensions\WebApplicationExtensions.cs` — `UseBlazorInteractiveServerModeForNihdi<T>` (`[Obsolete]`) + legacy privates | **Drop** | — | Already deprecated; wrong middleware ordering was its own documented defect. |
| `Extensions\WebApplicationExtensions.Log.cs` | **Drop** (re-expressed) | Internal `LoggerMessage` set written fresh for the new pipeline | Log scaffolding follows the code it logs; contains an orphaned message (`LogGzipWorkaroundActive` — nothing calls it) evidencing drift. Not public surface in Cloudstrap. |
| `HttpClient\ServiceCollectionExtensions.cs` (`AddNihdiHttpServiceClient` BlazorServer variant) | **Replace** (by shipped Cloudstrap code) | #4's `AddCloudstrapHttpServiceClient<TI,TImpl>` + `AddUserAccessToken`/`AddClientAccessToken` flags + #9/#10 providers | Zero production consumers even in the source (finding 2); its only delta over the Common variant was attaching the dropped handler. The shipped registration already does configuration-driven base address/timeout, correlation, token attachment and per-client health checks — a BlazorServer wrapper would be a second way to do the same thing. |
| `.csproj` — `Nihdi.AspNetCore.AccessTokenManagement` 5.2.5 | **Drop** | — | Dead reference: zero code references in the package (finding 7). Token management is Duende ATM via #9/#10 — the founding-spec decision, already shipped. |
| `.csproj` — `Nihdi.Core.Configuration.Common` ProjectReference | **Replace** | `Cloudstrap.Extensions` ProjectReference (brings `Cloudstrap.Core` + `Cloudstrap.Observability` transitively) | The Common grab-bag was split by design (founding spec package map). |
| `.csproj` — `StyleCop.Analyzers.Unstable` | **Drop** | — | Repo-wide decision #0: SDK analyzers, no StyleCop. |

---

## Public API Sketch

*(Shapes and names, not implementations. Final — reflects the user-approved answers to all three Open
Questions (D-9/D-12/D-13). Single flat namespace `Cloudstrap.BlazorServer`.)*

```csharp
namespace Cloudstrap.BlazorServer;

/// Settings bound from Cloudstrap:BlazorServer, validated at host startup.
public sealed class CloudstrapBlazorServerOptions
{
    public const string SectionName = "Cloudstrap:BlazorServer";
    public HstsSettings Hsts { get; set; }                         // Enabled / MaxAgeDays / IncludeSubDomains / Preload — package-local re-expression (Mvc D-2 precedent)
    public ExceptionHandlingSettings ExceptionHandling { get; set; } // UseDeveloperExceptionPage: bool? (null = environment default)
    public bool EnableFrameOptions { get; set; }                   // default true: X-Frame-Options: SAMEORIGIN, set-if-absent (D-12); false skips the header
}

/// Code-level overrides configuration cannot express (mirrors CloudstrapMvcConfigurator).
public sealed class CloudstrapBlazorServerConfigurator
{
    /// InteractiveServer (default) | StaticServer. Decided ONCE here; the pipeline follows it (fixes the source's duplicated Add/Use knobs).
    public BlazorInteractivity Interactivity { get; set; }

    /// Final say over antiforgery, after Cloudstrap's hardened defaults (HttpOnly, Secure=Always, SameSite=Strict).
    public Action<AntiforgeryOptions>? Antiforgery { get; set; }

    /// Final say over the Razor components builder (e.g. AddInteractiveWebAssemblyComponents from a
    /// consumer-referenced package, circuit options, hub options).
    public Action<IRazorComponentsBuilder>? RazorComponents { get; set; }
}

public enum BlazorInteractivity { InteractiveServer, StaticServer }

/// Pipeline placement hooks + endpoint shaping (mirrors MvcPipelineOptions).
public sealed class BlazorServerPipelineOptions
{
    public bool MapStaticAssets { get; set; }                      // default true
    public IList<Assembly> AdditionalAssemblies { get; }           // extra routable component assemblies — replaces NihdiControls
    public Action<IApplicationBuilder>? BeforeRouting { get; set; }
    public Action<IApplicationBuilder>? BeforeAuthorization { get; set; }
    public Action<IApplicationBuilder>? BeforeEndpoints { get; set; }
    public Action<RazorComponentsEndpointConventionBuilder>? ConfigureComponentEndpoints { get; set; } // e.g. AddInteractiveWebAssemblyRenderMode — consumer references the package, no reflection
    public Action<IEndpointRouteBuilder>? ConfigureEndpoints { get; set; }
}

public static class WebApplicationBuilderExtensions
{
    /// Service side: Cloudstrap core options, correlation, stock health checks, Razor components
    /// (+ interactive server per Interactivity), cascading authentication state, hardened
    /// antiforgery, HSTS, and IBlazorInteractionTrace (D-9, source contributed additively to any
    /// OTel tracer pipeline). Registers NO authentication and NO observability pipeline — those
    /// stay separate, visible calls (AddCloudstrapOpenIdConnect / UseCloudstrapObservability).
    public static WebApplicationBuilder AddCloudstrapBlazorServer(
        this WebApplicationBuilder builder,
        Action<CloudstrapBlazorServerConfigurator>? configure = null);
}

public static class WebApplicationExtensions
{
    /// Pipeline side, fixed order (the point of the call); throws on a second call.
    public static WebApplication UseCloudstrapBlazorServer<TRootComponent>(
        this WebApplication app,
        Action<BlazorServerPipelineOptions>? configure = null);
}

/// Wraps one user interaction (circuit event handler) in its own root trace so its spans and
/// outbound calls are exported and correlated instead of hanging off the dropped SignalR hub trace.
public interface IBlazorInteractionTrace
{
    /// Starts a root activity (package-owned source, detached from the hub trace) and points the
    /// ambient correlation id at its trace id; both are restored when the scope is disposed.
    /// Safe no-op when no telemetry listener is active.
    IDisposable StartInteraction(string interactionName);
}
```

Typed HttpClients: **no BlazorServer-specific registration API.** Consumers call #4's
`AddCloudstrapHttpServiceClient<TI,TImpl>` exactly as any other host does; the `AddUserAccessToken`
flag plus the #10 provider attach the signed-in user's token from circuit context (already proven by
the demo + E2E).

---

## Behaviors & Conventions

| Behavior | Default | Override |
|---|---|---|
| Render mode | Interactive Server components + render mode | `Configurator.Interactivity = StaticServer`; WASM/auto render modes via `Configurator.RazorComponents` + `PipelineOptions.ConfigureComponentEndpoints` (consumer references `Microsoft.AspNetCore.Components.WebAssembly.Server` itself) |
| Health probes | `/healthz` (liveness) + `/ready` (readiness) via `MapCloudstrapHealthChecks` — additive on the stock builder | `Cloudstrap:HealthChecks` section (paths, `Enabled=false`); consumers add checks to the stock `IHealthChecksBuilder` |
| Antiforgery | On, hardened: cookie `HttpOnly`, `Secure=Always`, `SameSite=Strict`; middleware after auth, before endpoints | `Configurator.Antiforgery` has the final say (visible code, never config drift — #10's precedent) |
| Security headers | `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: SAMEORIGIN` (D-12), all set-if-absent (package-local middleware) | Pre-set any header yourself (never overwritten); `Cloudstrap:BlazorServer:EnableFrameOptions=false` for legitimately framed apps; richer set (CSP `frame-ancestors` etc.) via the NetEscapades recipe in `BeforeRouting` (documented) |
| HSTS | On outside Development; `Cloudstrap:BlazorServer:Hsts` values | Config section; `Enabled=false` |
| Exception handling | Developer page in Development; otherwise `UseExceptionHandler` re-executing `Cloudstrap:Application:ExceptionHandlerPath` (with scope-for-errors, the razor-components idiom) | `Cloudstrap:BlazorServer:ExceptionHandling:UseDeveloperExceptionPage` |
| Correlation | `UseCloudstrapCorrelation` after routing, before auth (a 401 is as traceable as a 200 — Mvc rationale) | `Cloudstrap:Correlation` (header name etc., shipped #2) |
| Authentication | None registered; middleware appears only when a scheme map is non-empty (placement markers claimed so minimal hosting never inserts it pre-routing) | Pair with `AddCloudstrapOpenIdConnect` or any scheme |
| Path base | None; `Cloudstrap:Application:PathBase` when set | Config |
| Static assets | `MapStaticAssets()` on | `PipelineOptions.MapStaticAssets = false` |
| Routable component assemblies | Root component's assembly only | `PipelineOptions.AdditionalAssemblies` |
| Forwarded headers / HTTPS redirection / CORS | Not wired (platform conventions: `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, TLS at the Azure front end; Blazor Server is same-origin) | `BeforeRouting` hook |
| Observability | Not wired here; `UseCloudstrapObservability` stays a separate call. The package's interaction activity source (D-9) is contributed **additively** to whatever tracer pipeline exists — owner, contribute, or Aspire-style — no exporter, no second pipeline (AC-BS6) | Consumers owning their pipeline can also `AddSource` the published constant themselves |
| Data protection | Not auto-wired; README documents that multi-instance Blazor Server needs `AddCloudstrapDataProtection` (antiforgery + auth cookies survive restarts/scale-out) | Explicit call (shipped #4) |
| Repeat calls | `Add…` idempotent/additive; `Use…` throws on the second call (a pipeline is built once — Mvc precedent) | — |

---

## Dependencies

| Reference | Kind | License | Justification |
|---|---|---|---|
| `Cloudstrap.Extensions` (→ `Cloudstrap.Core`, `Cloudstrap.Observability`) | ProjectReference | MIT | Health probes, typed-client story, correlation, core options — the constituents the composite orders. |
| `Microsoft.AspNetCore.App` | FrameworkReference | MIT | Razor components, antiforgery, auth middleware types. |
| **No new external NuGet packages.** NWebsec (last release 2019) and `Nihdi.AspNetCore.AccessTokenManagement` (dead reference) are not carried; no MudBlazor (not needed here — roadmap note). | | | |
| ~~`Cloudstrap.BlazorCommon`~~ | — | MIT | **Not referenced** *(final, D-13, user-approved)*: nothing in this package's surface consumes `IViewModel`/`IErrorHandler`; the demo app references BlazorCommon directly to prove band interop, and the `blazor.md` band table is corrected in this deliverable's doc pass. |

Zero `Aspire.*` (AC-ASP2), zero `Nihdi.*` (AC-A3).

---

## Deliberate Behavior Changes (vs. the source library)

| # | Change | Why |
|---|---|---|
| D-1 | Probe endpoints `/probe` + `/probe.aspx` → `/healthz` + `/ready`, configurable | Founding-spec De-NIHDI item; shipped `MapCloudstrapHealthChecks`. |
| D-2 | Antiforgery default: `SecurePolicy=SameAsRequest` (WAF workaround) → hardened `Always`/`HttpOnly`/`Strict` | The old default existed for an on-prem WAF terminating TLS — out of the cloud-native hosting posture; hardened is the correct open-source default, with a code override. |
| D-3 | Auth middleware gate: `Security.EnableAuthentication` config flag → presence of a registered scheme | Config flags that must agree with code registrations drift; the scheme map is the truth (shipped Mvc mechanic). |
| D-4 | Path base: `basepath` env var + `/{WorkloadName}` fallback + `EnvironmentIsLocal()` sniffing → `Cloudstrap:Application:PathBase` only | Hosting-posture rule: explicit configuration, no environment sniffing, no implicit workload-name URL convention. |
| D-5 | `UseForwardedHeaders` + `UseHttpsRedirection` removed from the composite | Platform conventions (`ASPNETCORE_FORWARDEDHEADERS_ENABLED`; TLS terminates at the Azure front end); Mvc precedent — never a silent library default. |
| D-6 | `AddControllers` + `MapControllers().RequireAuthorization()` removed | Not every Blazor Server app hosts controllers; forced `RequireAuthorization` fights the auth package's fallback-policy design. `ConfigureEndpoints` hook + consumer's own `AddControllers` cover the case. |
| D-7 | Interactive WebAssembly support via reflection removed; replaced by explicit hooks | Reflection against a non-referenced assembly failing to a runtime log line is not a contract; a consumer who hosts WASM components references the package and writes one visible line in each hook. |
| D-8 | KeyVault data protection, localization, Scalar and observability wiring removed from the composite | Each is its own explicit call/package (#4 `AddCloudstrapDataProtection`, #24, #5, #2); the source composite's Scalar gate served the enterprise Wfe/Cfe host split. Composites order what belongs to their feature; they don't hide unrelated registrations. |
| D-9 | `IDistributedTraceService` (5 generic overloads, DI scopes, static listener) → the one-method `IBlazorInteractionTrace.StartInteraction(name)` scope: root activity from a package-owned source detached from the dropped hub trace, ambient correlation id set to the new trace id, both restored on dispose; registered by the composite; source contributed additively (AC-BS6). The automatic delegating-handler variant is explicitly rejected. *(resolved 2026-08-22, user-approved — was OQ-1)* | Zero production consumers of the old surface (finding 2) but a real circuit-tracing/correlation gap (finding 3); the old API's unusability, not absent need, explains its history. |
| D-10 | Legacy `[Obsolete]` methods (`AddBlazorServerForNihdi`, `UseBlazorInteractiveServerModeForNihdi`) not ported | No consumers exist; Cloudstrap ships no deprecated surface at birth. |
| D-11 | Dev/test gates `EnvironmentIsDevTest()` (LOC/DEV/TST taxonomy) → standard `IsDevelopment()` + explicit option | Founding-spec environment-taxonomy decision. |
| D-12 | `X-Frame-Options: SAMEORIGIN` ships in the default security headers — set-if-absent, disabled via `Cloudstrap:BlazorServer:EnableFrameOptions=false` *(resolved 2026-08-22, user-approved — was OQ-2)* | Same-origin framing protection breaks almost nothing and protects the highest-risk (interactive, cookie-authenticated) hosting model; stays a constant header with no dependency, so the Mvc D-4 rationale (no NWebsec, no app-specific policy guessing) is preserved. The sibling packages (Mvc/WebApi) may later align under the pre-release breaking allowance — user's call, separate deliverable. |
| D-13 | No `Cloudstrap.BlazorCommon` ProjectReference *(resolved 2026-08-22, user-approved — was OQ-3)* | Zero consumed symbols — an empty dependency edge is closure weight and a false signal. The band dependency is honored with evidence instead: the demo app adopts `AddCloudstrapBlazorCommon` + `IViewModel` directly, and `.claude/instructions/blazor.md`'s band table is corrected in this deliverable's doc pass. #11's "#12 references it" phrasing was an ordering forecast, not a consumed contract. |

---

## Edge Cases

| Case | Expected behavior |
|---|---|
| `UseCloudstrapBlazorServer` called twice | `InvalidOperationException` (pipeline built once — Mvc precedent). |
| No `Cloudstrap:BlazorServer` section | All defaults apply; startup validation passes (section optional). |
| `StaticServer` interactivity + a component declaring `@rendermode InteractiveServer` | Framework's own runtime error — documented; the package adds no detection. |
| `ConfigureComponentEndpoints` adds a WASM render mode without the consumer registering WASM component services | Framework's own exception at startup — honest failure, no reflection fallback masking it. |
| Interaction scope disposed out of order / never disposed (D-9) | Activity restore is stack-safe per scope (restore-to-previous on dispose); an undisposed scope ends with the circuit's async context — documented, never throws. |
| `StartInteraction` with no telemetry listener (D-9) | Safe no-op scope (matches `IBusinessTrace` semantics); correlation id still set so the outbound header is stable. |
| A consumer sets `X-Content-Type-Options` (or any header the middleware writes) | Never overwritten — set-if-absent. |
| `AdditionalAssemblies` contains duplicates or the root assembly | Passed through to the framework's `AddAdditionalAssemblies` semantics; documented, not "fixed". |

---

## Demo & E2E (standing rule / workflow rule 9)

`src/demo/BlazorServer/Cloudstrap.Demo.BlazorServer` (port 5340) is rewritten onto the composite —
resolving #27's "deliberately no Cloudstrap.BlazorServer helper code" placeholder: `AddCloudstrapBlazorServer` +
`UseCloudstrapBlazorServer<App>` replace the manual `AddRazorComponents`/`UseAuthentication`/
`UseAntiforgery`/`MapRazorComponents` block; `AddCloudstrapOpenIdConnect`, the `DemoApi` user-token
typed client and `MapCloudstrapAuthenticationEndpoints` (via `ConfigureEndpoints` or before the
composite) stay. The API-calling page is restructured to the ViewModel pattern — implementing
`IViewModel`, registered through `AddCloudstrapBlazorCommon` (a direct demo-level BlazorCommon
reference, D-13) — and wraps its call in `IBlazorInteractionTrace.StartInteraction` (D-9).
≥ 1 new test in `Cloudstrap.Demo.E2E.Tests` (`BlazorServerTests`): probe endpoints + hardened headers
(including `X-Frame-Options`, D-12) + the existing user-token round trip green through the composite
pipeline (AC-BS10).

---

## Out of Scope (this deliverable — the planner must not resurrect any of it)

- **`NihdiControls` / `NihdiControlsAssemblyOptions`** — dropped; `AdditionalAssemblies` is the replacement.
- **`ActivitySourceDelegatingHandler`, `DistributedTrace`, the `UseDistributedTrace<T1..T5>` overloads, the static `ActivityListener`** — dropped (D-9 keeps only the explicit one-method scope; no automatic per-request handler).
- **A BlazorServer-specific typed-HttpClient registration API** — #4's registration is the one way.
- **`SecurityHardeningOptions`** (both halves) and the NWebsec dependency.
- **Interactive WebAssembly / auto render-mode support in the composite** (hooks only, D-7); cookie/XSRF/BFF browser-auth → #13; `BlazorHubSampler` stays in `Cloudstrap.Observability`.
- Obsolete legacy methods (D-10); Scalar/OpenAPI (#5's package); localization (#24); MudBlazor (nothing here needs it); dashboard component discovery (#20).
- Founding-spec global out-of-scope items: message encryption, MessagingBridge, Dynatrace, ServicePlatform, `Cloudstrap.Functional`.

---

## Decision Log (gate answers, 2026-08-22 — zero Open Questions remain; spec is planner-ready)

All three Open Questions were answered by the user, in each case accepting the analyst's recommendation.
The full evidence and rejected options remain on record in the Code-reading findings, the Port Decision
Table, and the Deliberate Behavior Changes rows they resolved into.

| OQ | Decision (final) | Rationale kept on record |
|---|---|---|
| OQ-1 → **D-9** | **Slim redesign**: ship `IBlazorInteractionTrace.StartInteraction(name)` — a root activity from a package-owned source detached from the dropped hub trace, ambient correlation id set to the new trace id, both restored on dispose; registered by the composite; source contributed additively to any tracer pipeline (AC-BS6). | Zero production consumers of the old surface (finding 2) but a real circuit-tracing/correlation gap (finding 3); the old API's unusability (five generic overloads, DI-scope juggling, static `ActivityListener`) — not absent need — explains its history; Cloudstrap's own demo consumes the new scope (AC-BS5/AC-BS10). Rejected: drop everything (leaves orphaned traces + missing correlation headers); the automatic delegating handler (implicit per-request magic, duplicates instrumentation spans, cannot group a multi-call interaction). |
| OQ-2 → **D-12** | **Include `X-Frame-Options: SAMEORIGIN`** in the default security-header middleware — set-if-absent, disabled via `Cloudstrap:BlazorServer:EnableFrameOptions=false`. | Same-origin framing protection breaks almost nothing and protects the highest-risk (interactive, cookie-authenticated) hosting model; stays a constant header with no dependency, preserving the Mvc D-4 rationale. Mvc/WebApi may later align under the pre-release breaking allowance — separate deliverable, user's call. Rejected: matching Mvc's two-header minimum (leaves the prime clickjacking target unprotected by default). |
| OQ-3 → **D-13** | **No `Cloudstrap.BlazorCommon` ProjectReference.** The demo app references BlazorCommon directly (`AddCloudstrapBlazorCommon` + `IViewModel` on the API-calling page), and `.claude/instructions/blazor.md`'s band table is corrected in this deliverable's doc pass. | Zero consumed symbols in the package surface — an empty edge is closure weight and a false signal; #11's "#12 references it" phrasing was an ordering forecast, not a consumed contract; the source package itself never referenced BlazorCommon. Rejected: dead reference; configurator-forwarded convention-scan sugar (gold-plating). |
