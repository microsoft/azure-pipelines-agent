# Temporary VSO Command Suppression

## Summary

Add an agent-owned logging command that temporarily disables processing of `##vso[...]` commands emitted by a task. Processing resumes only when the same task emits a matching command with a caller-generated token, or when the task ends.

This is intended for trusted task or script logic that must print less-trusted content, such as repository content, remote command output, model output, or generated diagnostics, without allowing that content to mutate pipeline state.

Related requirements:

- Azure DevOps feature 2459969: temporarily disable and enable VSO commands for pipeline authors.
- Azure DevOps bug 2459494: untrusted pull request content was interpreted as logging commands.
- GitHub Actions provides similar stop/resume semantics through `::stop-commands::<token>` and `::<token>::`.

## Current behavior

Task stdout and stderr handlers pass each output line to `WorkerCommandManager.TryProcessCommand`. The manager:

1. Finds `##vso[` anywhere in the line.
2. Parses the command through `Command.TryParse`.
3. Dispatches it to the registered command extension.
4. Returns `true` so the handler does not write the command as ordinary output.

The same singleton `WorkerCommandManager` is shared across the worker process, but every task has a distinct `IExecutionContext.Id`. Agent-owned operations such as final task completion, timeline updates, telemetry, and resource warnings call execution-context or extension APIs directly; they do not pass through task stdout parsing.

This makes `WorkerCommandManager` the appropriate enforcement point, provided suppression state is scoped by execution-context ID rather than stored as one process-wide flag.

## Proposed command contract

Use an agent-reserved command area so the control commands are handled by `WorkerCommandManager` itself and cannot be replaced by a task command extension:

```text
##vso[agent.pausecommands]<token>
##vso[agent.resumecommands]<token>
```

Example:

```powershell
$token = [guid]::NewGuid().ToString("N")
Write-Host "##vso[agent.pausecommands]$token"

# Output from a less-trusted source is treated as ordinary text.
Get-Content untrusted-output.txt

Write-Host "##vso[agent.resumecommands]$token"
```

Contract:

- The pause and resume control lines must be the complete output line. Unlike existing logging commands, embedded control commands are not accepted.
- The command area and action are case-insensitive, matching existing VSO command behavior. The token is required and compared using an ordinal, case-sensitive comparison.
- Tokens use the conservative `[A-Za-z0-9_-]` alphabet and must be 16-128 characters. A GUID in `N` format is the recommended token.
- A pause command is consumed and stores the token for the current `IExecutionContext.Id`.
- While paused, every `##vso[...]` command except the exact matching resume command is ignored as a command and is emitted as ordinary task output.
- A resume command with the wrong token is emitted as ordinary output and does not replace or clear the active token.
- A second pause command while paused is ordinary output and cannot replace the active token.
- A matching resume command is consumed, clears the token, and restores normal parsing.
- Suppression state does not cross task execution-context boundaries. If the task never resumes parsing, the next task still starts with command processing enabled.

The token must be unpredictable if less-trusted output could know or reproduce static script content. This feature reduces accidental or injected command interpretation; it does not protect a job that executes attacker-controlled code with access to the trusted script's memory, environment, files, or stdout.

## Recommended implementation

### `WorkerCommandManager`

Maintain suppression tokens keyed by `IExecutionContext.Id`. Read and mutate the dictionary under the existing `_commandSerializeLock` so stop, suppressed output, and resume checks are serialized with normal command execution.

Refactor `TryProcessCommand` into these logical stages:

1. Reject empty input.
2. Under the command lock, check whether the context is currently suppressed.
3. If suppressed:
   - recognize only the exact canonical `agent.resumecommands` line with the matching token;
   - clear state and return `true` for a match;
   - otherwise return `false`, bypassing `Command.TryParse` and all extensions.
4. If enabled, parse normally.
5. Before extension lookup, recognize and validate an exact `agent.pausecommands` line, store the token, and return `true`.
6. Reject `agent.resumecommands` when no suppression is active as an unknown or invalid control command; it must not affect state.
7. Dispatch all other commands through the existing extension and restriction flow.

Add an explicit cleanup method to `IWorkerCommandManager`, for example:

```csharp
void ResetCommandSuppression(IExecutionContext context);
```

Call it from `TaskRunner.RunAsync` in `finally`. The execution-context key already prevents leakage into another task, while explicit cleanup bounds memory use and makes the task-end guarantee clear. Suppression should remain active across task retry attempts because retries run under the same task execution context; the final task boundary resets it.

Do not implement the controls as a normal `IWorkerCommandExtension`. Resume must remain recognizable while extension dispatch is disabled, and the reserved behavior should not be affected by task command restrictions.

### Diagnostics

- Write a token-free message to the task log when processing pauses, explicitly resumes, or resumes automatically at task completion.
- Trace pause, successful resume, and task-boundary cleanup without tracing the token.
- Do not emit the token in debug, warning, or error messages.
- Invalid pause and resume commands remain visible as ordinary output and do not change suppression state.
- Do not run the existing malformed-command warning path for suppressed content; warnings for every literal `##vso` line would defeat the purpose of safely dumping untrusted data.

## Compatibility and scope

- Existing command parsing is unchanged until a task emits the new pause command.
- Commands printed before suppression and after a matching resume retain current behavior.
- Agent-owned task status and progress operations remain functional because they do not depend on parsing the task's output.
- The state is local to the current worker and task execution context; no server contract or job-message change is required.
- Containers and all current handlers are covered because their output reaches the same command manager.
- Plugin-internal commands emitted through stdout are also suppressed for that execution context. Direct plugin or agent API calls are unaffected.

### Formatting command limitation

This proposal covers agent-parsed `##vso[...]` commands. Azure Pipelines also has `##[...]` formatting directives such as `##[error]`, `##[warning]`, and `##[group]`. They are not parsed by `Command.TryParse` or dispatched by `WorkerCommandManager`; they are written to the log stream.

The implementation and documentation must not claim that `agent.pausecommands` neutralizes `##[...]` directives unless the output path is separately changed and validated. If feature 2459969 is expected to cover those directives as well, extend the design with a safe-output API or handler contract that preserves visible text while preventing service-side formatting interpretation, and validate it against the live log service.

## Test-first implementation plan

1. Add failing L0 tests in `WorkerCommandManagerL0` for:
   - a valid pause command being consumed;
   - a normal command being processed before pause;
   - the same command not being processed while paused;
   - literal `##vso[...]` text remaining ordinary output while paused;
   - a wrong, differently cased, embedded, empty, short, or oversized resume token not restoring parsing;
   - a second pause command not replacing the original token;
   - the exact matching resume command restoring parsing;
   - suppression being isolated between two execution-context IDs;
   - cleanup restoring the original context;
   - malformed suppressed commands not producing parser warnings.
2. Add a failing `TaskRunnerL0` test proving cleanup runs when the handler succeeds, throws, or is cancelled.
3. Implement the reserved control-command recognition and per-context state in `WorkerCommandManager`.
4. Add task-boundary cleanup in `TaskRunner.RunAsync.finally`.
5. Add localized warning text only for invalid control commands that require a user-visible diagnostic.
6. Run the focused Worker and TaskRunner L0 tests.
7. Build the agent and run a local task through at least the Node, PowerShell, and process handlers.
8. Create a retained validation pipeline using the designated test agent pool. Verify:
   - `task.setvariable`, `task.logissue`, `task.setprogress`, `task.complete`, and `build.addbuildtag` are inert during suppression;
   - the same commands work before pause and after the matching resume;
   - an incorrect token cannot resume;
   - an unclosed suppression scope does not affect the next task;
   - task completion and agent-generated diagnostics still work;
   - behavior is identical for Windows PowerShell, PowerShell Core, Bash, Node tasks, and container steps where available.
9. Separately test and document `##[error]`, `##[warning]`, and grouping behavior. Treat any desired suppression of those directives as additional implementation scope rather than assuming this parser change covers them.

## Open decisions

1. Confirm whether `##[...]` formatting directives are in scope for the first version.
2. Confirm whether suppression should persist across retries of the same task. The recommended behavior is yes, with reset only at the final task boundary.
