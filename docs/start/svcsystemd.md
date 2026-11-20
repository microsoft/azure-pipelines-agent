# Running As A Service On Unix with systemd

Key Points:
  - This is a convenience which only creates OS specific service files.
  - systemd is used on Linux.  Ubuntu 16 LTS, Redhat 7.1 has systemd
  - systemd commands require sudo

## Managing the Service

./svc.sh was generated to manage your service

## Install

Install will create a systemd unit file on Linux

```bash
$ sudo ./svc.sh install
...
Creating runsvc.sh
Creating .Service
svc install complete
```

Service files point to `./runsvc.sh` which will setup the environment and start the agents host.  See Environment section below.

### Start
```bash
$ sudo ./svc.sh start
```

### Status
```bash
$ sudo ./svc.sh status
```

### Stop
```bash
$ sudo ./svc.sh stop
```

### Uninstall
```bash
$ sudo ./svc.sh uninstall

```

## Setting the Environment

When you install and/or configure tools, your path is often setup or other environment variables are set.  Examples are PATH, JAVA_HOME, ANT_HOME, MYSQL_PATH etc...

If your environment changes at any time, you can run env.sh and it will update your path.  You can also manually edit .env file.  Changes are retained. 

Stop and start the service for changes to take effect.

```bash
$ ./env.sh 
$ sudo ./svc.sh stop
...

$ sudo ./svc.sh start
...
```

## Environment

Configuring as a service will snapshot off your PATH and other "interesting variables" like LANG, JAVA_HOME etc..  When the service starts, it will read these and set.  This allows for unified environment management between

```bash
$ ls -la
-rwxrwx---    1 bryanmac  staff   189 May 29 11:42 .agent
-rwxrwx---    1 bryanmac  staff   106 May 29 11:41 .credentials
-rw-r--r--    1 bryanmac  staff    58 May 29 11:44 .env
-rw-r--r--    1 bryanmac  staff   187 May 29 11:40 .path
...
-rwxr-xr-x    1 bryanmac  staff   546 May 29 11:40 env.sh
```

Run ./env.sh to update.

You can also inject anything you want to run when the service runs.  For example setting up environment, calling other scripts etc...

Put these customizations in `runsvc.hook.sh` in the agent root instead of editing `runsvc.sh`. `runsvc.sh` sources the hook from the agent root before it starts the agent, so exported variables and other shell changes apply to the agent. The hook does not need to be executable, and agent updates and `svc.sh install` do not change it. Keep its permissions restrictive if it contains sensitive values.

./runsvc.hook.sh
```bash
export MY_SETTING=value
```

During self-update, a root `runsvc.sh` that matches a known stock script is replaced with the entire script from the new agent. The previous script is saved as `runsvc.sh.<previous version>.bak.<suffix>`. The new script takes effect the next time the service starts. Stock scripts end with a `# agent-stock-sha256:` line that records the hash of the rest of the script; any edit to the script, including that line, marks it as customized.

Before making any change, self-update checks that the bundled Node.js 24 in the new agent can run on the machine. If it cannot (for example, because the operating system's C library is too old), the update is blocked, the current agent stays active, and the error is written to the `_diag/SelfUpdate-*.log.failed` file.

Automatic migration requires lines added only directly after `# insert anything to setup env when running as a service`, with an otherwise exact known stock script and no existing hook. Only blank lines, comment lines, and one `export NAME=value` per line are accepted. Variable names must be ordinary environment variables, not Bash special variables (such as `_`, `RANDOM`, `BASH_*`, or `IFS`). Values must be single-quoted literals, double-quoted literals without `$`, backticks or backslashes, or unquoted literals containing only letters, digits, `-`, `_`, `.`, `/`, `:`, `@`, `%`, `+`, and `,`. Empty values are allowed. No expansions (including `$PATH` or `$_`), commands, traps, nested source files, continuations, or other shell syntax are migrated. More complex hooks require manual migration and review because sourcing can change shell context.

The update checks these rules without running customer code. Removing the inserted lines must reconstruct a script with a valid stock SHA-256 stamp, or an unstamped script in the frozen historical fingerprint list. Do not modify or regenerate the stamp when customizing a script. The accepted lines are copied unchanged to a new `runsvc.hook.sh` with the same owner and permissions as `runsvc.sh`, then `runsvc.sh` is replaced. If the update later fails, the original `runsvc.sh` is restored and the new hook is removed. Migration success is reported only after switching both active agent directories.

If `runsvc.sh` is a symbolic link or has customizations that do not meet these rules, the agent update is blocked before switching the active agent binaries. An existing `runsvc.hook.sh` blocks automatic migration of a customized script, not an upgrade of an otherwise-stock script; stock-script upgrades preserve the hook. The existing files are left untouched, and the new script is saved as `runsvc.sh.new` when possible without overwriting an existing file. To retry, back up the script and any symlink target, manually review and move required customizations to `runsvc.hook.sh`, and restore a known stock script. The error appears in service output and `_diag/SelfUpdate-*.log.failed`; agents with this change also warn in the listener log when the root script differs from the shipped script. Historical stock scripts are recognized, but the target agent's operating system requirements still apply.

## Service Files

This is a convenience that simply creates a service file for you can sets permissions.  You are free to manually configure and control your service using alternate methods.

Details are in .service file in root of the agent

Linux systemd: /etc/systemd/system/vsts.agent.{accountName}.{agentName}.service

These files are created from a template located

Linux: ./bin/vsts.agent.service.template
