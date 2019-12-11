## Features
- Add support for step targets (#2604)
- Add build number output variable (#2642)
- Optional task signature verification (#2571)

## Bugs
- Remove references to the commit message (#2627)
- A customer reported an issue of a NPE when initializing secret masker (#2633) 
- Fix string literal project name in the DPA V2 task (#2635)
- Fix download fileshare artifacts (#2643)
- Fix dotnet scripts. (#2650)
- Fixed variable expansion for container targets #2646 (#2649)
- Guard against potential null object referenced by issue 11902 in task repo (#2651)
- Update to guard against potential NPE (#2640)

## Misc
- Allow signing to pass (#2620) 
- update necore version (#2622) 
- React to nested artifacts (#2623)
- Throw a nice error when we can't start the tar process (#2625)
- never report things called SYSTEM_ACCESSTOKEN as capabilities (#2601)
- Load proxy bypass list during agent configuration (#2638)
- Minor tweaks to update scripts to make things consistent (#2631)
- Updated to not allow processing of worker commands embedded in git comment (#2645)
- Do not sign debug builds (#2636)

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
