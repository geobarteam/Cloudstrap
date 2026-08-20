---
description: "Use when writing, reviewing, or generating unit tests or integration tests. Covers MSTest conventions, AAA structure, Moq usage, integration test factory pattern, and test folder layout for Cloudstrap."
applyTo: "src/Test/**"
---
# Test Conventions — Cloudstrap

## Frameworks & Tooling
- **NUnit 4** + **Moq** for all tests, on **Microsoft.Testing.Platform** — each test project builds an executable (`OutputType=Exe`, `EnableNUnitRunner`, wired centrally in `src/Test/Directory.Build.props` for `*.Tests` projects only). `dotnet test` is NOT supported — run the built test `.exe` directly.
- `[TestFixture]`/`[Test]`/`[SetUp]`/`[TearDown]`, assertions via the `Assert.That` constraint model. No xUnit, no MSTest.
  *(Code samples below still show MSTest attributes from the source repo — translate to NUnit when applying them; the conventions themselves are unchanged.)*
- E2E tests additionally use **Microsoft.Playwright** (see *E2E tests — WASM SUT* below).
- Prioritize meaningful tests over chasing a coverage number.

## Project Layout

| Project | Contains |
|---|---|
| `Test/UnitTest/` | Unit tests (mirror source structure per library package) |
| `Test/E2E/` | `Cloudstrap.Demo.E2E.Tests` — Playwright E2E suite driving the demo apps |
| `Test/TestIdentityProvider/` | `Cloudstrap.TestIdentityProvider` — shared OpenIddict test IdP library |
| `../demo/` | The demo applications the E2E suite drives (Api, BlazorServer, BlazorWasm, Mvc, shared IdP host) |

Mirror the source folder structure inside each test project. Name test files `<ClassUnderTest>Tests.cs`.

## Naming
`<Method>_<Scenario>_<Expected>` — e.g. `Execute_WhenDoctorNameDuplicated_ReturnsError`

A failing test name should tell you *what broke* without reading the code.

## Structure — Arrange-Act-Assert (AAA)

Every test follows AAA with **one Act and one logical assertion** per test.

```csharp
[TestMethod]
public void Add_GivenTwoIntegers_ReturnsTheirSum()
{
    // Arrange
    var calculator = new Calculator();

    // Act
    int result = calculator.Add(2, 3);

    // Assert
    Assert.AreEqual(5, result);
}
```

Avoid logic (`if`, `for`, `switch`) inside tests. If you need multiple scenarios, use data-driven tests instead.

---

## Writing Testable Code

1. **Single Responsibility** — one reason to change per class/method.
2. **Dependency Injection** — inject dependencies; never `new` up infrastructure inside business logic.
3. **Depend on abstractions** — interfaces for boundaries (repositories, service clients). Enables mocking.
4. **Avoid static / singleton state** — except for pure utility/extension methods (see point 6).
5. **Favor composition over inheritance** — composed objects are easier to mock and test in isolation.
6. **Extract pure functions** — make side-effect-free logic `static`; test inputs → outputs directly.
7. **Keep constructors simple** — no logic that can fail or needs testing.

---

## Unit Tests — `src/Test/Unit/`

| Source layer | Test folder |
|---|---|
| `Core/Application/Functionalities/<Feature>/` | `Test/Unit/Application/` |
| `Core/Domain/` | `Test/Unit/Domain/` |
| `Presentation/ViewModels/` | `Test/Unit/Presentation/` |

### What to unit test (highest ROI)
- **Application Handlers** — orchestrate business logic; mock repositories and services.
- **Domain Objects** — business rules, invariants, validations.
- **ViewModels** — presentation logic decoupled from UI (MVVM pattern).

Do **not** unit-test trivial DTOs, auto-generated code, or infrastructure plumbing.

### Reference pattern

```csharp
[TestClass]
public sealed class GetDoctorsQueryTests
{
    private Mock<IDoctorRepository> _mockRepository = null!;
    private GetDoctorsQuery _query = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepository = new Mock<IDoctorRepository>();
        _query = new GetDoctorsQuery(_mockRepository.Object);
    }

    [TestMethod]
    public async Task Execute_WhenCalled_ReturnsAllDoctors()
    {
        // Arrange
        _mockRepository.Setup(r => r.ListAllAsync())
            .ReturnsAsync([new Doctor { Id = 1, Name = "Dr. Smith" }]);

        // Act
        var result = await _query.Execute();

        // Assert
        Assert.HasCount(1, result);
        _mockRepository.Verify(r => r.ListAllAsync(), Times.Once);
    }
}
```

### Key rules
- **Mock only at boundaries** (interfaces). No real DB, HTTP, or file-system calls.
- `[TestInitialize]` for per-test setup; avoid `[ClassInitialize]` in unit tests.
- Prefer helper/factory methods over complex `[TestInitialize]` when setup varies between tests.
- Every bugfix starts with a **failing regression test** (RED step).
- Mark test classes `sealed` when they are not a base class.
- **One Act per test**. If a test needs multiple acts, split it.

---

## Mocking — Use Wisely

Mock infrastructure boundaries (repositories, service clients); do **not** mock the class under test or its value objects.

**Pitfalls of over-mocking:**
- **False security** — tests pass but real integration fails.
- **Brittle tests** — break on internal refactors even when behavior is unchanged.
- **Hides design problems** — excessive mocks often signal high coupling.

---

## DateTime Mocking

Use the built-in `TimeProvider` abstraction (not a custom `IClockService` interface).
Production code injects `TimeProvider`; tests supply `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.

```csharp
[TestMethod]
public void IsExpired_ReturnsTrue_WhenPastExpiration()
{
    // Arrange
    var fakeTime = new FakeTimeProvider();
    fakeTime.SetUtcNow(new DateTimeOffset(2026, 3, 13, 10, 0, 0, TimeSpan.Zero));
    var service = new MyService(fakeTime);

    // Act
    var result = service.IsExpired(new DateTimeOffset(2026, 3, 13, 9, 0, 0, TimeSpan.Zero));

    // Assert
    Assert.IsTrue(result);
}
```

---

## Data-Driven Tests

Use `[DataTestMethod]` + `[DataRow]` to run the same test with multiple inputs. Keeps tests declarative, avoids loops and conditionals.

```csharp
[TestClass]
public sealed class CalculatorTests
{
    private Calculator _calculator = null!;

    [TestInitialize]
    public void Setup() => _calculator = new Calculator();

    [DataTestMethod]
    [DataRow(1, 1, 2)]
    [DataRow(2, 2, 4)]
    [DataRow(3, 3, 6)]
    [DataRow(-1, -1, -2)]
    public void Add_NumbersAsParameters_AddsNumbersCorrectly(int a, int b, int expected)
    {
        // Act
        int result = _calculator.Add(a, b);

        // Assert
        Assert.AreEqual(expected, result);
    }
}
```

For complex or reusable data sets, use `[DynamicData]` pointing to a static property or method instead of `[DataRow]`.

---

## Integration / Service Tests — `src/Test/Integration/`

Test the **real HTTP pipeline** — routing, middleware, DI, serialization, auth, and responses — via `WebApplicationFactory`. Keep business-logic coverage mainly in unit tests; integration tests verify that the layers work together.

### Principles

- **Keep the app host as real as possible.** Replace only true external dependencies (e.g. `IMessagingService`, third-party HTTP clients). Do not over-mock the application.
- **Fake authentication, keep real authorization.** Register a custom test auth scheme in `ConfigureTestServices` that produces a `ClaimsPrincipal` with the claims, roles, and scopes you need — but let the real authorization middleware and policies run. Do **not** disable authorization.
- **Mock external services** — never call real third-party APIs from tests.

### Database strategy

| Option | When to use |
|--------|-------------|
| **SQLite in-memory** | **Recommended default** — fast, lightweight, enforces FK constraints and basic SQL semantics |
| **Real database engine** (SQL Server LocalDB / Docker) | When tests rely on SQL Server–specific features (stored procedures, specific data types) |
| **EF Core InMemory provider** | **Do NOT use** — it does not enforce constraints, relationships, or SQL semantics |

`CustomWebApplicationFactory` defaults to **SQLite in-memory**. For backends that require SQL Server features, make the DB configurable: SQL Server locally, SQLite on CI.

### Folder structure

```
Test/Integration/
  Infrastructure/
    CustomWebApplicationFactory.cs      # bootstraps DB + test auth scheme
    CloudstrapDbContextExtensions.cs      # SeedDatabase()
  Endpoints/
    <Feature>/                          # one folder per controller / endpoint group
  Basic/                                # infrastructure health / smoke tests
```

### Authentication & authorization testing

Register a test auth scheme in `CustomWebApplicationFactory.ConfigureTestServices`:

```csharp
services.AddAuthentication("TestScheme")
    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("TestScheme", _ => { });
```

Build test identities with claims, roles, scopes, and tenant info per test scenario. Verify these three cases for every protected endpoint:

| Scenario | Expected |
|----------|----------|
| Anonymous (no auth header) | `401 Unauthorized` |
| Authenticated without required permission/role | `403 Forbidden` |
| Authenticated with correct permission/role | Success (2xx) |

### Real auth coverage

Keep a **small set** of end-to-end tests that use the real IdP (STS) or real token flow. Use them to validate: issuer, audience, claim mapping, scopes, roles, login flow, callback, and logout. These are not part of the regular integration suite — run them selectively (e.g. via `[TestCategory("E2E")]`).

### Authorization design

- Prefer **policy-based** and **claim-based** authorization.
- Centralize policies (e.g. in a shared `AuthorizationPolicies` class or the DI registration).
- Avoid scattered inline authorization checks.

### Reference pattern

```csharp
[TestClass]
[TestCategory("Integration")]
public sealed class GetDoctorsTest : IDisposable
{
    private readonly CustomWebApplicationFactory _factory = new();
    private readonly HttpClient _client;

    public GetDoctorsTest() => _client = _factory.CreateClient();

    [TestMethod]
    public async Task Get_WithExistingDoctors_ReturnsOkWithDoctorList()
    {
        // Arrange
        await _factory.SeedTestDataAsync();

        // Act
        var response = await _client.GetAsync("/api/doctor");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var doctors = await response.Content.ReadFromJsonAsync<IEnumerable<DoctorDto>>();
        Assert.IsNotNull(doctors);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
```

### Key rules
- `CustomWebApplicationFactory` replaces the DB with **SQLite in-memory** (see Database strategy above) and registers a test auth scheme with configurable claims.
- Seed via `_factory.SeedTestDataAsync()` → calls `CloudstrapDbContextExtensions.SeedDatabase()`.
- Override services with `ConfigureTestServices` **only for true external dependencies** (e.g. `IMessagingService`, external HTTP clients).


---

## UI Tests (BUnit) — Optional Layer

For Blazor front-ends, use **BUnit** to test Razor components in isolation. Write tests in `.cs` files (not `.razor` files) for full MSTest compatibility (`[TestInitialize]`, data-driven attributes, etc.).

Focus BUnit tests on **rendering and interaction behavior**. Mock the ViewModel to isolate UI logic — the ViewModel itself is already covered by unit tests.

Start with ViewModel unit tests (cheaper, faster, more stable), then add targeted BUnit tests for critical UI interactions.

---

## Testing Pyramid Summary

| Layer | Description | Automation |
|---|---|---|
| **Unit** (base) | Fast, isolated, most numerous | Every build |
| **UI / BUnit** | Optional; complements ViewModel tests | Every build |
| **Integration** | `WebApplicationFactory` + SQLite; real pipeline, fake auth, real authz | PR → MAIN |
| **E2E / Real Auth** | Small set with real IdP — validates issuer, audience, claim mapping, login flow | Selective |
| **Manual E2E** | Exploratory + smoke tests by testers | Not automated (pending IaC) |

---

## Manual E2E — Test Data

When a tester validates a feature through the UI, the application must contain realistic test data for the scenario under test. Before manual validation:

1. **Check if relevant test data exists** in the database for the feature being validated.
2. **If it does not exist, generate and seed it** — add seed data to the appropriate migration or seed script so it is reproducible across environments.
3. Keep test data **realistic and representative** (use plausible names, dates, and values — not `"test123"` or `"foo"`).
4. Document the required test data per feature in the test plan or README so testers know what to expect.

---

## E2E tests — demo apps (`src/demo`)

Every extraction deliverable ends with a demonstration slice here (CLAUDE.md workflow rule 9, planner rule 15): extend the appropriate demo app (page/endpoint/config) and add ≥ 1 E2E test proving the behavior through the **real running app**.

**Project**: `src/Test/E2E/Cloudstrap.Demo.E2E.Tests/` — NUnit 4 + Microsoft.Playwright, part of the normal MTP test leg (locally and in CI).

**Harness** (`E2eFixture` + `Infrastructure/`):
- `E2eFixture` (`[SetUpFixture]`) boots once per run: the demo IdP (in-process, 5310), the Api host (5330, polled on `/healthz`), then the Bff (`http://127.0.0.1:5300`) — `dotnet run --no-build`, so build the solution first; process trees are killed afterwards. `CLOUDSTRAP_E2E_BASEURL` attaches to an already-running Bff instead (IdP + Api stay fixture-booted). The Mvc and BlazorServer fixtures boot their own hosts by project path.
- Browser tests inherit `Infrastructure/PageTestBase` — headless Chromium, fresh context per test, `ConsoleErrors` collection; prefer `data-testid` selectors (`Page.GetByTestId`).
- API-level tests use plain `HttpClient` against `E2eFixture.BaseUrl` — no browser needed.
- Telemetry assertions poll `E2eFixture.CapturedSutOutput` (the SUT's stdout, OTel Console exporter) with a deadline — never a bare sleep.
- `SutProcess.Start(baseUrl, applicationArguments, projectRelativePath)` launches extra short-lived host instances (different port, any demo project by repo-relative path) for startup/fail-fast scenarios.
- Suite is `[assembly: NonParallelizable]` (fixed ports). Tests must not depend on run order — the in-memory store lives for the whole run.
- One-time setup: `pwsh <E2E bin>/playwright.ps1 install chromium`. A missing browser fails loudly with that instruction — tests never silently skip.
- After adding a demo, update the feature matrix in the extended app's README under `src/demo`.

## Run Commands

```powershell
# Build first, then run the MTP executables directly (dotnet test is not supported)
dotnet build src/Cloudstrap.sln
src\Test\UnitTest\Cloudstrap.Core.Tests\bin\Debug\net10.0\Cloudstrap.Core.Tests.exe
src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe
<TestExePath> --filter "<TestMethod>"                          # filtered run
```
