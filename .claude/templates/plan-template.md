# Plan: <Feature Name> — <User Story>

## Overview
<1–3 sentences: what this feature does, which existing feature to use as pattern reference.>

<!--
Decomposition strategy: vertical slices, not horizontal layers.
Each slice follows: Stub (UI with mocked backend) → Wire (replace mock with real code top-to-bottom).
Name steps by what the user/system can do — never by which layer is built.
Good: "Display subscription list (stubbed)", "Wire subscription list to real API"
Bad: "Create Entity", "Add Repository", "Build Controller"

Gate placement: steps run back-to-back WITHOUT user intervention — the executor stops ONLY
at a 🛑 HUMAN GATE block. Place one gate at the end of each vertical slice. Add an extra
gate only where earlier user judgment is essential (stub-UI sign-off before backend work
starts, or after a ⚠️ Risk Area step). Never attach a gate to every step.

Demonstration slice (MANDATORY for extraction deliverables — planner rule 15): the LAST slice
of the plan extends the appropriate demo app under src/demo (API/hosting → Api ·
interactive/BFF/browser → BlazorWasm · MVC → Mvc · Blazor Server → BlazorServer ·
worker/headless-hosting → Worker) to exercise
the deliverable's headline behavior and adds ≥ 1 E2E test to
src/Test/E2E/Cloudstrap.Demo.E2E.Tests/ proving it through the running app. Its RED
failing-run command targets the E2E executable, e.g.:
  src\Test\E2E\Cloudstrap.Demo.E2E.Tests\bin\Debug\net10.0\Cloudstrap.Demo.E2E.Tests.exe --filter "<TestMethod>"
Also update the feature matrix in the extended app's README under src/demo. The slice's
🛑 HUMAN GATE covers the demo. References: _plans/25-WasmTestProjectSut.md,
_plans/27-DemoAppsRestructure.md.
-->

---

## Step N — <Behavior slice: what the user/system can do after this step>

- [ ] Done *(checked by the executor when VERIFY passes — user approval happens at the next 🛑 HUMAN GATE)*

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

**VERIFY** *(after making GREEN changes, run these checks; when all green, mark this step's `Done` checkbox and continue straight to the next step — stop only when the next plan item is a 🛑 HUMAN GATE)*: build + all tests + code analysis + format — all green (exact commands in copilot-instructions.md)

**REFACTOR** *(these instructions are for the executor, not the planner)*:
- Analyse the produced code with code-analysis.agent and fix any new issues before proceeding to the next step.
- Optional: any additional refactorings to improve code quality, maintainability, or align with patterns — but only after RED-GREEN-VERIFY is complete for this step. Do not refactor during RED-GREEN cycles, only after the feature slice is fully working and verified.

---

<!-- Repeat the Step block above for every step in the slice, then close the slice with a gate.
     A gate is its own top-level block between steps — NOT a section inside a step. -->

## 🛑 HUMAN GATE — <end of Slice X> *(covers Steps M–N)*

*Executor: STOP here. Present the results of all covered steps and WAIT for user approval — do not start the next step.*

- [ ] Behavioral verification: <how to confirm the new behavior of the covered steps — e.g., integration test names, API call + expected response, UI action + expected result>
- [ ] Code review: <what the reviewer should check across the covered steps>
- [ ] User approved — implementation may continue past this gate
