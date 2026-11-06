#!/usr/bin/env bash
set -euo pipefail

# Block destructive commands. Extend this list as needed.
BLOCKED_PATTERNS=(
  "git reset --hard"
  "rm -rf /"
  "DROP DATABASE"
  "format C:"
  "mkfs"
)

INPUT="$*"
for pattern in "${BLOCKED_PATTERNS[@]}"; do
  if echo "$INPUT" | grep -qi "$pattern"; then
    echo "Blocked: destructive pattern detected: $pattern" >&2
    exit 1
  fi
done
