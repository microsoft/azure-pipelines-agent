<# 
A PowerShell script that is used to invoke a VSTS task script. This script is used by the VSTS task runner to invoke the task script.
This script replaces some legacy stuff in PowerShell3Handler.cs and turns it into a dedicated signed script. 
since it is parameterized it can be signed and trusted for WDAC and CLM.
#>

param ( 
    [Parameter(mandatory = $true)]
    [string]$VstsSdkPath,

    [Parameter(mandatory = $true)]
    [string]$DebugOption,

    [Parameter(mandatory = $true)]
    [string]$ScriptBlockString

)

function Get-ClmStatus {
    # This is new functionality to detect if we are running in a constrained language mode.
    # This is only used to display debug data if the device is in CLM mode by default.

    # Create a temp file and add the command which not allowed in constrained language mode.
    $tempFileGuid = New-Guid | Select-Object -Expand Guid 
    $tempFile = "$($env:AGENT_TEMPDIRECTORY)\$($tempFileGuid).ps1"

    Write-Output '$null = New-Object -TypeName System.Collections.ArrayList' | Out-File -FilePath $tempFile

    try {
        . $tempFile
        $status = "FullLanguage"
    }
    catch [System.Management.Automation.PSNotSupportedException] {
        $status = "ConstrainedLanguage"
    }

    Remove-Item $tempFile 
    return $status 
}

$VerbosePreference = $DebugOption
$DebugPreference = $DebugOption 

if (!$PSHOME) { 
    Write-Error -Message "The execution cannot be continued since the PSHOME variable is not defined." -ErrorAction Stop
}

# Check if the device is in CLM mode by default.
$clmResults = Get-ClmStatus
Write-Verbose "PowerShell Language mode: $($clmResults)"

if ([Console]::InputEncoding -is [Text.UTF8Encoding] -and [Console]::InputEncoding.GetPreamble().Length -ne 0) {
    [Console]::InputEncoding = New-Object Text.UTF8Encoding $false 
}

Import-Module -Name ([System.IO.Path]::Combine($PSHOME, 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1')) 
Import-Module -Name ([System.IO.Path]::Combine($PSHOME, 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1'))

$importSplat = @{
    Name        = $VstsSdkPath 
    ErrorAction = 'Stop'
}

# Import the module and catch any errors
try {
    Import-Module @importSplat     
}
catch {
    Write-Verbose $_.Exception.Message -Verbose 
    throw $_.Exception
}

# Now create the task and hand of to the task script
try {
    Invoke-VstsTaskScript -ScriptBlock ([scriptblock]::Create( $ScriptBlockString ))
}
# We want to add improved error handling here - if the error is "xxx\powershell.ps1 is not recognized as the name of a cmdlet, function, script file, or operable program"
# 
catch {
    Write-Verbose "Invoke-VstsTaskScript -ScriptBlock ([scriptblock]::Create( $ScriptBlockString ))"
    Write-Verbose $_.Exception.Message -Verbose 
    throw $_.Exception
}
#
