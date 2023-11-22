
## Agent Downloads

|                | Package | SHA-256 |
| -------------- | ------- | ------- |
| Windows x64    | [vsts-agent-win-x64-<AGENT_VERSION>.zip](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-win-x64-<AGENT_VERSION>.zip) | <HASH> |
| Windows x86    | [vsts-agent-win-x86-<AGENT_VERSION>.zip](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-win-x86-<AGENT_VERSION>.zip) | <HASH> |
| macOS x64      | [vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| macOS ARM64    | [vsts-agent-osx-arm64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-osx-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux x64      | [vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux ARM      | [vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux ARM64    | [vsts-agent-linux-arm64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux musl x64 | [vsts-agent-linux-musl-x64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-musl-x64-<AGENT_VERSION>.tar.gz) | <HASH> |

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

***Note:*** Node 6 does not exist for Alpine.

## Alternate Agent Downloads

Alternate packages below do not include Node 6 and are only suitable for users who do not use Node 6 dependent tasks. 
See [notes](docs/node6.md) on Node version support for more details.

|             | Package | SHA-256 |
| ----------- | ------- | ------- |
| Windows x64 | [pipelines-agent-win-x64-<AGENT_VERSION>.zip](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-win-x64-<AGENT_VERSION>.zip) | <HASH> |
| Windows x86 | [pipelines-agent-win-x86-<AGENT_VERSION>.zip](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-win-x86-<AGENT_VERSION>.zip) | <HASH> |
| macOS x64   | [pipelines-agent-osx-x64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-osx-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| macOS ARM64 | [pipelines-agent-osx-arm64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-osx-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux x64   | [pipelines-agent-linux-x64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-linux-x64-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux ARM   | [pipelines-agent-linux-arm-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-linux-arm-<AGENT_VERSION>.tar.gz) | <HASH> |
| Linux ARM64 | [pipelines-agent-linux-arm64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-linux-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |
