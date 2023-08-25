$ErrorActionPreference = 'Stop'

if ($pwd -notlike '*tfsgheus20' ) {
    Remove-DistributedTaskPackage -PackageType agent -Platform win-x64 -Version <AGENT_VERSION>

    Remove-DistributedTaskPackage -PackageType agent -Platform win-x86 -Version <AGENT_VERSION>

    Remove-DistributedTaskPackage -PackageType agent -Platform osx-x64 -Version <AGENT_VERSION>

    Remove-DistributedTaskPackage -PackageType agent -Platform linux-x64 -Version <AGENT_VERSION>

    Remove-DistributedTaskPackage -PackageType agent -Platform linux-arm -Version <AGENT_VERSION>

    Remove-DistributedTaskPackage -PackageType agent -Platform osx-arm64 -Version <AGENT_VERSION>
}