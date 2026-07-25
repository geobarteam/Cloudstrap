---
name: fix-violations
description: "Use when fixing compiler diagnostics and analyzer findings with modern .NET and C# best practices. Prefer the native compiler, SDK analyzers, and idiomatic code — this repo uses no StyleCop."
metadata:
  argument-hint: "Warning code or symptom, e.g. 'CA1859', 'nullable warning', 'use collection expressions'"
---

# Fix Violations — Modern .NET Code Analysis

Use this skill when you need to clean up warnings and analyzer violations in a modern .NET codebase. The default approach is to trust the native C# compiler, the .NET SDK analyzers, and broadly accepted community practices. This repo intentionally has **no StyleCop** — style is enforced by `.editorconfig` severities (`EnforceCodeStyleInBuild`) and `dotnet format`.

## Core principles

- Warnings are errors: `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, and `EnforceCodeStyleInBuild` are set in `Directory.Build.props` (fixed — do not modify).
- Prefer the compiler and SDK analyzers over custom style rule churn.
- Favor modern C# and .NET idioms: nullable reference types, `required` members, pattern matching, switch expressions, collection expressions, `using` declarations, and `await using`.
- Prefer clarity and correctness over unnecessary abstraction.
- Only suppress a warning when there is a strong, documented reason.

## Preferred workflow

1. Build (warnings are already promoted to errors via `Directory.Build.props`):

```powershell
dotnet build src/Cloudstrap.sln
```

2. Apply formatting and style fixes:

```powershell
dotnet format src/Cloudstrap.sln
```

3. Review the remaining diagnostics and fix them in this order:
   - compiler errors and warnings
   - nullable warnings
   - SDK analyzer findings (`CA*`, `IDE*`)
   - test analyzer findings (`NUnit*`)
   - formatting issues

4. Re-run the build and verify the workspace is clean.

## Modern rule families

### Compiler and nullable issues

| Warning | Typical fix |
|--------|-------------|
| `CS8600`, `CS8602`, `CS8604`, `CS8618` | Fix nullability with guards, annotations, `required`, `?`, `!`, or better null-handling flow. |
| `CS0618` | Replace the obsolete API with the supported alternative. |
| `CS1998` | Remove `async` when no await is used, or add the missing await. |
| `CS4014` | Await the task or explicitly ignore the result when intentional. |

### Common .NET analyzer rules

| Rule | Fix |
|------|-----|
| `CA1001` | Ensure disposable types are properly disposed. |
| `CA1031` | Avoid broad catch blocks; catch specific exceptions or rethrow appropriately. |
| `CA1305` | Use culture-aware formatting and avoid implicit culture-sensitive conversions. |
| `CA1307` | Specify `StringComparison` for string comparisons. |
| `CA1310` | Prefer `string.Contains`/`StartsWith` with `StringComparison` rather than culture-sensitive overloads. |
| `CA1508` | Simplify complex conditional logic or extract helper methods. |
| `CA1822` | Mark helpers as `static` when they do not use instance state. |
| `CA1848` | Use `LoggerMessage`-style logging patterns for high-volume logging paths when applicable. |
| `CA1859` | Prefer concrete types over interface/abstract types for locals, fields, and private returns. |
| `CA1860` | Prefer `Count`/`Length`/`IsEmpty` checks over `Enumerable.Any()`. |
| `CA1861` | Avoid passing constant arrays as arguments; extract them to a `static readonly` field or use a collection expression. |
| `CA2000` | Dispose objects created by `new` when ownership is clear. |
| `CA2208` | Throw `ArgumentException`/`ArgumentNullException` with the correct constructor overload. |
| `CA2249` | Prefer `ArgumentNullException.ThrowIfNull` over ad-hoc null checks. |

### Modern IDE style rules

| Rule | Fix |
|------|-----|
| `IDE0055` | Apply formatter output and consistent spacing. |
| `IDE0060` | Remove unused parameters when the API allows it. |
| `IDE0290` | Prefer primary constructors for simple types when it improves clarity. |
| `IDE0300` | Use collection expressions such as `[]` instead of `new List<T>()`. |
| `IDE0301` | Simplify empty-collection initialization with `[]`. |
| `IDE0161` | Use file-scoped namespaces. |

### Test analyzer rules (NUnit.Analyzers)

| Rule | Fix |
|------|-----|
| `NUnit1xxx` | Fix test structure: fixture/test signatures, `TestCase` argument mismatches. |
| `NUnit2xxx` | Modernize assertions to the `Assert.That` constraint model (e.g. `NUnit2005`). |

## Practical patterns to prefer

- Use `ArgumentNullException.ThrowIfNull(value)` instead of manual null checks.
- Use `string.Equals(a, b, StringComparison.Ordinal)` for ordinal comparisons.
- Use `await using` for `IAsyncDisposable` resources.
- Use `using var` for disposable values that should be scoped to a block.
- Prefer `switch` expressions and pattern matching over large `if` chains.
- Prefer `record` for immutable data containers when the model fits.
- Use `sealed` classes by default unless inheritance is explicitly required.

## What to avoid

- Adding StyleCop packages, `stylecop.json`, or SA-rule suppressions — this repo does not use StyleCop.
- Adding suppressions without a clear reason.
- Chasing style-only changes that do not improve readability or correctness.
- Using old patterns just because they are familiar if the compiler and analyzers already guide a better option.

## Verification

Use these commands to prove the result:

```powershell
dotnet build src/Cloudstrap.sln
```

```powershell
dotnet format src/Cloudstrap.sln --verify-no-changes
```

If warnings remain, fix them until both commands succeed.
