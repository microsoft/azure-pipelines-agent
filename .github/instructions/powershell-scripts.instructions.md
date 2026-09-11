---
applyTo: '**/*.ps1'
---
# PowerShell Script Conventions
**When to read:** Editing PowerShell scripts in any directory.

- Use `$ErrorActionPreference = 'Stop'` for deterministic failure behavior.
- Prefer explicit parameters and clear failure messages over silent fallbacks.
- Avoid destructive operations unless explicitly requested by the task.
