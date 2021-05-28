function Parse-Version  {
    <#
        .SYNOPSIS
            Parses version from provided. Allows incomplete versions like 16.0. Returns empty string if there is more than 4 numbers divided by dot or string is not in semver format

        .EXAMPLE
            Parse-Version -Version "1.3.5" 
            
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version)

    if ($Version.IndexOf(".") -lt 0) {
        return [System.Version]::Parse("$($Version).0")
    }
    try {
        $res = [System.Version]::Parse($Version)

        return $res
    } catch {
        return ""
    }
}