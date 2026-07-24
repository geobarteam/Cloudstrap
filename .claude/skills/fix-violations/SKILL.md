---
name: fix-violations
description: "Use when fixing code analysis violations, StyleCop warnings (SA*), Roslyn analyzer issues (CA*), compiler warnings (CS*), or MSTest warnings. Quick-reference lookup of common fixes and the project's disabled rules. Use for: fixing build warnings, resolving SA/CA/CS codes, running dotnet format, understanding which rules are disabled."
metadata:
  argument-hint: "Rule code or description, e.g. 'SA1210', 'fix all warnings', 'CA1822'"
---

# Fix Violations — Code Analysis Quick Reference

Fix .NET analyzer violations in Cloudstrap. Run `dotnet format` first, then fix remaining issues manually.

> **When to reach for this vs `@code-analysis`:** use this skill as a **lookup table** for a specific rule code (e.g. "how do I fix SA1210?"). Use the `@code-analysis` agent when you need to **orchestrate** a full sweep — it collects violations, runs the formatter, fixes them file-by-file, and verifies. This skill's tables are the agent's reference; keep them in sync.
>
> `build-feature`'s CODE ANALYSIS step invokes `@code-analysis`, which in turn consults these tables.

## Step 1 — Auto-fix with `dotnet format`

```powershell
dotnet format src/Cloudstrap.sln
```

This resolves many SA rules automatically (whitespace, spacing, ordering).

## Step 2 — Collect remaining violations

```powershell
dotnet build src/Cloudstrap.sln 2>&1 | Select-String -Pattern ": (warning|error) (SA|SX|CA|CS|MSTEST)\d+" | Sort-Object | Get-Unique
```

Output format: `<FilePath>(<line>,<col>): warning <RuleId>: <Message> [<project>]`

## Step 3 — Fix by rule

### StyleCop Rules (SA*)

| Rule | Fix |
|------|-----|
| **SA1210** | Sort `using` directives alphabetically (System first, then others) |
| **SA1028** | Remove trailing whitespace on the line |
| **SA1412** | Re-save as UTF-8 with BOM: `$c = Get-Content file -Raw; [IO.File]::WriteAllText(file, $c, [Text.UTF8Encoding]::new($true))` |
| **SA1507** | Remove duplicate blank lines |
| **SA1508** | Remove blank line before closing brace |
| **SA1516** | Add/remove blank line between members as required |
| **SA1515** | Single-line comment must be preceded by blank line |
| **SA1127** | Generic type constraints must be on their own line |
| **SA1128** | Constructor initializer must be on its own line |
| **SA1201** | Elements must appear in the correct order (fields → constructors → properties → methods) |

### Roslyn/CA Rules

| Rule | Fix |
|------|-----|
| **CA1822** | Add `static` modifier to method that doesn't access instance data |
| **CA1510** | Replace `if (x == null) throw new ArgumentNullException(...)` with `ArgumentNullException.ThrowIfNull(x)` |
| **CA1827** | Replace `.Count() > 0` with `.Any()` |
| **CA1829** | Use `Length`/`Count` property instead of `Count()` method |
| **CA1861** | Avoid constant arrays as arguments — extract to `static readonly` field |
| **CA2007** | Add `.ConfigureAwait(false)` to awaited tasks (library code only) |

### SonarQube Rules (S*)

| Rule | Fix |
|------|-----|
| **S1155** | Replace `.Count() == 0` with `!.Any()` |
| **S1481** | Remove unused local variable |
| **S6678** | Use PascalCase for structured log placeholders: `{doctorId}` → `{DoctorId}` |
| **S2325** | Mark method as static (same as CA1822 — suppress if needed for module pattern) |

### MSTest Rules

| Rule | Fix |
|------|-----|
| **MSTEST0049** | Add `CancellationToken` parameter: use `TestContext.CancellationToken` |

## Disabled Rules — DO NOT FIX

These rules are disabled in `Directory.Build.props`. Skip them if they appear:

```
SA0001 SA1100 SA1101 SA1124 SA1200 SA1202 SA1309 SA1310 SA1413
SA1502 SA1504 SA1512 SA1600 SA1601 SA1602 SA1604 SA1605 SA1609
SA1611 SA1615 SA1618 SA1619 SA1629 SA1633 SA1634 SA1635 SA1636
SA1637 SA1638 SA1640 SA1641 SA1649 SA1652 SX1101
```

Key disabled rules to remember:
- **SA1309** disabled → `_camelCase` private fields are **allowed** (project convention)
- **SA1600–SA1652** disabled → XML doc comments are **not required**
- **SA1101** disabled → `this.` prefix is **not required**

## Step 4 — Verify

```powershell
dotnet build src/Cloudstrap.sln 2>&1 | Select-String -Pattern ": (warning|error) (SA|SX|CA|CS|MSTEST)\d+"
```

Output must be empty. If violations remain, repeat Step 3.

## Analyzer Config

- **Package**: `StyleCop.Analyzers.Unstable` v1.2.0.556
- **`TreatWarningsAsErrors = true`** — non-excluded warnings are build errors
- **StyleCop SA/SX rules** are in `WarningsNotAsErrors` — they produce warnings (not errors) but should still be fixed
- Analyzer rules in `Directory.Build.props` are **fixed** — do not modify the file
