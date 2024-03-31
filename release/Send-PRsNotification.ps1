# Send notifications by POST method to MS Teams webhook
# Body of message is compiled as Office 365 connector card
# More details about cards - https://docs.microsoft.com/en-us/microsoftteams/platform/task-modules-and-cards/cards/cards-reference#office-365-connector-card

$wikiLink = "[Wiki](https://dev.azure.com/mseng/AzureDevOps/_wiki/wikis/AzureDevOps.wiki/18816/How-to-release-the-agent)"

$arePRsCreated = $env:ADO_PR_ID -and $env:CC_PR_ID
if ($arePRsCreated) {
    $adoPrLink = "[ADO PR $env:ADO_PR_ID]($env:ADO_PR_LINK)"
    $ccPrLink = "[CC PR $env:CC_PR_ID]($env:CC_PR_LINK)"
    $title = "Agent ADO release PRs created"
    $text = "Created PRs with update of agent packages in ADO and ConfigChange repos.`n- $adoPrLink`n-$ccPrLink.`nRelated agent release article in $wikiLink."
    $themeColor = "#4974A5"
}
else {
    $pipelineLink = "$env:SYSTEM_TEAMFOUNDATIONCOLLECTIONURI$env:SYSTEM_TEAMPROJECT/_build/results?buildId=$env:BUILD_BUILDID&_a=summary"
    $buildLink = "[ID $($env:BUILD_BUILDID)]($($pipelineLink))"
    $title = "Agent release pipeline failed - ID $($env:BUILD_BUILDID)"
    $text = "Failed to create agent release. Please review the results of failed build: $buildLink. Related article in $wikiLink."
    $themeColor = "#FF0000"
}

# Notifications will be sent only if PRs are created or if it's the first failed attempt.
$shouldNotify = $arePRsCreated -or $env:SYSTEM_JOBATTEMPT -eq '1'
if ($shouldNotify) {
    $body = [PSCustomObject]@{
        title      = $title
        text       = $text
        themeColor = $themeColor
    } | ConvertTo-Json

    Invoke-RestMethod -Uri $env:TEAMS_WEBHOOK -Method Post -Body $body -ContentType "application/json"
}
else {
    Write-Host "Skipping notification."
}
