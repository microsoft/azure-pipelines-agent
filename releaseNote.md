## Features
 - Changes to skip Not Executed state test in result.publish() command (#5116)
 - Updated Node 20 version to safe version (#5118)
 - Custom tasks that use Node 16 runner version should emit a warning (#5125)
 - Optionally use max parallelism value from the client settings API (#5132)
 - Add output variable for type of artifact downloaded. (#5133)

## Bugs
 - Suppressing CodeQL [SM02196] warning (#5122)
 - Skip removing read only attribute for Symlinks AB#2248480 (#5144)

## Misc
 - Update ADO Client package to 251 (#5112)
 - Check git version before running sparse checkout (#5117)
 - Remove unused maxParallelism parameter from CreateDedupManifestClientAsync (#5131)
 - add Ubuntu 20 to the list of deprecated iamges (#5148)
 - Fix indentation in secret masking files (#5163)


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
| Linux musl ARM64 | [vsts-agent-linux-musl-arm64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-musl-arm64-<AGENT_VERSION>.tar.gz) | <HASH> |

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

***Note:*** Node 6 does not exist for Alpine.

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-linux-musl-x64-<AGENT_VERSION>.tar.gz
```

## Alpine ARM64

***Note:*** Node 6 does not exist for Alpine.

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-linux-musl-ARM64-<AGENT_VERSION>.tar.gz
```

## Alternate Agent Downloads

Alternate packages below do not include Node 6 & 10 and are only suitable for users who do not use Node 6 & 10 dependent tasks.
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
