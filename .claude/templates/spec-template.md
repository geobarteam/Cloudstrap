# Spec: <Feature Name>

## User Story

**As a** <role>,
**I want to** <action>,
**So that** <benefit>.

---

## Acceptance Criteria

| # | Given | When | Then |
|---|-------|------|------|
| AC-1 | ... | ... | ... |
| AC-2 | ... | ... | ... |

---

## Data Model

### Entity: `<EntityName>`

| Property | Type | Required | Constraints |
|----------|------|----------|-------------|
| Id | int | PK | Auto-generated |
| ... | ... | ... | ... |

### Relationships

- EntityA (1) → (n) EntityB

---

## API Endpoints

### `<VERB> /api/<route>`
**Purpose**: ...
**Auth**: Bearer / Anonymous
**Query params**: `param1` (type, required/optional)
**Request body** _(if POST/PUT)_:
```json
{ }
```
**Response** (200):
```json
{ }
```
**Error responses**: 400 → `"message"`, 404, 500

---

## Business Rules

| # | Rule | Error message |
|---|------|---------------|
| BR-1 | ... | "..." |
| BR-2 | ... | "..." |

---

## Events & Messaging _(optional — remove if none)_

### <Event name>
**Trigger**: What causes this event to be published.
**Payload**: Summary of data carried (no C# types — those go in the plan).
**Side effect**: What happens when the event is consumed.

---

## UI / Pages _(optional — remove if no Blazor page)_

### Page name (`/<route>`, authenticated / anonymous)
**Purpose**: ...

**User flow**:
1. Step — what the user does and sees
2. Step — what the user does and sees

**Navigation**: where the user goes after (success / error)

---

## Edge Cases

| Case | Expected behavior |
|------|-------------------|
| ... | ... |

---

## Out of Scope

- Item 1
- Item 2

---

## Open Questions _(remove when resolved)_

- [ ] Question 1?
- [ ] Question 2?
