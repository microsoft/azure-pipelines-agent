# Pipeline Definitions

## Verification
- Run: `src\dev.cmd build`

## Conventions
- Keep pipeline edits minimal and consistent with existing YAML structure.
- Prefer existing scripts and variables over introducing new execution paths.

## Constraints
- Never add production deployment behavior unless explicitly requested.
- Never bypass security, signing, or compliance checks in pipeline changes.
