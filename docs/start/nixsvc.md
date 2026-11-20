# Running As A Service On Unix and OSX

Key Points:
  - This is a convenience which only creates OS specific service files.
  - SystemD is used on Linux.  Ubuntu 16 LTS, Redhat 7.1 has SystemD
  - SystemD requires sudo so all scripts below must be called with sudo.  OSX does not.

## Managing the Service

./svc.sh was generated to manage your service

## Install

Install will create a LaunchAgent plist on OSX or a systemd unit file on Linux

```bash
$ ./svc.sh install
...
Creating runsvc.sh
Creating .Service
svc install complete
```

Service files point to `./runsvc.sh` which will setup the environment and start the agents host.  See Environment section below.

### Start
```bash
$ sudo ./svc.sh start

starting vsts.agent.bryanmac.testsvc
status vsts.agent.bryanmac.testsvc:

/Users/bryanmac/Library/LaunchAgents/vsts.agent.bryanmac.testsvc.plist

Started:
25324 0 vsts.agent.bryanmac.testsvc
```

### Status
```bash
$ sudo ./svc.sh status

status vsts.agent.bryanmac.testsvc:

/Users/bryanmac/Library/LaunchAgents/vsts.agent.bryanmac.testsvc.plist

Started:
25324 0 vsts.agent.bryanmac.testsvc
```

Left number is the pid if the service is running

### Stop
```bash
$ sudo ./svc.sh stop

stopping vsts.agent.bryanmac.testsvc
status vsts.agent.bryanmac.testsvc:

/Users/bryanmac/Library/LaunchAgents/vsts.agent.bryanmac.testsvc.plist

Stopped
```

### Uninstall

> NOTE: Stop is a separate command with launchd (script is a thin wrapper).  You likely want to stop before uninstalling.

```bash
$ sudo ./svc.sh uninstall

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

./runsvc.sh
```
# insert anything to setup env when running as a service
```

During self-update, a root `runsvc.sh` that matches a known stock script is replaced with the entire script from the new agent. The previous script is saved as `runsvc.sh.<previous version>.bak.<suffix>`. The new script takes effect the next time the service starts.

If `runsvc.sh` is customized, a symbolic link, or otherwise unrecognized, the agent update is blocked before switching the active agent binaries. The existing script is left untouched, and the new script is saved as `runsvc.sh.new` when possible without overwriting an existing file. To retry, back up the script and any symlink target, move required customizations to your service configuration, and restore a known stock script. The error appears in service output and `_diag/SelfUpdate-*.log.failed`; agents with this change also warn in the listener log when the root script differs from the shipped script. Historical stock scripts are recognized, but the target agent's operating system requirements still apply.

## Service Files

This is a convenience that simply creates a service file for you can sets permissions.  You are free to manually configure and control your service using alternate methods.

Details are in .service file in root of the agent

OSX LaunchAgent: ~/Library/LaunchAgents/vsts.agent.{accountName}.{agentName}.plist
Linux SystemD: /etc/systemd/system/vsts.agent.{accountName}.{agentName}.service

These files are created from a template located

OSX: ./bin/vsts.agent.plist.template
Linux: ./bin/vsts.agent.service.template

For example, on OSX you could use that template to run as a launch daemon if you are not needing UI tests and/or don't want to configure auto logon lock. [Details Here](https://developer.apple.com/library/mac/documentation/MacOSX/Conceptual/BPSystemStartup/Chapters/CreatingLaunchdJobs.html)

