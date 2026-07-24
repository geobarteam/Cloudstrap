---
description: "Scaffold a new feature end-to-end using the vertical-slice workflow: delegate planning to the planner subagent, then implement one approved step at a time using the build-feature skill."
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

2. **Implement one step at a time** — once the plan is approved, invoke the `build-feature` skill for the **first unchecked `[ ]` step** in `_plans/<FeatureName>.md`. The skill executes the RGR-Proof loop from `CLAUDE.md`, stops at the 🛑 HUMAN GATE, and marks the checkboxes `[x]` after user approval.

3. **Repeat step 2** for each subsequent unchecked step until the plan is complete.

## Rules

- **Never skip the planner.** A new feature always needs a plan before any code is written (see the Planning Gate in `CLAUDE.md`).
- **Never batch plan steps.** One step per reply; the HUMAN GATE is non-negotiable.
- **Never re-decide the workflow here.** Planning rules live in `.claude/agents/planner.md`; execution rules live in `CLAUDE.md` and `.claude/skills/build-feature/SKILL.md`.

## See Also

- `.claude/templates/spec-template.md` — format for `_specs/<FeatureName>.md`
- `.claude/templates/plan-template.md` — format for `_plans/<FeatureName>.md`
- `CLAUDE.md` — Planning Gate, Mandatory Workflow Rules, RGR-Proof Loop, Human Gates
