# Running As A Service OSX

Key Points:
  - This is a convenience which only creates OS specific service files.
  - Creates a LaunchAgent service scoped to a user for UI tests etc...
  - Starts at login. Auto login and lock recommended.

## Managing the Service

./svc.sh was generated to manage your service

## Install

Install will create a LaunchAgent plist on OSX

```bash
$ ./svc.sh install
...
Creating runsvc.sh
Creating .Service
svc install complete
```

plist points to `./runsvc.sh` which will setup the environment and start the agents host.  See Environment section below.

### Start
```bash
$ ./svc.sh start

starting vsts.agent.bryanmac.testsvc
status vsts.agent.bryanmac.testsvc:

/Users/bryanmac/Library/LaunchAgents/vsts.agent.bryanmac.testsvc.plist

Started:
25324 0 vsts.agent.bryanmac.testsvc
```

### Status
```bash
$ ./svc.sh status

status vsts.agent.bryanmac.testsvc:

/Users/bryanmac/Library/LaunchAgents/vsts.agent.bryanmac.testsvc.plist

Started:
25324 0 vsts.agent.bryanmac.testsvc
```

Left number is the pid if the service is running

### Stop
```bash
$ ./svc.sh stop

stopping vsts.agent.bryanmac.testsvc
status vsts.agent.bryanmac.testsvc:

/Users/bryanmac/Library/LaunchAgents/vsts.agent.bryanmac.testsvc.plist

Stopped
```

### Uninstall
```bash
$ ./svc.sh uninstall

```

## OSX Auto Logon and Lock

On OSX the convenience default is to create the service as a LaunchAgent.  A LaunchAgent runs when the user logs which gives it access to the UI for UI tests.  If you want it start when the box reboots, you can configure it to auto logon that account and lock on startup.

[Auto Logon and Lock](http://www.tuaw.com/2011/03/07/terminally-geeky-use-automatic-login-more-securely/)

## Setting the Environment

When you install and/or configure tools, your path is often setup or other environment variables are set.  Examples are PATH, JAVA_HOME, ANT_HOME, MYSQL_PATH etc...

If your environment changes at any time, you can run env.sh and it will update your path.  You can also manually edit .env file.  Changes are retained. 

Stop and start the service for changes to take effect.

```bash
$ ./env.sh 
$ sudo ./svc.sh stop
...
Stopped

$ sudo ./svc.sh start
...
Started:
15397 0 vsts.agent.bryanmac.testsvc2
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

The update checks these rules without running customer code, copies the accepted lines unchanged to a new `runsvc.hook.sh` with the same owner and permissions as `runsvc.sh`, then replaces `runsvc.sh`. If the update later fails, the original `runsvc.sh` is restored and the new hook is removed. Migration success is reported only after switching both active agent directories.

If `runsvc.sh` is a symbolic link or has customizations that do not meet these rules, the agent update is blocked before switching the active agent binaries. An existing `runsvc.hook.sh` blocks automatic migration of a customized script, not an upgrade of an otherwise-stock script; stock-script upgrades preserve the hook. The existing files are left untouched, and the new script is saved as `runsvc.sh.new` when possible without overwriting an existing file. To retry, back up the script and any symlink target, manually review and move required customizations to `runsvc.hook.sh`, and restore a known stock script. The error appears in service output and `_diag/SelfUpdate-*.log.failed`; agents with this change also warn in the listener log when the root script differs from the shipped script. Historical stock scripts are recognized, but the target agent's operating system requirements still apply.

## Service Files

This is a convenience that simply creates a service file for you can sets permissions.  You are free to manually configure and control your service using alternate methods.

Details are in .service file in root of the agent

OSX LaunchAgent: ~/Library/LaunchAgents/vsts.agent.{accountName}.{agentName}.plist
Linux SystemD: /etc/systemd/system/vsts.agent.{accountName}.{agentName}.service

These files are created from a template located

OSX: ./bin/vsts.agent.plist.template

For example, on OSX you could use that template to run as a launch daemon if you are not needing UI tests and/or don't want to configure auto logon lock. [Details Here](https://developer.apple.com/library/mac/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html)
