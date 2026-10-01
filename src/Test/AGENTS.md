# Test Projects

## Verification
- Run: `src\dev.cmd test`

## Conventions
- Prefer extending existing test patterns in `src/Test/` over inventing new harnesses.
- Keep tests deterministic; avoid clock, network, or environment flakiness.

## Constraints
- Never weaken assertions just to make tests pass.
- Never change production code for test-only requests unless explicitly asked.
