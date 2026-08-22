# Cloudstrap Demo Applications

Consumer-facing example apps for the Cloudstrap suite — and the maintainers' end-to-end test
bed. Every delivered Cloudstrap package demonstrates its headline behavior in one of these apps,
proven by an end-to-end test that drives the real running app (headless Chromium for the browser
flows). The demo apps reference the packages by `ProjectReference`; a consumer copies the same
calls with `PackageReference Cloudstrap.*` instead — nothing else changes.

## Architecture

```
┌──────────────────────────────┐            ┌───────────────────────────────────────────┐
│ Shared/IdentityProvider       │            │ Browser                                    │
│ http://127.0.0.1:5310         │◄── login ──┤  WASM SPA (BlazorWasm/Client, served by    │
│ demo-only seeded test IdP     │    form    │  the Bff)  ·  __Host-Cloudstrap cookie     │
│  · demo-bff (machine)         │            └──────────────┬────────────────────────────┘
│  · demo-web (interactive)     │                           │ cookie (SameSite=Lax)
│  · demo-blazorserver          │                           ▼
│  · user geobarteam/password   │            ┌───────────────────────────────────────────┐
└──────────────┬───────────────┘            │ BlazorWasm/Bff  http://127.0.0.1:5300      │
               │ tokens                      │ cookie OIDC + JWT bearer + M2M clients     │
               │                             │  · SelfApi  → itself   [machine token]     │
               ▼                             │  · UserApi  → the Api  [user token]        │
┌──────────────────────────────┐            └──────────────┬────────────────────────────┘
│ BlazorServer                  │                           │ Authorization: Bearer (user)
│ http://127.0.0.1:5340         │                           ▼
│ OIDC login + DemoApi client ──┼──────────► ┌───────────────────────────────────────────┐
└──────────────────────────────┘   user      │ Api  http://127.0.0.1:5330                 │
                                   token     │ pure JWT host — authenticated by DEFAULT   │
┌──────────────────────────────┐            │  · GET api/v1/downstream/whoami            │
│ Mvc  http://127.0.0.1:5320    │            │  · /healthz anonymous (probe carve-out)    │
│ two-call MVC consumer example │            └───────────────────────────────────────────┘
│ (anonymous by design)         │
└──────────────────────────────┘
```

## Port map

| Port | Host | Owner |
|---|---|---|
| 5300 | BlazorWasm Bff | E2E fixture (or `dotnet run`) |
| 5301–5304 | Bff second-instance startup scenarios | individual E2E tests |
| 5310 | shared demo IdP | E2E fixture (in-process) / the IdP host project |
| 5311 | IdP host instance | `SelfHostedIdentityProviderTests` |
| 5320 | Mvc demo | `MvcHostTests` (or `dotnet run`) |
| 5330 | Api demo | E2E fixture (or `dotnet run`) |
| 5340 | BlazorServer demo | `BlazorServerTests` (or `dotnet run`) |
| 5350 | Worker demo (health listener) | `WorkerHostTests` (or `dotnet run`) |
| 59999 | dead-port test | `ExtensionsTests` |

All ports are launch-profile/configuration defaults — override with `ASPNETCORE_URLS` and
`Cloudstrap:HttpClients:*:BaseAddress`; the IdP's redirect URIs follow
`Demo:ApplicationBaseAddresses`.

## Layout

```
src/demo/
├── Api/            Cloudstrap.Demo.Api                 pure JWT API host — hardened by default (README inside)
├── BlazorServer/   Cloudstrap.Demo.BlazorServer        stock Blazor Server + OIDC + user-token client (README inside)
├── BlazorWasm/
│   ├── Bff/        Cloudstrap.Demo.BlazorWasm.Bff      serves the WASM app + cookie/JWT API surface
│   ├── Client/     Cloudstrap.Demo.BlazorWasm.Client   Blazor WebAssembly client
│   └── Presentation/  …BlazorWasm.Presentation         Razor Class Library (MudBlazor pages)
├── Mvc/            Cloudstrap.Demo.Mvc                 two-call server-rendered MVC example (README inside)
├── Worker/         Cloudstrap.Demo.Worker              headless worker + truthful health listener (README inside)
└── Shared/
    ├── Contracts/  Cloudstrap.Demo.Contracts           DTOs shared across the demo apps
    └── IdentityProvider/  Cloudstrap.Demo.IdentityProvider  demo-only seeded IdP host (README inside)

src/Test/E2E/Cloudstrap.Demo.E2E.Tests                  NUnit 4 + Microsoft.Playwright (MTP executable)
```

Each demo app's README carries its feature matrix: which package call it demonstrates and which
E2E test proves it.

## Running the apps manually

One F5 in VS Code: the compound launch configuration **"Demo apps (all hosts + IdP)"** boots the
IdP first, then the Api, Mvc, BlazorServer and Bff hosts (stopping the session stops all). Per
app, in a terminal:

```powershell
dotnet run --project src/demo/Shared/IdentityProvider    # the demo IdP — start it first: 5310
dotnet run --project src/demo/Api                        # 5330
dotnet run --project src/demo/Mvc                        # 5320 (needs no other process)
dotnet run --project src/demo/Worker                     # health on 5350 (needs no other process)
dotnet run --project src/demo/BlazorServer               # 5340
dotnet run --project src/demo/BlazorWasm/Bff             # 5300  (-lp https → https://localhost:7200)
```

Sign in anywhere as **`geobarteam` / `password`** — the placeholder demo user seeded by
`TestIdentityProviderSeed`. Every host boots gracefully without its peers: probes stay green and
anonymous pages serve, while token acquisition/validation fails loudly naming the missing peer
(metadata retrieval is lazy). Two readiness couplings are deliberate: the Bff's `/ready` probes
its `SelfApi` (itself) **and** its `UserApi` peer (the Api host) — a standalone Bff run reports
`/ready` unhealthy until the Api listens, or override `Cloudstrap:HttpClients:UserApi`.

**Placeholder-credentials rule**: every secret under `src/demo` is a visibly fake
`local-e2e-placeholder-*` value for the loopback-only demo IdP. Real secrets come from KeyVault,
environment variables or user-secrets — never `appsettings.json`.

## Running the E2E tests

One-time browser install (also required on CI agents):

```powershell
pwsh src/Test/E2E/Cloudstrap.Demo.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

Then build the solution (the fixture launches hosts with `--no-build`) and run the executable
like any other MTP suite:

```powershell
dotnet build src/Cloudstrap.sln
src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe
```

Harness behavior (`E2eFixture` / `Infrastructure/`):

- **Boot order: IdP → Api → Bff.** The fixture hosts the seeded IdP in-process on 5310 (keeping
  the token-request counter the caching test asserts), boots the Api host on 5330
  (readiness-polled on `/healthz`), then the Bff on 5300 — and disposes in reverse. The Mvc and
  BlazorServer fixtures boot their own hosts by project path.
- Set **`CLOUDSTRAP_E2E_BASEURL`** to attach to an already-running Bff instead — the IdP and the
  Api host are still fixture-booted in attach mode.
- Captures each launched host's stdout/stderr (`E2eFixture.CapturedSutOutput` for the Bff) so
  tests can assert on console telemetry (OpenTelemetry Console exporter output).
- Observability modes are deliberately varied so every mode runs somewhere: the Bff boots in
  **AzureMonitor** mode (unreachable connection string — exporters live, nothing transmitted,
  console telemetry still captured), the Api in **Console** mode, the BlazorServer app in
  **Otlp** mode (no collector required to boot).
- Browser tests inherit `PageTestBase` (headless Chromium, fresh context per test, console-error
  collection); API-level tests use plain `HttpClient`. A missing Chromium fails loudly with the
  install command — tests never silently skip.
