---
description: "Scaffold a new feature end-to-end using the vertical-slice workflow: delegate planning to the planner subagent, then implement gate to gate using the build-feature skill, stopping only at 🛑 HUMAN GATEs."
argument-hint: "<feature name> [— short description], e.g. 'Prescriptions — patient views and creates prescribed medications'"
---

# New Vertical Slice Feature

You are building a new feature in the Cloudstrap project. The workflow is codified across three artefacts — this command only orchestrates the hand-off between them.

**User argument**: $ARGUMENTS

## Input

The user provides a **feature name** and, optionally, user stories or acceptance criteria. If a spec already exists at `_specs/<FeatureName>.md`, read it first — it is the contract for the feature.

## Workflow

1. **Plan first** — invoke the `planner` subagent (via the Task tool) with the feature name. The planner will:
   - Read any `_specs/<FeatureName>.md` present.
   - Ask the user for the **reference feature** to pattern after (do not assume).
   - Interview the user on missing details (public API surface, options classes, messaging contracts, DI registration, sample-app pages/endpoints).
   - Produce `_plans/<FeatureName>.md` at the repo root using `.claude/templates/plan-template.md`.
   - **Stop and wait for user approval.** No code is written.

2. **Implement gate to gate** — once the plan is approved, invoke the `build-feature` skill starting at the **first unchecked `[ ]`** in `_plans/<FeatureName>.md`. The skill executes the RGR-Proof loop from `CLAUDE.md` for each step, runs consecutive steps back-to-back (checking each step's `Done` box as its VERIFY passes), stops at the next 🛑 HUMAN GATE, and marks the gate's checkboxes `[x]` after user approval.

3. **Repeat step 2** for each subsequent gate until the plan is complete.

## Rules

- **Never skip the planner.** A new feature always needs a plan before any code is written (see the Planning Gate in `CLAUDE.md`).
- **Never blend plan steps into one RGR cycle, and never skip a gate.** Steps run continuously between gates; stopping at every 🛑 HUMAN GATE is non-negotiable.
- **Never re-decide the workflow here.** Planning rules live in `.claude/agents/planner.md`; execution rules live in `CLAUDE.md` and `.claude/skills/build-feature/SKILL.md`.

## See Also

- `.claude/templates/spec-template.md` — format for `_specs/<FeatureName>.md`
- `.claude/templates/plan-template.md` — format for `_plans/<FeatureName>.md`
- `CLAUDE.md` — Planning Gate, Mandatory Workflow Rules, RGR-Proof Loop, Human Gates
