# AGENTS.md

## Verification
- Run: `src\dev.cmd test`
- If it fails, fix the root cause and re-run.

## Environment
- Use `src\dev.cmd` (Windows) and `src/dev.sh` (Linux/macOS) as canonical entrypoints.
- Use repo-managed .NET bootstrap via `src/dev.*` rather than ad-hoc local SDK assumptions.
- Use npm only for the two existing Node utility folders; do not introduce new JS package managers.

## Guardrails
- Run `layout` before first build/test on a machine.
- Keep code changes in `src/` and tests in `src/Test/` unless requested otherwise.
- Avoid drive-by refactors; keep diffs task-scoped.
- Do not duplicate instructions across `AGENTS.md` and `.github/copilot-instructions.md`.

## Constraints
- Keep diffs minimal and scoped to the request.
- Update/add tests for behavior changes.
- Do not modify CI, dependency versions, or security settings unless asked.
- Never print, log, or commit secrets.

## Definition of Done
- Verification passes with `src\dev.cmd test`.
- No new lint/test warnings introduced.
- Changes are scoped to the request.

## Where to find more
- Path-specific rules: `.github/instructions/`
- Multi-step workflows: `.github/skills/*/SKILL.md`
