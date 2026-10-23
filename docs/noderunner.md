# Node 6 support

Agent tasks can be implemented in PowerShell or Node. The agent currently ships with multiple versions of Node that tasks can target: 6, 10, 16, 20 & 24.

Since Node 6 has long passed out of the upstream maintenance window, and all officially supported tasks are migrated from Node 6 to Node 10, Node 6 soon will be removed from the agent package. 
It's also highly recommended to third-party task maintainers migrate tasks to Node 10 or Node 16.

However, to support backward compatibility with the Node 6 tasks we provide self-service methods to install the designated Node runner manually.

## Install Node 6 runner manually

To support the execution of Node 6 tasks agent should be provided with the latest Node 6 version - `6.17.1.0`.

Despite that Node 6 is officially reached the End-of-Life, please, notice that it still can have maintenance updates, so it is required for the agent to get the latest binaries. You can check the currently existing Node versions [here](https://nodejs.org/dist/).

Please use the following steps to manually install the required runner:

1. Download the latest available version of Node 6 binaries for your operating system from the official Node [registry](https://nodejs.org/dist/).

1. Create a folder named `node` under the `agent/externals` directory, and extract downloaded Node binaries into that folder.

You can also use the following commands to install the Node 6 runner via the Powershell or Bash:

Windows:
```powershell
    $agentFolder = ""   // Specify the Azure DevOps Agent folder, e.g. C:\agents\my_agent
    $osArch = ""        // Specify the OS architecture, e.g. x64 / x86

    New-Item -Type Directory -Path "${agentFolder}\externals\node"

    Invoke-WebRequest -Uri "https://nodejs.org/dist/v6.17.1/win-${osArch}/node.exe" -OutFile "${agentFolder}\externals\node\node.exe"
    Invoke-WebRequest -Uri "https://nodejs.org/dist/v6.17.1/win-${osArch}/node.lib" -OutFile "${agentFolder}\externals\node\node.lib"
```

Linux / macOS:
```bash
    agent_folder=""   // Specify the Azure DevOps Agent folder, e.g. /home/user/agents/my_agent
    os_platform=""    // Specify the OS platform, e.g. linux / darwin
    os_arch=""        // Specify the OS architecture, e.g. x64 / x86

    mkdir "${agent_folder}/externals/node"

    wget -O "/tmp/node-v6.17.1-${os_platform}-${os_arch}.tar.gz" "https://nodejs.org/dist/v6.17.1/node-v6.17.1-${os_platform}-${os_arch}.tar.gz"

    tar -xvf "/tmp/node-v6.17.1-${os_platform}-${os_arch}.tar.gz" -C "${agent_folder}/externals/node/"
```

## Install Node runner via NodeTaskRunnerInstaller

You can also use the Azure DevOps task [NodeTaskRunnerInstaller](https://github.com/microsoft/azure-pipelines-tasks/tree/master/Tasks/NodeTaskRunnerInstallerV0) to install the required runner version via Azure DevOps CI.

Use the following pipeline task sample to install the latest version of Node 6 runner:

```yaml
  - task: NodeTaskRunnerInstaller@0
    inputs:
      runnerVersion: 6
```

Please, check more details in [NodeTaskRunnerInstaller task](https://learn.microsoft.com/azure/devops/pipelines/tasks/reference/node-task-runner-installer-v0) documentation.

## Scheduled Node CVE monitoring

`.azure-pipelines/agent-node-cve-monitoring.yml` checks all bundled Node pins
in `src/Misc/externals.sh` daily at 06:00 UTC on `master`, even when the
repository has not changed. It discovers `NODE_VERSION`,
`NODE<major>_VERSION`, and platform-specific `NODE<major>_<variant>_VERSION`
assignments automatically, rather than maintaining a fixed list of majors.

The [official Node.js lifecycle schedule](https://github.com/nodejs/Release/blob/main/schedule.json)
determines which bundled versions are currently in Active LTS or Maintenance
LTS. Both phases are checked. Eligibility is evaluated using the run's UTC
date, starting on the `lts` date and stopping on the `end` date. End-of-life,
Current/pre-LTS, and not-yet-released lines are marked as not checked in the
summary, with lifecycle dates in JSON; they do not cause security-update failures.
Unknown majors or invalid lifecycle data fail the check as incomplete.
The release index's historical `lts` labels are not used to decide whether
a line is still supported. A newly bundled major is included automatically
when its scheduled LTS period starts, including platform-specific pins.
Supported upstream lines the agent does not bundle are outside this check's
scope, as is the monitoring machine's Node runtime.

The [Node.js release index](https://nodejs.org/dist/index.json) is the source
for security release decisions. For each eligible pin, the check selects the
newest stable version in the same major marked `security: true`, comparing
semantic versions rather than release dates or index ordering. If any pin is
older, the pipeline fails and reports the variable, pinned version, latest
security release, and release date. This failure does not depend on individual
CVE records being available. Prereleases and other majors are excluded.
A newer nonsecurity release only produces a warning when that pin already
includes its major's latest security release.

The [Node.js security working group's core advisory index](https://github.com/nodejs/security-wg/blob/main/vuln/core/index.json)
provides available CVE details. The check evaluates both vulnerable and patched
version ranges, across all agent platforms. Applicable indexed CVEs also fail
the pipeline, even if no newer release is marked `security: true`. The index
can lag disclosure: if no matching CVEs are listed but a newer security release
exists, the report explicitly states that the check still fails for the
security update. CVE identifiers are never inferred from a release flag.
No RSS feeds or security blog pages are fetched.

A failed download, invalid or duplicate pin, invalid source data, or
missing/non-boolean security flag on a checked major's stable release fails
the check as incomplete rather than treating unknown status as `false`.

Every completed check publishes Markdown and JSON reports as the
`node-cve-monitoring` pipeline artifact, including when a security update is
required, an applicable CVE is found, or the check is incomplete. The JSON
report (`node-lts-report.json`) contains `checked` and `skipped` pin arrays,
with lifecycle status and dates. Each checked pin includes
`latestSecurityVersion`, `latestSecurityReleaseDate`, `securityUpdateRequired`,
and any applicable indexed CVEs. If a major has no stable security release,
its latest security version and date are `null`. When no bundled pins are
currently in LTS, the check succeeds with a warning and explicitly reports
that no security comparisons were performed, rather than claiming a clean
vulnerability assessment. The Markdown report is `node-lts-report.md`.
A clean result depends on upstream flags and advisory coverage; it is not a
guarantee that no vulnerabilities exist. A security flag does not establish
which CVEs affect the pin or exploitability in an agent job.

### Requesting missing Alpine builds

The scheduled pipeline also checks
[Alpine-NodeJs-Build](https://dev.azure.com/mseng/AzureDevOps/_build?definitionId=21894&_a=summary)
in `mseng/AzureDevOps`. For each outdated bundled LTS major, it requests
the **latest stable release**, including when the update is nonsecurity-only.
Multiple packaging pins targeting the same version produce only one request.
End-of-life and not-yet-LTS pins never request builds.

Before queuing, it scans retained runs of pipeline 21894, following
pagination, and matches the exact `nodeVersion` template parameter. It keeps
only matching runs and stops once every target version is found. If any
target is missing, it completes the history scan before queuing. Any
existing run prevents another request, regardless of branch, pool, status,
or result. Failed and canceled runs are reported with a warning and link,
not retried automatically. Deleted or expired runs are no longer available
for duplicate detection.

New runs use `refs/heads/master` of `CIPlat.Externals` with
`nodeVersion: <latest stable version>` and
`poolName: 1ES-ABTT-Shared-ARM-64-Pool`, matching the pool used by successful
Alpine builds. That pipeline builds the binary and opens its normal binary
update PR; this monitor does not wait for completion, change agent pins, or
clear the original security failure. Markdown and JSON reports include an
`alpineBuilds` list of existing/queued runs and any per-version queue errors.
The Markdown summary lists pin status, versions, CVE identifiers, and Alpine
run outcomes. CVE descriptions, severity, patched ranges, and lifecycle/release
dates remain in JSON. Console output contains only actionable warnings/errors
and report-publication commands, rather than repeating the full report.

The YAML maps `System.AccessToken` only to the monitoring step. Its build
identity needs **View builds** and **Queue builds** on pipeline 21894; no
personal access token is stored in the repository or passed on the command
line. Authorization failures, incomplete history, and queue errors fail the
check with exit code `2`, while preserving the security findings and any
successful requests in the report. Queue requests are not retried after
ambiguous failures. History checks and queuing are separate API operations,
so avoid overlapping monitoring runs when a new version first appears.

Register an Azure DevOps pipeline named `Agent - Node CVE monitoring` pointing
to `.azure-pipelines/agent-node-cve-monitoring.yml`. Use `master` as the default
branch after merging the YAML.
Keep UI-defined schedules empty so the YAML schedule takes effect. The default
pool is Microsoft-hosted `ubuntu-latest`; organizations using self-hosted pools
can replace the `pool` parameter default. For manual testing in `tarun-msft` /
`tasks test`, pass `pool: { name: AgentTestPool }` (queue 27). No pipeline
registration, notification subscription, or version bump is performed by the script.
Configure Azure DevOps build-completion notifications separately if email alerts
are required. Updating the pin still requires the usual agent build and
platform-specific packaging verification.

To run the monitoring tests and a live check locally, from the repository root:

```powershell
Push-Location .azure-pipelines\scripts
npm ci --ignore-scripts --no-audit --no-fund
npm test
node check-node-lts.js "$env:TEMP\agent-node-cve-monitoring"
Pop-Location
```

Local checks do not access or trigger Azure DevOps builds by default. To
enable the same build behavior as the scheduled pipeline, append
`--trigger-alpine-builds` and supply a short-lived authorized token through
the `SYSTEM_ACCESSTOKEN` environment variable.

The script uses Node 24 as its monitoring runtime, but its scope is all
currently LTS bundled pins. Its exit codes are `0` for no required security
update or applicable indexed CVEs among checked pins (including a
nonsecurity-only warning or an explicitly empty LTS scope), `1` when any
checked pin requires a security update or has applicable indexed CVEs, and
`2` for an incomplete check, Alpine build request failure, or reporting failure.
The Markdown and JSON output directory is the script's only required argument.
