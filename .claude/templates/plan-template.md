# Plan: <Feature Name> — <User Story>

## Overview
<1–3 sentences: what this feature does, which existing feature to use as pattern reference.>

<!--
Decomposition strategy: vertical slices, not horizontal layers.
Each slice follows: Stub (UI with mocked backend) → Wire (replace mock with real code top-to-bottom).
Name steps by what the user/system can do — never by which layer is built.
Good: "Display subscription list (stubbed)", "Wire subscription list to real API"
Bad: "Create Entity", "Add Repository", "Build Controller"
-->

---

## Step N — <Behavior slice: what the user/system can do after this step>

**Scope** *(all layers touched by this slice)*:
- `<path/file.cs>` *(create | modify)*
- `<path/testfile.cs>` *(create | modify)*

**RED** *(write these tests first, run them, confirm they fail before writing production code)*:
- Unit test file: `<path>`
- Unit test method: `<MethodName>`
- Failing-run command: `dotnet test --project src/Test/Unit/ --filter "<MethodName>"`
- *(When this step adds a controller or endpoint — omit for presentation-only steps)*:
  - Integration test file: `src/Test/Integration/Endpoints/<Feature>/<TestClassName>.cs` *(create | modify)*
  - Integration test methods: `<Method>_<Scenario>_<Expected>` (at minimum: happy-path + one error case per endpoint action)
  - Pattern: `CustomWebApplicationFactory` + `HttpClient` + `SeedTestDataAsync()` — see `tests.instructions.md` § *Integration / Service Tests* and existing `GetDoctorsTest` / `PostDoctorTest` for reference
  - Failing-run command: `dotnet test --project src/Test/Integration/ --filter "<TestClassName>"`

**GREEN** *(minimal production code across all necessary layers to make RED pass)*:
- <What to implement — be specific about class names, interfaces, properties, method signatures>

**DB changes**: *use the skills/database-changes/SKILL.md workflow for any SQL scripts, DACPAC builds, or Schema Compare steps. Link to the relevant SQL script file(s) here.*

**VERIFY** *(after making GREEN changes, run these checks before moving to the next step)*: build + all tests + code analysis + format — all green (exact commands in copilot-instructions.md)

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

**🛑 HUMAN GATE**:
- [ ] Behavioral verification: <how to confirm new behavior — e.g., integration test name, API call + expected response, UI action + expected result>
- [ ] Code review: <what the reviewer should check>
