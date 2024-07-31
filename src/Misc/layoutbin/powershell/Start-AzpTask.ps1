<# 
A PowerShell script that is used to invoke a VSTS task script. This script is used by the VSTS task runner to invoke the task script.
This script replaces some legacy stuff in PowerShell3Handler.cs and turns it into a dedicated signed script. 
since it is parameterized it can be signed and trusted for WDAC and CLM.
This is the original code that has been repalced - 
@". ([scriptblock]::Create('if ([Console]::InputEncoding -is [Text.UTF8Encoding] -and [Console]::InputEncoding.GetPreamble().Length -ne 0) {{ [Console]::InputEncoding = New-Object Text.UTF8Encoding $false }} if (!$PSHOME) {{ $null = Get-Item -LiteralPath ''variable:PSHOME'' }} else {{ Import-Module -Name ([System.IO.Path]::Combine($PSHOME, ''Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1'')) ; Import-Module -Name ([System.IO.Path]::Combine($PSHOME, ''Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1'')) }}')) 2>&1 | ForEach-Object {{ Write-Verbose $_.Exception.Message -Verbose }} ; Import-Module -Name '{0}' -ArgumentList @{{ NonInteractive = $true }} -ErrorAction Stop ; $VerbosePreference = '{1}' ; $DebugPreference = '{1}' ; Invoke-VstsTaskScript -ScriptBlock ([scriptblock]::Create('. ''{2}'''))",
#>

param ( 
    [Parameter(mandatory = $true)]$name, # Argument 0 
    [Parameter(mandatory = $true)]$DebugOption, # Argument 1
    [Parameter(mandatory = $true)]$scriptBlockString	# Argument 2 
)

function GetCLM {
    # This is new functionality to detect if we are running in a constrained language mode.
    # This is only used to display debug data if the device is in CLM mode by default.
    # This test is modeled after the traditional __PSSCRIPTPOLICYTEST_LQU1DAME file
    # This creates a temporary file with a changing signature 
    # which will return the execution context language mode. 
    $tempFileGuid = New-Guid | Select-Object -Expand Guid 
    $tempFile = "$($env:temp)\$($tempFileGuid).ps1"
    # Use the command which not allowed in constrained language mode.
    Write-Output 'New-Object -TypeName System.Collections.ArrayList' | Out-File -FilePath $tempFile -append 
    # Now we execute the file and return the result. We need to try catch this because of how PS handles 
    # the execution of CLM files in a non CLM environment.

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

# This is the notes we want to display if CLM is active on the device by default.
$verboseNotes = @"
##############################
## Basics ##
Start-AzpTask is a pre handler function to safely hand off PowerShell commands. 
It imports the module Invoke-VstsTaskScript and executes it with proper arguments.
## Workflow ## 
ADO Agent receives a request. The agent launches Agent.Worker.exe. 
When the PowerShell3Handler within Agent.Worker.exe executes it opens a PowerShell instance, 
launching Start-AzpTask script with arguments. Start-AzpTask receives a handler script file name to use
(such as xxx\_tasks\CopyFiles_<guid>\xxx), along with the file name of an instance of 
Invoke-VstsTaskScript for use (_tasks\xxx\xxx\ps_modules\VstsTaskSdk\xxx) 
Debug display and error handling is also forwarded here.
## Requirements ## 
For pre handler script to work correctly on CLM enabled device, 
full language mode must be available for itself, and everything it directly launches.
Start-AzpTask (the output you are reading) language mode is: $($ExecutionContext.SessionState.LanguageMode). 
If this isn't full language, fix this first.
This instance is running from: $($MyInvocation.MyCommand.Path).
Once Start-AzpTask is running in full language mode, verify that the 
VstsTaskScript and handler are also in full language mode.
Each script file used by Start-AzpTask must be signed or whitelisted to receive full language mode.
The VstsTaskScript requested by this instance is: 
$name
The handler being requested by this instance is: 
$scriptBlockString
Please note other supporting files may also need to be signed or whitelisted to receive full language mode.
These can be located using procmon, sysmon or similar tools.
## Error Causes ##
Start-AzpTask and the task handler scripts can run in different language modes. 
Mixing language modes is not supported and will result in errors. 
These may reflect as dot sourcing errors, or errors about a file not being recognized as 
a cmdlet, function, script file, or operable program.
## Advanced Usage ##
Inline PowerShell from pipeline YML task definition is not signed. It runs in CLM by default.
Signature blocks do not survive conversion from YML so inline scripts can't be signed.
If you need full language mode for the task action being run by the handler, 
you must execute it from disk, with proper signing or whitelisting in place.
##############################

"@

$VerbosePreference = $DebugOption
$DebugPreference = $DebugOption

# First we check if the device is in CLM mode by default.
$clmResults = GetCLM 
# Now the behavior based on CLM and debug settings.
# Case 1, device is full language, continue as normal 
if ( $clmResults -eq "FullLanguage" -and $DebugOption -ceq "Continue") {
    Write-Verbose "Full Language mode detected, continuing traditional workflow."
}
# Case 2, device is constrained language  
else {
    # 2a We have a request to display debug data and the device is in CLM
    if ( $DebugOption -ceq "Continue" ) {
        $verboseNotesString = $verboseNotes -split "\n" 
        foreach ( $line in $verboseNotesString ) { 
            Write-Verbose "$line" 
        }
    }
    else {
        # 2b We have a request to run in constrained language mode, but verbose is not set.
        Write-Output "Constrained Language Mode detected, but verbose is not set. "
        Write-Output "Suggest enabling system diagnostics if you need more information."
    }
    Write-Output ""
}

if ([Console]::InputEncoding -is [Text.UTF8Encoding] -and [Console]::InputEncoding.GetPreamble().Length -ne 0) {
    [Console]::InputEncoding = New-Object Text.UTF8Encoding $false 
} 

if (!$PSHOME) { 
    $null = Get-Item -LiteralPath 'variable:PSHOME' 
}
else { 
    Import-Module -Name ([System.IO.Path]::Combine($PSHOME, 'Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1')) 
    Import-Module -Name ([System.IO.Path]::Combine($PSHOME, 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1')) 
}

$importSplat = @{
    Name        = $name 
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
    Invoke-VstsTaskScript -ScriptBlock ([scriptblock]::Create( $scriptBlockString ))
}
# We want to add improved error handling here - if the error is "xxx\powershell.ps1 is not recognized as the name of a cmdlet, function, script file, or operable program"
# 
catch {
    Write-Verbose "Invoke-VstsTaskScript -ScriptBlock ([scriptblock]::Create( $scriptBlockString ))"
    Write-Verbose $_.Exception.Message -Verbose 
    throw $_.Exception
}
#
