[CmdletBinding()]
param()

function Get-MavenDirectoryFromPath {
    $mavenBin = "$env:PATH".Split(';') |
        ForEach-Object { "$_".Trim() } |
        Where-Object { "$_" -clike "*Maven\*\bin*" } |
        Select-Object -First 1

    if (!$mavenBin) {
        return
    }

    if (!(Test-Container -LiteralPath $mavenBin)) {
        return
    }

    return [System.IO.Directory]::GetParent($mavenBin.TrimEnd([System.IO.Path]::DirectorySeparatorChar)).FullName
}

Write-Host "Checking: env:JAVA_HOME"
if (!$env:JAVA_HOME) {
    Write-Host "Value not found or empty."
    return
}

if($env:M2_HOME) {
    Add-CapabilityFromEnvironment -Name 'maven' -VariableName 'M2_HOME'
} else {
	Write-Host "M2_HOME not set. Checking in PATH"

    # Determine the Maven directory from the PATH.
    $maven_directory = Get-MavenDirectoryFromPath

    Write-Host "maven_directory is '$maven_directory'"

    if($maven_directory) {
        Write-Capability -Name 'maven' -Value $maven_directory
    }
}