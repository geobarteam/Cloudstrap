---
name: add-public-api
description: "Add a new public type, interface, or method to the Cloudstrap library with full test coverage and XML documentation. Follows Red-Green-Refactor per step with HUMAN GATE. Use when adding a new public contract, implementation, extension method, or options class to the library's API surface."
argument-hint: "What to add, e.g. 'ITokenValidator interface with ValidateAsync method'"
---

# Add Public API — Implementation Skill

Add a new public type, interface, or method to the library with full test coverage, XML documentation, and DI registration.

## Prerequisites

- Know **what** to add: the public type name, its purpose, and its expected API surface.
- Identify a **reference feature** in the codebase — the existing feature whose patterns you will follow. If none specified, **ask the user** which existing feature to use. Study it first.
- If ≥ 3 files are involved, a `_plans/<FeatureName>.md` should exist and be approved. For smaller additions (single type + test), proceed directly.

## Mandatory Workflow Per Step

```
1. READ — understand what to add and which reference feature to follow
2. RED — write a failing test FIRST
3. RUN — dotnet test --project src/Test/Unit/ --filter "<TestMethod>" → confirm FAIL
4. GREEN — write minimal production code to pass
5. RUN — dotnet test --project src/Test/Unit/ --filter "<TestMethod>" → confirm PASS
6. REFACTOR — clean up, ensure XML docs are complete on all public members
7. CODE ANALYSIS — fix all violations:
   a. dotnet build src/Cloudstrap.sln 2>&1 | Select-String -Pattern ": (warning|error) (SA|SX|CA|CS|MSTEST)\d+" | Sort-Object | Get-Unique
   b. dotnet format src/Cloudstrap.sln
   c. Fix remaining violations manually
   d. Repeat a–c until clean
8. PROVE:
   - dotnet build src/Cloudstrap.sln → zero warnings/errors
   - dotnet test --project src/Test/Unit/ → all pass
   - dotnet format src/Cloudstrap.sln --verify-no-changes → exit 0
9. 🛑 STOP — present results, wait for user approval
10. MARK DONE — update _plans/ checkboxes if applicable
```

**Never batch steps. Never skip RED. Never skip CODE ANALYSIS. Never proceed past 🛑 without user confirmation.**

---

## Workflow Steps

### Step 1 — Define the Contract

Create the public interface or abstract class with full XML documentation.

**Location**: `src/Cloudstrap/Abstractions/` or `src/Cloudstrap/<Feature>/`

```csharp
namespace Cloudstrap.<Feature>;

/// <summary>
/// Describe the contract's purpose.
/// </summary>
public interface I<Name>
{
    /// <summary>
    /// Describe the method.
    /// </summary>
    /// <param name="input">Describe the parameter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Describe the return value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
    Task<Result> ProcessAsync(string input, CancellationToken ct);
}
```

**Rules**:
- `public` only for the intended API surface — everything else `internal`.
- Every public member gets `<summary>`, `<param>`, `<returns>`, `<exception>`.
- `CancellationToken` as the last parameter on every async method.
- Seal classes by default unless designed for inheritance.

**Test**: Verify the contract shape — compilation, nullability, interface members exist.

---

### Step 2 — RED: Write Failing Test

Write a unit test that exercises the new API through its interface.

**Location**: `src/Test/Unit/<Feature>/`

```csharp
namespace Cloudstrap.Test.Unit.<Feature>;

[TestClass]
public sealed class <Name>Tests
{
    [TestMethod]
    public async Task ProcessAsync_ValidInput_ReturnsSuccess()
    {
        // Arrange
        var sut = new <Implementation>(/* dependencies */);

        // Act
        var result = await sut.ProcessAsync("valid-input", CancellationToken.None);

        // Assert
        Assert.IsTrue(result.IsSuccess);
    }
}
```

**Rules**:
- Test method naming: `<Method>_<Scenario>_<Expected>`.
- AAA structure (Arrange/Act/Assert).
- Mock at boundary (interfaces only, Moq). No real external services.
- Run the test → confirm it **fails** (RED).

---

### Step 3 — GREEN: Implement

Write the minimal implementation to make the test pass.

**Location**: `src/Cloudstrap/<Feature>/`

```csharp
namespace Cloudstrap.<Feature>;

/// <summary>
/// Describe the implementation.
/// </summary>
internal sealed class <Name>(/* dependencies */) : I<Name>
{
    /// <inheritdoc />
    public async Task<Result> ProcessAsync(string input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        // Implementation
        return Result.Success();
    }
}
```

**Rules**:
- `internal sealed` by default — consumers use the interface.
- Guard clauses on all public method parameters.
- Run the test → confirm it **passes** (GREEN).

---

### Step 4 — REFACTOR

- Ensure XML docs are complete on all public members.
- Ensure naming follows the project's conventions (check the reference feature).
- No unnecessary dependencies.

---

### Step 5 — DI Registration

Add the new type to `ServiceCollectionExtensions` if consumers need to resolve it via DI.

**Location**: `src/Cloudstrap/Extensions/ServiceCollectionExtensions.cs`

```csharp
/// <summary>
/// Adds <see cref="I<Name>"/> to the service collection.
/// </summary>
public static IServiceCollection Add<Feature>(
    this IServiceCollection services,
    Action<<Feature>Options>? configure = null)
{
    if (configure is not null)
        services.Configure(configure);

    services.AddSingleton<I<Name>, <Name>>();
    return services;
}
```

**Test**: Integration test verifying DI resolution:

```csharp
[TestMethod]
public void Add<Feature>_RegistersServices()
{
    // Arrange
    var services = new ServiceCollection();

    // Act
    services.Add<Feature>();
    var provider = services.BuildServiceProvider();

    // Assert
    Assert.IsNotNull(provider.GetRequiredService<I<Name>>());
}
```

---

### Step 6 — Options Class (if applicable)

If the feature needs configuration, create an options class:

**Location**: `src/Cloudstrap/<Feature>/<Feature>Options.cs`

```csharp
namespace Cloudstrap.<Feature>;

/// <summary>
/// Configuration options for <see cref="I<Name>"/>.
/// </summary>
public sealed class <Feature>Options
{
    /// <summary>
    /// Gets or sets the description of the option.
    /// </summary>
    public string Setting { get; set; } = "default";
}
```

---

### Step 7 — CODE ANALYSIS + PROOF + 🛑 HUMAN GATE

1. Fix all SA/CA/CS violations.
2. Run the full proof trilogy: build → test → format.
3. Present results to the user.
4. **🛑 STOP and wait for approval.**
