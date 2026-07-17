---
name: test-tier-selection
description: 'Select and run the right test tier (L0/L1/full). Use when deciding fast feedback vs integration confidence in src/Test.'
---

# Test Tier Selection

Choose the smallest reliable test surface first, then expand.

## When to Use

- Unsure whether to run L0, L1, or full tests.
- Investigating test regressions.

## Process

### Step 1: Classify change impact
<!-- TODO: Define examples for unit-only vs integration-impacting changes -->

### Step 2: Pick minimum tier
<!-- TODO: Document your default decision matrix for testl0/testl1/test -->

### Step 3: Escalate if needed
<!-- TODO: Define escalation triggers to broader test runs -->

## Constraints

- Prefer deterministic tests.
- Do not reduce assertions to hide failures.

## Validation

- Selected tier passes and is justified in PR notes.
