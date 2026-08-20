# Job-scoped task environment

## Configuration

`UseJobScopedTaskEnvironment` controls job-scoped task environment state. It is
off by default and can be supplied, in precedence order, by:

1. The `UseJobScopedTaskEnvironment` pipeline feature.
2. The `AZP_AGENT_USE_JOB_SCOPED_TASK_ENVIRONMENT` runtime knob.
3. The `AZP_AGENT_USE_JOB_SCOPED_TASK_ENVIRONMENT` agent environment variable.
4. The built-in default, `false`.

The proxy-variable exclusions in the ProcessHandler legacy worker-environment
promotion path are independent of this feature.

## Behavior

When disabled, handlers receive the legacy plain environment dictionary.
ProcessHandler uses its legacy command and parser, mutates the worker process
environment when `modifyEnvironment` is enabled, and retains the existing
PluginHost, expansion, and container payload behavior.

When enabled, each job owns an environment state shared by that job's child
execution contexts. A new job starts with empty state. ProcessHandler V1 and V2
capture a successful task's final environment in memory and commit only exact
changes. Unchanged values are not promoted into job state. An explicit empty
string is a value; only a name absent from a successful final capture becomes a
removal tombstone.

Each handler invocation composes its environment in this order, from lowest to
highest precedence:

```text
Worker/container baseline
  < job-scoped values/removals
  < explicit task env
  < current public runtime variables
  < handler-required generated values
  < PATH prepend
  < TF_BUILD=True
```

A higher-precedence value restores a tombstoned name for that invocation.
Removals are also sent to container handlers. Environment expansion uses the
same job-scoped values and removals.

AgentPluginHandler and the legacy PowerShell handler project current public
runtime variables with the same precedence. Secret runtime variables and the
public/secret variable-name lists are not newly projected into PluginHost
process environments.

## Capture and security

Capture uses the existing line-based `cmd.exe set` output. The output between
per-attempt start and completion markers is consumed as capture data rather
than written to task output. State is committed only after a complete capture,
a zero process exit code, no failing standard-error condition or cancellation,
and a successful task result. Failed or incomplete attempts leave state
unchanged.

The feature adds no cscript, PowerShell, or Node helper; environment snapshot
file; named or anonymous pipe; or other capture dependency. Secure argument
transport values and handler-owned values such as correlation IDs are excluded
from job state. Capture itself creates no artifact in the agent temp directory
and does not intentionally log captured values.

The existing line transport cannot unambiguously serialize environment values
containing embedded newlines. It also remains susceptible to deliberate output
spoofing by another process running as the same user. This feature improves the
state destination, atomic commit, completion validation, and delta semantics;
it does not redesign the transport or add IPC.

## Rollout and rollback

Enable the feature for a limited set of jobs first. Verify ProcessHandler tasks
that use `modifyEnvironment`, PluginHost tasks, container tasks, retries, and
jobs that intentionally remove or empty environment variables before expanding
the rollout.

To roll back, set the pipeline feature, runtime knob, or agent environment knob
to `false` at the appropriate precedence. Subsequent jobs use the legacy
behavior and do not initialize or consume job-scoped state.
