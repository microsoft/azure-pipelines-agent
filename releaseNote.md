## Features
- Add retry handler for the CreateArtifactAsync function (#2495)
- Throw appropriate exception in case Git or Git LFS could not be found (#1825)
- Log when a process is exited abnormally (#2501)
- Add Dotnet Core capability detection (#1845)
- Moving ContainerInfo to Agent.Sdk (#2476)
- add a welcome banner at config time (#2531)
- Add an option to reduce checkout verbosity (#2547)
- Make tarring default for Pipeline Caching and modifying Display to Info from Warning for Save Cache (#2535)

## Bugs
- Use DirectorySeparatorChar in git path (#2492)
- Ignore fileshare artifacts in the DPA V1 task (#2518)
- Fix to remove hardcoded instanceId (#2524)
- Fix null ref with new env var (#2526)
- fix retrieval of ImageVersion environment variable (#2539)
- Fixed to not error out on null source volume path (#2551)
- Fixed null ref getting relative paths (#2603)

## Misc
- future-proof AZP_CACHING_CONTENT_FORMAT (#2400)
- Support for multi-repo scenarios (#2485)

## Agent Downloads  

|         | Package                                                                                                       |
| ------- | ----------------------------------------------------------------------------------------------------------- |
| Windows x64 | [vsts-agent-win-x64-<AGENT_VERSION>.zip](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-win-x64-<AGENT_VERSION>.zip)      |
| Windows x86 | [vsts-agent-win-x86-<AGENT_VERSION>.zip](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-win-x86-<AGENT_VERSION>.zip)      |
| macOS   | [vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz)   |
| Linux x64  | [vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz) |
| Linux ARM  | [vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz) |
| RHEL 6 x64  | [vsts-agent-rhel.6-x64-<AGENT_VERSION>.tar.gz](https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-rhel.6-x64-<AGENT_VERSION>.tar.gz) |

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

## OSX

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz
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

## RHEL 6 x64

``` bash
~/$ mkdir myagent && cd myagent
~/myagent$ tar xzf ~/Downloads/vsts-agent-rhel.6-x64-<AGENT_VERSION>.tar.gz
```
