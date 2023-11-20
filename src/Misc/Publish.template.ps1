$ErrorActionPreference = 'Stop'

if ($pwd -notlike '*tfsgheus20') {

    # primary packages

    Add-DistributedTaskPackage -PackageType agent -Platform win-x64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-win-x64-<AGENT_VERSION>.zip -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename vsts-agent-win-x64-<AGENT_VERSION>.zip

    Add-DistributedTaskPackage -PackageType agent -Platform win-x86 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-win-x86-<AGENT_VERSION>.zip -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename vsts-agent-win-x86-<AGENT_VERSION>.zip

    Add-DistributedTaskPackage -PackageType agent -Platform osx-x64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename vsts-agent-osx-x64-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType agent -Platform linux-x64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename vsts-agent-linux-x64-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType agent -Platform linux-arm -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename vsts-agent-linux-arm-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType agent -Platform linux-arm64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-arm64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename vsts-agent-linux-arm64-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType agent -Platform linux-musl-x64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-linux-musl-x64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename vsts-agent-linux-musl-x64-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType agent -Platform osx-arm64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/vsts-agent-osx-arm64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename vsts-agent-osx-arm64-<AGENT_VERSION>.tar.gz

    # alternate packages

    Add-DistributedTaskPackage -PackageType pipelines-agent -Platform win-x64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-win-x64-<AGENT_VERSION>.zip -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename pipelines-agent-win-x64-<AGENT_VERSION>.zip

    Add-DistributedTaskPackage -PackageType pipelines-agent -Platform win-x86 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-win-x86-<AGENT_VERSION>.zip -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename pipelines-agent-win-x86-<AGENT_VERSION>.zip

    Add-DistributedTaskPackage -PackageType pipelines-agent -Platform osx-x64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-osx-x64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename pipelines-agent-osx-x64-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType pipelines-agent -Platform linux-x64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-linux-x64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename pipelines-agent-linux-x64-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType pipelines-agent -Platform linux-arm -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-linux-arm-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename pipelines-agent-linux-arm-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType pipelines-agent -Platform linux-arm64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-linux-arm64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename pipelines-agent-linux-arm64-<AGENT_VERSION>.tar.gz

    Add-DistributedTaskPackage -PackageType pipelines-agent -Platform osx-arm64 -Version <AGENT_VERSION> -DownloadUrl https://vstsagentpackage.azureedge.net/agent/<AGENT_VERSION>/pipelines-agent-osx-arm64-<AGENT_VERSION>.tar.gz -HashValue <HASH_VALUE> -InfoUrl https://go.microsoft.com/fwlink/?LinkId=798199 -Filename pipelines-agent-osx-arm64-<AGENT_VERSION>.tar.gz
}
