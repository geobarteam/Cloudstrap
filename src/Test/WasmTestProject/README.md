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
- Set **`CLOUDSTRAP_E2E_BASEURL`** to attach to an already-running instance instead.
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

*(Extended by every deliverable — see `_plans/ROADMAP.md`.)*

### Harness notes for deliverable #5

- **The Bff's entire request pipeline is now one `UseCloudstrapWebApi` call.** The Blazor composition rides
  on its documented hook points: `BeforeRouting` carries `UseBlazorFrameworkFiles()` + `UseStaticFiles()`,
  and `ConfigureEndpoints` carries `MapFallbackToFile("index.html")`. The WASM app, the API, the probes and
  the SPA fallback all stay reachable — `HomePageTests` is the proof that the static-file branch survived.
- **The SUT is anonymous by design.** It deliberately never calls `AddCloudstrapJwtBearer`, which is exactly
  the AC-W10 scenario: the pipeline must not assume authentication exists. Auth demonstrations arrive with
  deliverables #9/#10, which bring an identity provider to demonstrate against.
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
