---
name: agent-dev-loop
description: 'Run the standard Azure Pipelines Agent development loop. Use when implementing code changes in src/, updating tests, and validating with dev scripts.'
---

# Agent Development Loop

Standardize implementation flow with repo entrypoints.

## When to Use

- Any feature or fix in `src/`.
- Test updates in `src/Test/`.

## Process

### Step 1: Bootstrap environment
<!-- TODO: Document team-default setup command and prerequisites -->

### Step 2: Build and test
<!-- TODO: Add exact commands and when to run test versus testl0/testl1 -->

### Step 3: Prepare PR-ready changes
<!-- TODO: Add checklist for docs/tests/changelog expectations -->

## Constraints

- Use `src/dev.*` scripts as source of truth.
- Avoid unrelated refactors.

## Validation

- `src\dev.cmd test` passes and changed behavior is covered.
