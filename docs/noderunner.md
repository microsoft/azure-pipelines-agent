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

## Scheduled Node 24 security monitoring

`.azure-pipelines/node24-security.yml` checks the `NODE24_VERSION` pin in
`src/Misc/externals.sh` daily at 06:00 UTC on `master`, even when the repository
has not changed. It does not inspect the Node runtime on the monitoring machine
or the older Node major versions shipped for backward compatibility.

The [Node.js release index](https://nodejs.org/dist/index.json) is the source
for security release decisions. The check selects the newest stable Node 24
version marked `security: true`, comparing semantic versions rather than
release dates or index ordering. If the agent pin is older, the pipeline fails
and reports the pinned version, latest security release, and release date.
This failure does not depend on individual CVE records being available.
Releases from other major versions and prereleases are excluded. A newer
nonsecurity release only produces a warning when the pin already includes
the latest security release.

The [Node.js security working group's core advisory index](https://github.com/nodejs/security-wg/blob/main/vuln/core/index.json)
provides available CVE details. The check evaluates both vulnerable and patched
version ranges, across all agent platforms. Applicable indexed CVEs also fail
the pipeline, even if no newer release is marked `security: true`. The index
can lag disclosure: if no matching CVEs are listed but a newer security release
exists, the report explicitly states that the check still fails for the
security update. CVE identifiers are never inferred from a release flag.
No RSS feeds or security blog pages are fetched.

A failed download, invalid pin, invalid source data, or missing/non-boolean
security flag on a stable Node 24 release fails the check as incomplete rather
than treating unknown security status as `false`.

Every completed check publishes Markdown and JSON reports as the
`node24-security` pipeline artifact, including when a security update is
required, an applicable CVE is found, or the check is incomplete. The JSON
includes `latestSecurityVersion`, `latestSecurityReleaseDate`, and
`securityUpdateRequired`, in addition to the current pin and any indexed CVEs.
If no stable Node 24 release is marked as a security release, the latest
security version and date are `null` and the report states this explicitly.
A clean result depends on upstream flags and advisory coverage; it is not a
guarantee that no vulnerabilities exist. A security flag does not establish
which CVEs affect the pin or exploitability in an agent job.

After merging, register a new Azure DevOps YAML pipeline pointing to
`.azure-pipelines/node24-security.yml`, with `master` as its default branch.
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
node check-node24.js "$env:TEMP\agent-node24-security"
Pop-Location
```

The script requires Node 24. Its exit codes are `0` for no required security
update and no applicable indexed CVEs (including a nonsecurity-only update
warning), `1` for a required security update or applicable indexed CVEs, and
`2` for an incomplete check or reporting failure. The Markdown and JSON output
directory is the script's only required argument.
