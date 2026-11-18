# Remove a runtime job variable

Proposed logging command:

```text
##vso[task.unsetvariable variable=SSL_CERT_FILE]
```

This removes a mutable variable from the agent's runtime variable collection for
the current job. Subsequent tasks no longer receive that variable through normal
pipeline-variable environment injection. Variable names are case-insensitive.
Removing a missing variable is a no-op; a missing or blank name is an error.

## Why setting an empty value is different

`##vso[task.setvariable variable=SSL_CERT_FILE]` defines the variable with an empty
value. The agent still injects `SSL_CERT_FILE=` into subsequent task processes.
Clients such as OpenSSL distinguish an empty certificate-file override from an
absent override, so emptying the variable need not restore their default behavior.

The worker already uses `Variables.Unset` internally for scoped variables, but
tasks currently have no logging command that exposes that operation. This proposal
exposes the existing operation without changing `task.setvariable` or treating
empty values as deletion.

## Example

```yaml
steps:
- bash: echo "##vso[task.setvariable variable=SSL_CERT_FILE]/tmp/temporary.pem"
- bash: |
    # Finish using the temporary certificate configuration.
    echo "##vso[task.unsetvariable variable=SSL_CERT_FILE]"
- bash: |
    if [ "${SSL_CERT_FILE+x}" = x ]; then
      echo "SSL_CERT_FILE is still defined"
      exit 1
    fi
```

The final check assumes no agent-machine or explicit task environment value
defines `SSL_CERT_FILE`.

## Scope and compatibility

- The change affects subsequent tasks in this job, not the environment of the
  already-running script. Use the shell's `unset SSL_CERT_FILE` (or equivalent)
  separately if the current script must also clear its own environment.
- Removing the job variable stops the agent injecting that override. It does not
  delete an inherited agent-machine environment variable, an explicit task `env:`
  entry, or another variable that maps to the same environment name. Such a value
  can still be visible to subsequent processes. This is not a request to scrub
  the process environment.
- The runtime collection has no stack of previous values. Removing a variable
  does not restore an earlier pipeline-level value overwritten by
  `task.setvariable`.
- Read-only variables, including protected system variables and read-only output
  bindings, cannot be removed, even when legacy read-only enforcement is disabled.
  The command uses the same settable-variable restrictions as `task.setvariable`,
  including in restricted command mode.
- Secret values are removed from the runtime collection, but their existing log
  masking registrations remain. Removing a variable must not expose old secrets
  in later logs; deletion is not secure erasure of past values.
- This does not change task-private variables, published output/timeline records,
  pipeline definitions, variable groups, other jobs, or other stages. It does not
  undo values already expanded into task inputs or other strings. Template
  expansion has already happened; a later unresolved `$(name)` macro is not a
  guarantee that the literal text disappears.
- A newer agent is required. Older agents do not recognize `task.unsetvariable`;
  task authors must require a supporting agent version rather than assuming
  cleanup succeeded.

## Design considerations

The code establishes a missing public command, not the historical reason no
command was added. The existing additive `setvariable` protocol has no deletion
marker. Reinterpreting an empty value would break intentional empty variables.
Deletion also needs explicit answers for read-only protections, restricted tasks,
secret masking, output propagation, environment inheritance, and evaluation
timing. These are compatibility boundaries, not evidence that job-local removal
is inherently unsafe.

A separate command avoids overloading the existing value format and is rejected
as unknown by older agents instead of being silently interpreted as an empty
assignment. It reuses the worker's existing removal and restriction mechanisms;
there is no server-side output-deletion protocol or machine-environment mutation
in this proposal.
