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
- Browser tests inherit `PageTestBase` (headless Chromium, fresh context per test,
  console-error collection). API-level tests use plain `HttpClient` against `E2eFixture.BaseUrl`.
- A missing Chromium fails loudly with the install command — tests never silently skip.

## What each page demonstrates

| Page / endpoint | Package | E2E coverage |
|---|---|---|
| `/` Home | — (skeleton) | `HomePageTests` — app boots, WASM renders, no console errors |
| `/diagnostics` + `GET api/diagnostics/options` | Cloudstrap.Core (#1) | `DiagnosticsTests` — server-side binding (`AddCloudstrapCore`/`GetCloudstrapOptions`), client-side WASM binding (header badge), fail-fast startup validation |

*(Extended by every deliverable — see `_plans/ROADMAP.md`.)*
