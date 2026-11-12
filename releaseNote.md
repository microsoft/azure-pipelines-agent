
## Agent Downloads

|                | Package | SHA-256 |
| -------------- | ------- | ------- |
| Windows x64    | [vsts-agent-win-x64-<AGENT_VERSION>.zip](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-win-x64-<AGENT_VERSION>.zip) | <HASH> |
| Windows x86    | [vsts-agent-win-x86-<AGENT_VERSION>.zip](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-win-x86-<AGENT_VERSION>.zip) | <HASH> |
| Windows ARM64  | [vsts-agent-win-arm64-<AGENT_VERSION>.zip](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-win-arm64-<AGENT_VERSION>.zip) | <HASH> |
| macOS x64      | [vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| macOS ARM64    | [vsts-agent-osx-arm64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-osx-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux x64      | [vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux ARM      | [vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux ARM64    | [vsts-agent-linux-arm64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-linux-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux musl x64 | [vsts-agent-linux-musl-x64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-linux-musl-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux musl ARM64 | [vsts-agent-linux-musl-arm64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/vsts-agent-linux-musl-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |

After Download:

## Windows x64

``` bash
C:\> mkdir myagent && cd myagent
C:\myagent> Add-Type -AssemblyName System.IO.Compression.FileSystem ; [System.IO.Compression.ZipFile]::ExtractToDirectory("$HOME\Downloads\vsts-agent-win-x64-<AGENT_VERSION>.zip", "$PWD")
```

## Windows x86

``` bash
C:\> mkdir myagent && cd myagent
C:\myagent> Add-Type -AssemblyName System.IO.Compression.FileSystem ; [System.IO.Compression.ZipFile]::ExtractToDirectory("$HOME\Downloads\vsts-agent-win-x86-<AGENT_VERSION>.zip", "$PWD")
```

## Windows ARM64

``` bash
C:\> mkdir myagent && cd myagent
C:\myagent> Add-Type -AssemblyName System.IO.Compression.FileSystem ; [System.IO.Compression.ZipFile]::ExtractToDirectory("$HOME\Downloads\vsts-agent-win-arm64-<AGENT_VERSION>.zip", "$PWD")
```

## macOS x64

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz
```

## macOS ARM64

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-osx-arm64-<AGENT_VERSION>.tar.gz
```

## Linux x64

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz
```

## Linux ARM

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz
```

## Linux ARM64

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-linux-arm64-<AGENT_VERSION>.tar.gz
```

## Alpine x64

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-linux-musl-x64-<AGENT_VERSION>.tar.gz
```

## Alpine ARM64

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-linux-musl-ARM64-<AGENT_VERSION>.tar.gz
```

## Alternate Agent Downloads

The `pipelines-agent` package now includes only Node 24; `vsts-agent` continues to include Node 20 and Node 24, subject to platform availability. Because Node 24 has no official Windows x86 or Linux ARM32 build, `pipelines-agent` is not published for those platforms.
`AGENT_RESTRICT_EOL_NODE_VERSIONS=true` selects Node 24 for agent-managed task runners and fails if that runtime cannot run; an explicitly configured custom Node path can override the policy. With the policy disabled, tasks are not automatically upgraded to Node 24; a Node 20 task fails if its runner is missing or incompatible, unless an explicit override is configured.
With the EOL policy disabled, `AGENT_WARN_ON_NODE20_TASK=true` enables a Node 20 retirement warning when Node 20 is selected. Container startup uses Node 24 when the bundled Node 20 runner is absent.
See [agent package notes](docs/agentpackages.md) on Node version support for more details.

|             | Package | SHA-256 |
| ----------- | ------- | ------- |
| Windows x64 | [pipelines-agent-win-x64-<AGENT_VERSION>.zip](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/pipelines-agent-win-x64-<AGENT_VERSION>.zip) | <HASH> |
| Windows ARM64 | [pipelines-agent-win-arm64-<AGENT_VERSION>.zip](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/pipelines-agent-win-arm64-<AGENT_VERSION>.zip) | <HASH> |
| macOS x64     | [pipelines-agent-osx-x64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/pipelines-agent-osx-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| macOS ARM64 | [pipelines-agent-osx-arm64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/pipelines-agent-osx-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux x64   | [pipelines-agent-linux-x64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/pipelines-agent-linux-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux ARM64 | [pipelines-agent-linux-arm64-<AGENT_VERSION>.tar.gz](https://download.agent.dev.azure.com/agent/<AGENT_VERSION>/pipelines-agent-linux-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |
