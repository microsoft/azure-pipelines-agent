---
name: ci-failure-triage
description: 'Triage Azure Pipelines and GitHub Actions failures. Use when build checks fail, tests regress, or pipeline jobs become unstable.'
---

# CI Failure Triage

Identify failing job, map to owning area, and reproduce locally.

## When to Use

- Failing `.azure-pipelines/*.yml` or `.github/workflows/*`.
- Repeated flaky test or build failures.

## Process

### Step 1: Capture failing job context
<!-- TODO: Add links and commands your team uses to fetch pipeline logs -->

### Step 2: Reproduce locally
<!-- TODO: Add exact local command mapping (for example src/dev.cmd build/test/testl0/testl1) -->

### Step 3: Contain and fix
<!-- TODO: Define how to scope fixes and rollback criteria -->

## Constraints

- Do not bypass checks, signing, or security gates.
- Keep fixes minimal and traceable.

## Validation

- Re-run failing pipeline and the local verification command.
