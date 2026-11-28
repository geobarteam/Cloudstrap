# Cloudstrap extraction backlog

What remains to port from the private source library, in execution order, and the porting rules that hold for every deliverable. Product decisions (package map, Decisions Made, De-NIHDI-fication Checklist, acceptance criteria) stay in the founding spec [`_specs/Cloudstrap.md`](../_specs/Cloudstrap.md). Each deliverable below becomes one feature of the spec-driven workflow (`.claude/rules/spec-driven.md`): its spec is `_specs/<N>-<Deliverable>/spec.md`, started with `/new-feature`.

Status: ⬜ not started · 📝 spec in progress · ✅ done. The `#` is a stable identifier (shipped code and specs refer to it); deliverables are never renumbered, the table is in execution order.

## Done

| # | Deliverable | Packages |
|---|---|---|
| 0 | Repo scaffolding | `src/Cloudstrap.sln`, `Directory.Build.props`, `Directory.Packages.props` (CPM), `.editorconfig`, `GitVersion.yml`, CI workflows |
| 1 | Core settings model | Cloudstrap.Core |
| 2 | Observability base | Cloudstrap.Observability |
| 3 | Azure Monitor exporter | Cloudstrap.Observability.AzureMonitor |
| 4 | Config/KeyVault/HTTP extensions | Cloudstrap.Extensions |
| 5 | WebApi bootstrap | Cloudstrap.WebApi |
| 6 | MVC bootstrap | Cloudstrap.Mvc |
| 7 | Worker bootstrap | Cloudstrap.Worker |
| 8 | Test helpers | Cloudstrap.TestInfrastructure (test-only, never packed) |
| 9 | Client-credentials auth | Cloudstrap.Authentication.ClientCredentials |
| 10 | OIDC login | Cloudstrap.Authentication.OpenIdConnect |
| 11 | Blazor shared abstractions | Cloudstrap.BlazorCommon |
| 12 | Blazor Server helpers | Cloudstrap.BlazorServer |
| 13 | Blazor WASM helpers | Cloudstrap.BlazorWasm |
| 14 | Messaging (Wolverine) | Cloudstrap.Messaging |
| 15 | Blob claim-check | Cloudstrap.Messaging.AzureBlob |
| 16 | Hangfire scheduler | Cloudstrap.Hangfire |
| 25 | WASM demo + E2E harness | not shipped (relocated by #27) |
| 27 | Demo applications (`src/demo`) | not shipped |

Their specs `_specs/<N>-<Deliverable>.md` are history: written before the spec-driven workflow, shipped, and to be verified against the code before anything is taken from them.

## Remaining, in execution order

| # | Deliverable | Packages | Depends on | Status |
|---|---|---|---|---|
| 17 | YARP trusted-subsystem proxy | Cloudstrap.Proxy | 4 ✅ (uses the #9/#10 token seams) | 📝 |
| 18 | Hangfire dashboard proxy | Cloudstrap.Hangfire.Proxy | 16 ✅, 17 | 📝 |
| 19 | Dashboard backend | Cloudstrap.Dashboard.Contracts + Cloudstrap.Dashboard.Api | 1 ✅, 14 ✅ | ⬜ |
| 20 | Dashboard UI (MudBlazor) | Cloudstrap.Dashboard.Components.Shared + .Components + .Components.BlazorWasm | 11 ✅, 13 ✅, 19 | ⬜ |
| 21 | Cookie consent | Cloudstrap.CookieConsent | 0 ✅ | ⬜ |
| 22 | Analytics abstraction + Matomo | Cloudstrap.Analytics + Cloudstrap.Analytics.Matomo | 21 | ⬜ |
| 23 | Google Analytics adapter | Cloudstrap.Analytics.GoogleAnalytics | 22 | ⬜ |
| 24 | Localization setup | Cloudstrap.Localization | 1 ✅ | ⬜ |
| 26 | Test identity provider package | `Cloudstrap.Testing.*` test-audience package | 10 ✅; preferably after 17 | ⬜ deferred, demand-driven |

Source paths are relative to the read-only reference repo `D:\source\Nihdi-Core-Configuration\Nihdi-Core-Configuration\src\`.

### 17. YARP trusted-subsystem proxy
- **Goal**: a front-door host (BFF / interactive web app) forwards selected routes to a downstream API with one registration call: the front door authenticates the user, the downstream API receives the application's access token, the user's cookie/token never leaves the front door, and an unauthenticated caller can never ride on the application's token.
- **Source**: `Nihdi.Core.Configuration.Proxy\` (references Common; `Yarp.ReverseProxy`); consumed by `Hangfire.Proxy` (#18).
- **Decisions already taken**: token attachment through the shipped #4 seams (`IClientAccessTokenHandlerProvider` / `IUserAccessTokenHandlerProvider`, no Duende reference in `Cloudstrap.Proxy`); fail loud when a token is configured but no provider is registered (#4 `AccessTokenHandlerWiring` rule); no resilience added (AC-ASP3); correlation propagated on the proxied hop (#2); `Duende.BFF` rejected (commercial, not OSI).
- **State**: all four slices built; the final review ran on 2026-11-28 (job `01M8GT8NGG37JJVR8HERQZDG2J`). The spec is `_specs/17-TrustedSubsystemProxy/spec.md` (carried over from the approved `_specs/17-TrustedSubsystemProxy.md`, which stays as history). The retired build record `_plans/17-TrustedSubsystemProxy.md` was removed the same day; what it still held open is listed here:
  - **Owner's acceptance of Slice 3** (old Gate 4): the handler chain order (correlation → token → YARP's handler), the package surface pinned by `PackageSurfaceTests`, `src/Cloudstrap.Proxy/README.md` (the unsafe opt-outs, "one owner" for destination resolution, the D-13 TLS note), the characterization tests green on arrival (`RegressionGuard`), and the mutation evidence `_specs/17-TrustedSubsystemProxy/mutation-1-3.md` with its two `accepted` survivors.
  - ✅ **Slice 4 accepted by the owner on 2026-11-28** (no mutation score for the demo-only code accepted) and merged into `dev`; #17 goes to `main` only when the owner asks. What was accepted (old final gate): the three AC-PX14 E2E results, the Bff `ReverseProxy` route/cluster, the `demo-bff` audience change (Q-1), the #18 shared contract (path-prefixed route + `X-Forwarded-Prefix`, `Client` mode, authenticated by default).
  - **Follow-up (not in #17)**: the #9 `ClientCredentialsAccessTokenHandlerProvider` warnings name `Cloudstrap:HttpClients:{name}:TokenRequestParameters` even when the name is a proxy cluster id (the key is really `Cloudstrap:Proxy:Clusters:{clusterId}:TokenRequestParameters`). Cosmetic, internal; a `bugfix` when the owner wants it.
  - **#18 hand-off**: the facts of D-15 in the approved spec (the source maps `/hangfire` with a role policy; an empty role means any signed-in user).
  - Then: the owner pushes the branch and completes the pull request; the spec's Status gets "shipped" and this row moves to Done.
- **Risks**: ⚠️ auth / credential handling (stock YARP forwards `Cookie` + `Authorization`); ⚠️ public API; ⚠️ new dependency `Yarp.ReverseProxy` (MIT); ⚠️ shared contract with #18 (forwarded-prefix headers, token mode); Aspire overlap with `Microsoft.Extensions.ServiceDiscovery.Yarp` — compose through YARP's destination-resolver seam, never reference.
- **Demo**: proposed BlazorWasm Bff (5300) → Api (5330).

### 18. Hangfire dashboard proxy
- **Goal**: a consumer exposes the Hangfire dashboard securely through a proxy host.
- **Source**: `Nihdi.Core.Configuration.Hangfire.Proxy\` (references Hangfire + Proxy; relied on `InternalsVisibleTo` from Hangfire — #16 provides a public seam instead).
- **Risks**: ⚠️ dashboard auth path.
- **State**: spec `_specs/18-HangfireDashboardProxy/spec.md` written as a **Draft** on 2026-11-28 (unattended job: investigation + spec + one critic pass, no interview). Open: the owner's answers to Q-1…Q-7, then approval. Build only after #17 is merged into `dev`.

### 19. Dashboard backend
- **Goal**: a consumer mounts the ops-dashboard API: Azure Service Bus queue peek/purge/retry, diagnostics, claims viewer — without message decryption (dropped).
- **Source**: `Nihdi.Core.Configuration.Dashboard\Dashboard.Contracts\` + `Dashboard.Api\` (`Azure.Messaging.ServiceBus`, `Scrutor`).
- **Decisions already taken**: dashboard settings (old Core `Settings\Dashboard\`) live here, so `Cloudstrap.Core` stays a leaf; decryption deleted; messaging integration re-targeted from NServiceBus to Wolverine / the ASB SDK; functional primitives from LanguageExt.Core.
- **Risks**: ⚠️ largest product-layer surface; claims viewer touches auth data.

### 20. Dashboard UI (MudBlazor)
- **Goal**: dashboard Blazor components for Server and WASM hosts on plain MudBlazor.
- **Source**: `Nihdi.Core.Configuration.Dashboard\Dashboard.Components.Shared\`, `Dashboard.Components\`, `Dashboard.Components.BlazorWasm\`.
- **Decisions already taken**: internal design system → plain MudBlazor (MIT); `Newtonsoft.Json` → System.Text.Json.
- **Risks**: ⚠️ UI-visible restyle.

### 21. Cookie consent
- **Goal**: a CSP-friendly cookie-consent UI (bundled orejime assets, no CDN) in one component.
- **Source**: `Nihdi.Core.Configuration.CookieConsent\` (standalone Razor library).
- **Risks**: ⚠️ bundled JS licence attribution (orejime — verify the current licence and NOTICE requirements).

### 22. Analytics abstraction + Matomo
- **Goal**: `IAnalyticsTracker`, consent-gated through CookieConsent, with the Matomo adapter; no tracking before consent, endpoint always required (no default instance URL).
- **Source**: `Nihdi.Core.Configuration.Analytics.Matomo\` (JS-interop tracker; the abstraction is new).

### 23. Google Analytics adapter
- **Goal**: GA4 (gtag) behind the same `IAnalyticsTracker`; new code, consent gating inherited.

### 24. Localization setup
- **Goal**: one-call culture-negotiation defaults and a supported-cultures config section over stock ASP.NET Core localization.
- **Source**: none — `Nihdi.AspNetCore.Localization` is internal; this is a thin new implementation. `Cloudstrap.Extensions` must not depend on it. The BlazorWasm culture-persistence helpers not ported with #13 may land here if call-site evidence justifies them.

### 26. Test identity provider package (deferred)
- **Goal**: a consumer tests their own app's Cloudstrap auth wiring against a hermetic in-process OpenID Connect server from one test-audience package.
- **Source**: the in-repo `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/` (repackaging only).
- **Trigger**: only on evidence of consumer demand; runtime `Cloudstrap.*` packages stay free of OpenIddict / EF Core.

## Porting rules (every deliverable)

- **Every source feature earns its place.** The investigation classifies every public type and feature of the source material as **Port** (valuable, decently designed), **Redesign** (the capability earns its place, the design does not), **Replace** (a well-maintained library does it better — the spec names it and the thin seam) or **Drop** (no credible value for an open-source consumer), each with a justification, in the spec's Findings. When value is equal: Replace beats Redesign beats Port. No feature the source never had and no criterion asks for.
- **A replacement library** must be OSI-licensed (prefer MIT/Apache-2.0; copyleft is a question for the user), actively maintained, .NET 10 compatible, with a reasonable dependency footprint, and must reduce the code Cloudstrap owns. `Aspire.*` packages are never eligible.
- **Hosting posture: cloud-native Azure only** (Azure Web Apps, containers/Kubernetes). Features that detect or accommodate on-prem hosting (host sniffing, machine-name or local-path conventions, credential switching by host type) default to Drop.
- **Breaking with old behavior is allowed** (nothing is published yet); every deliberate behavior change is stated in the spec.
- **The founding spec's Decisions Made are not re-litigated silently**: evidence against one is an open question for the user; only the user amends `_specs/Cloudstrap.md`.
- **Out of scope everywhere**: message encryption, MessagingBridge, Dynatrace, ServicePlatform/ServicePulse, a `Cloudstrap.Functional` port (LanguageExt.Core is used instead), `Cloudstrap.Aspire` in v1.
- **Done means**: build, tests and format green; XML docs on all public API; package metadata complete; zero `Nihdi`/`NIHDI`/`Riziv` identifiers; the founding spec's acceptance criteria for the area met; the headline behavior demonstrated in the demo app `CLAUDE.md` § Extraction Phase names for the feature type, with ≥ 1 E2E test in `Cloudstrap.Demo.E2E.Tests`.
