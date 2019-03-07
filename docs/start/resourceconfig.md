# Configure Resource Limits for Azure Pipelines Agent

## Linux

### Memory
When the agent on a Linux system that is under high memory pressure, it is important to ensure that the agent does not get killed or become otherwise unusable.
If the agent process dies or runs out of memory it cannot stream pipeline logs or report pipeline status back to the server, so it is preferable
to reclaim system memory from pipeline job processes before the agent process.

#### CGroups
`cgroups` can be used to prevent job processes from consuming too many resources or to isolate resources between multiple agents. For a single agent it is useful
to isolate the agent from the jobs it runs. The following `cgconfig.conf` [file](https://linux.die.net/man/5/cgconfig.conf) sets up two cgroups that impose different
memory limits:

```
group agent {
    memory {}
}
group job {
    memory {
        memory.limit_in_bytes = 7g;
    }
}
```
In this scenario, the agent cgroup has no limit set, so it inherits from the root or parent cgroup. By default, this would be the root cgroup which has no limit. The job cgroup
imposes a limit of 7g of memory. This can be used in conjunction with the following `cgrules.conf` [config file](https://linux.die.net/man/5/cgrules.conf):

```
agent_user:Agent.Listener memory agent
agent_user:Agent.Worker memory agent
agent_user memory job
```
It is important to use two groups, because otherwise the pipeline job processes will inherit the group from their parent, the agent, so there will be no distinction in terms of control.
A second "job" cgroup allows the job processes to be managed independent of the agent, e.g. in an out-of-memory scenario (when the job exceeds the limits given by the `job` cgroup), the job
will be killed instead of the agent. If a single cgroup is used, the agent may killed to reclaim memory from the cgroup.

#### Understanding the Out of Memory Killer
If a Linux system runs out of memory, it invokes the oom-killer to reclaim memory. The oom-killer chooses a process to sacrifice based on heuristics,
and adjusted by `oom_score_adj`. Higher scores are more likely to get killed, and range from -1000 to 1000. It is important that the agent process has a lower
score than the job processes it manages, because if the agent is killed the job effectively dies as well.

The agent can help manage process oom scores. By default, processes that are invoked by the agent will have an oom score of 500. This score can be overriden with
the `VSTS_JOB_OOMSCOREADJ` environment variable. This value must be between -1000 and 1000 and cannot be less than the `oom_score_adj` of the agent process, which
is 0 by default. Typically this does not need to change unless there are other processes on your host that need to be more or less likely to be killed.

There are multiple ways to set the agent `oom_score_adj`. When running interactively the score can be set in the shell, and will be inherited by the agent:

```bash
$ echo $oomScoreAdj > /proc/$$/oom_score_adj
$ ./run.sh
```

If the agent is being managed by systemd, the `OOMScoreAdjust` directive can be set in the unit file:
```
$ cat /etc/systemd/system/vsts.agent.user.linux-host.service
[Unit]
Description=VSTS Agent (user.linux-host)
After=network.target

[Service]
ExecStart=/home/user/agent/runsvc.sh
User=user
WorkingDirectory=/home/user/agent
KillMode=process
KillSignal=SIGTERM
TimeoutStopSec=5min
OOMScoreAdjust=-999

[Install]
WantedBy=multi-user.target
```

In this configuration, the `Agent.Listener` and `Agent.Worker` processes will run with `oom_score_adj = -999`, and all other processes invoked by the agent will have 500 by default, or the value given by `VSTS_JOB_OOMSCOREADJ`, ensuring the agent is kept alive even if the job causes out-of-memory conditions.