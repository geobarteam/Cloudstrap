---
description: "Use when writing, reviewing, or generating tests in Cloudstrap. Covers the NUnit 4 conventions, AAA structure, Moq usage, the host-level test pattern, the E2E harness and the test folder layout. The order in which tests are written and which tests are worth writing are in .claude/rules/testing-for-agents.md; the test levels are in CLAUDE.md § Project settings § Test levels."
applyTo: "src/Test/**"
---
# Test Conventions — Cloudstrap

The order of the tests (integration test first, then the unit tests the logic needs) and the rules T-1…T-13 / R-1…R-7 are in `.claude/rules/testing-for-agents.md`; the test levels are in `CLAUDE.md` § Project settings § Test levels. This file holds only the project's own conventions.

## Frameworks & Tooling
- **NUnit 4** + **Moq** for all tests, on **Microsoft.Testing.Platform** — each test project builds an executable (`OutputType=Exe`, `EnableNUnitRunner`, wired centrally in `src/Test/Directory.Build.props` for `*.Tests` projects only). `dotnet test` is NOT supported — run the built test `.exe` directly.
- `[TestFixture]`/`[Test]`/`[TestCase]`/`[TestCaseSource]`/`[SetUp]`/`[TearDown]`, assertions via the `Assert.That` constraint model. No xUnit, no MSTest.
- Blazor component tests use **bUnit**; E2E tests additionally use **Microsoft.Playwright**.
- Time: production code injects `TimeProvider`; tests supply `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) — never a custom clock interface.

## Project Layout

| Project | Contains |
|---|---|
| `src/Test/UnitTest/Cloudstrap.<Package>.Tests/` | One project per package: unit tests **and** the package's host-level integration tests (`Infrastructure/<Package>TestHost.cs`) |
| `src/Test/TestInfrastructure/Cloudstrap.TestInfrastructure/` | Shared test helpers (`CloudstrapTestHost`, `SqlServerTestDatabase`, `InvocationRecorder`); test-only, never packed |
| `src/Test/TestIdentityProvider/Cloudstrap.TestIdentityProvider/` | Shared OpenIddict test IdP library (in-process / loopback) |
| `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/` | Playwright E2E suite driving the demo apps |
| `src/demo/` | The demo applications the E2E suite drives (Api, BlazorServer, BlazorWasm, Mvc, Worker, shared IdP host) |

Mirror the source folder structure inside each test project. Name test files `<ClassUnderTest>Tests.cs`; mark test classes `sealed` when they are not a base class.

## Naming
`<Method>_<Scenario>_<Expected>` — e.g. `Track_WithoutConsent_DoesNothing`. A failing test name should tell you *what broke* without reading the code. A test that proves an acceptance criterion carries its id (`AC-W1`) in its XML doc `<summary>`, or `[Property("AC", "AC-3")]` where a filterable trait is wanted.

## Structure — Arrange-Act-Assert (AAA)

One Act and one logical assertion per test; no `if`/`for`/`switch` in a test — a second scenario is a `[TestCase]` row or a second test.

```csharp
[Test]
public void Add_GivenTwoIntegers_ReturnsTheirSum()
{
    // Arrange
    var calculator = new Calculator();

    // Act
    int result = calculator.Add(2, 3);

    // Assert
    Assert.That(result, Is.EqualTo(5));
}

[TestCase(1, 1, 2)]
[TestCase(-1, -1, -2)]
public void Add_NumbersAsParameters_AddsNumbersCorrectly(int a, int b, int expected)
{
    Assert.That(new Calculator().Add(a, b), Is.EqualTo(expected));
}
```

## Mocking
- Mock only at boundaries (interfaces, Moq): token providers, external HTTP, Azure SDK clients, time. Never the class under test, a value object, a DTO or the package's own handlers.
- No real external services in tests (no live Azure, no live Application Insights, no network). Messaging tests run on Wolverine's in-memory/local transport.

## Host-level integration tests (the reference: `Cloudstrap.WebApi.Tests`)
- Each package's `Infrastructure/<Package>TestHost` boots a real ASP.NET Core / generic host in process (`TestServer`), registers the package through its public `AddCloudstrap*` / `UseCloudstrap*` entry points on the real DI container, and the test asserts through HTTP or the resolved services.
- Replace only true externals (IdP, Azure, network). SQL Server and Azurite go through `Cloudstrap.TestInfrastructure` (`SqlServerTestDatabase`); the IdP is `Cloudstrap.TestIdentityProvider`.
- **Fake authentication only where the IdP is not under test, keep real authorization.** For a protected endpoint cover: anonymous → `401`, authenticated without the required permission → `403`, authenticated with it → `2xx`.
- Test data is neutral and realistic — never a real person, address or secret.

## E2E tests — demo apps (`src/demo`)

**Project**: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/` — NUnit 4 + Microsoft.Playwright, part of the normal MTP test run (locally and in CI). Which demo app demonstrates which kind of feature: `CLAUDE.md` § Extraction Phase.

**Harness** (`E2eFixture` + `Infrastructure/`):
- `E2eFixture` (`[SetUpFixture]`) boots once per run: the demo IdP (in-process, 5310), the Api host (5330, polled on `/healthz`), then the Bff (`http://127.0.0.1:5300`) — `dotnet run --no-build`, so build the solution first; process trees are killed afterwards. `CLOUDSTRAP_E2E_BASEURL` attaches to an already-running Bff instead (IdP + Api stay fixture-booted). The Mvc, BlazorServer and Worker tests boot their own hosts by project path.
- Browser tests inherit `Infrastructure/PageTestBase` — headless Chromium, fresh context per test, `ConsoleErrors` collection; prefer `data-testid` selectors (`Page.GetByTestId`).
- API-level tests use plain `HttpClient` against `E2eFixture.BaseUrl` — no browser needed.
- Telemetry assertions poll `E2eFixture.CapturedSutOutput` (the SUT's stdout, OTel Console exporter) with a deadline — never a bare sleep.
- `SutProcess.Start(baseUrl, applicationArguments, projectRelativePath)` launches extra short-lived host instances (different port, any demo project by repo-relative path) for startup/fail-fast scenarios.
- Suite is `[assembly: NonParallelizable]` (fixed ports). Tests must not depend on run order — the in-memory store lives for the whole run.
- One-time setup: `pwsh src/Test/E2E/Cloudstrap.Demo.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`. A missing browser fails loudly with that instruction — tests never silently skip.
- After adding a demo, update the feature matrix in the extended app's README under `src/demo`.

## Run Commands

```bash
# Build first, then run the MTP executables directly (dotnet test is not supported)
dotnet build src/Cloudstrap.sln -v q -nologo
src/Test/UnitTest/Cloudstrap.WebApi.Tests/bin/Debug/net10.0/Cloudstrap.WebApi.Tests.exe --filter "FullyQualifiedName~WebApiDefaultsTests"
src/Test/E2E/Cloudstrap.Demo.E2E.Tests/bin/Debug/net10.0/Cloudstrap.Demo.E2E.Tests.exe
```

The full set of test executables is in `.claude/spec-driven.json` (`verify.commands`); `node .claude/hooks/verify.mjs` runs them.
