# Cloudstrap WASM Test Project (SUT)

The Blazor WebAssembly **system-under-test** for the Cloudstrap suite. Every delivered
Cloudstrap package demonstrates its headline behavior here, proven by an end-to-end test
that drives the real running app in a real browser.

Modeled on the source repo's `Test\WasmTestProject` (Bff + Wasm host split), rebuilt
neutral and trimmed to what Cloudstrap has actually shipped — layers (auth, messaging,
persistence, …) are added by the deliverable that demonstrates them.

## Layout

```
src/
├── Contracts/       Cloudstrap.WasmTestProject.Contracts        DTOs shared client/server
├── Presentation/    Cloudstrap.WasmTestProject.Presentation     Razor Class Library (MudBlazor pages)
└── Host/
    ├── Wasm/        Cloudstrap.WasmTestProject.Host.Wasm        Blazor WebAssembly client
    └── Bff/         Cloudstrap.WasmTestProject.Host.Bff         ASP.NET Core server: serves the WASM app + API
test/
└── Cloudstrap.WasmTestProject.E2E.Tests                         NUnit 4 + Microsoft.Playwright (MTP executable)
```

## Running the app manually

```powershell
dotnet run --project src/Test/WasmTestProject/src/Host/Bff            # http profile: http://127.0.0.1:5300
dotnet run --project src/Test/WasmTestProject/src/Host/Bff -lp https  # https://localhost:7200
```

## Running the E2E tests

One-time browser install (also required on CI agents):

```powershell
pwsh src/Test/WasmTestProject/test/Cloudstrap.WasmTestProject.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Then build the solution (the fixture launches the Bff host with `--no-build`) and run the
test executable like any other MTP suite:

```powershell
dotnet build src/Cloudstrap.sln
src\Test\WasmTestProject\test\Cloudstrap.WasmTestProject.E2E.Tests\bin\Debug\net10.0\Cloudstrap.WasmTestProject.E2E.Tests.exe
```

Harness behavior (`E2eFixture` / `Infrastructure/`):

- Launches the Bff host once per test run on **`http://127.0.0.1:5300`** (`--no-launch-profile`,
  same build configuration as the test assembly) and kills the process tree afterwards.
  Port map: **5300** Bff · **5301–5303** second-instance tests · **5310** the test identity provider
  (`Cloudstrap.TestIdentityProvider`, hosted by the fixture on Kestrel loopback, started before the Bff
  and disposed after it) · **59999** the dead-port test.
- Set **`CLOUDSTRAP_E2E_BASEURL`** to attach to an already-running instance instead — the identity
  provider is still booted by the fixture in attach mode.
- Captures the SUT's stdout/stderr (`E2eFixture.CapturedSutOutput`) so tests can assert on
  console telemetry (OpenTelemetry Console exporter output).
- The Bff runs in **`AzureMonitor`** mode against a syntactically valid but unreachable connection
  string, so the Azure Monitor exporters are live while nothing is ever transmitted — the exporter
  retries in the background without disturbing the app. `EnableConsole` stays at its default, which
  is why the console-telemetry assertions above still work; `SamplingRatio` is pinned to `1.0` so
  no span is sampled away. Offline storage is disabled, so a run leaves no telemetry spool behind.
- The Bff calls **itself** through the `SelfApi` typed client (`Cloudstrap:HttpClients:SelfApi`,
  pointed at `http://127.0.0.1:5300/`), so a genuine outbound HTTP hop — correlation propagation and
  a dependency health check against a live peer — is demonstrable in one process. Readiness therefore
  depends on the app being able to reach itself; startup-scenario instances on other ports must either
  override that base address or expect `/ready` to report unhealthy.
- Browser tests inherit `PageTestBase` (headless Chromium, fresh context per test,
  console-error collection). API-level tests use plain `HttpClient` against `E2eFixture.BaseUrl`.
- A missing Chromium fails loudly with the install command — tests never silently skip.

## What each page demonstrates

| Page / endpoint | Package | E2E coverage |
|---|---|---|
| `/` Home | — (skeleton) | `HomePageTests` — app boots, WASM renders, no console errors |
| `/diagnostics` + `GET api/diagnostics/options` | Cloudstrap.Core (#1) | `DiagnosticsTests` — server-side binding (`AddCloudstrapCore`/`GetCloudstrapOptions`), client-side WASM binding (header badge), fail-fast startup validation |
| `/healthz` + `/ready` + `GET api/diagnostics/correlation` | Cloudstrap.Observability (#2) | `HealthAndCorrelationTests` — tagged health probes (`CloudstrapHealthCheckTags`), ambient correlation id (inbound header adopted, generated otherwise) |
| `/doctors` + `GET/POST api/doctor` | Cloudstrap.Observability (#2) | `DoctorsTests` — client→API round-trip; `AddDoctor` business span (`IBusinessTrace`) asserted in the captured console telemetry |
| `/diagnostics` mode badge + startup scenarios | Cloudstrap.Observability.AzureMonitor (#3) | `AzureMonitorTests` — exporter-contribution guard lifted (the host boots in `AzureMonitor` mode at all), fail-fast naming both connection-string sources, per-environment mode flip on unchanged code |
| `/healthz` + `/ready` (Cloudstrap-mapped) + `GET api/diagnostics/outbound` | Cloudstrap.Extensions (#4) | `ExtensionsTests` — typed-client outbound hop propagating the caller's correlation id, the `SelfApi-liveness` dependency check feeding readiness, and a second instance whose unreachable peer flips `/ready` to 503 while `/healthz` stays 200 |

| `GET api/v1/status` + `api/v2/status` · `/openapi/v{n}.json` · `/scalar` · `GET api/v1/status/boom` | Cloudstrap.WebApi (#5) | `WebApiTests` — versioned endpoints reporting `api-supported-versions`, one OpenAPI document per version with the unversioned controllers assigned the default version, the hardened problem-details error response with the caller's correlation id, the Scalar shell listing both documents, and the constant security headers on API and probe alike; `ScalarPageTests` — the reference UI loads in headless Chromium |
| `GET api/v1/machine/call` + `api/v1/machine/status` | Cloudstrap.Authentication.ClientCredentials (#9) + the test identity provider (D-5) | `ClientCredentialsTests` — the flagged `SelfApi` client transparently carries a bearer token issued by the loopback IdP into the one `[Authorize]` endpoint (#5 validates it against the IdP's real discovery document), the direct unauthenticated call gets 401, and two round trips reuse one cached token |
| `/account/login` + `/account/logout` + `GET api/v1/user/whoami` + `api/v1/user/call` | Cloudstrap.Authentication.OpenIdConnect (#10) + the test identity provider's interactive flows (D-4) | `OpenIdConnectTests` — a real Chromium signs in at the loopback IdP through auth-code + PKCE, the hardened `__Host-Cloudstrap` cookie is inspected in the browser, the user-flagged `UserApi` client calls the protected API *as that user*, logout ends both sessions, and the anonymous browser is challenged while the machine endpoint still answers 401 |

*(Extended by every deliverable — see `_plans/ROADMAP.md`.)*

### Harness notes for deliverable #5

- **The Bff's entire request pipeline is now one `UseCloudstrapWebApi` call.** The Blazor composition rides
  on its documented hook points: `BeforeRouting` carries `UseBlazorFrameworkFiles()` + `UseStaticFiles()`,
  and `ConfigureEndpoints` carries `MapFallbackToFile("index.html")`. The WASM app, the API, the probes and
  the SPA fallback all stay reachable — `HomePageTests` is the proof that the static-file branch survived.
- **The SUT stays effectively anonymous.** Since deliverable #9 the Bff *does* call `AddCloudstrapJwtBearer`,
  but with `Cloudstrap:JwtBearer:RequireAuthenticatedEndpoints: false` — #5's documented whole-application
  opt-out — so the 28 pre-#9 anonymous tests still exercise the AC-W10 posture unchanged. Only the
  machine endpoint (`api/v1/machine/status`, #9) and the user endpoints (`api/v1/user/*`, #10) opt back
  in with `[Authorize]`.
- **`Cloudstrap:WebApi:ExceptionHandling:IncludeDetails` is pinned to `false`** in `appsettings.json`. The
  E2E suite runs in `Development`, where the unset default would include exception detail; pinning it makes
  the *hardened* shape the one assertable here. `Error_WithIncludeDetailsEnabled_ReturnsTheExceptionDetail`
  starts a second short-lived instance with the switch flipped on to cover the other mode.
- **The Scalar assertions are shell-based**, not console-based: the reference UI pulls its JavaScript bundle
  from a CDN a CI agent may not reach, so `ScalarPageTests` asserts only that the page loads and has a
  title. Unlike `HomePageTests`, it makes no zero-console-errors assertion.
- **Each status version lives on its own controller**, so URL-segment versioning gives each one its own
  endpoint and `api-supported-versions` reports that endpoint's version alone. That both versions exist is
  proven by the v2 payload and by the two OpenAPI documents.

### Harness notes for deliverable #9

- **The Bff acquires machine tokens from the fixture-hosted identity provider on `http://127.0.0.1:5310`.**
  `Cloudstrap:HttpClients:SelfApi:AddClientAccessToken: true` plus `AddCloudstrapClientCredentials()` is the
  whole conversion — the `SelfApi` outbound hop now carries a bearer token everywhere, including to the
  anonymous endpoints the pre-#9 tests exercise (which simply ignore it). The `SelfApi-liveness` readiness
  probe client gets no token, by construction.
- **The configured `ClientSecret` is an obvious placeholder** (`local-e2e-placeholder-secret`) for the
  local-only test IdP, and only for it — real secrets belong in KeyVault, environment variables or
  user-secrets, never `appsettings.json`.
- **A manual `dotnet run` of the Bff without the IdP still boots** — token acquisition and the bearer's
  metadata retrieval are both lazy, so `/healthz` answers while `api/v1/machine/call` fails loudly naming
  the token endpoint until something listens on 5310. Second-instance startup-scenario tests likewise never
  need the IdP to be reachable.

### Harness notes for deliverable #10

- **Interactive login is one call plus configuration.** `AddCloudstrapOpenIdConnect()` +
  `Cloudstrap:OpenIdConnect` (client `wasmtestproject-web` at the same loopback IdP — no new port) and
  `MapCloudstrapAuthenticationEndpoints()` in the `ConfigureEndpoints` hook, mapped before the SPA
  fallback so the explicit routes win.
- **`Cloudstrap:OpenIdConnect:RequireAuthenticatedEndpoints: false`** is D-6's documented
  whole-application opt-out: the 31 pre-#10 anonymous E2E tests keep exercising their posture unchanged,
  and the two `api/v1/user/*` endpoints opt back in with `[Authorize]` (cookie scheme). The **cookie
  settings stay at their D-1 defaults on purpose** — the E2E run is the proof that `__Host-`/`Secure`
  cookies work in Chromium on the trustworthy loopback origin, over plain `http://127.0.0.1:5300`.
- **`api/v1/machine/status` is now pinned to the `Bearer` scheme**
  (`[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`) — the documented
  per-endpoint override. With a cookie/OIDC default scheme in the host, the pin keeps the #9 contract
  intact: a tokenless call answers **401**, never a login redirect. The #9 test file itself is untouched.
- **The `UserApi` client demonstrates AC-CC13 live**: flagged **both** `AddUserAccessToken` and
  `AddClientAccessToken` at the same base address as `SelfApi` — the signed-in user's token is the one
  that reaches the protected endpoint (its `subject` is the test user, its `clientId` the web client).
  `SelfApi` keeps its machine-only flag, so the anonymous diagnostics hops are undisturbed.
- **The `ClientSecret` is an obvious placeholder** (`local-e2e-placeholder-secret-web`) for the
  local-only test IdP — the same rule as #9's.
- **A manual `dotnet run` without the IdP still boots** — OIDC metadata retrieval is lazy, so `/healthz`
  answers while `/account/login` fails loudly naming the authority until something listens on 5310.
