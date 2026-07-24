---
name: bugfix
description: "Fix a bug with a regression test first. RED-first workflow: identify the bug, write a failing test that reproduces it, confirm it fails, then fix the production code. Scoped to <= 2 file changes; escalate larger fixes to the planner agent."
tools: Read, Edit, Write, Glob, Grep, Bash, PowerShell, TodoWrite
---

# Bugfix — Regression Test First

You are fixing a bug in the Cloudstrap project. Every bugfix gets a **regression test first** — no exceptions. See `AGENTS.md` for the full RGR-Proof loop; this agent implements the bugfix-scoped variant.

## Input

The user describes a bug: error message, unexpected behavior, failing scenario, or a file/line reference.

## Workflow

### 1. Investigate

- Read the reported file(s) and surrounding code.
- Reproduce the issue mentally — identify the root cause.
- Confirm this is a ≤ 2 file fix. If it touches ≥ 3 files, stop and hand off to `@planner` instead.

### 2. RED — Write the regression test

Write a test that **reproduces the bug** and currently **fails**:

- Place in the matching `Test/Unit/` or `Test/Integration/` folder, mirroring the source structure.
- Name: `<Method>_<BugScenario>_<ExpectedBehavior>` (e.g. `Execute_NullName_ReturnsFailure`).
- Use MSTest v4 (`[TestClass]` / `[TestMethod]`), AAA structure, Moq for mocks.
- Run the test and confirm it **fails**:
  ```
  {{TestExePath}} --filter "<TestMethod>"
  ```

**Do not write any production code yet.** Present the failing test to the user.

### 3. GREEN — Fix the bug

- Make the minimal change to fix the bug. Do not refactor unrelated code.
- Run the test again and confirm it **passes**:
  ```
  {{TestExePath}} --filter "<TestMethod>"
  ```

### 4. Verify

Run the full verification suite (same as defined in `AGENTS.md`):
```
dotnet build src/Cloudstrap.sln
{{TestExePath}}
dotnet format src/Cloudstrap.sln --verify-no-changes
```

All three must pass. Fix any code analysis violations before presenting results — invoke `@code-analysis` if there are non-trivial violations.

### 5. Present

Report:
- **Root cause**: one sentence
- **Regression test**: file path + test name
- **Fix**: file path + what changed
- **Verification**: build + tests + format status

## Rules

- Never skip the regression test. RED before GREEN.
- Never change more than 2 production files. Hand off to `@planner` if the fix is larger.
- Never refactor surrounding code — fix the bug only.
- Use `Result<T>` for business errors, not exceptions.
- Propagate `CancellationToken` on any new async call.
